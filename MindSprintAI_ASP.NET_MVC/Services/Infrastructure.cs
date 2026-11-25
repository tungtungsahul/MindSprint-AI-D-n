using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using MindSprintAI_ASP.NET_MVC.Data.Entities;
using MindSprintAI_ASP.NET_MVC.Data;
using Microsoft.EntityFrameworkCore;

namespace MindSprintAI_ASP.NET_MVC.Services;

public interface IEmailSender
{
    Task SendAsync(string to, string subject, string htmlBody);
}

// Ghi email ra App_Data/emails: dùng khi chưa cấu hình Smtp:* (xem SmtpEmail.cs), và làm bản sao ở máy dev.
public class FileEmailSender(IWebHostEnvironment env, TimeProvider clock) : IEmailSender
{
    public async Task SendAsync(string to, string subject, string htmlBody)
    {
        var dir = Path.Combine(env.ContentRootPath, "App_Data", "emails");
        Directory.CreateDirectory(dir);
        var safeTo = string.Concat(to.Where(c => char.IsLetterOrDigit(c) || c is '@' or '.' or '-' or '_'));
        var file = Path.Combine(dir, $"{clock.GetUtcNow():yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}_{safeTo}.html");
        await File.WriteAllTextAsync(file,
            $"<p><b>Đến:</b> {WebUtility.HtmlEncode(to)}<br><b>Tiêu đề:</b> {WebUtility.HtmlEncode(subject)}</p>{EmailLayout.Wrap(subject, htmlBody)}");
    }
}

// Việt Nam không đổi giờ theo mùa nên dùng độ lệch cố định UTC+7. Database luôn lưu UTC.
public static class VnTime
{
    private static readonly TimeSpan Offset = TimeSpan.FromHours(7);
    public static DateOnly Date(DateTime utc) => DateOnly.FromDateTime(utc + Offset);

    public static string Format(DateTime? utc) => utc == null ? "" : (utc.Value + Offset).ToString("HH:mm dd/MM/yyyy");

    public static string ToInput(DateTime? utc) => utc == null ? "" : (utc.Value + Offset).ToString("yyyy-MM-ddTHH:mm");

    public static DateTime? FromInput(DateTime? vnLocal) =>
        vnLocal == null ? null : DateTime.SpecifyKind(vnLocal.Value - Offset, DateTimeKind.Utc);
}

