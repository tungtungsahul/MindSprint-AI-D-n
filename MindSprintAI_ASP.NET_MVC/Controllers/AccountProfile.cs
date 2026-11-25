using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MindSprintAI_ASP.NET_MVC.Services;

namespace MindSprintAI_ASP.NET_MVC.Controllers;

// Tự cập nhật hồ sơ ở trang Tài khoản (quy tắc trong ProfileService). Lỗi nghiệp vụ: BusinessExceptionFilter đưa về trang trước.
public partial class AccountController
{
    [HttpPost, ValidateAntiForgeryToken, EnableRateLimiting("account-post")]
    public async Task<IActionResult> ChangeName(string? displayName, [FromServices] ProfileService profile)
    {
        await profile.RenameAsync(UserId, displayName);
        Success("Đã đổi tên hiển thị. Lần đổi tiếp theo cách ít nhất 7 ngày.");
        return Redirect(Url.Action(nameof(Index))! + "#sec-profile");
    }

    [HttpPost, ValidateAntiForgeryToken, EnableRateLimiting("account-post"), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> ChangePassword(string? currentPassword, string? newPassword, string? confirmPassword, [FromServices] ProfileService profile)
    {
        await profile.ChangePasswordAsync(UserId, currentPassword, newPassword, confirmPassword, AccountSecurityUrl());
        await RefreshTwoFactorSessionAsync(); // giữ máy đang dùng; SecurityStamp mới làm thiết bị khác bị đăng xuất
        Success("Đã đổi mật khẩu. Các thiết bị khác đã bị đăng xuất.");
        return Redirect(Url.Action(nameof(Index))! + "#sec-password");
    }
}
