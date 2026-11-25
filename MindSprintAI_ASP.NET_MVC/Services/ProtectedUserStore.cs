using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using MindSprintAI_ASP.NET_MVC.Data;
using MindSprintAI_ASP.NET_MVC.Data.Entities;

namespace MindSprintAI_ASP.NET_MVC.Services;

// EF Identity 10 lưu mã dự phòng và khóa app xác thực dạng rõ trong AspNetUserTokens; chỉ thay phần lưu, giữ API Identity.
// - Mã dự phòng: băm SHA-256 (mã ngẫu nhiên dài, so khớp một chiều là đủ).
// - Khóa app xác thực: mã hóa bằng Data Protection (người dùng yêu cầu 2026-11-06), vì phải đọc lại để tính mã.
//   Lộ riêng DB không đủ để tính mã; cần cả thư mục khóa Data Protection (đã mã hóa DPAPI/chứng chỉ, xem BAO_MAT_TRIEN_KHAI.md).
public sealed class ProtectedUserStore(AppDbContext db, IdentityErrorDescriber errors, IDataProtectionProvider protection)
    : UserStore<ApplicationUser, IdentityRole, AppDbContext>(db, errors)
{
    public const string AuthenticatorPurpose = "MindSprintAI.Identity.AuthenticatorKey.v1";
    private const string Provider = "[AspNetUserStore]", Name = "RecoveryCodes";
    private readonly IDataProtector authenticator = protection.CreateProtector(AuthenticatorPurpose);

    public override Task<IdentityResult> CreateAsync(ApplicationUser user, CancellationToken cancellationToken = default)
    {
        if (!user.IsGuest)
        {
            Context.Audit?.Add(AuditAction.Registered, "User", user.Id, user.Id); // (NK3) gồm tạo bằng Google/Microsoft.
            if (user.EmailConfirmed) Context.Audit?.Add(AuditAction.EmailConfirmed, "User", user.Id, user.Id);
        }
        return base.CreateAsync(user, cancellationToken);
    }

    public override Task<IdentityResult> UpdateAsync(ApplicationUser user, CancellationToken cancellationToken = default)
    {
        // (NK3) Hook lưu Identity: cùng SaveChanges với đổi mật khẩu/xác minh, không ghi hash hay mã.
        var entry = Context.Entry(user);
        if (!user.IsGuest && entry.State != Microsoft.EntityFrameworkCore.EntityState.Detached)
        {
            if (user.EmailConfirmed && !entry.Property(u => u.EmailConfirmed).OriginalValue)
                Context.Audit?.Add(AuditAction.EmailConfirmed, "User", user.Id, user.Id);
            var old = entry.Property(u => u.PasswordHash).OriginalValue;
            if (old != user.PasswordHash && user.PasswordHash != null)
                Context.Audit?.Add(Context.Audit.PasswordAction ?? (old == null ? AuditAction.PasswordSet : AuditAction.PasswordChanged), "User", user.Id, user.Id);
        }
        return base.UpdateAsync(user, cancellationToken);
    }

    public override Task SetAuthenticatorKeyAsync(ApplicationUser user, string key, CancellationToken cancellationToken) =>
        base.SetAuthenticatorKeyAsync(user, authenticator.Protect(key), cancellationToken);

    public override async Task<string?> GetAuthenticatorKeyAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var stored = await base.GetAuthenticatorKeyAsync(user, cancellationToken);
        if (string.IsNullOrEmpty(stored)) return stored;
        // Không giải mã được (khóa lưu dạng rõ trước 2026-11-06, hoặc mất thư mục khóa): coi như chưa có app.
        // Người dùng vẫn vào bằng mã dự phòng (chỉ băm, không phụ thuộc Data Protection) rồi bật lại app.
        try { return authenticator.Unprotect(stored); }
        catch (CryptographicException) { return null; }
    }

    public override Task ReplaceCodesAsync(ApplicationUser user, IEnumerable<string> recoveryCodes, CancellationToken cancellationToken) =>
        SetTokenAsync(user, Provider, Name, string.Join(';', recoveryCodes.Select(TwoFactorService.HashRecovery)), cancellationToken);

    public override async Task<bool> RedeemCodeAsync(ApplicationUser user, string code, CancellationToken cancellationToken)
    {
        var codes = (await GetTokenAsync(user, Provider, Name, cancellationToken) ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries);
        var hash = TwoFactorService.HashRecovery(code);
        if (!codes.Contains(hash)) return false;
        await SetTokenAsync(user, Provider, Name, string.Join(';', codes.Where(c => c != hash)), cancellationToken);
        return true;
    }
}