// Link đặt lại mật khẩu: hạn 30 phút, dùng một lần, xin link mới thì link cũ hết hiệu lực (55).
public class PasswordResetService(UserManager<ApplicationUser> users, IEmailSender email, TimeProvider clock, AccountLockService locks,
    AccountEmailService accountEmail, AppDbContext db, AuditLog? audit = null)
{
    private const string Provider = "QuizReset", Name = "Nonce";
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);

    // Trả về link (để màn hình thử ở máy hiện ra); null nếu email không có trong hệ thống.
    public async Task<string?> RequestAsync(string emailAddress, Func<string, string, string, string> buildLink)
    {
        var user = await users.FindByEmailAsync(emailAddress.Trim());
        if (audit != null) await audit.WriteSeparateAsync(AuditAction.PasswordRequested, "User", user?.Id ?? "", user?.Id); // (NK3)
        if (user == null) return null;
        if (!user.EmailConfirmed) return await accountEmail.SendRegistrationAsync(user);
        if (!await accountEmail.TryReserveAsync(user)) return null;
        var token = await users.GeneratePasswordResetTokenAsync(user);
        var nonce = Guid.NewGuid().ToString("N");
        var expires = clock.GetUtcNow().UtcDateTime + Lifetime;
        // Ghi đè nonce cũ: link xin trước đó không còn khớp nữa.
        await users.SetAuthenticationTokenAsync(user, Provider, Name, $"{nonce}|{expires:O}");
        var link = buildLink(user.Id, token, nonce);
        await email.SendAsync(user.Email!, $"Đặt lại mật khẩu {Brand.Name}",
            $"<p>Bấm link sau trong 30 phút để đặt lại mật khẩu (chỉ dùng được một lần):</p><p><a href=\"{link}\">{WebUtility.HtmlEncode(link)}</a></p>");
        return link;
    }

    public async Task<bool> ResetAsync(string userId, string token, string nonce, string newPassword)
    {
        bool removedLogins;
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT [Id] FROM [AspNetUsers] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {userId}");
            var user = await users.FindByIdAsync(userId) ?? throw new BusinessException("Link không đúng hoặc đã được dùng. Hãy xin link mới ở Quên mật khẩu.");
            await db.Entry(user).ReloadAsync();
            var stored = (await users.GetAuthenticationTokenAsync(user, Provider, Name))?.Split('|');
            if (stored is not [var savedNonce, var expiresText] || savedNonce != nonce
                || DateTime.Parse(expiresText, null, System.Globalization.DateTimeStyles.RoundtripKind) <= clock.GetUtcNow().UtcDateTime)
                throw new BusinessException("Link đặt lại mật khẩu đã hết hiệu lực. Hãy xin link mới.");

            if (audit != null) audit.PasswordAction = AuditAction.PasswordReset; // (NK3)
            var result = await users.ResetPasswordAsync(user, token, newPassword);
            if (audit != null) audit.PasswordAction = null;
            if (!result.Succeeded) throw new BusinessException(string.Join(" ", result.Errors.Select(e => e.Description)));
            var logins = await users.GetLoginsAsync(user);
            foreach (var external in logins)
            {
                var removed = await users.RemoveLoginAsync(user, external.LoginProvider, external.ProviderKey);
                if (!removed.Succeeded) throw new BusinessException(string.Join(" ", removed.Errors.Select(e => e.Description)));
                db.Audit?.Add(AuditAction.ExternalRemoved, "User", userId, userId);
            }
            await users.RemoveAuthenticationTokenAsync(user, Provider, Name);
            // Đặt lại mật khẩu đổi SecurityStamp → thiết bị khác bị đăng xuất; gỡ luôn khóa 15 phút (55).
            await users.SetLockoutEndDateAsync(user, null);
            await users.ResetAccessFailedCountAsync(user);
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            removedLogins = logins.Count > 0;
        }
        await locks.UnlockProtectiveAsync(userId);
        return removedLogins;
    }
}

public class VietnameseIdentityErrors : IdentityErrorDescriber
{
    public override IdentityError PasswordTooShort(int length) => new() { Code = nameof(PasswordTooShort), Description = $"Mật khẩu phải có ít nhất {length} ký tự." };
    public override IdentityError PasswordRequiresDigit() => new() { Code = nameof(PasswordRequiresDigit), Description = "Mật khẩu phải có ít nhất một chữ số." };
    public override IdentityError PasswordRequiresLower() => new() { Code = nameof(PasswordRequiresLower), Description = "Mật khẩu phải có ít nhất một chữ thường." };
    public override IdentityError PasswordRequiresUpper() => new() { Code = nameof(PasswordRequiresUpper), Description = "Mật khẩu phải có ít nhất một chữ hoa." };
    public override IdentityError PasswordRequiresNonAlphanumeric() => new() { Code = nameof(PasswordRequiresNonAlphanumeric), Description = "Mật khẩu phải có ít nhất một ký tự đặc biệt (ví dụ ! @ #)." };
    public override IdentityError DuplicateEmail(string email) => new() { Code = nameof(DuplicateEmail), Description = "Email này đã được đăng ký." };
    public override IdentityError DuplicateUserName(string userName) => new() { Code = nameof(DuplicateUserName), Description = "Email này đã được đăng ký." };
    public override IdentityError InvalidToken() => new() { Code = nameof(InvalidToken), Description = "Link không hợp lệ hoặc đã hết hạn." };
    public override IdentityError InvalidEmail(string? email) => new() { Code = nameof(InvalidEmail), Description = "Email không hợp lệ." };
}

