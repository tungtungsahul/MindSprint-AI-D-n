using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MindSprintAI_ASP.NET_MVC.Services;

namespace MindSprintAI_ASP.NET_MVC.Controllers;

public partial class AccountController
{
    [AllowAnonymous, AllowGuest, HttpPost, ValidateAntiForgeryToken, EnableRateLimiting(AccountRateLimits.EntryPolicy), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> ExternalLogin(string provider, string? returnUrl, [FromServices] ExternalLoginService external)
    {
        if (!await external.AvailableAsync(provider)) return BadRequest();
        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
        var callback = Url.Action(nameof(ExternalLoginCallback), new { returnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl : "/" })!;
        return Challenge(signIn.ConfigureExternalAuthenticationProperties(provider, callback), provider);
    }

    [AllowAnonymous, AllowGuest, HttpGet, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> ExternalLoginCallback(string? returnUrl, [FromServices] ExternalLoginService external)
    {
        try
        {
            var info = await signIn.GetExternalLoginInfoAsync();
            if (info == null)
            {
                await external.RecordFailedAsync(); // (NK3)
                throw new BusinessException("Đăng nhập Google/Microsoft bị hủy hoặc đã hết hạn. Hãy thử lại.");
            }
            // Callback đăng nhập không được nhận cookie từ luồng nối tài khoản.
            if (info.AuthenticationProperties?.Items.ContainsKey("XsrfId") == true) return BadRequest();
            var result = await external.SignInAsync(info, guests, Url.IsLocalUrl(returnUrl) ? returnUrl : "/");
            if (result.RequiresTwoFactor) return RedirectToAction(nameof(LoginTwoFactor));
            if (result.Succeeded)
            {
                PostLoginMessage(result);
                return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl! : "/");
            }
            TempData["Error"] = result.Error;
        }
        catch (BusinessException ex) { await RecordRejectionAsync(ex); TempData["Error"] = ex.Message; }
        finally { await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme); }
        return RedirectToAction(nameof(Login), new { returnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl : "/" });
    }

    [HttpPost, ValidateAntiForgeryToken, EnableRateLimiting("account-post"), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> LinkLogin(string provider, string? password, string? code, [FromServices] ExternalLoginService external)
    {
        if (!await external.AvailableAsync(provider)) return BadRequest();
        var properties = await external.BeginLinkAsync(UserId, provider, password, code, Url.Action(nameof(LinkLoginCallback))!);
        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
        return Challenge(properties, provider);
    }

    [HttpGet, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> LinkLoginCallback([FromServices] ExternalLoginService external)
    {
        try
        {
            var info = await signIn.GetExternalLoginInfoAsync(UserId);
            if (info == null) throw new BusinessException("Việc liên kết cách đăng nhập đã hết thời gian hoặc bị hủy. Hãy thử lại.");
            await external.LinkAsync(UserId, info, AccountSecurityUrl());
            await RefreshTwoFactorSessionAsync();
            Success("Đã nối với " + info.LoginProvider + ".");
        }
        catch (BusinessException ex) { await RecordRejectionAsync(ex); TempData["Error"] = ex.Message; }
        finally { await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme); }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken, EnableRateLimiting("account-post"), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> RemoveLogin(string provider, string providerKey, string? password, string? code, [FromServices] ExternalLoginService external)
    {
        if (!ExternalProviderConfig.Known(provider)) return BadRequest();
        await external.RemoveAsync(UserId, provider, providerKey, password, code, AccountSecurityUrl());
        await RefreshTwoFactorSessionAsync();
        Success("Đã gỡ liên kết đăng nhập.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken, EnableRateLimiting("account-post"), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> SetPassword(string? newPassword, string? password, string? code, [FromServices] ExternalLoginService external)
    {
        await external.SetPasswordAsync(UserId, newPassword, password, code, AccountSecurityUrl());
        await RefreshTwoFactorSessionAsync();
        Success("Đã đặt mật khẩu. Các thiết bị khác sẽ bị đăng xuất.");
        return RedirectToAction(nameof(Index));
    }
}
