using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MindSprintAI_ASP.NET_MVC.Models;
using MindSprintAI_ASP.NET_MVC.Services;

namespace MindSprintAI_ASP.NET_MVC.Controllers;

public partial class AccountController
{
    private void PostLoginMessage(LoginOutcome result)
    {
        if (result.MergedGuest) Success("Đã lưu kết quả làm khi là khách. Nếu bài Kiểm tra đã có lượt, giảng viên sẽ chọn lượt giữ lại.");
    }
    private async Task<IActionResult> PendingExpiredAsync()
    {
        if (HttpMethods.IsPost(Request.Method)) await login.RecordFailedAsync(); // (NK3)
        await HttpContext.SignOutAsync(IdentityConstants.TwoFactorUserIdScheme);
        await HttpContext.SignOutAsync(TwoFactorService.PasskeyPendingScheme);
        TempData["Error"] = "Đã hết thời gian xác nhận danh tính. Hãy đăng nhập lại.";
        return RedirectToAction(nameof(Login));
    }
    private async Task<TwoFactorLoginPage> LoginPageAsync(PendingTwoFactor pending, TwoFactorService twoFactor) =>
        new(await TwoFactorService.HasAuthenticatorAsync(users, pending.User), await twoFactor.EmailAllowedAsync(pending.User),
            (await users.GetPasskeysAsync(pending.User)).Count > 0, await users.CountRecoveryCodesAsync(pending.User) > 0);