// Lỗi nghiệp vụ → báo lỗi tiếng Việt (JSON cho fetch, quay lại trang trước cho form); không có quyền → 403.
public class BusinessExceptionFilter(ITempDataDictionaryFactory tempData, AuditLog? audit = null,
    ILogger<BusinessExceptionFilter>? logger = null) : IAsyncExceptionFilter
{
    public const string BusyMessage = "Hệ thống đang bận, bạn thử lại sau vài giây.";

    // Thử tải P3 14/11/2026 (người dùng duyệt): chờ khóa quá lâu khi máy chủ quá tải không phải lỗi hệ thống —
    // báo câu dễ hiểu thay cho trang lỗi 500. 50000/51000 = khóa ứng dụng (sp_getapplock) hết giờ chờ, -2 = lệnh SQL quá giờ,
    // 1222 = chờ khóa dòng quá giờ, 1205 = bị chọn hủy khi hai bên chờ nhau. Câu SQL lỗi vẫn được EF ghi nhật ký.
    public static bool IsBusy(Exception? ex)
    {
        for (; ex != null; ex = ex.InnerException)
            if (ex is Microsoft.Data.SqlClient.SqlException sql && sql.Number is 50000 or 51000 or -2 or 1222 or 1205) return true;
        return false;
    }

    public async Task OnExceptionAsync(ExceptionContext context)
    {
        if (IsBusy(context.Exception)) context.Exception = new BusinessException(BusyMessage);
        if (context.Exception is BusinessException or ForbiddenException)
        {
            if (audit != null) await audit.RejectedAsync(context.Exception,
                context.RouteData.Values["controller"] + "/" + context.RouteData.Values["action"]); // (NK1, NK3)
            else logger?.LogInformation("Business rejection {ExceptionType} at {RequestPath}",
                context.Exception.GetType().Name, LogRedaction.StripQuery(context.HttpContext.Request.Path));
        }
        OnException(context);
    }

    // Giữ hàm dựng response thuần để các ca tầng trước tiếp tục kiểm tra mà không truy cập DB.
    public void OnException(ExceptionContext context)
    {
        var http = context.HttpContext;
        // fetch từ trang (kèm header mã chống giả mạo) luôn nhận lỗi JSON: "mở câu" xin HTML nhưng không được chuyển hướng
        // về trang trước, nếu không play.js sẽ chèn nguyên trang vào chỗ câu hỏi.
        var wantsJson = http.Request.Headers.Accept.ToString().Contains("application/json")
            || http.Request.Headers.ContainsKey("RequestVerificationToken");
        switch (context.Exception)
        {
            case ForbiddenException:
                context.Result = wantsJson ? new ObjectResult(new { error = context.Exception.Message }) { StatusCode = 403 }
                    : new ViewResult { ViewName = "Forbidden", StatusCode = 403 };
                break;
            case BusinessException ex when wantsJson:
                context.Result = new BadRequestObjectResult(new { error = ex.Message });
                break;
            case BusinessException ex:
                tempData.GetTempData(http)["Error"] = ex.Message;
                var back = http.Request.Headers.Referer.ToString();
                context.Result = new RedirectResult(Uri.TryCreate(back, UriKind.Absolute, out var u) && u.Host == http.Request.Host.Host
                    ? back : "/");
                break;
            default:
                return;
        }
        context.ExceptionHandled = true;
    }
}

// Người dùng báo 14/11/2026 (thử tải A4, Safari): mở thêm trang khác của MindSprint AI rồi quay lại trang làm bài thì mọi thao tác bị từ chối
// (mã chống giả mạo của trang đã cũ) và trang chỉ báo "hãy thử lại". Ghi nhật ký để biết lý do; yêu cầu gửi từ trang (fetch kèm
// header mã) nhận JSON reload để trang làm bài hiện nút "Tải lại trang".
// Lý do bị từ chối đã được chính bộ lọc chống giả mạo ghi (mức Information, xem SystemLogging) — không ghi thêm dòng nữa để tránh tràn nhật ký.
public sealed class StaleFormFilter : IAlwaysRunResultFilter
{
    // Câu chung cho mọi trang (đăng nhập passkey, thẻ, thông báo...); trang làm bài tự thêm "các câu đã lưu vẫn còn".
    public const string Message = "Trang này đã cũ (có thể bạn vừa mở một trang khác của MindSprint AI). Hãy tải lại trang để làm tiếp.";

    public void OnResultExecuting(ResultExecutingContext context)
    {
        if (context.Result is not Microsoft.AspNetCore.Mvc.Core.Infrastructure.IAntiforgeryValidationFailedResult) return;
        var request = context.HttpContext.Request;
        if (request.Headers.ContainsKey("RequestVerificationToken") || request.Headers.Accept.ToString().Contains("application/json"))
            context.Result = new BadRequestObjectResult(new { error = Message, reload = true });
    }

    public void OnResultExecuted(ResultExecutedContext context) { }
}
