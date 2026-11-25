using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Claims;
using System.Net;
using System.Net.Sockets;

namespace MindSprintAI_ASP.NET_MVC.Services;

public static class AccountRateLimits
{
    // Người dùng chốt 13/11/2026: cả giảng đường dùng chung Wi-Fi trường = một địa chỉ mạng. Các bước vào tài khoản
    // (đăng nhập, đăng ký, quên mật khẩu, bước 2, Google, Passkey) có hạn rộng theo địa chỉ mạng để cả lớp vào cùng lúc được;
    // hạn chặt tính theo từng email. Chống đoán mật khẩu theo từng tài khoản (5 lần sai → xác minh, 10 lần → khóa 15 phút) giữ nguyên.
    public const string EntryPolicy = "account-entry";
    // Vào lớp/bài bằng mã (người dùng chốt 13/11/2026 sau thử tải: 651 khách chung một Wi-Fi đều vào được ở mức 1000/phút).
    // Chống dò mã vẫn do JoinCodeGuardService (theo trình duyệt 10 lần, theo khu vực mạng 50 lần).
    public const string JoinCodePolicy = "join-code";
    public const int EntryPerMinute = 1000, PerEmailPerMinute = 30;
    public const string EmailBusy = "Email này vừa được thử quá nhiều lần trong một phút. Vui lòng chờ một phút rồi thử lại.";

    private static readonly PartitionedRateLimiter<string> EmailLimiter = PartitionedRateLimiter.Create<string, string>(email =>
        RateLimitPartition.GetFixedWindowLimiter(email, _ => new FixedWindowRateLimiterOptions
        { PermitLimit = PerEmailPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true }));

    // shortcut: đếm trong bộ nhớ (mất khi khởi động lại, một máy chủ); đủ cho chặn dồn dập vài phút, giống các hạn theo phút khác.
    public static bool TryEmail(string? email)
    {
        var key = (email ?? "").Trim().ToUpperInvariant();
        if (key.Length == 0) return true; // form trống đã bị kiểm lỗi ở bước sau
        using var lease = EmailLimiter.AttemptAcquire(key);
        return lease.IsAcquired;
    }

    public static IServiceCollection AddAccountRateLimits(this IServiceCollection services) => services.AddRateLimiter(o =>
    {
        o.AddPolicy(EntryPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
            AccountKey(context.Connection.RemoteIpAddress), _ => new FixedWindowRateLimiterOptions
            { PermitLimit = EntryPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true }));
        o.AddPolicy(JoinCodePolicy, context => RateLimitPartition.GetFixedWindowLimiter(
            AccountKey(context.Connection.RemoteIpAddress), _ => new FixedWindowRateLimiterOptions
            { PermitLimit = EntryPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true }));
        o.AddPolicy("ai-question", context => RateLimitPartition.GetFixedWindowLimiter(
            context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous", _ => AiOptions()));
        o.AddPolicy("account-post", context => RateLimitPartition.GetFixedWindowLimiter(
            AccountKey(context.Connection.RemoteIpAddress), _ => new FixedWindowRateLimiterOptions
            { PermitLimit = 100, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true }));
        o.OnRejected = async (context, cancellation) =>
        {
            context.HttpContext.Response.StatusCode = 429;
            context.HttpContext.Response.Headers.RetryAfter = "60";
            if (context.HttpContext.Request.Headers.Accept.ToString().Contains("application/json"))
            {
                await context.HttpContext.Response.WriteAsJsonAsync(new { error = "Bạn đã gửi quá nhiều yêu cầu. Vui lòng chờ một phút rồi thử lại." }, cancellation);
                return;
            }
            context.HttpContext.Response.ContentType = "text/html; charset=utf-8";
            context.HttpContext.Response.Headers.RetryAfter = "60";
            await context.HttpContext.Response.WriteAsync("<!doctype html><html lang=\"vi\"><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><title>Quá nhiều yêu cầu</title><main><h1>Bạn thao tác quá nhanh</h1><p>Vui lòng chờ một phút rồi thử lại.</p><a href=\"/\">Về trang chủ</a></main></html>", cancellation);
        };
    });

    private static string AccountKey(IPAddress? address)
    {
        if (address == null) return "unknown";
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (address.AddressFamily != AddressFamily.InterNetworkV6) return address.ToString();
        var bytes = address.GetAddressBytes();
        Array.Clear(bytes, 8, 8); // IPv6 dùng chung hạn trong /64; IPv4 vẫn theo từng địa chỉ.
        return new IPAddress(bytes).ToString();
    }

    public static FixedWindowRateLimiterOptions AiOptions() => new()
    { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true };
}
