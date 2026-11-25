using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace MindSprintAI_ASP.NET_MVC.Services;

public static class DeploymentSecurity
{
    public static void ConfigureProxy(ForwardedHeadersOptions options, IConfiguration config)
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.ForwardLimit = 1;
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
        var proxies = config.GetSection("ReverseProxy:KnownProxies").Get<string[]>() ?? ["127.0.0.1", "::1"];
        if (proxies.Length == 0) throw new InvalidOperationException("ReverseProxy:KnownProxies phải có ít nhất một IP proxy tin cậy.");
        foreach (var address in proxies) options.KnownProxies.Add(IPAddress.Parse(address));
    }

    public static void Validate(IConfiguration config, bool isDevelopment, ILogger logger)
    {
        if (isDevelopment) return;
        var baseUrl = config["App:BaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl) || !baseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || !Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host)
            || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            Fail("App:BaseUrl bắt buộc là URL HTTPS công khai khi không chạy Development; đặt App__BaseUrl.");
        if ((config["AllowedHosts"] ?? "").Split(';', StringSplitOptions.TrimEntries).Contains("*"))
            logger.LogWarning("AllowedHosts vẫn là '*': chạy thật phải đặt AllowedHosts bằng tên miền thật.");
        foreach (var key in new[] { "SiteKey", "SecretKey" })
            if (string.IsNullOrWhiteSpace(config["Turnstile:" + key]) || SecretConfig.IsTurnstileTestKey(config["Turnstile:" + key]))
                Fail("Turnstile đang trống hoặc là khóa thử của Cloudflare: đặt Turnstile__SiteKey/Turnstile__SecretKey thật bằng biến môi trường.");

        void Fail(string message)
        {
            logger.LogCritical("{ConfigurationError}", message);
            throw new InvalidOperationException(message);
        }
    }
}
