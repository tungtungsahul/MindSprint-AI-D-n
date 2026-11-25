using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace MindSprintAI_ASP.NET_MVC.Services;

public record DeviceSignalResult(string? LightHash = null, string? FullHash = null, string? NetworkPrefix = null);

// (F7) không lưu/log thông số gốc; lỗi thu thập chỉ có nghĩa là chưa biết.
public sealed class DeviceSignalKey
{
    private readonly byte[]? key;
    public DeviceSignalKey(IConfiguration config, IHostEnvironment env, IDataProtectionProvider protection, ILogger<DeviceSignalKey>? logger = null)
    {
        try { key = Load(config["DeviceSignals:HmacKey"], Path.Combine(env.ContentRootPath, "App_Data"), protection); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or CryptographicException or FormatException or InvalidOperationException)
        {
            // (F9) không dùng khóa tạm/khóa yếu, không đổi file hỏng và không chặn học vì nhận diện lỗi.
            logger?.LogWarning("Chưa đọc được khóa nhận diện máy. Đặc điểm máy tạm thời chưa xác định.");
        }
    }

    public static byte[] Load(string? configured, string directory, IDataProtectionProvider protection)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var configuredKey = Convert.FromBase64String(configured);
            if (configuredKey.Length < 32) throw new InvalidOperationException("DeviceSignals:HmacKey requires at least 32 bytes.");
            return configuredKey;
        }
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "device-signals.key");
        var protector = protection.CreateProtector("MindSprint.DeviceSignals.Key.v1");
        // (F7) khóa file giữa các tiến trình; không thay khóa khi file hỏng/không giải bảo vệ được.
        using var gate = OpenGate(path + ".lock");
        if (File.Exists(path))
        {
            var stored = protector.Unprotect(File.ReadAllBytes(path));
            if (stored.Length < 32) throw new CryptographicException("Invalid device signal key.");
            return stored;
        }
        var generated = RandomNumberGenerator.GetBytes(32);
        var temporary = path + ".tmp";
        File.WriteAllBytes(temporary, protector.Protect(generated));
        File.Move(temporary, path, true);
        return generated;
    }

    private static FileStream OpenGate(string path)
    {
        for (var i = 0; ; i++)
        {
            try { return new(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (i < 50) { Thread.Sleep(100); }
        }
    }

    public string? Sign(string canonical) => key == null ? null : Compute(key, canonical);
    public static string Compute(byte[] key, string canonical) =>
        Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(canonical)));
}

public sealed class DeviceSignalService(DeviceSignalKey key, IDataProtectionProvider protection, TimeProvider clock)
{
    private const string Cookie = "mindsprint_machine_hint";
    private readonly IDataProtector protector = protection.CreateProtector("MindSprint.DeviceSignals.Light.v1");
    private static readonly string[] LightKeys = ["screen", "timezone", "language", "platform", "cores", "memory"];
    private static readonly string[] FullKeys = [.. LightKeys, "canvas", "webgl", "audio", "fonts"];

    public DeviceSignalResult Read(string? json, IPAddress? address, bool full)
    {
        var prefix = Prefix(address);
        if (string.IsNullOrWhiteSpace(json) || json.Length > 16000) return new(NetworkPrefix: prefix);
        try
        {
            var signals = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            string? Hash(string[] keys) => signals != null && keys.All(k => signals.TryGetValue(k, out var v)
                && !string.IsNullOrWhiteSpace(v) && v.Length <= 4096)
                ? key.Sign(JsonSerializer.Serialize(keys.Order(StringComparer.Ordinal).Select(k => new[] { k, signals[k] }))) : null;
            return new(Hash(LightKeys), full ? Hash(FullKeys) : null, prefix);
        }
        catch (JsonException) { return new(NetworkPrefix: prefix); }
    }

    public static string? Prefix(IPAddress? address)
    {
        var ip = GeoIpLocation.Normalize(address);
        if (ip == null) return null;
        var bytes = ip.GetAddressBytes();
        return bytes.Length == 4 ? $"{bytes[0]}.{bytes[1]}.{bytes[2]}.0/24"
            : Convert.ToHexString(bytes.AsSpan(0, 6)) + "/48";
    }

    public void RememberLight(HttpContext context, string? json)
    {
        var result = Read(json, context.Connection.RemoteIpAddress, false);
        if (result.LightHash == null) return;
        var value = JsonSerializer.Serialize(new LightHint(result.LightHash, result.NetworkPrefix,
            LearnDevice.Read(context), clock.GetUtcNow().AddHours(24).ToUnixTimeSeconds()));
        context.Response.Cookies.Append(Cookie, protector.Protect(value), new CookieOptions
        { HttpOnly = true, Secure = context.Request.IsHttps, SameSite = SameSiteMode.Lax, IsEssential = true, Path = "/", MaxAge = TimeSpan.FromHours(24) });
    }

    public DeviceSignalResult ReadLight(HttpContext context)
    {
        var prefix = Prefix(context.Connection.RemoteIpAddress);
        try
        {
            var value = context.Request.Cookies[Cookie];
            if (value == null) return new(NetworkPrefix: prefix);
            var hint = JsonSerializer.Deserialize<LightHint>(protector.Unprotect(value));
            if (hint == null || hint.Expires < clock.GetUtcNow().ToUnixTimeSeconds()
                || hint.Device != LearnDevice.Read(context) || hint.Prefix != prefix) return new(NetworkPrefix: prefix);
            return new(hint.Hash, NetworkPrefix: prefix);
        }
        catch (Exception e) when (e is CryptographicException or JsonException or FormatException)
        { return new(NetworkPrefix: prefix); }
    }
    private record LightHint(string Hash, string? Prefix, string? Device, long Expires);
}

public record DeviceHintNote(string Title, string Detail);
public static class DeviceHintRules
{
    // (F8c, F9) chỉ ghi chú, không bao giờ quyết định thay giảng viên.
    public static DeviceHintNote? Note(string? oldLight, string? oldFull, string? oldNetwork,
        string? newLight, string? newFull, string? newNetwork)
    {
        var oldHash = oldFull != null && newFull != null ? oldFull : oldLight;
        var newHash = oldFull != null && newFull != null ? newFull : newLight;
        if (oldHash == null || newHash == null || oldNetwork == null || newNetwork == null) return null;
        if (oldHash != newHash) return new("Đây là một thiết bị khác", "Khác loại thiết bị hoặc trình duyệt so với thiết bị đang làm bài.");
        return oldNetwork == newNetwork
            ? new("Có vẻ vẫn là thiết bị cũ của học viên", "Cùng loại thiết bị, cùng trình duyệt và cùng mạng Wi-Fi/Internet với thiết bị đang làm bài. Thường gặp khi trình duyệt bị xóa dữ liệu hoặc học viên mở lại trình duyệt.")
            : new("Cùng loại thiết bị nhưng khác mạng", "Giống thiết bị đang làm bài nhưng đang dùng mạng Wi-Fi/Internet khác, ví dụ chuyển từ Wi-Fi sang 4G.");
    }
}
