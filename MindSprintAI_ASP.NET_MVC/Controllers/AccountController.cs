using System.Text;
using System.Net;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.AspNetCore.RateLimiting;
using MindSprintAI_ASP.NET_MVC.Data.Entities;
using MindSprintAI_ASP.NET_MVC.Models;
using MindSprintAI_ASP.NET_MVC.Services;

namespace MindSprintAI_ASP.NET_MVC.Controllers;

public partial class AccountController(
    UserManager<ApplicationUser> users,
    SignInManager<ApplicationUser> signIn,
    PasswordResetService passwordReset,
    IWebHostEnvironment env,
    LoginService login,
    TurnstileService turnstile,
    AccountLockService locks,
    GuestService guests,
    EmailLinks links,
    AccountEmailService accountEmail) : AppController
{
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeviceChangeBell(bool enabled, [FromServices] ReminderService reminders)
    {
        await reminders.SetLecturerBellEnabledAsync(UserId, NotificationType.DeviceChange, enabled); // (F11)
        Success("Đã lưu tùy chọn chuông khi học viên xin chuyển thiết bị.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ScreenLeaveBell(bool enabled, [FromServices] ReminderService reminders)
    {
        await reminders.SetLecturerBellEnabledAsync(UserId, NotificationType.ScreenLeave, enabled); // (F11)
        Success("Đã lưu tùy chọn chuông khi học viên rời màn hình làm bài.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> SrsBell(bool enabled, [FromServices] ReminderService reminders)
    {
        await reminders.SetSrsBellEnabledAsync(UserId, enabled); // (D5c)
        Success("Đã lưu tùy chọn báo thẻ đến hạn ôn ở chuông.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> EmailReminders(bool enabled, [FromServices] ReminderService reminders)
    {
        await reminders.SetEmailEnabledAsync(UserId, enabled);
        Success("Đã lưu tùy chọn email nhắc việc.");
        return RedirectToAction(nameof(Index));
    }
    private static string Encode(string token) => WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
    private static string Decode(string? code)
    {
        try { return Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code ?? "")); }
        catch (FormatException) { return ""; } // link bị sửa tay → coi như token sai
    }

    // (52, 55) token khôi phục/xác minh chỉ hiện khi thử ngay trên máy phát triển.
    public static bool CanShowDevLink(bool isDevelopment, IPAddress? remoteAddress) =>
        isDevelopment && remoteAddress != null
        && IPAddress.IsLoopback(remoteAddress.IsIPv4MappedToIPv6 ? remoteAddress.MapToIPv4() : remoteAddress);

    private void ShowDevLink(string? link)
    {
        if (CanShowDevLink(env.IsDevelopment(), HttpContext.Connection.RemoteIpAddress) && link != null)
            TempData["DevLink"] = link;
        else TempData.Remove("DevLink");
    }

    [AllowAnonymous, AllowGuest]
    public IActionResult Register()
    {
        RequireAccountChallenge();
        return View(new RegisterModel());
    }

    private void RequireAccountChallenge()
    {
        ViewData["ChallengeRequired"] = true;
        ViewData["TurnstileSiteKey"] = turnstile.Configured ? turnstile.SiteKey : "";
    }

    private async Task<bool> VerifyAccountChallengeAsync()
    {
        RequireAccountChallenge();
        var result = await turnstile.VerifyAsync(Request.Form["cf-turnstile-response"], HttpContext.Connection.RemoteIpAddress?.ToString());
        if (!result.Verified) ModelState.AddModelError("", result.Error!);
        return result.Verified;
    }

    [AllowAnonymous, AllowGuest, HttpPost, EnableRateLimiting(AccountRateLimits.EntryPolicy)]
    public async Task<IActionResult> Register(RegisterModel model)
    {
        if (!AccountRateLimits.TryEmail(model.Email)) { ModelState.AddModelError("", AccountRateLimits.EmailBusy); return View(model); }
        if (!await VerifyAccountChallengeAsync()) return View(model);
        if (!ModelState.IsValid) return View(model);
        // Pháp lý: 2 ô ở đây là của người gõ form; lần đồng ý của chủ email ghi khi họ đặt mật khẩu qua link (SetPasswordAsync).
        var user = new ApplicationUser { UserName = model.Email.Trim(), Email = model.Email.Trim(), DisplayName = model.DisplayName.Trim() };
        var result = await users.CreateAsync(user);
        if (!result.Succeeded)
        {
            var existing = result.Errors.All(e => e.Code is "DuplicateEmail" or "DuplicateUserName")
                ? await users.FindByEmailAsync(model.Email.Trim()) : null;
            if (existing == null)
            {
                foreach (var e in result.Errors) ModelState.AddModelError("", e.Description);
                return View(model);
            }
            user = existing;
        }
        ShowDevLink(await accountEmail.SendRegistrationAsync(user));
        return View("CheckEmail", $"Hãy mở email {model.Email.Trim()} để làm tiếp.");
    }

    [AllowAnonymous, AllowGuest]
    public Task<IActionResult> ConfirmEmail(string userId, string code) =>
        Task.FromResult<IActionResult>(RedirectToAction(nameof(RegisterPassword), new { userId, code }));

    [AllowAnonymous, AllowGuest, HttpGet, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> RegisterPassword(string userId, string code)
    {
        if (!await accountEmail.CanSetPasswordAsync(userId, Decode(code)))
            return View("Message", AccountEmailService.InvalidSetupLink);
        return View("SetPassword", new SetPasswordModel { UserId = userId, Code = code });
    }

    [AllowAnonymous, AllowGuest, HttpPost, EnableRateLimiting(AccountRateLimits.EntryPolicy), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> RegisterPassword(SetPasswordModel model)
    {
        if (!await accountEmail.CanSetPasswordAsync(model.UserId, Decode(model.Code)))
            return View("Message", AccountEmailService.InvalidSetupLink);
        if (!ModelState.IsValid) return View("SetPassword", model);
        try { await accountEmail.SetPasswordAsync(model.UserId, Decode(model.Code), model.Password); }
        catch (BusinessException ex)
        {
            await RecordRejectionAsync(ex);
            if (ex.Message == AccountEmailService.InvalidSetupLink) return View("Message", ex.Message);
            ModelState.AddModelError("", ex.Message);
            return View("SetPassword", model);
        }
        ViewData["ShowLoginLink"] = true;
        return View("Message", "Đã xác minh email và đặt mật khẩu. Bạn có thể đăng nhập.");
    }

    [AllowAnonymous, AllowGuest, HttpGet]
    public IActionResult ResendConfirmation()
    {
        RequireAccountChallenge();
        return View(model: Request.Query["email"].ToString());
    }

    [AllowAnonymous, AllowGuest, HttpPost, EnableRateLimiting(AccountRateLimits.EntryPolicy)]
    public async Task<IActionResult> ResendConfirmation(string email)
    {
        if (!AccountRateLimits.TryEmail(email)) { ModelState.AddModelError("", AccountRateLimits.EmailBusy); return View("ResendConfirmation", email); }
        if (!await VerifyAccountChallengeAsync()) return View("ResendConfirmation", email);
        var user = await users.FindByEmailAsync((email ?? "").Trim());
        ShowDevLink(user != null && !user.EmailConfirmed ? await accountEmail.SendRegistrationAsync(user) : null);
        return View("CheckEmail", "Nếu email có trong hệ thống và chưa xác minh, link đặt mật khẩu mới đã được gửi.");
    }

    [AllowAnonymous, AllowGuest, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Login(string? returnUrl) => View(new LoginModel { ReturnUrl = returnUrl });

    [AllowAnonymous, AllowGuest, HttpPost, EnableRateLimiting(AccountRateLimits.EntryPolicy), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Login(LoginModel model)
    {
        if (!ModelState.IsValid) return View(model);
        if (!AccountRateLimits.TryEmail(model.Email)) { ModelState.AddModelError("", AccountRateLimits.EmailBusy); return View(model); }
        var result = await login.LoginAsync(model.Email, model.Password, model.RememberMe,
            Request.Form["cf-turnstile-response"], HttpContext.Connection.RemoteIpAddress?.ToString(), guests,
            Url.IsLocalUrl(model.ReturnUrl) ? model.ReturnUrl : "/");
        if (result.RequiresTwoFactor) return RedirectToAction(nameof(LoginTwoFactor));
        if (result.Succeeded)
        {
            PostLoginMessage(result);
            return LocalRedirect(Url.IsLocalUrl(model.ReturnUrl) ? model.ReturnUrl! : "/");
        }
        ViewData["ChallengeRequired"] = result.ChallengeRequired;
        ViewData["TurnstileSiteKey"] = turnstile.Configured ? turnstile.SiteKey : "";
        ModelState.AddModelError("", result.Error!);
        return View(model);
    }

    [HttpPost, AllowGuest, AllowWithoutPolicy]
    public async Task<IActionResult> Logout([FromServices] AccountLifecycleService lifecycle)
    {
        await lifecycle.LogoutAsync(UserId);
        return Redirect("/");
    }

    // Pháp lý C2: tài khoản tạo bằng Google lần đầu, khách cũ, hoặc chính sách vừa tăng phiên bản.
    [HttpGet, AllowGuest, AllowWithoutPolicy]
    public async Task<IActionResult> AcceptPolicy(string? returnUrl)
    {
        var user = await users.GetUserAsync(User);
        if (user == null) return RedirectToAction(nameof(Login));
        if (user.PolicyVersion >= LegalPolicy.CurrentVersion) return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl! : "/");
        return View(new AcceptPolicyModel { ReturnUrl = returnUrl, Updated = user.PolicyVersion != null });
    }

    [HttpPost, AllowGuest, AllowWithoutPolicy]
    public async Task<IActionResult> AcceptPolicy(AcceptPolicyModel model)
    {
        var user = await users.GetUserAsync(User);
        if (user == null) return RedirectToAction(nameof(Login));
        if (!ModelState.IsValid) { model.Updated = user.PolicyVersion != null; return View(model); }
        user.PolicyAcceptedAt = DateTime.UtcNow;
        user.PolicyVersion = LegalPolicy.CurrentVersion;
        await users.UpdateAsync(user); // claim cũ còn tới lần kiểm phiên kế tiếp; cổng tự hỏi lại DB nên mở ngay
        return LocalRedirect(Url.IsLocalUrl(model.ReturnUrl) ? model.ReturnUrl! : "/");
    }

    [AllowAnonymous]
    public IActionResult ForgotPassword()
    {
        RequireAccountChallenge();
        return View();
    }

    [AllowAnonymous, HttpPost, EnableRateLimiting(AccountRateLimits.EntryPolicy)]
    public async Task<IActionResult> ForgotPassword(string email)
    {
        if (!AccountRateLimits.TryEmail(email)) { ModelState.AddModelError("", AccountRateLimits.EmailBusy); return View("ForgotPassword", email); }
        if (!await VerifyAccountChallengeAsync()) return View("ForgotPassword", email);
        var link = await passwordReset.RequestAsync(email ?? "", (userId, token, nonce) =>
            links.To(Url.Action(nameof(ResetPassword), "Account", new { userId, code = Encode(token), nonce })!));
        ShowDevLink(link);
        // Không cho biết email có tồn tại hay không.
        return View("CheckEmail", "Nếu email có trong hệ thống, link đặt lại mật khẩu (hạn 30 phút) đã được gửi.");
    }

    [AllowAnonymous]
    public IActionResult ResetPassword(string userId, string code, string nonce) =>
        View(new ResetPasswordModel { UserId = userId, Code = code, Nonce = nonce });

    [AllowAnonymous, HttpPost]
    public async Task<IActionResult> ResetPassword(ResetPasswordModel model)
    {
        if (!ModelState.IsValid)
        {
            ViewData["LinkInvalid"] = new[] { nameof(model.UserId), nameof(model.Code), nameof(model.Nonce) }
                .Any(field => ModelState.TryGetValue(field, out var entry) && entry.Errors.Count > 0);
            return View(model);
        }
        bool removedLogins;
        try
        {
            removedLogins = await passwordReset.ResetAsync(model.UserId, Decode(model.Code), model.Nonce, model.Password);
        }
        catch (BusinessException ex)
        { await RecordRejectionAsync(ex);
            ViewData["LinkInvalid"] = ex.Message.StartsWith("Link", StringComparison.Ordinal)
                || ex.Message.Contains(users.ErrorDescriber.InvalidToken().Description, StringComparison.Ordinal);
            ModelState.AddModelError("", ex.Message);
            return View(model);
        }
        return View("Message", removedLogins
            ? "Đã đặt lại mật khẩu. Để bảo vệ tài khoản, các liên kết đăng nhập bằng Google/Microsoft đã được gỡ; bạn có thể nối lại trong Tài khoản."
            : "Đã đặt lại mật khẩu. Các thiết bị khác đã bị đăng xuất. Bạn có thể đăng nhập bằng mật khẩu mới.");
    }

    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Index([FromServices] UserApiKeyService keys, [FromServices] TwoFactorService twoFactor, [FromServices] PasskeyService passkeys,
        [FromServices] ExternalLoginService external, [FromServices] DeviceSessionService devices,
        [FromServices] AccountDeletionService deletion, [FromServices] ProfileService profile)
    {
        ViewData["DeleteBlock"] = await deletion.BlockReasonAsync(UserId);
        ViewData["GeminiLastFour"] = await keys.LastFourAsync(UserId);
        ViewData["GeminiNoticeAccepted"] = await keys.NoticeAcceptedAsync(UserId);
        var user = (await users.GetUserAsync(User))!;
        ViewData["TwoFactor"] = await twoFactor.StatusAsync(user);
        ViewData["Passkeys"] = await passkeys.ListAsync(user);
        ViewData["ExternalLogins"] = await external.PageAsync(user);
        ViewData["Devices"] = await devices.ListAsync(UserId, User); // (T6)
        ViewData["NextRenameAt"] = profile.NextRenameAt(user);
        ViewData["HasPassword"] = await users.HasPasswordAsync(user);
        return View(user);
    }

    [HttpPost, ValidateAntiForgeryToken, EnableRateLimiting("account-post")]
    public async Task<IActionResult> DeleteDevice(long deviceId, [FromServices] DeviceSessionService devices)
    {
        await devices.DeleteAsync(UserId, User, deviceId); // (T7) quyền và chặn xóa phiên hiện tại ở service.
        Success("Đã xóa thiết bị. Thiết bị đó sẽ bị đăng xuất trong khoảng 1 phút.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken, EnableRateLimiting("account-post")]
    public async Task<IActionResult> SignOutOtherDevices([FromServices] DeviceSessionService devices)
    {
        await devices.DeleteAsync(UserId, User, null); // (T9)
        Success("Đã xóa các thiết bị khác. Các thiết bị đó sẽ bị đăng xuất trong khoảng 1 phút.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> RequestLock()
    {
        ShowDevLink(await locks.RequestProtectiveAsync(UserId, (userId, token, nonce) =>
            links.To(Url.Action(nameof(ConfirmLock), "Account", new { userId, code = Encode(token), nonce })!)));
        return View("CheckEmail", "Link xác nhận khóa bảo vệ (hạn 30 phút) đã được gửi tới email của bạn.");
    }

    // (52) GET chỉ mở form: trình quét link email không được tự khóa tài khoản.
    [AllowAnonymous, HttpGet]
    public IActionResult ConfirmLock(string userId, string code, string nonce) => View(new LockConfirmationModel(userId, code, nonce));

    [AllowAnonymous, HttpPost]
    public async Task<IActionResult> ConfirmLock(LockConfirmationModel model)
    {
        if (!ModelState.IsValid) return View("Message", "Link xác nhận khóa không hợp lệ hoặc đã hết hạn.");
        await locks.ConfirmProtectiveAsync(model.UserId, Decode(model.Code), model.Nonce);
        await signIn.SignOutAsync();
        return View("Message", "Tài khoản đang tạm khóa để bảo vệ. Bạn có thể dùng Quên mật khẩu để mở khóa.");
    }

    // Chỉ đổi màn hình chính, không đổi quyền (38A).
    [HttpPost]
    public async Task<IActionResult> SwitchMode()
    {
        var user = await users.GetUserAsync(User);
        if (user == null) return Redirect("/");
        user.CurrentMode = user.CurrentMode == UserMode.Lecturer ? UserMode.Learner : UserMode.Lecturer;
        await users.UpdateAsync(user);
        return Redirect("/");
    }
}
