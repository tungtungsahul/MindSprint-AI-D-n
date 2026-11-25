using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using MindSprintAI_ASP.NET_MVC.Data;
using MindSprintAI_ASP.NET_MVC.Data.Entities;
using MindSprintAI_ASP.NET_MVC.Models;

namespace MindSprintAI_ASP.NET_MVC.Services;

public class PasskeyService(AppDbContext db, UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn,
    TwoFactorService twoFactor, LoginService login, IEmailSender email, TimeProvider clock)
{
    public const string Invalid = "Không xác thực được passkey. Hãy thử lại.";
    // Lỗi thật khi thử: bước 2 quá 5 phút mà vẫn báo "thử lại" → người dùng bấm lặp vô ích. Báo đúng là hết hạn.
    public const string Expired = "Đã hết thời gian xác nhận danh tính. Hãy bấm Đăng nhập lại.";
    public const int Maximum = 10;
    private const string Provider = "QuizPasskey";
    private static string LastUsedKey(byte[] id) => "LastUsed:" + Convert.ToHexString(SHA256.HashData(id));
    public static string ValidateName(string? name)
    {
        name = name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > 50) throw new BusinessException("Tên passkey phải có từ 1 đến 50 ký tự.");
        return name;
    }
    private static byte[] DecodeId(string? id)
    {
        if (string.IsNullOrEmpty(id) || id.Length > 1400) throw new BusinessException("Không tìm thấy passkey.");
        try { return WebEncoders.Base64UrlDecode(id); }
        catch (FormatException) { throw new BusinessException("Không tìm thấy passkey."); }
    }
    private static void Check(IdentityResult result)
    {
        if (!result.Succeeded) throw new BusinessException("Tài khoản vừa thay đổi. Hãy tải lại và thử lại.");
    }
    private async Task<ApplicationUser> LockUserAsync(string id)
    {
        await AccountLockState.EnsureAllowedAsync(db, id, clock.GetUtcNow().UtcDateTime);
        var user = await users.FindByIdAsync(id) ?? throw new ForbiddenException();
        await db.Entry(user).ReloadAsync();
        if (user.IsGuest || !user.EmailConfirmed || await users.IsLockedOutAsync(user)) throw new ForbiddenException();
        return user;
    }
    public async Task<PasskeyPage> ListAsync(ApplicationUser user)
    {
        var items = new List<PasskeyItem>();
        foreach (var key in await users.GetPasskeysAsync(user))
        {
            var value = await users.GetAuthenticationTokenAsync(user, Provider, LastUsedKey(key.CredentialId));
            DateTime? last = DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var time) ? time.UtcDateTime : null;
            items.Add(new(WebEncoders.Base64UrlEncode(key.CredentialId), key.Name ?? "Passkey", key.CreatedAt.UtcDateTime, last));
        }
        return new(items.OrderBy(x => x.CreatedAt).ToArray(), await users.HasPasswordAsync(user));
    }
    public async Task<string> CreationOptionsAsync(string userId)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        var user = await LockUserAsync(userId);
        if ((await users.GetPasskeysAsync(user)).Count >= Maximum) throw new BusinessException("Mỗi tài khoản có tối đa 10 passkey.");
        await signIn.Context.SignOutAsync(TwoFactorService.PasskeyPendingScheme);
        var json = await signIn.MakePasskeyCreationOptionsAsync(new() { Id = user.Id, Name = user.UserName!, DisplayName = user.DisplayName });
        await tx.CommitAsync();
        return json;
    }
    public async Task RegisterAsync(string userId, string? name, string? credentialJson, string accountUrl)
    {
        name = ValidateName(name);
        if (string.IsNullOrEmpty(credentialJson) || credentialJson.Length > 262144) throw new BusinessException(Invalid);
        PasskeyAttestationResult result;
        try { result = await signIn.PerformPasskeyAttestationAsync(credentialJson); }
        catch (InvalidOperationException) { throw new BusinessException(Invalid); }
        if (!result.Succeeded || result.UserEntity.Id != userId || !result.Passkey.IsUserVerified) throw new BusinessException(Invalid);
        await using var tx = await db.Database.BeginTransactionAsync();
        var user = await LockUserAsync(userId);
        if ((await users.GetPasskeysAsync(user)).Count >= Maximum) throw new BusinessException("Mỗi tài khoản có tối đa 10 passkey.");
        // Không cập nhật/chuyển quyền sở hữu một credential đã đăng ký.
        if (await users.FindByPasskeyIdAsync(result.Passkey.CredentialId) != null) throw new BusinessException("Passkey này đã được đăng ký.");
        result.Passkey.Name = name;
        Check(await users.AddOrUpdatePasskeyAsync(user, result.Passkey));
        Check(await users.UpdateSecurityStampAsync(user));
        db.Audit?.Add(AuditAction.PasskeyAdded, "User", userId, userId); // (NK3)
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        await NotifyAsync(user, "Đã thêm passkey", accountUrl);
    }
    public async Task RenameAsync(string userId, string? id, string? name)
    {
        name = ValidateName(name);
        var credentialId = DecodeId(id);
        await using var tx = await db.Database.BeginTransactionAsync();
        var user = await LockUserAsync(userId);
        var key = await users.GetPasskeyAsync(user, credentialId) ?? throw new BusinessException("Không tìm thấy passkey.");
        key.Name = name;
        Check(await users.AddOrUpdatePasskeyAsync(user, key));
        db.Audit?.Add(AuditAction.PasskeyRenamed, "User", userId, userId); // (NK3)
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }
    // Một chỗ đếm cách đăng nhập; 6c dùng lại khi gỡ Google/Microsoft.
    public static async Task<int> CountSignInMethodsAsync(UserManager<ApplicationUser> users, ApplicationUser user) =>
        (await users.HasPasswordAsync(user) ? 1 : 0) + (await users.GetLoginsAsync(user)).Count + (await users.GetPasskeysAsync(user)).Count;

    public async Task DeleteAsync(string userId, string? id, string? password, string? code, string accountUrl)
    {
        var credentialId = DecodeId(id);
        await using var tx = await db.Database.BeginTransactionAsync();
        var user = await LockUserAsync(userId);
        if (await users.GetPasskeyAsync(user, credentialId) == null) throw new BusinessException("Không tìm thấy passkey.");
        if (!await twoFactor.ReauthenticateAsync(user, password, code))
        {
            await tx.CommitAsync(); // Giữ bộ đếm sai; không rollback như lỗi nhập liệu thông thường.
            throw new BusinessException("Không xác nhận được danh tính. Kiểm tra mật khẩu hoặc mã xác thực 2 lớp hiện tại.");
        }
        if (await CountSignInMethodsAsync(users, user) <= 1) throw new BusinessException("Không thể xóa cách đăng nhập cuối cùng.");
        if (user.IsSystemAdmin && (await users.GetPasskeysAsync(user)).Count == 1 && !await TwoFactorService.HasAuthenticatorAsync(users, user))
            throw new BusinessException("Quản trị viên phải giữ ít nhất một cách xác thực 2 lớp: ứng dụng xác thực hoặc Passkey.");
        Check(await users.RemovePasskeyAsync(user, credentialId));
        Check(await users.RemoveAuthenticationTokenAsync(user, Provider, LastUsedKey(credentialId)));
        Check(await users.UpdateSecurityStampAsync(user));
        db.Audit?.Add(AuditAction.PasskeyDeleted, "User", userId, userId); // (NK3)
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        await NotifyAsync(user, "Đã xóa passkey", accountUrl);
    }
    private Task NotifyAsync(ApplicationUser user, string change, string accountUrl) => email.SendAsync(user.Email!, change + " · " + Brand.Name,
        $"<p>{change}.</p><p>Nếu không phải bạn, hãy vào <a href=\"{WebUtility.HtmlEncode(accountUrl)}\">Tài khoản — Khóa bảo vệ</a> để khóa tài khoản.</p>");

    public async Task<string> RequestOptionsAsync(bool secondStep)
    {
        var pending = secondStep ? await twoFactor.PendingAsync() : null;
        if (secondStep && pending == null) throw new BusinessException(Expired);
        if (secondStep && (await users.GetPasskeysAsync(pending!.User)).Count == 0) throw new BusinessException(Invalid);
        if (pending != null)
        {
            var identity = new ClaimsIdentity(TwoFactorService.PasskeyPendingScheme);
            identity.AddClaim(new(ClaimTypes.Name, pending.User.Id));
            identity.AddClaim(new(signIn.Options.ClaimsIdentity.SecurityStampClaimType, pending.SecurityStamp));
            var properties = new AuthenticationProperties { ExpiresUtc = pending.ExpiresAt, IssuedUtc = clock.GetUtcNow() };
            properties.Items["remember"] = pending.Remember.ToString(); properties.Items["returnUrl"] = pending.ReturnUrl;
            await signIn.Context.SignInAsync(TwoFactorService.PasskeyPendingScheme, new ClaimsPrincipal(identity), properties);
        }
        else await signIn.Context.SignOutAsync(TwoFactorService.PasskeyPendingScheme);
        return await signIn.MakePasskeyRequestOptionsAsync(pending?.User);
    }
    public async Task<(LoginOutcome Outcome, string ReturnUrl)> LoginAsync(string? credentialJson, bool secondStep, bool remember, GuestService? guests = null)
    {
        var result = await LoginCoreAsync(credentialJson, secondStep, remember, guests);
        if (!result.Outcome.Succeeded && !result.Outcome.RequiresTwoFactor && db.Audit is { HasFailure: false })
            await db.Audit.WriteSeparateAsync(AuditAction.LoginFailed, "User", "", result: AuditResult.Failed); // (NK3)
        return result;
    }

    private async Task<(LoginOutcome Outcome, string ReturnUrl)> LoginCoreAsync(string? credentialJson, bool secondStep, bool remember, GuestService? guests)
    {
        var pending = secondStep ? await twoFactor.PendingAsync() : null;
        (LoginOutcome, string) Failed() => (new(false, false, Invalid), pending?.ReturnUrl ?? "/");
        if (secondStep && pending == null) return (new(false, false, Expired), "/");
        if (string.IsNullOrEmpty(credentialJson) || credentialJson.Length > 262144) return Failed();
        PasskeyAssertionResult<ApplicationUser> result;
        try { result = await signIn.PerformPasskeyAssertionAsync(credentialJson); }
        catch (InvalidOperationException) { return Failed(); }
        if (!result.Succeeded || !result.Passkey.IsUserVerified || pending != null && result.User.Id != pending.User.Id) return Failed();
        var stamp = pending?.SecurityStamp ?? result.User.SecurityStamp;
        var user = result.User;
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            // Khóa cùng hàng user với thao tác xóa; không hồi sinh credential vừa bị xóa.
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT [Id] FROM [AspNetUsers] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {user.Id}");
            await db.Entry(user).ReloadAsync();
            // Đọc scalar mới từ DB: EF đã track credential lúc verify, có thể cũ khi chờ khóa hàng user.
            var stored = await db.UserPasskeys.AsNoTracking()
                .Where(p => p.UserId == user.Id && p.CredentialId.SequenceEqual(result.Passkey.CredentialId))
                .Select(p => new { p.Data.Name, p.Data.SignCount }).SingleOrDefaultAsync();
            if (stored == null || user.SecurityStamp != stamp || pending?.ExpiresAt <= clock.GetUtcNow()
                || (result.Passkey.SignCount != 0 || stored.SignCount != 0) && result.Passkey.SignCount <= stored.SignCount) return Failed();
            result.Passkey.Name = stored.Name;
            Check(await users.AddOrUpdatePasskeyAsync(user, result.Passkey));
            Check(await users.SetAuthenticationTokenAsync(user, Provider, LastUsedKey(result.Passkey.CredentialId), clock.GetUtcNow().ToString("O")));
            await tx.CommitAsync();
        }
        var outcome = await login.CompleteAsync(user, pending?.Remember ?? remember, guests, stamp, "passkey");
        return (outcome, pending?.ReturnUrl ?? "/");
    }
}
