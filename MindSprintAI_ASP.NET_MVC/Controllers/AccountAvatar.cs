using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MindSprintAI_ASP.NET_MVC.Services;

namespace MindSprintAI_ASP.NET_MVC.Controllers;

public partial class AccountController
{
    [HttpPost, ValidateAntiForgeryToken, EnableRateLimiting("account-post"), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> GoogleAvatarVisibility(bool enabled, [FromServices] AvatarService avatars)
    {
        await avatars.SetVisibleAsync(UserId, enabled);
        Success("Đã lưu tùy chọn ảnh đại diện.");
        return RedirectToAction(nameof(Index));
    }

    [Authorize, HttpPost, ValidateAntiForgeryToken, EnableRateLimiting("account-post"), RequestSizeLimit(AvatarService.MaxUploadBytes + 100_000)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> UploadAvatar(IFormFile? image, [FromServices] AvatarService avatars)
    {
        await avatars.UploadAsync(UserId, image);
        Success("Đã đổi ảnh đại diện.");
        return RedirectToAction(nameof(Index));
    }

    // Chỉ chủ tài khoản xem được ảnh của mình (ảnh chỉ hiện ở thanh trên và trang Tài khoản). ?v= đổi khi đổi ảnh nên trình duyệt giữ lâu được.
    [Authorize, HttpGet, ResponseCache(Duration = 86400, Location = ResponseCacheLocation.Client)]
    public async Task<IActionResult> AvatarImage([FromServices] AvatarService avatars) =>
        await avatars.ReadAsync(UserId) is { } image ? File(image.Bytes, image.Type) : NotFound();

    [HttpPost, ValidateAntiForgeryToken, EnableRateLimiting("account-post"), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> RemoveAvatar([FromServices] AvatarService avatars)
    {
        await avatars.RemoveAsync(UserId);
        Success("Đã gỡ ảnh đại diện.");
        return RedirectToAction(nameof(Index));
    }
}
