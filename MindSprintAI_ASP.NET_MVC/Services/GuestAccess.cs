using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MindSprintAI_ASP.NET_MVC.Data;
using MindSprintAI_ASP.NET_MVC.Data.Entities;

namespace MindSprintAI_ASP.NET_MVC.Services;

[AttributeUsage(AttributeTargets.Method)]
public sealed class AllowGuestAttribute : Attribute;

public static class GuestAccess
{
    public const string Claim = "qem_guest";
    public static bool IsGuest(ClaimsPrincipal? user) => user?.HasClaim(Claim, "true") == true;
}

public sealed class GuestClaimsFactory(UserManager<ApplicationUser> users, RoleManager<IdentityRole> roles,
    IOptions<IdentityOptions> options) : UserClaimsPrincipalFactory<ApplicationUser, IdentityRole>(users, roles, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        if (user.IsGuest) identity.AddClaim(new(GuestAccess.Claim, "true"));
        // Pháp lý: cổng đồng ý đọc claim này, không truy vấn DB mỗi request; tạo lại mỗi lần kiểm phiên (1 phút).
        if (user.PolicyVersion is int v) identity.AddClaim(new(LegalPolicy.Claim, v.ToString()));
        return identity;
    }
}

// Người đã đăng nhập mà chưa đồng ý Điều khoản/Chính sách phiên bản hiện tại: mở trang nào cũng được đưa tới trang đồng ý (C2).
// Chỉ chặn lúc mở trang (GET): việc đang làm dở như gửi câu trả lời, nộp bài, lưu form không bị cắt ngang giữa chừng
// khi vừa lên bản mới hay vừa tăng phiên bản chính sách; trang kế tiếp họ mở sẽ hỏi đồng ý.
public sealed class PolicyGateFilter(AppDbContext db) : IAsyncAuthorizationFilter, IOrderedFilter
{
    public int Order => int.MinValue + 1; // sau NotGuestFilter
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var http = context.HttpContext;
        var page = HttpMethods.IsGet(http.Request.Method) && http.Request.Headers.XRequestedWith != "XMLHttpRequest"
            && http.Request.Headers.Accept.ToString().Contains("text/html");
        if (!page || context.Result != null || !LegalPolicy.NeedsAcceptance(http.User)
            || (context.ActionDescriptor as ControllerActionDescriptor)?.MethodInfo.IsDefined(typeof(AllowWithoutPolicyAttribute)) == true) return;
        // Claim chỉ làm mới mỗi phút: vừa đồng ý xong thì hỏi lại DB (chỉ người chưa đồng ý mới tới đây).
        var id = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (await db.Users.AnyAsync(u => u.Id == id && u.PolicyVersion >= LegalPolicy.CurrentVersion)) return;
        context.Result = new RedirectToActionResult("AcceptPolicy", "Account", new { returnUrl = http.Request.Path + http.Request.QueryString });
    }
}

// Không dùng AllowAnonymous để bỏ chặn khách: trang công khai như đặt mật khẩu vẫn bị khóa.
public sealed class NotGuestFilter : IAuthorizationFilter, IOrderedFilter
{
    public int Order => int.MinValue;
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        if (GuestAccess.IsGuest(context.HttpContext.User)
            && (context.ActionDescriptor as ControllerActionDescriptor)?.MethodInfo.IsDefined(typeof(AllowGuestAttribute)) != true)
            context.Result = new StatusCodeResult(StatusCodes.Status403Forbidden);
    }
}

public sealed class NotGuestHubFilter : IHubFilter
{
    public ValueTask<object?> InvokeMethodAsync(HubInvocationContext context, Func<HubInvocationContext, ValueTask<object?>> next)
    {
        if (GuestAccess.IsGuest(context.Context.User) && !context.HubMethod.IsDefined(typeof(AllowGuestAttribute)))
            throw new HubException("Khách không có quyền dùng chức năng này.");
        return next(context);
    }
}

// Kiểm bí mật trên mọi request khách, kể cả cookie Identity cũ sau khi đã gộp.
// Thử tải 14/11/2026: trước đây mỗi yêu cầu hỏi DB 3 lần (khách hợp lệ? tài khoản còn? đang bị khóa?). Nay gộp thành 1 câu,
// vẫn kiểm ở MỌI yêu cầu (không nhớ tạm) nên khóa tài khoản/đổi mật khẩu vẫn có hiệu lực ngay. AccountLockMiddleware dùng lại kết quả.
public sealed record SessionCheck(string? SecurityStamp, bool Locked, int? GuestId)
{
    public const string Key = "SessionCheck";
    public static async Task<SessionCheck?> LoadAsync(AppDbContext db, string? userId, string? guestHash, DateTime now) =>
        userId == null ? null : await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => new SessionCheck(
            u.SecurityStamp,
            db.AccountLocks.Any(x => x.UserId == u.Id && x.UnlockedAt == null && x.StartsAt <= now && (x.EndsAt == null || x.EndsAt > now)),
            guestHash == null ? null : db.Guests.Where(g => g.UserId == u.Id && g.BrowserTokenHash == guestHash
                && g.MergedIntoUserId == null && u.IsGuest && u.LockoutEnd == null).Select(g => (int?)g.Id).FirstOrDefault())).SingleOrDefaultAsync();
}

public sealed class GuestSessionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, AppDbContext db, TimeProvider clock)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var guest = GuestAccess.IsGuest(context.User);
            var hash = guest ? GuestService.HashToken(context.Request.Cookies[GuestService.CookieName]) : null;
            var check = await SessionCheck.LoadAsync(db, context.User.FindFirstValue(ClaimTypes.NameIdentifier), hash, clock.GetUtcNow().UtcDateTime);
            context.Items[SessionCheck.Key] = check;
            if (guest)
            {
                // (NK1, NK3) Lấy mã khách ngay trong lần kiểm quyền vốn có, không thêm query vì ghi log.
                if (check?.GuestId == null)
                {
                    await context.SignOutAsync(IdentityConstants.ApplicationScheme);
                    context.User = new ClaimsPrincipal(new ClaimsIdentity());
                }
                else context.Items["AuditGuestId"] = check.GuestId;
            }
        }
        await next(context);
    }
}
