using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using MindSprintAI_ASP.NET_MVC.Data;
using MindSprintAI_ASP.NET_MVC.Data.Entities;

namespace MindSprintAI_ASP.NET_MVC.Services;

public class AccountEmailService(AppDbContext db, UserManager<ApplicationUser> users, IEmailSender email,
    TimeProvider clock, EmailLinks links)
{
    public const string InvalidSetupLink = "Link không hợp lệ hoặc đã hết hạn. Hãy đăng ký lại bằng email này để nhận link mới.";

    public async Task<bool> CanSetPasswordAsync(string userId, string token)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrEmpty(token)) return false;
        var user = await users.FindByIdAsync(userId);
        return user != null && !user.IsGuest && !user.EmailConfirmed
            && await users.VerifyUserTokenAsync(user, users.Options.Tokens.EmailConfirmationTokenProvider,
                UserManager<ApplicationUser>.ConfirmEmailTokenPurpose, token);
    }

    public async Task SetPasswordAsync(string userId, string token, string password)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT [Id] FROM [AspNetUsers] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {userId}");
        var user = await users.FindByIdAsync(userId) ?? throw new BusinessException(InvalidSetupLink);
        await db.Entry(user).ReloadAsync();
        if (!await CanSetPasswordAsync(userId, token)) throw new BusinessException(InvalidSetupLink);
        Ensure(await users.ConfirmEmailAsync(user, token));
        // Pháp lý C1: lần đồng ý ghi ở đây, lúc chủ email đặt mật khẩu (controller bắt tick 2 ô).
        user.PolicyAcceptedAt = clock.GetUtcNow().UtcDateTime; user.PolicyVersion = LegalPolicy.CurrentVersion;
        if (await users.HasPasswordAsync(user)) Ensure(await users.RemovePasswordAsync(user));
        Ensure(await users.AddPasswordAsync(user, password));
        Ensure(await users.UpdateSecurityStampAsync(user));
        // ProtectedUserStore ghi xác minh và đặt mật khẩu cùng giao dịch; giữ passkey và ứng dụng xác thực.
        await tx.CommitAsync();
    }

    private static void Ensure(IdentityResult result)
    {
        if (!result.Succeeded) throw new BusinessException(string.Join(" ", result.Errors.Select(e => e.Description)));
    }

    // Dùng bảng token sẵn có; khóa dòng người nhận để cả yêu cầu đồng thời cũng chỉ đặt chỗ một lần.
    public async Task<bool> TryReserveAsync(ApplicationUser user)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT [Id] FROM [AspNetUsers] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {user.Id}");
        await db.Entry(user).ReloadAsync();
        var now = clock.GetUtcNow();
        // Đọc mới dưới khóa, kể cả scope đã từng đọc mốc trước khi yêu cầu khác ghi.
        var stored = await db.UserTokens.AsNoTracking().Where(t => t.UserId == user.Id && t.LoginProvider == "AccountEmail").ToListAsync();
        var history = (stored.FirstOrDefault(t => t.Name == "RecentSends")?.Value ?? "")
            .Split('|', StringSplitOptions.RemoveEmptyEntries).ToList();
        // Giữ cả mốc cũ khi nâng cấp, trước khi hai loại thư dùng chung lịch sử.
        foreach (var purpose in new[] { "Confirmation", "PasswordReset" })
            if (stored.FirstOrDefault(t => t.Name == purpose)?.Value is { } previous) history.Add(previous);
        var recent = history.Select(s => DateTimeOffset.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind))
            .Where(s => s > now.AddHours(-24)).Distinct().ToList();
        if (recent.Count >= 5 || recent.Any(s => now < s.AddMinutes(1))) return false;
        recent.Add(now);
        var result = await users.SetAuthenticationTokenAsync(user, "AccountEmail", "RecentSends",
            string.Join('|', recent.Select(s => s.ToString("O", CultureInfo.InvariantCulture))));
        if (!result.Succeeded) throw new InvalidOperationException("Không lưu được thời gian gửi email tài khoản.");
        await tx.CommitAsync();
        return true;
    }

    public async Task<string?> SendRegistrationAsync(ApplicationUser user)
    {
        // Thông báo tài khoản đã có dùng chung hạn với xác minh, tránh đăng ký trùng để dội thư.
        if (!await TryReserveAsync(user)) return null;
        if (user.EmailConfirmed)
        {
            var forgot = WebUtility.HtmlEncode(links.To("/Account/ForgotPassword"));
            await email.SendAsync(user.Email!, $"Bạn đã có tài khoản {Brand.Name}",
                $"<p>Có người vừa dùng email này để đăng ký {Brand.Name}, nhưng email này đã có tài khoản.</p><p>Nếu là bạn và bạn quên mật khẩu, hãy mở trang <a href=\"{forgot}\">Quên mật khẩu</a>. Nếu không phải bạn, hãy bỏ qua thư này.</p>");
            return null;
        }
        var token = await users.GenerateEmailConfirmationTokenAsync(user);
        var link = links.To(QueryHelpers.AddQueryString("/Account/RegisterPassword", new Dictionary<string, string?>
        { ["userId"] = user.Id, ["code"] = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token)) }));
        var safeLink = WebUtility.HtmlEncode(link);
        await email.SendAsync(user.Email!, $"Đặt mật khẩu cho tài khoản {Brand.Name}",
            $"<p>Chào bạn, bấm link sau để xác minh email và đặt mật khẩu cho tài khoản {Brand.Name}:</p><p><a href=\"{safeLink}\">{safeLink}</a></p><p>Link dùng được trong 24 giờ. Nếu bạn không đăng ký, hãy bỏ qua thư này.</p>");
        return link;
    }
}
