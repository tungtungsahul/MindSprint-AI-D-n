namespace MindSprintAI_ASP.NET_MVC.Services;

public interface ILearnDevice
{
    string Id { get; }
}

public sealed class LearnDevice(IHttpContextAccessor accessor) : ILearnDevice
{
    public const string CookieName = "qem_device";
    public string Id => Read(accessor.HttpContext) ?? throw new BusinessException("Không nhận dạng được thiết bị. Hãy tải lại trang.");

    public static string? Read(HttpContext? context) =>
        context?.Items[CookieName] as string ?? context?.Request.Cookies[CookieName];
}

public sealed class LearnDeviceMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var existing = context.Request.Cookies[LearnDevice.CookieName];
        if (existing?.Length != 64 || !existing.All(Uri.IsHexDigit))
        {
            var id = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
            context.Items[LearnDevice.CookieName] = id;
            context.Response.Cookies.Append(LearnDevice.CookieName, id, new CookieOptions
            {
                HttpOnly = true, Secure = context.Request.IsHttps, SameSite = SameSiteMode.Lax,
                MaxAge = TimeSpan.FromDays(365), Path = "/", IsEssential = true
            });
        }
        await next(context);
    }
}
