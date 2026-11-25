using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MindSprintAI_ASP.NET_MVC.Services;

namespace MindSprintAI_ASP.NET_MVC.Controllers;

public partial class AccountController
{
    [HttpPost, ValidateAntiForgeryToken, EnableRateLimiting("account-post"), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> PasskeyCreationOptions([FromServices] PasskeyService passkeys) =>
        Content(await passkeys.CreationOptionsAsync(UserId), "application/json");

    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(300000), EnableRateLimiting("account-post"), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> RegisterPasskey(string? name, string? credentialJson, [FromServices] PasskeyService passkeys)
    {
        await passkeys.RegisterAsync(UserId, name, credentialJson, AccountSecurityUrl());
        await RefreshTwoFactorSessionAsync();
        Success("Đã thêm passkey.");
        return Json(new { redirect = Url.Action(nameof(Index)) });
    }

    [HttpPost, ValidateAntiForgeryToken, EnableRateLimiting("account-post"), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> RenamePasskey(string? id, string? name, [FromServices] PasskeyService passkeys)
    {
        await passkeys.RenameAsync(UserId, id, name);
        Success("Đã đổi tên passkey.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken, EnableRateLimiting("account-post"), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> DeletePasskey(string? id, string? password, string? code, [FromServices] PasskeyService passkeys)
    {
        await passkeys.DeleteAsync(UserId, id, password, code, AccountSecurityUrl());
        await RefreshTwoFactorSessionAsync();
        Success("Đã xóa passkey.");
        return RedirectToAction(nameof(Index));
    }

    [AllowAnonymous, AllowGuest, HttpPost, ValidateAntiForgeryToken, EnableRateLimiting(AccountRateLimits.EntryPolicy), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> PasskeyRequestOptions([FromServices] PasskeyService passkeys) =>
        Content(await passkeys.RequestOptionsAsync(false), "application/json");

    [AllowAnonymous, AllowGuest, HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(300000), EnableRateLimiting(AccountRateLimits.EntryPolicy), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> PasskeyLogin(string? credentialJson, bool rememberMe, string? returnUrl, [FromServices] PasskeyService passkeys)
    {
        var result = await passkeys.LoginAsync(credentialJson, false, rememberMe, guests);
        if (!result.Outcome.Succeeded) return BadRequest(new { error = result.Outcome.Error });
        PostLoginMessage(result.Outcome);
        return Json(new { redirect = Url.IsLocalUrl(returnUrl) ? returnUrl : "/" });
    }

    [AllowAnonymous, AllowGuest, HttpPost, ValidateAntiForgeryToken, EnableRateLimiting(AccountRateLimits.EntryPolicy), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> TwoFactorPasskeyOptions([FromServices] PasskeyService passkeys) =>
        Content(await passkeys.RequestOptionsAsync(true), "application/json");

    [AllowAnonymous, AllowGuest, HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(300000), EnableRateLimiting(AccountRateLimits.EntryPolicy), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> TwoFactorPasskeyLogin(string? credentialJson, [FromServices] PasskeyService passkeys)
    {
        var result = await passkeys.LoginAsync(credentialJson, true, false, guests);
        if (!result.Outcome.Succeeded) return BadRequest(new { error = result.Outcome.Error });
        PostLoginMessage(result.Outcome);
        return Json(new { redirect = Url.IsLocalUrl(result.ReturnUrl) ? result.ReturnUrl : "/" });
    }
}
