using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MindSprintAI_ASP.NET_MVC.Data;
using MindSprintAI_ASP.NET_MVC.Data.Entities;
using MindSprintAI_ASP.NET_MVC.Models;

namespace MindSprintAI_ASP.NET_MVC.Services;

public enum TwoFactorChange { EnableEmail, DisableEmail, Disable, ResetAuthenticator, Regenerate }
public record PendingTwoFactor(ApplicationUser User, bool Remember, string ReturnUrl, DateTimeOffset ExpiresAt, string SecurityStamp);

public class TwoFactorService(AppDbContext db, UserManager<ApplicationUser> users,
    SignInManager<ApplicationUser> signIn, IEmailSender email, TimeProvider clock)
{
    public const string InvalidCode = "Mã không đúng hoặc đã hết hạn.";
    public const string Provider = "QuizTwoFactor";
    public const string PasskeyPendingScheme = "QuizPasskeyPending";
    public const string SignInTimeClaim = "qem:sign-in-time";
    public const string RecentSignInRequired = "Hãy đăng xuất rồi đăng nhập lại, sau đó thử lại trong 10 phút.";
    public static bool IsRecentSignIn(ClaimsPrincipal principal, string userId, DateTimeOffset now) =>
        principal.Identity?.IsAuthenticated == true && principal.FindFirstValue(ClaimTypes.NameIdentifier) == userId
        && long.TryParse(principal.FindFirstValue(SignInTimeClaim), NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds)
        && seconds <= now.ToUnixTimeSeconds() && seconds >= now.ToUnixTimeSeconds() - 600;

    // Refresh cookie/stamp không được biến phiên cũ thành một lần đăng nhập mới.
    public static void PreserveSignInTime(ClaimsPrincipal? current, ClaimsPrincipal? renewed)
    {
        if (current != null && renewed != null
            && current.FindFirstValue(ClaimTypes.NameIdentifier) == renewed.FindFirstValue(ClaimTypes.NameIdentifier)
            && renewed.FindFirst(SignInTimeClaim) == null && current.FindFirst(SignInTimeClaim) is { } claim)
            ((ClaimsIdentity)renewed.Identity!).AddClaim(claim);
    }
    public static readonly TimeSpan EmailLifetime = TimeSpan.FromMinutes(5);
    private DateTimeOffset Now => clock.GetUtcNow();
    // Bỏ khoảng trắng/gạch ngang: người dùng hay gõ "ABCDE FGHIJ" hoặc thiếu dấu "-" của "ABCDE-FGHIJ".
    public static string HashRecovery(string code) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        new string(code.Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray()).ToUpperInvariant())));
    public static string GroupKey(string key) => string.Join(' ', Enumerable.Range(0, (key.Length + 3) / 4).Select(i => key.Substring(i * 4, Math.Min(4, key.Length - i * 4))));
    public static string AuthenticatorUri(string emailAddress, string key) =>
        $"otpauth://totp/{Brand.Name}:{Uri.EscapeDataString(emailAddress)}?secret={key}&issuer={Brand.Name}&digits=6&period=30";
    private Task<string?> TokenAsync(ApplicationUser user, string name) => users.GetAuthenticationTokenAsync(user, Provider, name);
    private async Task SetAsync(ApplicationUser user, string name, string? value) => Check(await users.SetAuthenticationTokenAsync(user, Provider, name, value));
    private static void Check(IdentityResult result)
    {
        if (!result.Succeeded) throw new BusinessException("Tài khoản vừa thay đổi. Hãy tải lại và thử lại.");
    }

    // Có key chưa có nghĩa là đã xác nhận app. Cờ xác nhận là dấu băm của đúng key đã được nhập mã:
    // key bị tạo lại (đặt lại, hoặc key cũ không giải mã được) thì cờ tự hết hiệu lực, không thể "kế thừa" xác nhận cũ.
    public static string ConfirmationOf(string key) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("confirmed:" + key)));
    public static async Task<bool> HasAuthenticatorAsync(UserManager<ApplicationUser> users, ApplicationUser user)
    {
        if (!user.TwoFactorEnabled) return false;
        var key = await users.GetAuthenticatorKeyAsync(user);
        var confirmed = await users.GetAuthenticationTokenAsync(user, Provider, "AuthenticatorConfirmed");
        return !string.IsNullOrEmpty(key) && confirmed != null
            && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(confirmed), Encoding.UTF8.GetBytes(ConfirmationOf(key)));
    }
    public static async Task<bool> HasStrongMethodAsync(UserManager<ApplicationUser> users, ApplicationUser user) =>
        await HasAuthenticatorAsync(users, user) || (await users.GetPasskeysAsync(user)).Count > 0;
    public static async Task<bool> NeedsSecondStepAsync(UserManager<ApplicationUser> users, ApplicationUser user) =>
        user.IsSystemAdmin ? await HasStrongMethodAsync(users, user) : user.TwoFactorEnabled;
    public async Task<bool> EmailAllowedAsync(ApplicationUser user) => !user.IsGuest && !user.IsSystemAdmin && user.EmailConfirmed
        && await TokenAsync(user, "EmailEnabled") == "true";
    public async Task<TwoFactorStatus> StatusAsync(ApplicationUser user) => new(user.TwoFactorEnabled,
        await HasAuthenticatorAsync(users, user), await TokenAsync(user, "EmailEnabled") == "true", user.IsSystemAdmin,
        await users.HasPasswordAsync(user), await users.CountRecoveryCodesAsync(user));

    public static async Task BeginAsync(SignInManager<ApplicationUser> signIn, ApplicationUser user, bool remember, string? returnUrl, DateTimeOffset now)
    {
        await signIn.Context.SignOutAsync(PasskeyPendingScheme);
        var identity = new ClaimsIdentity(IdentityConstants.TwoFactorUserIdScheme);
        identity.AddClaim(new(ClaimTypes.Name, user.Id));
        identity.AddClaim(new(signIn.Options.ClaimsIdentity.SecurityStampClaimType, user.SecurityStamp!));
        var properties = new AuthenticationProperties { IssuedUtc = now, ExpiresUtc = now.AddMinutes(5) };
        properties.Items["remember"] = remember.ToString();
        properties.Items["returnUrl"] = returnUrl;
        await signIn.Context.SignInAsync(IdentityConstants.TwoFactorUserIdScheme, new ClaimsPrincipal(identity), properties);
    }

    public async Task<PendingTwoFactor?> PendingAsync()
    {
        var ticket = await signIn.Context.AuthenticateAsync(IdentityConstants.TwoFactorUserIdScheme);
        // Identity dùng cùng scheme cho challenge WebAuthn. Bản pending riêng giữ nguyên người/đích/stamp.
        if (ticket.Principal?.FindFirstValue(ClaimTypes.Name) == null)
            ticket = await signIn.Context.AuthenticateAsync(PasskeyPendingScheme);
        var id = ticket.Principal?.FindFirstValue(ClaimTypes.Name);
        var user = id == null ? null : await users.FindByIdAsync(id);
        if (user != null) await db.Entry(user).ReloadAsync();
        if (!ticket.Succeeded || ticket.Properties?.ExpiresUtc is not { } end || end <= Now || user == null || user.IsGuest
            || !user.EmailConfirmed || !await NeedsSecondStepAsync(users, user)
            || ticket.Principal!.FindFirstValue(signIn.Options.ClaimsIdentity.SecurityStampClaimType) != user.SecurityStamp) return null;
        ticket.Properties.Items.TryGetValue("remember", out var remember);
        ticket.Properties.Items.TryGetValue("returnUrl", out var returnUrl);
        return new(user, remember == "True", returnUrl ?? "/", end, user.SecurityStamp!);
    }

    private async Task<ApplicationUser> LockUserAsync(string id)
    {
        await AccountLockState.EnsureAllowedAsync(db, id, Now.UtcDateTime);
        var user = await users.FindByIdAsync(id) ?? throw new ForbiddenException();
        await db.Entry(user).ReloadAsync();
        if (user.IsGuest || !user.EmailConfirmed) throw new ForbiddenException();
        return user;
    }

    public async Task<AuthenticatorSetup> SetupAsync(string userId)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        var user = await LockUserAsync(userId);
        if (await HasAuthenticatorAsync(users, user)) throw new BusinessException("Ứng dụng xác thực đã bật. Muốn đổi ứng dụng, hãy dùng Đặt lại ứng dụng xác thực.");
        var key = await users.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrEmpty(key))
        {
            Check(await users.ResetAuthenticatorKeyAsync(user));
            key = (await users.GetAuthenticatorKeyAsync(user))!;
        }
        await tx.CommitAsync();
        return new(GroupKey(key), AuthenticatorUri(user.Email!, key));
    }

    public async Task<string[]> ConfirmSetupAsync(string userId, string? code, string accountUrl)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        var user = await LockUserAsync(userId);
        if (await HasAuthenticatorAsync(users, user)) throw new BusinessException("Ứng dụng xác thực đã bật.");
        if (await users.IsLockedOutAsync(user) || !await UseAppCodeAsync(user, code))
        {
            if (!await users.IsLockedOutAsync(user)) Check(await users.AccessFailedAsync(user));
            await tx.CommitAsync(); throw new BusinessException(InvalidCode);
        }
        await SetAsync(user, "AuthenticatorConfirmed", ConfirmationOf((await users.GetAuthenticatorKeyAsync(user))!));
        Check(await users.SetTwoFactorEnabledAsync(user, true));
        Check(await users.ResetAccessFailedCountAsync(user));
        var codes = await NewRecoveryCodesAsync(user);
        Check(await users.UpdateSecurityStampAsync(user));
        db.Audit?.Add(AuditAction.TwoFactorEnabled, "User", userId, userId); // (NK3)
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        await NotifyAsync(user, "Đã bật ứng dụng xác thực và xác thực 2 lớp", accountUrl);
        return codes;
    }

    private async Task<string[]> NewRecoveryCodesAsync(ApplicationUser user) =>
        (await users.GenerateNewTwoFactorRecoveryCodesAsync(user, 10))?.ToArray()
        ?? throw new BusinessException("Không tạo được mã dự phòng. Hãy thử lại.");

    // Hook 6c: tài khoản không mật khẩu dùng app/email/recovery hiện tại, cùng bộ đếm sai.
    public async Task<bool> ReauthenticateAsync(ApplicationUser user, string? password, string? code)
    {
        if (await users.IsLockedOutAsync(user)) throw new BusinessException("Tài khoản đang tạm khóa vì nhập sai nhiều lần. Hãy thử lại sau ít phút hoặc dùng Quên mật khẩu.");
        if (!await users.HasPasswordAsync(user) && !user.TwoFactorEnabled)
        {
            if (!IsRecentSignIn(signIn.Context.User, user.Id, Now)) throw new BusinessException(RecentSignInRequired);
            return true;
        }
        var valid = await users.HasPasswordAsync(user) ? await users.CheckPasswordAsync(user, password ?? "")
            : user.TwoFactorEnabled && (await VerifyCodeAsync(user, "app", code) || await VerifyCodeAsync(user, "email", code)
                || await VerifyCodeAsync(user, "recovery", code));
        Check(valid ? await users.ResetAccessFailedCountAsync(user) : await users.AccessFailedAsync(user));
        return valid;
    }

    public async Task<string[]> ChangeAsync(string userId, TwoFactorChange change, string? password, string? code, string accountUrl)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        var user = await LockUserAsync(userId);
        if (user.IsSystemAdmin && (change is TwoFactorChange.Disable or TwoFactorChange.ResetAuthenticator or TwoFactorChange.EnableEmail
            || change == TwoFactorChange.DisableEmail && !await HasStrongMethodAsync(users, user)))
            throw new BusinessException("Quản trị viên phải giữ ứng dụng xác thực. Mã qua email không dùng cho quản trị viên.");
        if (change != TwoFactorChange.EnableEmail && !await ReauthenticateAsync(user, password, code))
        {
            await tx.CommitAsync(); throw new BusinessException("Không xác nhận được danh tính. Kiểm tra mật khẩu hoặc mã xác thực 2 lớp hiện tại.");
        }
        var wasEnabled = user.TwoFactorEnabled;
        var hadEmail = await TokenAsync(user, "EmailEnabled") == "true";
        var codes = Array.Empty<string>();
        string notice;
        switch (change)
        {
            case TwoFactorChange.EnableEmail:
                if (await TokenAsync(user, "EmailEnabled") == "true") throw new BusinessException("Mã qua email đã bật.");
                await SetAsync(user, "EmailEnabled", "true");
                Check(await users.SetTwoFactorEnabledAsync(user, true));
                if (!wasEnabled) codes = await NewRecoveryCodesAsync(user);
                notice = "Đã bật mã qua email"; break;
            case TwoFactorChange.DisableEmail:
                await SetAsync(user, "EmailEnabled", null);
                await SetAsync(user, "EmailChallenge", null);
                Check(await users.SetTwoFactorEnabledAsync(user, await HasStrongMethodAsync(users, user)));
                notice = "Đã tắt mã qua email"; break;
            case TwoFactorChange.Disable:
                Check(await users.SetTwoFactorEnabledAsync(user, false));
                await SetAsync(user, "EmailEnabled", null);
                await SetAsync(user, "EmailChallenge", null);
                await SetAsync(user, "AuthenticatorConfirmed", null);
                await ClearRecoveryCodesAsync(user);
                notice = "Đã tắt xác thực 2 lớp và các phương thức"; break;
            case TwoFactorChange.ResetAuthenticator:
                await SetAsync(user, "AuthenticatorConfirmed", null);
                Check(await users.ResetAuthenticatorKeyAsync(user));
                Check(await users.SetTwoFactorEnabledAsync(user, await EmailAllowedAsync(user)));
                await ClearRecoveryCodesAsync(user);
                notice = "Đã đặt lại ứng dụng xác thực"; break;
            case TwoFactorChange.Regenerate:
                if (!user.TwoFactorEnabled) throw new BusinessException("Hãy bật xác thực 2 lớp trước.");
                codes = await NewRecoveryCodesAsync(user);
                notice = "Đã tạo lại mã dự phòng"; break;
            default: throw new ForbiddenException();
        }
        Check(await users.UpdateSecurityStampAsync(user));
        var auditAction = change switch // (NK3) Tắt email không có nghĩa là tắt toàn bộ xác thực 2 lớp.
        {
            TwoFactorChange.EnableEmail => AuditAction.TwoFactorEmailEnabled,
            TwoFactorChange.DisableEmail => AuditAction.TwoFactorEmailDisabled,
            TwoFactorChange.Regenerate => AuditAction.RecoveryRegenerated,
            TwoFactorChange.ResetAuthenticator => AuditAction.AuthenticatorReset,
            _ => AuditAction.TwoFactorDisabled
        };
        db.Audit?.Add(auditAction, "User", userId, userId);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        await NotifyAsync(user, notice, accountUrl);
        if (hadEmail && change == TwoFactorChange.Disable) await NotifyAsync(user, "Đã tắt mã qua email", accountUrl);
        if (wasEnabled != user.TwoFactorEnabled) await NotifyAsync(user, user.TwoFactorEnabled ? "Đã bật xác thực 2 lớp" : "Đã tắt xác thực 2 lớp", accountUrl);
        return codes;
    }

    private async Task ClearRecoveryCodesAsync(ApplicationUser user) => Check(await users.SetAuthenticationTokenAsync(user, "[AspNetUserStore]", "RecoveryCodes", ""));
    private Task NotifyAsync(ApplicationUser user, string change, string accountUrl) => email.SendAsync(user.Email!, change + " · " + Brand.Name,
        $"<p>{WebUtility.HtmlEncode(change)}.</p><p>Nếu không phải bạn, hãy vào <a href=\"{WebUtility.HtmlEncode(accountUrl)}\">Tài khoản — Khóa bảo vệ</a> để khóa tài khoản.</p>");
    private static string CleanCode(string? code) => (code ?? "").Replace(" ", "").Replace("-", "");

    // Chống dùng lại mã app (RFC 6238 §5.2; lỗi thật khi thử: 2 cửa sổ cùng dùng một mã đều vào được).
    // Identity nhận cùng một mã nhiều lần trong khung ±2 bước (~2,5 phút). Ghi bước 30 giây của mã vừa dùng,
    // chỉ nhận mã thuộc bước MỚI HƠN. Gọi bên trong giao dịch đã khóa hàng user (LockUserAsync) nên 2 yêu cầu cùng lúc không cùng lọt.
    private async Task<bool> UseAppCodeAsync(ApplicationUser user, string? code)
    {
        var clean = CleanCode(code);
        if (!await users.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultAuthenticatorProvider, clean)) return false;
        // Cùng giờ hệ thống với Identity để hai bên dùng chung khung bước.
        var step = MatchedStep((await users.GetAuthenticatorKeyAsync(user))!, clean, DateTimeOffset.UtcNow);
        if (step == null || long.TryParse(await TokenAsync(user, "AppLastStep"), CultureInfo.InvariantCulture, out var last) && step <= last) return false;
        await SetAsync(user, "AppLastStep", step.Value.ToString(CultureInfo.InvariantCulture));
        return true;
    }

    public static long? MatchedStep(string base32Key, string code, DateTimeOffset now)
    {
        var bits = new StringBuilder();
        foreach (var c in base32Key.ToUpperInvariant())
        {
            var value = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567".IndexOf(c);
            if (value < 0) return null;
            bits.Append(Convert.ToString(value, 2).PadLeft(5, '0'));
        }
        var key = Enumerable.Range(0, bits.Length / 8).Select(i => Convert.ToByte(bits.ToString(i * 8, 8), 2)).ToArray();
        var current = now.ToUnixTimeSeconds() / 30;
        for (var step = current - 2; step <= current + 2; step++)
        {
            var counter = BitConverter.GetBytes(step);
            if (BitConverter.IsLittleEndian) Array.Reverse(counter);
            var hash = HMACSHA1.HashData(key, counter); var offset = hash[^1] & 15;
            var number = ((hash[offset] & 127) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
            if (CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes((number % 1_000_000).ToString("D6", CultureInfo.InvariantCulture)),
                Encoding.ASCII.GetBytes(code))) return step;
        }
        return null;
    }

    public async Task SendEmailAsync(string userId)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        var user = await LockUserAsync(userId);
        if (!user.TwoFactorEnabled || !await EmailAllowedAsync(user) || await users.IsLockedOutAsync(user)) throw new ForbiddenException();
        if (DateTimeOffset.TryParse(await TokenAsync(user, "EmailSentAt"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var sent)
            && Now < sent.AddMinutes(1)) throw new BusinessException("Vui lòng chờ một phút trước khi gửi lại mã.");
        var code = RandomNumberGenerator.GetInt32(1_000_000).ToString("D6", CultureInfo.InvariantCulture);
        await SetAsync(user, "EmailSentAt", Now.ToString("O"));
        // SecurityStamp làm khóa HMAC: bản DB chỉ lưu dấu kiểm tra, không lưu mã 6 số.
        await SetAsync(user, "EmailChallenge", $"{Now.Add(EmailLifetime):O}|{EmailHash(user, code)}");
        await tx.CommitAsync();
        await email.SendAsync(user.Email!, $"Mã đăng nhập {Brand.Name}", $"<p>Mã đăng nhập: <b>{code}</b>. Mã hết hạn sau 5 phút, chỉ dùng một lần.</p><p>Nếu không phải bạn, hãy bỏ qua email này.</p>");
    }

    private static string EmailHash(ApplicationUser user, string code) => Convert.ToHexString(HMACSHA256.HashData(
        Encoding.UTF8.GetBytes(user.SecurityStamp!), Encoding.UTF8.GetBytes(user.Id + ":" + code)));
    private async Task<bool> VerifyCodeAsync(ApplicationUser user, string method, string? code)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length > 100) return false;
        if (method == "app") return await HasAuthenticatorAsync(users, user) && await UseAppCodeAsync(user, code);
        if (method == "recovery") return (await users.RedeemTwoFactorRecoveryCodeAsync(user, code.Trim().ToUpperInvariant())).Succeeded;
        if (method != "email" || !await EmailAllowedAsync(user)) return false;
        var stored = (await TokenAsync(user, "EmailChallenge"))?.Split('|');
        if (stored is not [var expires, var hash] || !DateTimeOffset.TryParse(expires, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var end)
            || end <= Now || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(hash), Encoding.UTF8.GetBytes(EmailHash(user, CleanCode(code))))) return false;
        await SetAsync(user, "EmailChallenge", null);
        return true;
    }

    // Equivalent của TwoFactor*SignInAsync: recovery cũng tính sai/khóa, Admin luôn bỏ remember.
    public async Task<bool> VerifyLoginAsync(PendingTwoFactor pending, string method, string? code)
    {
        var valid = await VerifyLoginCoreAsync(pending, method, code);
        if (!valid && db.Audit != null) await db.Audit.WriteSeparateAsync(AuditAction.LoginFailed, "User", pending.User.Id,
            pending.User.Id, AuditResult.Failed); // (NK3) context riêng, sau khi khóa nghiệp vụ đã nhả.
        return valid;
    }

    private async Task<bool> VerifyLoginCoreAsync(PendingTwoFactor pending, string method, string? code)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        var user = await LockUserAsync(pending.User.Id);
        if (pending.ExpiresAt <= Now || user.SecurityStamp != pending.SecurityStamp || !await NeedsSecondStepAsync(users, user) || await users.IsLockedOutAsync(user)) return false;
        var valid = await VerifyCodeAsync(user, method, code);
        if (!valid) Check(await users.AccessFailedAsync(user));
        await tx.CommitAsync();
        return valid;
    }
}

// Kiểm DB trên mọi /Admin request để việc vừa cấp quyền cũng có hiệu lực ngay.
public sealed class AdminTwoFactorMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, UserManager<ApplicationUser> users)
    {
        if (context.Request.Path.StartsWithSegments("/Admin") && context.User.Identity?.IsAuthenticated == true)
        {
            var user = await users.GetUserAsync(context.User);
            if (user is { IsSystemAdmin: true, IsGuest: false } && !await TwoFactorService.HasStrongMethodAsync(users, user))
            {
                context.Response.Redirect("/Account/SetupAuthenticator?adminRequired=true"); return;
            }
        }
        await next(context);
    }
}
