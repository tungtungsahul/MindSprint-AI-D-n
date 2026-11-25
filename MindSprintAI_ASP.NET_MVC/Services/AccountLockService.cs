using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MindSprintAI_ASP.NET_MVC.Data;
using MindSprintAI_ASP.NET_MVC.Data.Entities;

namespace MindSprintAI_ASP.NET_MVC.Services;

public class AccountLockService(AppDbContext db, UserManager<ApplicationUser> users, IEmailSender email,
    TimeProvider clock, ILearnNotifier notifier)
{
    private const string Provider = "QuizProtectiveLock", Name = "Nonce", Purpose = "ProtectiveLock";
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public Task RefreshUserAsync(ApplicationUser user) => db.Entry(user).ReloadAsync();

    public Task<AccountLock?> ActiveAsync(string userId) => db.AccountLocks.AsNoTracking()
        .Where(x => x.UserId == userId && x.UnlockedAt == null && x.StartsAt <= Now && (x.EndsAt == null || x.EndsAt > Now))
        .OrderByDescending(x => x.Kind).ThenByDescending(x => x.Id).FirstOrDefaultAsync();

    public static string Message(AccountLock row) => row.Kind == AccountLockKind.Protective
        ? "Tài khoản đang tạm khóa để bảo vệ."
        : $"Tài khoản bị khóa vi phạm: {row.Reason}. Thời hạn: {(row.EndsAt == null ? "đến khi quản trị viên mở khóa" : VnTime.Format(row.EndsAt))}.";

    public static string MessageForUser(AccountLock row) => Message(row) + (row.Kind == AccountLockKind.Protective
        ? " Muốn mở khóa, hãy bấm Quên mật khẩu ở trang đăng nhập và làm theo email."
        : " Hãy liên hệ quản trị viên.");

    public async Task<string> RequestProtectiveAsync(string userId, Func<string, string, string, string> buildLink)
    {
        var user = await users.FindByIdAsync(userId) ?? throw new ForbiddenException();
        if (!user.EmailConfirmed) throw new BusinessException("Hãy xác minh email trước.");
        var token = await users.GenerateUserTokenAsync(user, TokenOptions.DefaultProvider, Purpose);
        var nonce = Guid.NewGuid().ToString("N");
        Check(await users.SetAuthenticationTokenAsync(user, Provider, Name, $"{nonce}|{Now.AddMinutes(30):O}"));
        var link = buildLink(userId, token, nonce);
        await email.SendAsync(user.Email!, $"Xác nhận khóa bảo vệ {Brand.Name}",
            $"<p>Bấm link trong 30 phút để xác nhận khóa tài khoản (chỉ dùng một lần):</p><p><a href=\"{WebUtility.HtmlEncode(link)}\">Xác nhận khóa tài khoản</a></p>");
        return link;
    }

    public async Task ConfirmProtectiveAsync(string userId, string token, string nonce)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        await LockAssignmentsAsync(userId);
        await LockUserAsync(userId);
        var user = await users.FindByIdAsync(userId) ?? throw new BusinessException("Link không hợp lệ hoặc đã hết hạn.");
        await db.Entry(user).ReloadAsync();
        var stored = (await users.GetAuthenticationTokenAsync(user, Provider, Name))?.Split('|');
        if (stored is not [var saved, var expiry] || saved != nonce
            || !DateTime.TryParse(expiry, null, System.Globalization.DateTimeStyles.RoundtripKind, out var end) || end <= Now
            || !await users.VerifyUserTokenAsync(user, TokenOptions.DefaultProvider, Purpose, token))
            throw new BusinessException("Link không hợp lệ, đã dùng hoặc đã hết hạn.");
        Check(await users.RemoveAuthenticationTokenAsync(user, Provider, Name));
        db.Audit?.Add(AuditAction.AccountSelfLocked, "User", userId, userId); // (NK3)
        var ended = await ApplyAsync(user, AccountLockKind.Protective, null, null, null);
        await tx.CommitAsync();
        await new NotificationService(db, clock, notifier).PushAsync(userId);
        await NotifyAsync(ended);
    }

    public async Task LockAsync(string targetId, AccountLockKind kind, string? reason, DateTime? endsAt, string adminId, int? reportId = null)
    {
        Validate(kind, reason, endsAt, Now);
        await using var tx = await db.Database.BeginTransactionAsync();
        await AdminService.EnsureAsync(db, adminId, Now);
        ContentReport? report = null;
        if (reportId is { } rid)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT [Id] FROM [ContentReports] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {rid}");
            report = await db.ContentReports.SingleOrDefaultAsync(r => r.Id == rid) ?? throw new BusinessException("Không tìm thấy báo cáo.");
            await db.Entry(report).ReloadAsync();
            if (report.Status != ReportStatus.Pending || kind != AccountLockKind.Violation) throw new BusinessException("Báo cáo đã xử lý hoặc kiểu khóa không hợp lệ.");
            targetId = report.TargetOwnerId;
        }
        await LockAssignmentsAsync(targetId);
        await LockUserAsync(targetId);
        var target = await users.FindByIdAsync(targetId) ?? throw new BusinessException("Không tìm thấy tài khoản.");
        await db.Entry(target).ReloadAsync();
        db.Audit?.Add(AuditAction.AdminLockedUser, "User", targetId, adminId); // (NK3)
        var ended = await ApplyAsync(target, kind, reason, endsAt, adminId);
        if (report != null)
        {
            report.Status = ReportStatus.Handled; report.HandledById = adminId; report.HandledAt = Now;
            db.Audit?.Add(AuditAction.ReportHandled, "ContentReport", report.Id, adminId); // (NK3)
            await db.SaveChangesAsync();
        }
        await tx.CommitAsync();
        await new NotificationService(db, clock, notifier).PushAsync(targetId);
        await NotifyAsync(ended);
    }

    public static void Validate(AccountLockKind kind, string? reason, DateTime? endsAt, DateTime now)
    {
        if (!Enum.IsDefined(kind)) throw new BusinessException("Kiểu khóa không hợp lệ.");
        if (kind == AccountLockKind.Violation && string.IsNullOrWhiteSpace(reason)) throw new BusinessException("Khóa vi phạm cần lý do.");
        if (reason?.Length > 500) throw new BusinessException("Lý do tối đa 500 ký tự.");
        if (endsAt != null && (kind != AccountLockKind.Violation || endsAt <= now)) throw new BusinessException("Hạn khóa vi phạm phải ở tương lai.");
    }

    private async Task LockAssignmentsAsync(string userId)
    {
        // (52, B4b) bài → thành viên → tài khoản → người làm → lượt; Start cũng kiểm khóa sau thành viên.
        var ids = await db.Assignments.Where(a => a.Participants.Any(p => p.UserId == userId)
            || db.ClassMembers.Any(m => m.ClassroomId == a.ClassroomId && m.UserId == userId))
            .OrderBy(a => a.Id).Select(a => a.Id).ToListAsync();
        foreach (var id in ids) await AttemptLock.AssignmentAsync(db, id);
        var rooms = await db.Assignments.Where(a => ids.Contains(a.Id)).Select(a => a.ClassroomId).Distinct().OrderBy(x => x).ToListAsync();
        foreach (var id in rooms) await AttemptLock.MemberAsync(db, id, userId);
    }

    private Task LockUserAsync(string userId) => db.Database.ExecuteSqlInterpolatedAsync(
        $"SELECT [Id] FROM [AspNetUsers] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {userId}");

    private async Task<List<Attempt>> ApplyAsync(ApplicationUser user, AccountLockKind kind, string? reason, DateTime? endsAt, string? by)
    {
        db.AccountLocks.Add(new() { UserId = user.Id, Kind = kind, Reason = reason?.Trim(), StartsAt = Now, EndsAt = endsAt, LockedById = by });
        await new NotificationService(db, clock, notifier).AddAsync(user.Id, NotificationType.AccountLock,
            new("Tài khoản của bạn đã bị khóa. Thông tin được lưu để xem khi đăng nhập lại.", "/Account"));
        Check(await users.UpdateSecurityStampAsync(user));
        var participants = await db.Participants.Where(p => p.UserId == user.Id).OrderBy(p => p.Id).Select(p => p.Id).ToListAsync();
        var loaded = new List<Participant>();
        foreach (var id in participants) loaded.Add(await AttemptLock.ParticipantAsync(db, id));
        var ended = new List<Attempt>();
        foreach (var id in loaded.SelectMany(p => p.Attempts).Where(AttemptRules.IsOpen).OrderBy(a => a.Id).Select(a => a.Id).ToList())
        {
            var a = await AttemptLock.LoadAsync(db, id);
            AttemptRules.End(a, a.Participant, AttemptEndReason.AccountLocked, Now);
            await CardCompletion.RefreshAsync(db, a.Participant, Now);
            ended.Add(a);
        }
        await db.SaveChangesAsync();
        return ended;
    }

    private async Task NotifyAsync(List<Attempt> ended)
    {
        foreach (var a in ended)
            await notifier.AttemptAsync(a.Id, "account-locked", new(AttemptId: a.Id, Url: "/Account/Login"));
        foreach (var id in ended.Select(a => a.Participant.AssignmentId).Distinct()) await notifier.LecturersAsync(id);
    }

    public async Task UnlockAsync(string userId, string adminId)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        await AdminService.EnsureAsync(db, adminId, Now);
        await LockUserAsync(userId);
        db.Audit?.Add(AuditAction.AdminUnlockedUser, "User", userId, adminId); // (NK3)
        await EndLocksAsync(userId, adminId, protectiveOnly: false);
        await tx.CommitAsync();
        await new NotificationService(db, clock, notifier).PushAsync(userId);
    }

    // (52) reset mật khẩu chỉ mở bảo vệ; khóa vi phạm giữ nguyên dù đổi mật khẩu thành công.
    public async Task UnlockProtectiveAsync(string userId)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        await LockUserAsync(userId);
        await EndLocksAsync(userId, userId, protectiveOnly: true);
        await tx.CommitAsync();
        await new NotificationService(db, clock, notifier).PushAsync(userId);
    }

    private async Task EndLocksAsync(string userId, string by, bool protectiveOnly)
    {
        var rows = await db.AccountLocks.Where(x => x.UserId == userId && x.UnlockedAt == null
            && (!protectiveOnly || x.Kind == AccountLockKind.Protective)).ToListAsync();
        foreach (var row in rows) { row.UnlockedAt = Now; row.UnlockedById = by; }
        if (rows.Count > 0) await new NotificationService(db, clock, notifier).AddAsync(userId, NotificationType.AccountLock,
            new("Tài khoản của bạn đã được mở khóa.", "/Account"));
        await db.SaveChangesAsync();
    }

    // (14, 52) ghi một lần khi đăng nhập lại sau khi khóa có thời hạn tự hết hiệu lực.
    public async Task ExpireAsync(string userId)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        await LockUserAsync(userId);
        var rows = await db.AccountLocks.Where(l => l.UserId == userId && l.UnlockedAt == null && l.EndsAt <= Now).ToListAsync();
        if (rows.Count == 0) return;
        foreach (var row in rows) row.UnlockedAt = row.EndsAt;
        var notices = new NotificationService(db, clock, notifier);
        await notices.AddAsync(userId, NotificationType.AccountLock, new("Thời hạn khóa tài khoản của bạn đã kết thúc.", "/Account"));
        await db.SaveChangesAsync(); await tx.CommitAsync();
        await notices.PushAsync(userId);
    }

    private static void Check(IdentityResult result)
    {
        if (!result.Succeeded) throw new BusinessException("Tài khoản vừa thay đổi. Hãy tải lại và thử lại.");
    }
}