    [AllowAnonymous, AllowGuest, HttpGet, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> LoginTwoFactor([FromServices] TwoFactorService twoFactor)
    {
        var pending = await twoFactor.PendingAsync();
        return pending == null ? await PendingExpiredAsync() : View(await LoginPageAsync(pending, twoFactor));
    }

    [AllowAnonymous, AllowGuest, HttpPost, EnableRateLimiting(AccountRateLimits.EntryPolicy), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> LoginTwoFactor(string method, string? code, [FromServices] TwoFactorService twoFactor)
    {
        var pending = await twoFactor.PendingAsync();
        if (pending == null) return await PendingExpiredAsync();
        // Không đưa mã người dùng vừa nhập trở lại HTML qua ModelState.
        ModelState.Clear();
        try
        {
            if (await twoFactor.VerifyLoginAsync(pending, method, code))
            {
                var result = await login.CompleteAsync(pending.User, pending.Remember, guests, pending.SecurityStamp, method);
                if (result.Succeeded)
                {
                    PostLoginMessage(result);
                    return LocalRedirect(Url.IsLocalUrl(pending.ReturnUrl) ? pending.ReturnUrl : "/");
                }
                await HttpContext.SignOutAsync(IdentityConstants.TwoFactorUserIdScheme);
                await HttpContext.SignOutAsync(TwoFactorService.PasskeyPendingScheme);
                TempData["Error"] = result.Error;
                return RedirectToAction(nameof(Login));
            }
            ModelState.AddModelError("", TwoFactorService.InvalidCode);
        }
        catch (ForbiddenException ex) { await RecordRejectionAsync(ex); return await PendingExpiredAsync(); }
        return View(await LoginPageAsync(pending, twoFactor));
    }

    [AllowAnonymous, AllowGuest, HttpPost, EnableRateLimiting(AccountRateLimits.EntryPolicy), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> SendTwoFactorEmail([FromServices] TwoFactorService twoFactor)
    {
        var pending = await twoFactor.PendingAsync();
        if (pending == null) return await PendingExpiredAsync();
        try { await twoFactor.SendEmailAsync(pending.User.Id); Success("Đã gửi mã qua email. Mã hết hạn sau 5 phút. Không thấy thư thì xem mục Thư rác (Spam)."); }
        catch (BusinessException ex) { await RecordRejectionAsync(ex); TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(LoginTwoFactor));
    }

    [HttpGet, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> SetupAuthenticator(bool adminRequired, [FromServices] TwoFactorService twoFactor)
    {
        ViewData["AdminRequired"] = adminRequired;
        // App đã bật mà mở lại trang (F5, Quay lại): về Tài khoản. Để BusinessExceptionFilter quay về Referer
        // chính là trang này sẽ thành vòng chuyển hướng vô tận.
        var current = await users.GetUserAsync(User);
        if (current != null && await TwoFactorService.HasAuthenticatorAsync(users, current))
        {
            TempData["Error"] = "Ứng dụng xác thực đã được bật. Muốn đổi sang điện thoại hoặc ứng dụng khác, hãy bấm Đặt lại ứng dụng xác thực.";
            return RedirectToAction(nameof(Index));
        }
        var model = await twoFactor.SetupAsync(UserId);
        await RefreshTwoFactorSessionAsync();
        return View(model);
    }

    [HttpPost, EnableRateLimiting("account-post"), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> SetupAuthenticator(string? code, [FromServices] TwoFactorService twoFactor)
    {
        ModelState.Clear();
        try
        {
            var codes = await twoFactor.ConfirmSetupAsync(UserId, code, AccountSecurityUrl());
            await RefreshTwoFactorSessionAsync();
            return View("RecoveryCodes", codes);
        }
        catch (BusinessException ex)
        { await RecordRejectionAsync(ex);
            ModelState.AddModelError("", ex.Message);
            return View(await twoFactor.SetupAsync(UserId));
        }
    }

    private string AccountSecurityUrl() => links.To(Url.Action(nameof(Index), "Account")!);
    private async Task RefreshTwoFactorSessionAsync()
    {
        await signIn.ForgetTwoFactorClientAsync();
        var ticket = await HttpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        // (T7) giữ phiên khi thay đổi 2FA cấp lại cookie với security stamp mới.
        var claims = User.Claims.Where(c => c.Type is TwoFactorService.SignInTimeClaim or DeviceSession.Claim or "amr" or System.Security.Claims.ClaimTypes.AuthenticationMethod);
        await signIn.SignInWithClaimsAsync((await users.GetUserAsync(User))!, ticket.Properties ?? new(), claims);
    }
    private async Task<IActionResult> ChangeTwoFactorAsync(TwoFactorChange change, string? password, string? code, TwoFactorService twoFactor)
    {
        ModelState.Clear();
        var codes = await twoFactor.ChangeAsync(UserId, change, password, code, AccountSecurityUrl());
        await RefreshTwoFactorSessionAsync();
        if (codes.Length > 0) return View("RecoveryCodes", codes);
        Success(change == TwoFactorChange.ResetAuthenticator
            ? "Đã tắt ứng dụng xác thực cũ. Hãy quét mã mới và nhập mã 6 số để bật lại; chưa xong bước này thì tài khoản chưa có xác thực 2 lớp bằng ứng dụng."
            : "Đã cập nhật xác thực 2 lớp.");
        return change == TwoFactorChange.ResetAuthenticator ? RedirectToAction(nameof(SetupAuthenticator)) : RedirectToAction(nameof(Index));
    }

    [HttpPost, EnableRateLimiting("account-post"), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public Task<IActionResult> SetEmailTwoFactor(bool enabled, string? password, string? code, [FromServices] TwoFactorService twoFactor) =>
        ChangeTwoFactorAsync(enabled ? TwoFactorChange.EnableEmail : TwoFactorChange.DisableEmail, password, code, twoFactor);

    [HttpPost, EnableRateLimiting("account-post"), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public Task<IActionResult> ResetAuthenticator(string? password, string? code, [FromServices] TwoFactorService twoFactor) =>
        ChangeTwoFactorAsync(TwoFactorChange.ResetAuthenticator, password, code, twoFactor);

    [HttpPost, EnableRateLimiting("account-post"), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public Task<IActionResult> RegenerateRecoveryCodes(string? password, string? code, [FromServices] TwoFactorService twoFactor) =>
        ChangeTwoFactorAsync(TwoFactorChange.Regenerate, password, code, twoFactor);

    [HttpPost, EnableRateLimiting("account-post"), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public Task<IActionResult> DisableTwoFactor(string? password, string? code, [FromServices] TwoFactorService twoFactor) =>
        ChangeTwoFactorAsync(TwoFactorChange.Disable, password, code, twoFactor);
}
