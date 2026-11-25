using Microsoft.EntityFrameworkCore;
using MindSprintAI_ASP.NET_MVC.Data;

namespace MindSprintAI_ASP.NET_MVC.Services;

// Pháp lý (Chính sách mục 6): chỉ giữ dữ liệu khi còn cần. Chạy mỗi ngày cùng JoinCodeCleanupService.
public static class RetentionCleanup
{
    // "6 tháng kể từ lần đăng nhập cuối": LastSeenAt chỉ đổi khi đăng nhập (T5, NewDeviceService); máy cũ hơn phải đăng nhập lại.
    public static readonly TimeSpan DeviceIdle = TimeSpan.FromDays(183);
    public static readonly TimeSpan UnfinishedSignUp = TimeSpan.FromDays(7);

    public static async Task<int> RunAsync(AppDbContext db, DateTime now, CancellationToken ct = default)
    {
        var deviceCutoff = now - DeviceIdle;
        var devices = await db.KnownDevices.Where(d => d.LastSeenAt < deviceCutoff).ExecuteDeleteAsync(ct);

        // Đăng ký dở: có người gõ email vào form nhưng chủ email chưa từng đặt mật khẩu hay đăng nhập bằng cách nào.
        // Chỉ xóa khi không có dữ liệu nào gắn với tài khoản; bảng Identity (token gửi thư…) tự xóa theo.
        var signUpCutoff = now - UnfinishedSignUp;
        var signUps = await db.Users.Where(u => !u.IsGuest && !u.EmailConfirmed && u.PasswordHash == null && u.DeletedAt == null
                && u.CreatedAt < signUpCutoff
                && !db.UserLogins.Any(l => l.UserId == u.Id) && !db.UserPasskeys.Any(p => p.UserId == u.Id)
                && !db.ClassMembers.Any(m => m.UserId == u.Id) && !db.Participants.Any(p => p.UserId == u.Id)
                && !db.StudySets.Any(s => s.OwnerId == u.Id) && !db.Guests.Any(g => g.UserId == u.Id || g.MergedIntoUserId == u.Id)
                && !db.KnownDevices.Any(d => d.UserId == u.Id) && !db.Notifications.Any(n => n.UserId == u.Id)
                && !db.Reminders.Any(r => r.UserId == u.Id) && !db.UserApiKeys.Any(k => k.UserId == u.Id)
                && !db.AccountLocks.Any(l => l.UserId == u.Id) && !db.AdminRoleChanges.Any(r => r.TargetUserId == u.Id || r.AdminId == u.Id)
                && !db.Invitations.Any(i => i.RecipientId == u.Id || i.InviterId == u.Id))
            .ExecuteDeleteAsync(ct);
        return devices + signUps;
    }
}
