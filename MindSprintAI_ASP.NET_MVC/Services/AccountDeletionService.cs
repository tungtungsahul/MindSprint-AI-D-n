using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MindSprintAI_ASP.NET_MVC.Data;
using MindSprintAI_ASP.NET_MVC.Data.Entities;

namespace MindSprintAI_ASP.NET_MVC.Services;

// Pháp lý C9 (docs/PHAP_LY_VAN_BAN.md): xóa tài khoản = ẩn danh hóa. Hàng người dùng giữ lại (tên "Người dùng đã xóa",
// không email) để lượt làm và điểm trong bảng điểm của giảng viên không bị thủng; mọi dữ liệu riêng khác bị xóa.
public sealed class AccountDeletionService(AppDbContext db, UserManager<ApplicationUser> users, IEmailSender email,
    AccountEmailService accountEmail, TimeProvider clock, IWebHostEnvironment? env = null)
{
    public const string DeletedName = "Người dùng đã xóa";
    public const string InvalidLink = "Link này đã hết hạn hoặc đã được dùng. Hãy vào trang Tài khoản để yêu cầu lại.";
    private const string Provider = "QuizAccountDeletion", Name = "Nonce", Purpose = "DeleteAccount";
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    // Câu chặn đúng C9; null = xóa được. byAdmin: câu cho quản trị viên đọc khi xóa thay.
    public async Task<string?> BlockReasonAsync(string userId, bool byAdmin = false)
    {
        string Who(string self, string admin) => byAdmin ? admin : self;
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId && !u.IsGuest && u.DeletedAt == null)
            ?? throw new ForbiddenException();
        var now = Now;
        var classes = await db.ClassMembers.CountAsync(m => m.UserId == userId && m.Role == ClassRole.MainLecturer
            && m.Status == ClassMemberStatus.Active && m.Classroom.DeletedAt == null);
        if (classes > 0)
            return Who("Bạn", "Người này") + $" đang là giảng viên của {classes} lớp. Hãy chuyển lớp cho giảng viên khác hoặc xóa lớp trước khi xóa tài khoản.";
        // Quản trị viên đang bị khóa không tính là còn quản trị viên (cùng điều kiện AdminService.EnsureAsync).
        if (user.IsSystemAdmin && !await db.Users.AnyAsync(u => u.Id != userId && u.IsSystemAdmin && !u.IsGuest && u.DeletedAt == null
                && !db.AccountLocks.Any(x => x.UserId == u.Id && x.UnlockedAt == null && x.StartsAt <= now && (x.EndsAt == null || x.EndsAt > now))))
            return Who("Bạn là quản trị viên duy nhất", "Đây là quản trị viên duy nhất") + " nên chưa xóa được tài khoản.";
        // Cùng điều kiện "bài giao còn mở" với xóa bộ học (StudyLibraryService.EnsureAssignmentsEndedAsync).
        var open = db.Assignments.Where(a => (a.ClassroomId == null || a.Classroom!.DeletedAt == null) && a.Status != AssignmentStatus.Closed
            && (a.Mode == AssignmentMode.Test ? a.TestEndsAt == null || a.TestEndsAt > now : a.DueAt == null || a.DueAt > now));
        if (await open.AnyAsync(a => a.CreatedById == userId))
            return Who("Bạn", "Người này") + " còn bài giao đang mở. Hãy đóng các bài giao đó trước khi xóa tài khoản.";
        if (await open.AnyAsync(a => db.StudySets.Any(s => s.Id == a.StudySetId && s.OwnerId == userId)
            || (a.Quiz != null && a.Quiz.StudySet.OwnerId == userId)))
            return Who("Bộ học của bạn", "Bộ học của người này") + " đang được dùng trong lớp của giảng viên khác. Hãy nhờ họ gỡ bài giao đó trước khi xóa tài khoản.";
        if (await db.Attempts.AnyAsync(x => x.Participant.UserId == userId && x.Kind != AttemptKind.SelfReview
            && (x.Status == AttemptStatus.InProgress || x.Status == AttemptStatus.Paused) && (x.DeadlineAt == null || x.DeadlineAt > now)))
            return Who("Bạn đang có bài làm dở. Hãy nộp bài", "Người này đang có bài làm dở. Hãy chờ họ nộp bài") + " trước khi xóa tài khoản.";
        return null;
    }

    // Người dùng tự xóa: gửi link xác nhận 1 giờ, dùng 1 lần. Trả link để máy phát triển hiện ra (như các thư khác).
    public async Task<string?> RequestAsync(string userId, Func<string, string, string, string> buildLink)
    {
        if (await BlockReasonAsync(userId) is { } blocked) throw new BusinessException(blocked);
        var user = await users.FindByIdAsync(userId) ?? throw new ForbiddenException();
        if (string.IsNullOrEmpty(user.Email)) return null;
        // Chung hạn 5 thư/ngày, chờ 1 phút với các thư tài khoản khác; nói thật là chưa gửi để người dùng biết đường thử lại.
        if (!await accountEmail.TryReserveAsync(user))
            throw new BusinessException("Chưa gửi được thư lúc này vì email của bạn vừa nhận thư từ MindSprint AI. Hãy thử lại sau ít phút; nếu vẫn không được, hãy gửi email cho quản trị viên.");
        var token = await users.GenerateUserTokenAsync(user, TokenOptions.DefaultProvider, Purpose);
        var nonce = Guid.NewGuid().ToString("N");
        Check(await users.SetAuthenticationTokenAsync(user, Provider, Name, $"{nonce}|{Now.AddHours(1):O}"));
        var link = buildLink(userId, token, nonce);
        await email.SendAsync(user.Email, $"Xác nhận xóa tài khoản {Brand.Name}",
            $"<p>Chào bạn,</p><p>Bạn vừa yêu cầu xóa tài khoản {Brand.Name}. Bấm nút dưới đây trong vòng 1 giờ để xác nhận.</p>"
            + $"<p><a href=\"{WebUtility.HtmlEncode(link)}\">Xóa tài khoản</a></p>"
            + "<p>Nếu bạn không yêu cầu, hãy bỏ qua thư này và đổi mật khẩu ngay.</p>");
        return link;
    }

    // Trang mở từ link: kiểm trước khi hiện nút Xóa vĩnh viễn. Trả email để hiện trên trang, null = link không dùng được.
    public async Task<string?> CheckLinkAsync(string? userId, string? token, string? nonce)
    {
        var user = string.IsNullOrEmpty(userId) ? null : await users.FindByIdAsync(userId);
        return user != null && await LinkValidAsync(user, token, nonce) ? user.Email : null;
    }

    public async Task ConfirmAsync(string? userId, string? token, string? nonce)
    {
        var user = string.IsNullOrEmpty(userId) ? null : await users.FindByIdAsync(userId);
        if (user == null) throw new BusinessException(InvalidLink);
        await DeleteAsync(user.Id, user.Id, null, async u =>
        {
            if (!await LinkValidAsync(u, token, nonce)) throw new BusinessException(InvalidLink);
        });
    }

    // Quản trị viên xóa thay khi người dùng gửi yêu cầu qua email: bắt buộc lý do, không cần thư.
    public async Task DeleteByAdminAsync(string targetId, string? reason, string adminId)
    {
        await AdminService.EnsureAsync(db, adminId, Now);
        if (string.IsNullOrWhiteSpace(reason)) throw new BusinessException("Hãy nhập lý do xóa tài khoản.");
        if (reason.Trim().Length > 500) throw new BusinessException("Lý do tối đa 500 ký tự.");
        await DeleteAsync(targetId, adminId, reason.Trim(), null);
    }

    private async Task<bool> LinkValidAsync(ApplicationUser user, string? token, string? nonce)
    {
        if (user.IsGuest || user.DeletedAt != null || string.IsNullOrEmpty(token) || string.IsNullOrEmpty(nonce)) return false;
        var stored = (await users.GetAuthenticationTokenAsync(user, Provider, Name))?.Split('|');
        return stored is [var saved, var expiry] && saved == nonce
            && DateTime.TryParse(expiry, null, System.Globalization.DateTimeStyles.RoundtripKind, out var end) && end > Now
            && await users.VerifyUserTokenAsync(user, TokenOptions.DefaultProvider, Purpose, token);
    }

    private async Task DeleteAsync(string userId, string actorId, string? adminReason, Func<ApplicationUser, Task>? verify)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        // Cùng khóa, cùng thứ tự với cấp/gỡ quyền (51C): khóa Admin trước rồi mới khóa dòng người dùng, để luôn còn ít nhất một quản trị viên.
        await db.Database.ExecuteSqlRawAsync("DECLARE @r int; EXEC @r = sp_getapplock @Resource = 'admin-role-changes', @LockMode = 'Exclusive', @LockOwner = 'Transaction'; IF @r < 0 THROW 50000, N'Không lấy được khóa Admin.', 1;");
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT [Id] FROM [AspNetUsers] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {userId}");
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId) ?? throw new BusinessException("Không tìm thấy tài khoản.");
        await db.Entry(user).ReloadAsync();
        if (verify != null) await verify(user);
        if (await BlockReasonAsync(userId, byAdmin: adminReason != null) is { } blocked) throw new BusinessException(blocked); // kiểm lại ở bước cuối
        var now = Now;

        // Dữ liệu riêng: xóa hẳn. Bảng Identity: đăng nhập ngoài, passkey, khóa ứng dụng xác thực, mã dự phòng, token.
        await db.UserLogins.Where(x => x.UserId == userId).ExecuteDeleteAsync();
        await db.UserPasskeys.Where(x => x.UserId == userId).ExecuteDeleteAsync();
        await db.UserTokens.Where(x => x.UserId == userId).ExecuteDeleteAsync();
        await db.UserClaims.Where(x => x.UserId == userId).ExecuteDeleteAsync();
        await db.UserRoles.Where(x => x.UserId == userId).ExecuteDeleteAsync();
        await db.UserApiKeys.Where(x => x.UserId == userId).ExecuteDeleteAsync();
        var conversations = db.AiConversations.Where(c => c.UserId == userId).Select(c => c.Id);
        await db.AiCitations.Where(x => db.AiMessages.Any(m => m.Id == x.AiMessageId && conversations.Contains(m.ConversationId))).ExecuteDeleteAsync();
        await db.AiMessages.Where(x => conversations.Contains(x.ConversationId)).ExecuteDeleteAsync();
        await db.AiConversations.Where(x => x.UserId == userId).ExecuteDeleteAsync();
        await db.KnownDevices.Where(x => x.UserId == userId).ExecuteDeleteAsync();
        await db.Notifications.Where(x => x.UserId == userId).ExecuteDeleteAsync();
        await db.Reminders.Where(x => x.UserId == userId).ExecuteDeleteAsync();
        await db.ReminderDigests.Where(x => x.UserId == userId).ExecuteDeleteAsync();
        await db.UserSettingPresets.Where(x => x.UserId == userId).ExecuteDeleteAsync();
        await db.KanbanTasks.Where(x => x.UserId == userId).ExecuteDeleteAsync(); // việc cá nhân (Kanban) là dữ liệu riêng, xóa cùng tài khoản
        await db.StudySlots.Where(x => x.UserId == userId).ExecuteDeleteAsync(); // thời khóa biểu cá nhân
        await db.NotebookSources.Where(x => x.Notebook.UserId == userId).ExecuteDeleteAsync(); // sổ tay AI: nguồn, ghi chú rồi sổ
        await db.NotebookNotes.Where(x => x.Notebook.UserId == userId).ExecuteDeleteAsync();
        await db.Notebooks.Where(x => x.UserId == userId).ExecuteDeleteAsync();

        // Lời mời và chuyển lớp còn chờ: thu hồi để không ai nhận lời mời của/tới người đã xóa.
        var normalizedEmail = user.NormalizedEmail;
        await db.Invitations.Where(i => i.Status == InvitationStatus.Pending
                && (i.InviterId == userId || i.RecipientId == userId || (normalizedEmail != null && i.NormalizedEmail == normalizedEmail)))
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.Status, InvitationStatus.Revoked));
        await db.ClassTransfers.Where(t => t.Status == ClassTransferStatus.Pending && (t.FromUserId == userId || t.ToUserId == userId))
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, ClassTransferStatus.Cancelled));
        // Email cũ trong lời mời/chuyển lớp đã xong cũng là dữ liệu của người này.
        await db.Invitations.Where(i => i.RecipientId == userId || (normalizedEmail != null && i.NormalizedEmail == normalizedEmail))
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.Email, "").SetProperty(i => i.NormalizedEmail, ""));
        await db.ClassTransfers.Where(t => t.FromUserId == userId || t.ToUserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.InvitedEmail, (string?)null));
        // Rời mọi lớp (GV phụ, học viên, đang chờ duyệt): bài giao mới không thêm dòng "Người dùng đã xóa", không sinh nhắc việc.
        // Bảng điểm vẫn giữ người đã rời nếu họ có lượt làm (GradebookService).
        await db.ClassMembers.Where(m => m.UserId == userId && (m.Status == ClassMemberStatus.Active || m.Status == ClassMemberStatus.PendingApproval))
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.Status, ClassMemberStatus.Left).SetProperty(m => m.LeftAt, now));
        // Tên khách đã gộp vào tài khoản (bài Kiểm tra giữ cả dòng khách) và dấu nhận diện máy trong lượt làm.
        var guests = db.Guests.Where(g => g.UserId == userId || g.MergedIntoUserId == userId);
        await db.Participants.Where(p => p.GuestId != null && guests.Any(g => g.Id == p.GuestId) && p.GuestDisplayName != null)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.GuestDisplayName, DeletedName));
        await db.Users.Where(u => u.IsGuest && guests.Any(g => g.UserId == u.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.DisplayName, DeletedName));
        await guests.ExecuteUpdateAsync(s => s.SetProperty(g => g.DisplayName, DeletedName));
        var attempts = db.Attempts.Where(a => a.Participant.UserId == userId || (a.Participant.GuestId != null && guests.Any(g => g.Id == a.Participant.GuestId)));
        await db.DeviceChangeRequests.Where(d => attempts.Any(a => a.Id == d.AttemptId))
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.Reason, (string?)null).SetProperty(d => d.LightHash, (string?)null)
                .SetProperty(d => d.FullHash, (string?)null).SetProperty(d => d.NetworkPrefix, (string?)null));
        await attempts.ExecuteUpdateAsync(s => s.SetProperty(a => a.LightHash, (string?)null).SetProperty(a => a.FullHash, (string?)null));

        // Bộ học: vào thùng rác như khi chủ tự xóa (đã từng giao thì lưu trữ, không xóa hẳn để bảng điểm cũ còn đọc được).
        foreach (var set in await db.StudySets.Where(s => s.OwnerId == userId && s.DeletedAt == null).ToListAsync())
        {
            if (set.EverAssignedAt != null || await db.Assignments.AnyAsync(a => a.StudySetId == set.Id || (a.Quiz != null && a.Quiz.StudySetId == set.Id)))
                set.ArchivedAt ??= now;
            set.DeletedAt = now;
        }

        // Ghi lý do và khóa vĩnh viễn bằng bảng khóa sẵn có.
        db.AccountLocks.Add(new() { UserId = userId, Kind = AccountLockKind.Protective, StartsAt = now, LockedById = actorId,
            Reason = adminReason == null ? "Người dùng tự xóa tài khoản" : "Quản trị viên xóa tài khoản theo yêu cầu: " + adminReason });

        user.DeletedAt = now;
        user.DisplayName = DeletedName;
        user.UserName = user.NormalizedUserName = "DELETED-" + userId;
        user.Email = user.NormalizedEmail = null;
        user.EmailConfirmed = false;
        user.PasswordHash = null;
        user.PhoneNumber = null; user.PhoneNumberConfirmed = false;
        user.TwoFactorEnabled = false;
        user.LockoutEnabled = true; user.LockoutEnd = DateTimeOffset.MaxValue;
        user.SecurityStamp = Guid.NewGuid().ToString(); // đăng xuất mọi máy trong vòng 1 phút
        user.ConcurrencyStamp = Guid.NewGuid().ToString();
        user.IsSystemAdmin = false;
        user.AvatarUrl = null; user.AvatarSource = AvatarSource.None; user.AvatarUpdatedAt = null;
        user.EmailRemindersEnabled = false; user.SrsBellEnabled = false; user.DeviceChangeBellEnabled = false; user.ScreenLeaveBellEnabled = false;
        db.Audit?.Add(AuditAction.AccountDeleted, "User", userId, actorId);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        AvatarService.DeleteFile(env, userId); // ảnh tự tải lên (sau khi đã xóa chắc chắn)
    }

    private static void Check(IdentityResult result)
    {
        if (!result.Succeeded) throw new InvalidOperationException(string.Join(" ", result.Errors.Select(e => e.Description)));
    }
}
