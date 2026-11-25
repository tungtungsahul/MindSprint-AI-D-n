using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MindSprintAI_ASP.NET_MVC.Data.Entities;
using MindSprintAI_ASP.NET_MVC.Services;

namespace MindSprintAI_ASP.NET_MVC.Controllers;

public partial class AccountController
{
    public const string DemoEmail = "demo@demo.local";

    // "Dùng thử tài khoản demo" của nhóm (AuthController.DemoLogin: chỉ khi KHÔNG phải production). Ở đây chặt hơn:
    // chỉ môi trường Development và chỉ truy cập từ chính máy (như link thử email); không có mật khẩu viết sẵn trong mã.
    public static bool CanUseDemo(bool isDevelopment, System.Net.IPAddress? remoteAddress) => CanShowDevLink(isDevelopment, remoteAddress);

    [AllowAnonymous, HttpPost, ValidateAntiForgeryToken, EnableRateLimiting("account-post"), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> DemoLogin([FromServices] TimeProvider clock, [FromServices] DemoSeedService seed, [FromServices] LoginService login)
    {
        if (!CanUseDemo(env.IsDevelopment(), HttpContext.Connection.RemoteIpAddress)) return NotFound();
        var user = await users.FindByEmailAsync(DemoEmail);
        if (user == null)
        {
            user = new ApplicationUser { UserName = DemoEmail, Email = DemoEmail, EmailConfirmed = true, DisplayName = "Tài khoản demo",
                CreatedAt = clock.GetUtcNow().UtcDateTime };
            var created = await users.CreateAsync(user);
            if (!created.Succeeded) throw new BusinessException("Chưa tạo được tài khoản demo. Hãy thử lại.");
        }
        await seed.SeedIfEmptyAsync(user.Id); // chưa có bộ học nào (mới tạo, hoặc tạo trước khi có dữ liệu mẫu) → thêm dữ liệu mẫu
        // Đi qua bước cuối chung của mọi cách đăng nhập (mật khẩu, Google, passkey): kiểm khóa tài khoản, đăng ký phiên thiết bị
        // (claim DeviceSession), ghi thời điểm đăng nhập và nhật ký. Trước đây gọi thẳng SignInAsync → thiếu phiên thiết bị →
        // kênh thông báo trực tiếp (DeviceSessionHubFilter) coi như đã bị đăng xuất và từ chối (lỗi gặp 15/11).
        var outcome = await login.CompleteAsync(user, remember: false);
        if (!outcome.Succeeded) throw new BusinessException(outcome.Error ?? "Chưa đăng nhập được tài khoản demo. Hãy thử lại.");
        return RedirectToAction("Index", "Home");
    }
}
