using System.Text.Json;

namespace MindSprintAI_ASP.NET_MVC.Services;

public record BotVerification(bool Verified, string? Error = null);

public static class SecretConfig
{
    // Khóa thử công khai của Cloudflare (1x/2x/3x0000…AA): luôn qua hoặc luôn trượt, không chặn bot thật.
    public static bool IsTurnstileTestKey(string? secret) =>
        secret is { Length: > 6 } && secret[0] is '1' or '2' or '3' && secret.AsSpan(1).StartsWith("x0000");
}

public class TurnstileService(HttpClient http, IConfiguration config)
{
    public string SiteKey => config["Turnstile:SiteKey"] ?? "";
    public bool Configured => !string.IsNullOrWhiteSpace(SiteKey) && !string.IsNullOrWhiteSpace(config["Turnstile:SecretKey"]);

    public async Task<BotVerification> VerifyAsync(string? token, string? remoteIp)
    {
        // (G0) giải thích bước xác minh bằng việc người dùng cần làm.
        if (!Configured) return new(false, "Chưa bật xác minh người thật. Hãy báo quản trị viên.");
        if (string.IsNullOrWhiteSpace(token) || token.Length > 2048) return new(false, "Hãy xác minh bạn là người thật rồi thử lại.");
        try
        {
            using var body = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["secret"] = config["Turnstile:SecretKey"]!, ["response"] = token, ["remoteip"] = remoteIp ?? ""
            });
            using var response = await http.PostAsync("https://challenges.cloudflare.com/turnstile/v0/siteverify", body);
            if (!response.IsSuccessStatusCode) return new(false, "Chưa xác minh được bạn là người thật. Hãy thử lại.");
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            // (55, 73) token trong form không phải bằng chứng; chỉ tin success từ Cloudflare.
            return json.RootElement.TryGetProperty("success", out var success) && success.ValueKind == JsonValueKind.True
                ? new(true) : new(false, "Kết quả xác minh người thật không hợp lệ hoặc đã hết hạn. Hãy thử lại.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return new(false, "Không kết nối được nơi xác minh người thật. Hãy thử lại sau.");
        }
    }
}
