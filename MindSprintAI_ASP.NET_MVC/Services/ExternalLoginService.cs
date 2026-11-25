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

public record ExternalLoginPage(IList<UserLoginInfo> Linked, string[] Available, bool HasPassword, TwoFactorStatus Reauth);

public class ExternalLoginService(AppDbContext db, UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn,
    LoginService login, TwoFactorService twoFactor, AccountLockService locks, IEmailSender email, TimeProvider clock, IConfiguration config)
{
    public const string Collision = "Email này đã có tài khoản. Hãy đăng nhập bằng cách cũ rồi bấm 'Nối với Google/Microsoft' ở trang Tài khoản.";
    private const string Invalid = "Không xác thực được tài khoản Google/Microsoft. Hãy thử đăng nhập lại.";
    private const string LinkStamp = "qem:link-stamp", LinkUntil = "qem:link-until";
    public Task RecordFailedAsync() => db.Audit?.WriteSeparateAsync(AuditAction.LoginFailed, "User", "", result: AuditResult.Failed)
        ?? Task.CompletedTask; // (NK3) Nhà cung cấp hủy/lỗi trước khi có thông tin tài khoản.
    public async Task<string[]> ProvidersAsync() => (await signIn.GetExternalAuthenticationSchemesAsync())
        .Where(s => ExternalProviderConfig.Known(s.Name)).Select(s => s.Name).ToArray();
    public async Task<bool> AvailableAsync(string? provider) => ExternalProviderConfig.Known(provider)
        && (await ProvidersAsync()).Contains(provider);
    public async Task<ExternalLoginPage> PageAsync(ApplicationUser user) => new(await users.GetLoginsAsync(user),
        await ProvidersAsync(), await users.HasPasswordAsync(user), await twoFactor.StatusAsync(user));

    private static string ProviderEmail(ExternalLoginInfo info)
    {
        var address = info.Principal.FindFirstValue(ClaimTypes.Email)?.Trim();
        if (string.IsNullOrWhiteSpace(address)) throw new BusinessException("Google/Microsoft không cung cấp email. Hãy dùng tài khoản có email.");
        if (info.LoginProvider == "Google" && !string.Equals(info.Principal.FindFirstValue(ExternalProviderConfig.GoogleVerifiedClaim), "true", StringComparison.OrdinalIgnoreCase))
            throw new BusinessException("Email Google chưa được xác minh.");
        return address;
    }
    public static string DisplayName(ExternalLoginInfo info, string address)
    {
        var name = info.Principal.FindFirstValue(ClaimTypes.Name)?.Trim();
        if (string.IsNullOrWhiteSpace(name)) name = address.Split('@')[0];
        return name.Length > 100 ? name[..100] : name;
    }
    private async Task ValidateAsync(ExternalLoginInfo info)
    {
        if (!await AvailableAsync(info.LoginProvider) || string.IsNullOrWhiteSpace(info.ProviderKey)) throw new BusinessException(Invalid);
        ProviderEmail(info);
    }
    private static void Check(IdentityResult result)
    {
        if (!result.Succeeded) throw new BusinessException(string.Join(" ", result.Errors.Select(e => e.Description)));
    }
    // Hai tài khoản có thể nối cùng key đồng thời: khóa key trước khi kiểm chủ sở hữu.
    private Task LockKeyAsync(ExternalLoginInfo info)
    {
        var resource = "external-login:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(info.LoginProvider + ":" + info.ProviderKey)));
        return db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @r int; EXEC @r = sp_getapplock @Resource = {resource}, @LockMode = 'Exclusive', @LockOwner = 'Transaction'; IF @r < 0 THROW 50000, N'Không lấy được khóa liên kết.', 1;");
    }

    public async Task<LoginOutcome> SignInAsync(ExternalLoginInfo info, GuestService? guests = null, string? returnUrl = null)
    {
        try
        {
            var result = await SignInCoreAsync(info, guests, returnUrl);
            if (!result.Succeeded && !result.RequiresTwoFactor && db.Audit is { HasFailure: false })
                await db.Audit.WriteSeparateAsync(AuditAction.LoginFailed, "User", "", result: AuditResult.Failed); // (NK3)
            return result;
        }
        catch (BusinessException)
        {
            if (db.Audit != null) await db.Audit.WriteSeparateAsync(AuditAction.LoginFailed, "User", "", result: AuditResult.Failed); // (NK3)
            throw;
        }
    }

    private async Task<LoginOutcome> SignInCoreAsync(ExternalLoginInfo info, GuestService? guests, string? returnUrl)
    {
        await ValidateAsync(info);
        var user = await users.FindByLoginAsync(info.LoginProvider, info.ProviderKey);
        if (user == null)
        {
            // Tạo user và nối key cùng giao dịch: AddLogin lỗi không để lại tài khoản mồ côi.
            await using var tx = await db.Database.BeginTransactionAsync();
            await LockKeyAsync(info);
            user = await users.FindByLoginAsync(info.LoginProvider, info.ProviderKey);
            if (user == null)
            {
                var address = ProviderEmail(info);
                user = await users.FindByEmailAsync(address);
                if (user != null)
                {
                    if (info.LoginProvider != "Google") return new(false, false, Collision);
                    // Cùng khóa với đặt mật khẩu: đọc lại sau khóa để chỉ nhận tài khoản còn chưa xác minh.
                    await db.Database.ExecuteSqlInterpolatedAsync($"SELECT [Id] FROM [AspNetUsers] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {user.Id}");
                    await db.Entry(user).ReloadAsync();
                    if (user.IsGuest || user.EmailConfirmed) return new(false, false, Collision);
                    user.EmailConfirmed = true;
                    // Lần đồng ý cũ là của người gõ form đăng ký, có thể không phải chủ email: hỏi lại chủ thật (C2).
                    user.PolicyAcceptedAt = null; user.PolicyVersion = null;
                    Check(await users.UpdateAsync(user));
                    if (await users.HasPasswordAsync(user)) Check(await users.RemovePasswordAsync(user));
                    Check(await users.UpdateSecurityStampAsync(user));
                }
                else
                {
                    user = new ApplicationUser { UserName = address, Email = address, EmailConfirmed = true,
                        DisplayName = DisplayName(info, address), CreatedAt = clock.GetUtcNow().UtcDateTime };
                    var created = await users.CreateAsync(user);
                    if (!created.Succeeded)
                    {
                        if (created.Errors.Any(e => e.Code is "DuplicateEmail" or "DuplicateUserName")) return new(false, false, Collision);
                        Check(created);
                    }
                }
                Check(await users.AddLoginAsync(user, info));
                db.Audit?.Add(AuditAction.ExternalLinked, "User", user.Id, user.Id);
                await db.SaveChangesAsync();
            }
            await tx.CommitAsync();
        }
        // Bootstrap cũng áp cho email tạo sau khi server đã khởi động.
        if (!user.IsGuest && (config.GetSection("Admin:Emails").Get<string[]>() ?? [])
            .Any(e => users.NormalizeEmail(e.Trim()) == user.NormalizedEmail))
        {
            user.IsSystemAdmin = true; Check(await users.UpdateAsync(user));
        }
        await locks.RefreshUserAsync(user);
        var active = await locks.ActiveAsync(user.Id);
        if (active != null) return new(false, false, AccountLockService.MessageForUser(active));
        if (user.IsGuest || !user.EmailConfirmed || await users.IsLockedOutAsync(user)) return new(false, false, LoginService.Invalid);
        if (AvatarService.CaptureGoogle(user, info.LoginProvider, info.Principal, clock.GetUtcNow().UtcDateTime))
            await db.SaveChangesAsync();
        if (await TwoFactorService.NeedsSecondStepAsync(users, user))
        {
            await TwoFactorService.BeginAsync(signIn, user, false, returnUrl, clock.GetUtcNow());
            return new(false, false, RequiresTwoFactor: true);
        }
        // Provider không phải yếu tố thứ hai: tuyệt đối không gắn amr=mfa.
        return await login.CompleteAsync(user, false, guests, user.SecurityStamp, verifiedMethod: null);
    }

    private async Task<ApplicationUser> LockUserAsync(string userId)
    {
        await AccountLockState.EnsureAllowedAsync(db, userId, clock.GetUtcNow().UtcDateTime);
        var user = await users.FindByIdAsync(userId) ?? throw new ForbiddenException();
        await db.Entry(user).ReloadAsync();
        if (user.IsGuest || !user.EmailConfirmed || await users.IsLockedOutAsync(user)) throw new ForbiddenException();
        // Callback phải còn đúng phiên đã đăng nhập; chống đổi stamp trong lúc đang ở provider.
        if (signIn.Context.User.FindFirstValue(ClaimTypes.NameIdentifier) != user.Id
            || signIn.Context.User.FindFirstValue(signIn.Options.ClaimsIdentity.SecurityStampClaimType) != user.SecurityStamp)
            throw new ForbiddenException();
        return user;
    }
    private async Task ReauthenticateAsync(ApplicationUser user, string? password, string? code,
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction tx)
    {
        if (await twoFactor.ReauthenticateAsync(user, password, code)) return;
        await tx.CommitAsync(); // Giữ bộ đếm xác nhận sai.
        throw new BusinessException("Không xác nhận được danh tính. Kiểm tra mật khẩu hoặc mã xác thực 2 lớp hiện tại.");
    }

    public async Task<AuthenticationProperties> BeginLinkAsync(string userId, string provider, string? password, string? code, string callbackUrl)
    {
        if (!await AvailableAsync(provider)) throw new BusinessException(Invalid);
        await using var tx = await db.Database.BeginTransactionAsync();
        var user = await LockUserAsync(userId);
        await ReauthenticateAsync(user, password, code, tx);
        var properties = signIn.ConfigureExternalAuthenticationProperties(provider, callbackUrl, user.Id);
        // Các giá trị này đi trong state/cookie do handler bảo vệ, không tin query string callback.
        properties.Items[LinkStamp] = user.SecurityStamp;
        properties.Items[LinkUntil] = clock.GetUtcNow().AddMinutes(10).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        await tx.CommitAsync();
        return properties;
    }
    public async Task LinkAsync(string userId, ExternalLoginInfo info, string accountUrl)
    {
        await ValidateAsync(info);
        await using var tx = await db.Database.BeginTransactionAsync();
        var user = await LockUserAsync(userId);
        var props = info.AuthenticationProperties;
        if (props == null || !props.Items.TryGetValue("XsrfId", out var xsrf) || xsrf != user.Id
            || !props.Items.TryGetValue(LinkStamp, out var stamp) || stamp != user.SecurityStamp
            || !props.Items.TryGetValue(LinkUntil, out var expiry)
            || !long.TryParse(expiry, out var until) || until < clock.GetUtcNow().ToUnixTimeSeconds())
            throw new BusinessException("Đã hết thời gian liên kết cách đăng nhập. Hãy xác nhận lại danh tính.");
        // S2: tài khoản không mật khẩu/không 2FA — kiểm lại "đăng nhập trong 10 phút" ở callback để thời gian ở trang
        // provider không kéo dài cửa sổ. Nhánh này ném BusinessException khi quá hạn, không cộng bộ đếm sai.
        if (!await users.HasPasswordAsync(user) && !user.TwoFactorEnabled)
            await twoFactor.ReauthenticateAsync(user, null, null);
        await LockKeyAsync(info);
        var owner = await users.FindByLoginAsync(info.LoginProvider, info.ProviderKey);
        if (owner != null) throw new BusinessException(owner.Id == user.Id ? "Liên kết này đã có trên tài khoản."
            : "Tài khoản Google/Microsoft này đã nối với tài khoản khác.");
        Check(await users.AddLoginAsync(user, info));
        Check(await users.UpdateSecurityStampAsync(user));
        db.Audit?.Add(AuditAction.ExternalLinked, "User", userId, userId); // (NK3)
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        await NotifyAsync(user, "Đã nối với " + info.LoginProvider, accountUrl);
    }
    public async Task RemoveAsync(string userId, string provider, string providerKey, string? password, string? code, string accountUrl)
    {
        if (!ExternalProviderConfig.Known(provider)) throw new BusinessException(Invalid);
        await using var tx = await db.Database.BeginTransactionAsync();
        var user = await LockUserAsync(userId);
        await ReauthenticateAsync(user, password, code, tx);
        if (!(await users.GetLoginsAsync(user)).Any(l => l.LoginProvider == provider && l.ProviderKey == providerKey))
            throw new BusinessException("Không tìm thấy liên kết đăng nhập.");
        if (await PasskeyService.CountSignInMethodsAsync(users, user) <= 1) throw new BusinessException("Không thể xóa cách đăng nhập cuối cùng.");
        Check(await users.RemoveLoginAsync(user, provider, providerKey));
        if (provider == "Google") AvatarService.Clear(user, clock.GetUtcNow().UtcDateTime);
        Check(await users.UpdateSecurityStampAsync(user));
        db.Audit?.Add(AuditAction.ExternalRemoved, "User", userId, userId); // (NK3)
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        await NotifyAsync(user, "Đã gỡ liên kết " + provider, accountUrl);
    }
    public async Task SetPasswordAsync(string userId, string? newPassword, string? password, string? code, string accountUrl)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        var user = await LockUserAsync(userId);
        if (await users.HasPasswordAsync(user)) throw new BusinessException("Tài khoản đã có mật khẩu.");
        await ReauthenticateAsync(user, password, code, tx);
        Check(await users.AddPasswordAsync(user, newPassword ?? ""));
        db.Audit?.Add(AuditAction.PasswordSet, "User", userId, userId); // (NK3)
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        await NotifyAsync(user, "Đã đặt mật khẩu", accountUrl);
    }
    private Task NotifyAsync(ApplicationUser user, string change, string accountUrl) => email.SendAsync(user.Email!, change + " · " + Brand.Name,
        $"<p>{WebUtility.HtmlEncode(change)}.</p><p>Nếu không phải bạn, hãy vào <a href=\"{WebUtility.HtmlEncode(accountUrl)}\">Tài khoản — Khóa bảo vệ</a> để khóa tài khoản.</p>");
}
