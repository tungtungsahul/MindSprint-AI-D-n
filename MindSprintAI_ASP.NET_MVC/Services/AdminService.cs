using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using MindSprintAI_ASP.NET_MVC.Data;
using MindSprintAI_ASP.NET_MVC.Data.Entities;

namespace MindSprintAI_ASP.NET_MVC.Services;

public record AdminStats(int Users, int Classes, int Assignments, int Attempts);
public record AdminUser(ApplicationUser User, AccountLock? Lock);
public record AdminUsersPage(List<AdminUser> Rows, string Search, int Page, int Total, AdminStats Stats);

public class AdminService(AppDbContext db, TimeProvider clock)
{
    public static async Task EnsureAsync(AppDbContext db, string userId, DateTime? at = null)
    {
        var now = at ?? DateTime.UtcNow;
        if (!await db.Users.AnyAsync(u => u.Id == userId && u.IsSystemAdmin && !u.IsGuest)) throw new ForbiddenException();
        if (await db.AccountLocks.AnyAsync(x => x.UserId == userId && x.UnlockedAt == null && x.StartsAt <= now && (x.EndsAt == null || x.EndsAt > now))) throw new ForbiddenException();
    }

    public async Task<AdminUsersPage> UsersAsync(string adminId, string? search, int page)
    {
        await EnsureAsync(db, adminId, clock.GetUtcNow().UtcDateTime);
        if (db.Audit != null) await db.Audit.WriteSeparateAsync(AuditAction.AdminListedUsers, "User", "", adminId); // (NK3)
        var now = clock.GetUtcNow().UtcDateTime;
        search = (search ?? "").Trim(); page = Math.Max(1, page);
        var query = db.Users.AsNoTracking().Where(u => !u.IsGuest && (search == "" || u.DisplayName.Contains(search) || u.Email!.Contains(search)));
        var total = await query.CountAsync();
        page = Math.Min(page, Math.Max(1, (total + 19) / 20));
        var users = await query.OrderBy(u => u.DisplayName).ThenBy(u => u.Id).Skip((page - 1) * 20).Take(20).ToListAsync();
        var ids = users.Select(u => u.Id).ToList();
        var locks = await db.AccountLocks.AsNoTracking().Where(x => ids.Contains(x.UserId) && x.UnlockedAt == null
            && x.StartsAt <= now && (x.EndsAt == null || x.EndsAt > now)).OrderByDescending(x => x.Kind).ThenByDescending(x => x.Id).ToListAsync();
        return new(users.Select(u => new AdminUser(u, locks.FirstOrDefault(x => x.UserId == u.Id))).ToList(), search, page, total,
            new(await db.Users.CountAsync(u => !u.IsGuest), await db.Classrooms.CountAsync(), await db.Assignments.CountAsync(),
                await db.Attempts.CountAsync(at => at.Participant.User != null && !at.Participant.User.IsGuest)));
    }

    public async Task<ApplicationUser> UserAsync(string id, string adminId)
    {
        await EnsureAsync(db, adminId, clock.GetUtcNow().UtcDateTime);
        if (db.Audit != null) await db.Audit.WriteSeparateAsync(AuditAction.AdminViewedUser, "User", id, adminId); // (NK3)
        return await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == id && !u.IsGuest) ?? throw new BusinessException("Không tìm thấy tài khoản.");
    }

    public async Task ChangeRoleAsync(string targetId, bool grant, string? reason, string adminId)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 500) throw new BusinessException("Nhập lý do cấp/gỡ quyền, tối đa 500 ký tự.");
        await using var tx = await db.Database.BeginTransactionAsync();
        // (51C) cấp/gỡ đồng thời cũng phải giữ ít nhất một Admin.
        await db.Database.ExecuteSqlRawAsync("DECLARE @r int; EXEC @r = sp_getapplock @Resource = 'admin-role-changes', @LockMode = 'Exclusive', @LockOwner = 'Transaction'; IF @r < 0 THROW 50000, N'Không lấy được khóa Admin.', 1;");
        await EnsureAsync(db, adminId, clock.GetUtcNow().UtcDateTime);
        var target = await db.Users.SingleOrDefaultAsync(u => u.Id == targetId && !u.IsGuest && u.DeletedAt == null) ?? throw new BusinessException("Không tìm thấy tài khoản.");
        await db.Entry(target).ReloadAsync();
        ValidateRevoke(grant, targetId == adminId, await db.Users.CountAsync(u => u.IsSystemAdmin));
        if (target.IsSystemAdmin == grant) throw new BusinessException("Tài khoản đã có trạng thái quyền này.");
        target.IsSystemAdmin = grant;
        target.SecurityStamp = Guid.NewGuid().ToString();
        db.AdminRoleChanges.Add(new() { AdminId = adminId, TargetUserId = targetId, Granted = grant, Reason = reason.Trim(), At = clock.GetUtcNow().UtcDateTime });
        db.Audit?.Add(grant ? AuditAction.AdminGranted : AuditAction.AdminRevoked, "User", targetId, adminId); // (NK3)
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    public static void ValidateRevoke(bool grant, bool self, int admins)
    {
        if (!grant && self) throw new BusinessException("Không thể tự gỡ quyền quản trị viên.");
        if (!grant && admins <= 1) throw new BusinessException("Không thể gỡ quản trị viên cuối cùng.");
    }

    public async Task<List<AdminRoleChange>> HistoryAsync(string adminId)
    {
        await EnsureAsync(db, adminId, clock.GetUtcNow().UtcDateTime);
        if (db.Audit != null) await db.Audit.WriteSeparateAsync(AuditAction.AdminViewedRoleHistory, "Admin", "", adminId); // (NK3)
        return await db.AdminRoleChanges.AsNoTracking().OrderByDescending(x => x.At).ThenByDescending(x => x.Id).Take(100).ToListAsync();
    }

    // (51C) lịch sử hiện tên + email để Admin đọc được, không hiện mã tài khoản.
    public Task<Dictionary<string, string>> UserLabelsAsync(List<string> ids) => db.Users.AsNoTracking()
        .Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => $"{u.DisplayName} ({u.Email})");

    public static async Task BootstrapAsync(AppDbContext db, IEnumerable<string> emails)
    {
        foreach (var email in emails.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            var normalized = email.Trim().ToUpperInvariant();
            var user = await db.Users.SingleOrDefaultAsync(u => u.NormalizedEmail == normalized);
            if (user is { IsSystemAdmin: false })
            {
                user.IsSystemAdmin = true;
                user.SecurityStamp = Guid.NewGuid().ToString();
                db.Audit?.Add(AuditAction.AdminGranted, "User", user.Id); // (NK3) Hệ thống cấp theo danh sách khởi tạo.
            }
        }
        await db.SaveChangesAsync();
    }
}

public sealed class SystemAdminRequirement : IAuthorizationRequirement;
public sealed class SystemAdminHandler(AppDbContext db, TimeProvider clock) : AuthorizationHandler<SystemAdminRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, SystemAdminRequirement requirement)
    {
        var id = context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var now = clock.GetUtcNow().UtcDateTime;
        if (id != null && await db.Users.AnyAsync(u => u.Id == id && u.IsSystemAdmin && !u.IsGuest)
            && !await db.AccountLocks.AnyAsync(x => x.UserId == id && x.UnlockedAt == null && x.StartsAt <= now && (x.EndsAt == null || x.EndsAt > now)))
            context.Succeed(requirement);
    }
}
