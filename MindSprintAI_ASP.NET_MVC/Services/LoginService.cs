using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.AspNetCore.Authentication;
using System.Security.Claims;
using MindSprintAI_ASP.NET_MVC.Data.Entities;

namespace MindSprintAI_ASP.NET_MVC.Services;

public record LoginOutcome(bool Succeeded, bool ChallengeRequired, string? Error = null, bool RequiresTwoFactor = false, bool MergedGuest = false);

public class LoginService(UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn,
    IMemoryCache cache, TurnstileService turnstile, AccountLockService locks, TimeProvider clock, NewDeviceService? devices = null,
    AuditLog? audit = null)
{
    internal const string Invalid = "Email hoặc mật khẩu không đúng, hoặc tài khoản đang tạm khóa. Bạn vẫn có thể dùng Quên mật khẩu.";
    private const string Unconfirmed = "Email chưa được xác minh. Hãy dùng Gửi lại email xác minh.";
    private static readonly object CacheGate = new();
    private static readonly ApplicationUser UnknownUser = new();
    public Task RecordFailedAsync() => audit?.WriteSeparateAsync(AuditAction.LoginFailed, "User", "", result: AuditResult.Failed) ?? Task.CompletedTask; // (NK3)
    private static readonly string UnknownHash = new PasswordHasher<ApplicationUser>().HashPassword(UnknownUser, "Unknown-email-placeholder!73");
    private sealed class UnknownFailures
    {
        public int Count { get; set; }
        public DateTime? LockedUntil { get; set; }
    }

    public async Task<LoginOutcome> LoginAsync(string email, string password, bool remember, string? token, string? remoteIp,
        GuestService? guests = null, string? returnUrl = null)
    {
        var outcome = await LoginCoreAsync(email, password, remember, token, remoteIp, guests, returnUrl);
        if (!outcome.Succeeded && !outcome.RequiresTwoFactor && audit != null)
        {
            var user = await users.FindByEmailAsync(email.Trim());
            await audit.WriteSeparateAsync(AuditAction.LoginFailed, "User", user?.Id ?? "", user?.Id, AuditResult.Failed); // (NK3)
        }
        return outcome;
    }

    private async Task<LoginOutcome> LoginCoreAsync(string email, string password, bool remember, string? token, string? remoteIp,
        GuestService? guests, string? returnUrl)
    {
        var normalized = users.NormalizeEmail(email.Trim());
        var user = await users.FindByEmailAsync(email.Trim());
        // shortcut: in-memory for unknown emails, one server
        UnknownFailures unknown;
        lock (CacheGate)
            unknown = cache.GetOrCreate("login:" + normalized, entry =>
            {
                entry.SlidingExpiration = TimeSpan.FromHours(24);
                return new UnknownFailures();
            })!;
        var now = clock.GetUtcNow().UtcDateTime;
        lock (unknown)
            if (unknown.LockedUntil <= now) { unknown.Count = 0; unknown.LockedUntil = null; }
        if (user?.IsGuest == true) return new(false, false, Invalid);
        var identityLocked = user != null && await users.IsLockedOutAsync(user);
        var failures = user == null ? unknown.Count : user.AccessFailedCount;
        var challenge = failures >= 5 || identityLocked;
        if (challenge)
        {
            var verified = await turnstile.VerifyAsync(token, remoteIp);
            if (!verified.Verified) return new(false, true, verified.Error);
        }
        if (identityLocked || user == null && unknown.LockedUntil > now) return new(false, challenge, Invalid);
        // (55) email lạ cũng tốn một lần băm mật khẩu, tránh lộ tài khoản qua thời gian phản hồi.
        if (user == null) users.PasswordHasher.VerifyHashedPassword(UnknownUser, UnknownHash, password);
        if (user == null || !await users.CheckPasswordAsync(user, password))
        {
            if (user != null)
            {
                var result = await users.AccessFailedAsync(user);
                if (!result.Succeeded) return new(false, true, Invalid);
                challenge = user.AccessFailedCount >= 5 || await users.IsLockedOutAsync(user);
            }
            else lock (unknown)
            {
                unknown.Count++;
                if (unknown.Count >= 10) unknown.LockedUntil = now.AddMinutes(15);
                challenge = unknown.Count >= 5;
            }
            return new(false, challenge, Invalid);
        }
        // (52) chỉ người biết đúng mật khẩu mới thấy lý do khóa/xác minh email.
        var active = await locks.ActiveAsync(user.Id);
        if (active != null) return new(false, challenge, AccountLockService.MessageForUser(active));
        if (!user.EmailConfirmed) return new(false, challenge, Unconfirmed);
        // Không reset bộ đếm ở bước mật khẩu: sai mã 2FA phải cộng dồn với sai mật khẩu.
        var needsCode = await TwoFactorService.NeedsSecondStepAsync(users, user);
        // Không có "nhớ máy" (người dùng chốt 2026-11-06): máy dính mã độc thì cookie nhớ máy cho kẻ gian bỏ qua lớp 2.
        if (needsCode)
        {
            await TwoFactorService.BeginAsync(signIn, user, remember, returnUrl, clock.GetUtcNow());
            return new(false, false, RequiresTwoFactor: true);
        }
        cache.Remove("login:" + normalized);
        return await CompleteAsync(user, remember, guests, expectedSecurityStamp: user.SecurityStamp);
    }

    // Hook 6c: mọi cách đăng nhập hoàn tất tại đây, gồm kiểm khóa 5b và gộp khách 47.
    public async Task<LoginOutcome> CompleteAsync(ApplicationUser user, bool remember, GuestService? guests = null,
        string? expectedSecurityStamp = null, string? verifiedMethod = null)
    {
        var outcome = await CompleteCoreAsync(user, remember, guests, expectedSecurityStamp, verifiedMethod);
        if (audit != null) await audit.WriteSeparateAsync(outcome.Succeeded ? AuditAction.LoginSucceeded : AuditAction.LoginFailed,
            "User", user.Id, user.Id, outcome.Succeeded ? AuditResult.Success : AuditResult.Failed); // (NK3) mọi cách đăng nhập.
        return outcome;
    }

    private async Task<LoginOutcome> CompleteCoreAsync(ApplicationUser user, bool remember, GuestService? guests,
        string? expectedSecurityStamp, string? verifiedMethod)
    {
        await locks.RefreshUserAsync(user);
        var active = await locks.ActiveAsync(user.Id);
        if (active != null) return new(false, false, AccountLockService.MessageForUser(active));
        if (user.IsGuest || await users.IsLockedOutAsync(user)
            || expectedSecurityStamp != null && expectedSecurityStamp != user.SecurityStamp) return new(false, false, Invalid);
        if (!user.EmailConfirmed) return new(false, false, Unconfirmed);
        if (user.IsSystemAdmin && (verifiedMethod == "email" || verifiedMethod == null && await TwoFactorService.HasStrongMethodAsync(users, user)))
            return new(false, false, "Quyền tài khoản vừa thay đổi. Hãy đăng nhập lại để xác thực quản trị viên.");
        await locks.ExpireAsync(user.Id);
        var merged = guests != null && await guests.MergeAsync(signIn.Context.Request.Cookies[GuestService.CookieName], user.Id);
        await locks.RefreshUserAsync(user);
        active = await locks.ActiveAsync(user.Id);
        if (active != null) return new(false, false, AccountLockService.MessageForUser(active));
        if (user.IsGuest || await users.IsLockedOutAsync(user)
            || expectedSecurityStamp != null && expectedSecurityStamp != user.SecurityStamp) return new(false, false, Invalid);
        if (!user.EmailConfirmed) return new(false, false, Unconfirmed);
        if (user.IsSystemAdmin && (verifiedMethod == "email" || verifiedMethod == null && await TwoFactorService.HasStrongMethodAsync(users, user)))
            return new(false, false, "Quyền tài khoản vừa thay đổi. Hãy đăng nhập lại để xác thực quản trị viên.");
        if (!(await users.ResetAccessFailedCountAsync(user)).Succeeded) return new(false, false, Invalid);
        if (GuestAccess.IsGuest(signIn.Context.User)) await signIn.SignOutAsync();
        await signIn.Context.SignOutAsync(IdentityConstants.TwoFactorUserIdScheme);
        await signIn.Context.SignOutAsync(TwoFactorService.PasskeyPendingScheme);
        var claims = new List<Claim> { new(TwoFactorService.SignInTimeClaim,
            clock.GetUtcNow().ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture)) };
        if (verifiedMethod != null) claims.Add(new("amr", "mfa"));
        // (T5, T7) hook chung sau mọi kiểm tra đăng nhập, trước khi cấp cookie cuối cùng.
        if (devices != null)
        {
            var deviceId = await devices.SeenAsync(user, signIn.Context);
            if (deviceId == null) return new(false, false, "Chưa hoàn tất đăng nhập trên thiết bị này. Hãy thử lại.");
            claims.Add(new(DeviceSession.Claim, deviceId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }
        await signIn.SignInWithClaimsAsync(user, remember, claims);
        await signIn.ForgetTwoFactorClientAsync(); // xóa cookie nhớ máy còn sót từ bản trước
        if (merged) signIn.Context.Response.Cookies.Delete(GuestService.CookieName);
        cache.Remove("login:" + users.NormalizeEmail(user.Email!));
        return new(true, false, MergedGuest: merged);
    }
}