public static class AccountLockState
{
    public static async Task EnsureAllowedAsync(AppDbContext db, string userId, DateTime now)
    {
        // Khóa dòng tài khoản bằng câu RIÊNG rồi mới kiểm: trong cùng một câu, phần đọc AccountLocks (RCSI) lấy bản chụp lúc câu
        // bắt đầu, trước khi chờ được khóa → có thể sót lệnh khóa Admin vừa commit (tài khoản 2 rà 14/11/2026). 2 phép kiểm gộp 1 câu.
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT [Id] FROM [AspNetUsers] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {userId}");
        if (await db.Users.AnyAsync(u => u.Id == userId && (u.IsGuest && u.LockoutEnd != null
                || db.AccountLocks.Any(x => x.UserId == userId && x.UnlockedAt == null && x.StartsAt <= now && (x.EndsAt == null || x.EndsAt > now)))))
            throw new ForbiddenException();
    }
}

public class AccountLockMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, AppDbContext db, TimeProvider clock, SignInManager<ApplicationUser> signIn)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            // Kết quả của câu hỏi gộp ở GuestSessionMiddleware (cùng yêu cầu này); thiếu thì tự hỏi (ví dụ thứ tự middleware đổi).
            var check = context.Items.TryGetValue(SessionCheck.Key, out var saved) ? saved as SessionCheck
                : await SessionCheck.LoadAsync(db, context.User.FindFirstValue(ClaimTypes.NameIdentifier), null, clock.GetUtcNow().UtcDateTime);
            if (check == null || check.Locked
                || context.User.FindFirstValue("AspNet.Identity.SecurityStamp") != check.SecurityStamp)
            {
                await signIn.SignOutAsync();
                if (context.Request.Headers.Accept.ToString().Contains("application/json"))
                {
                    context.Response.StatusCode = 403;
                    await context.Response.WriteAsJsonAsync(new { error = "Bạn đã bị đăng xuất hoặc tài khoản đã bị khóa." });
                }
                else context.Response.Redirect("/Account/Login");
                return;
            }
        }
        await next(context);
    }
}
