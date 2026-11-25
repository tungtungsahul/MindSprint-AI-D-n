using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MindSprintAI_ASP.NET_MVC.Data;
using MindSprintAI_ASP.NET_MVC.Data.Entities;

namespace MindSprintAI_ASP.NET_MVC.Services;

// Tự cập nhật hồ sơ (UC-AC-02 của QuizEdu; rubric môn Web "xem, cập nhật thông tin cá nhân"). Người dùng chốt 19/11/2026:
// - Đổi tên hiển thị: kiểm tra như lúc đăng ký (bắt buộc, tối đa 100 ký tự) + mỗi lần đổi cách nhau ít nhất 7 ngày
//   (tên hiện trong lớp, bảng điểm, xếp hạng: chặn đổi liên tục để giả người khác); có ghi nhật ký.
// - Đổi mật khẩu: nhập mật khẩu hiện tại; quy tắc (55) đã chốt: đổi xong thì đăng xuất mọi thiết bị khác, máy đang dùng
//   vẫn đăng nhập (controller cấp lại phiên), gửi email báo.
public sealed class ProfileService(AppDbContext db, UserManager<ApplicationUser> users, IEmailSender email, TimeProvider clock)
{
    public const int MaxName = 100;
    public static readonly TimeSpan RenameGap = TimeSpan.FromDays(7);

    // null = đổi được ngay.
    public DateTime? NextRenameAt(ApplicationUser user) =>
        user.DisplayNameChangedAt is { } last && last + RenameGap > Now ? last + RenameGap : null;

    public async Task RenameAsync(string userId, string? name)
    {
        var clean = (name ?? "").Trim();
        if (clean.Length == 0) throw new BusinessException("Nhập tên hiển thị.");
        if (clean.Length > MaxName) throw new BusinessException($"Tên tối đa {MaxName} ký tự.");
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId) ?? throw new ForbiddenException();
        if (user.IsGuest || user.DeletedAt != null) throw new ForbiddenException();
        if (clean == user.DisplayName) throw new BusinessException("Tên mới giống tên đang dùng.");
        var now = Now;
        var since = now - RenameGap;
        // Kiểm tra 7 ngày và ghi trong cùng một câu lệnh: hai lần bấm cùng lúc không lọt cả hai.
        var changed = await db.Users
            .Where(u => u.Id == userId && (u.DisplayNameChangedAt == null || u.DisplayNameChangedAt <= since))
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.DisplayName, clean).SetProperty(u => u.DisplayNameChangedAt, now));
        if (changed == 0)
        {
            var fresh = await db.Users.AsNoTracking().SingleAsync(u => u.Id == userId);
            throw new BusinessException(NextRenameAt(fresh) is { } next
                ? $"Mỗi lần đổi tên cách nhau ít nhất 7 ngày. Bạn đổi lại được từ {VnTime.Format(next)}."
                : "Chưa đổi được tên. Hãy thử lại.");
        }
        db.Audit?.Add(AuditAction.DisplayNameChanged, "User", userId, userId);
        await db.SaveChangesAsync();
    }

    public async Task ChangePasswordAsync(string userId, string? current, string? next, string? confirm, string accountUrl)
    {
        if (string.IsNullOrEmpty(current)) throw new BusinessException("Nhập mật khẩu hiện tại.");
        if (string.IsNullOrEmpty(next)) throw new BusinessException("Nhập mật khẩu mới.");
        if (next != confirm) throw new BusinessException("Hai mật khẩu mới không khớp.");
        var user = await users.FindByIdAsync(userId) ?? throw new ForbiddenException();
        if (user.IsGuest || user.DeletedAt != null) throw new ForbiddenException();
        if (!await users.HasPasswordAsync(user)) throw new BusinessException("Tài khoản chưa có mật khẩu. Hãy dùng mục Đặt mật khẩu.");
        if (current == next) throw new BusinessException("Mật khẩu mới phải khác mật khẩu hiện tại.");
        // Identity kiểm tra mật khẩu hiện tại + độ mạnh mật khẩu mới, đổi SecurityStamp (thiết bị khác bị đăng xuất);
        // ProtectedUserStore ghi nhật ký PasswordChanged.
        var result = await users.ChangePasswordAsync(user, current, next);
        if (!result.Succeeded)
            throw new BusinessException(result.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.PasswordMismatch))
                ? "Mật khẩu hiện tại không đúng." : string.Join(" ", result.Errors.Select(e => e.Description)));
        await email.SendAsync(user.Email!, "Đã đổi mật khẩu · " + Brand.Name,
            $"<p>Mật khẩu tài khoản {WebUtility.HtmlEncode(Brand.Name)} của bạn vừa được đổi. Các thiết bị khác đã bị đăng xuất.</p>"
            + $"<p>Nếu không phải bạn, hãy vào <a href=\"{WebUtility.HtmlEncode(accountUrl)}\">Tài khoản — Khóa bảo vệ</a> để khóa tài khoản ngay.</p>");
    }

    private DateTime Now => clock.GetUtcNow().UtcDateTime;
}
