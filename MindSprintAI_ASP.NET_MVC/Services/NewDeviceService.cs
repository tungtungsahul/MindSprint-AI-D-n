using System.Net;
using System.Security.Cryptography;
using System.Text;
using MaxMind.GeoIP2;
using Microsoft.EntityFrameworkCore;
using MindSprintAI_ASP.NET_MVC.Data;
using MindSprintAI_ASP.NET_MVC.Data.Entities;

namespace MindSprintAI_ASP.NET_MVC.Services;

public static class DeviceLabel
{
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    // (T1) chỉ giữ nhãn ngắn, không giữ nguyên User-Agent có thể chứa dữ liệu tùy ý.
    public static string Parse(string ua)
    {
        var browser = ua.Contains("Edg/") ? "Edge" : ua.Contains("OPR/") ? "Opera"
            : ua.Contains("Firefox/") || ua.Contains("FxiOS/") ? "Firefox"
            : ua.Contains("Chrome/") || ua.Contains("CriOS/") ? "Chrome" : ua.Contains("Safari/") ? "Safari" : "Trình duyệt không xác định";
        var os = ua.Contains("Android") ? "Android" : ua.Contains("iPhone") || ua.Contains("iPad") ? "iOS"
            : ua.Contains("Windows") ? "Windows" : ua.Contains("Mac OS") ? "macOS" : ua.Contains("Linux") ? "Linux" : "Hệ điều hành không xác định";
        return $"{browser} trên {os}";
    }
}

public sealed class GeoIpLocation : IDisposable
{
    public const string Unknown = "không xác định";
    public const string Attribution = "Vị trí ước tính theo DB-IP.com";
    public const string AttributionHtml = "<a href=\"https://db-ip.com\">Vị trí ước tính theo DB-IP.com</a>";
    private readonly DatabaseReader? reader;

    // (T2b) một reader dùng chung; tuyệt đối không gọi dịch vụ tra cứu IP bên ngoài.
    public GeoIpLocation(IConfiguration config, IHostEnvironment env)
    {
        try
        {
            var path = config["GeoIp:CityDatabasePath"] ?? "App_Data/geoip/dbip-city-lite.mmdb";
            path = Path.IsPathRooted(path) ? path : Path.Combine(env.ContentRootPath, path);
            if (File.Exists(path)) reader = new DatabaseReader(path);
        }
        catch { /* (T2b) file thiếu/hỏng không được chặn đăng nhập. */ }
    }

    public static IPAddress? Normalize(IPAddress? ip) => ip?.IsIPv4MappedToIPv6 == true ? ip.MapToIPv4() : ip;
    public static bool IsPublic(IPAddress? address)
    {
        var ip = Normalize(address);
        if (ip == null || IPAddress.IsLoopback(ip) || ip.IsIPv6LinkLocal || ip.IsIPv6Multicast || ip.IsIPv6SiteLocal) return false;
        var b = ip.GetAddressBytes();
        if (b.Length == 16) return (b[0] & 0xfe) != 0xfc && !ip.Equals(IPAddress.IPv6Any);
        return b[0] != 0 && b[0] != 10 && b[0] != 127 && b[0] < 224
            && !(b[0] == 172 && b[1] >= 16 && b[1] <= 31) && !(b[0] == 192 && b[1] == 168)
            && !(b[0] == 169 && b[1] == 254) && !(b[0] == 100 && b[1] >= 64 && b[1] <= 127);
    }

    public string Estimate(IPAddress? ip)
    {
        try
        {
            if (reader != null && IsPublic(ip) && reader.TryCity(Normalize(ip)!, out var city))
            {
                var text = string.Join(", ", new[] { city.City.Name ?? city.MostSpecificSubdivision.Name, city.Country.Name }
                    .Where(x => !string.IsNullOrWhiteSpace(x)));
                if (text.Length > 0) return text.Length <= 200 ? text : text[..200];
            }
        }
        catch { /* (T2b) lookup thất bại cũng chỉ trả về không xác định. */ }
        return Unknown;
    }
    public void Dispose() => reader?.Dispose();
}

public class NewDeviceService(AppDbContext db, TimeProvider clock, IEmailSender email, NotificationService notifications,
    GeoIpLocation geo, ILogger<NewDeviceService> logger, EmailLinks links, DeviceSignalService? signals = null)
{
    // (G0, F8a) dùng chung chữ thuần cho email và chuông; nguồn vị trí giữ theo giấy phép 7c.
    // Địa chỉ của chính máy chủ (::1, 127.x) chỉ gặp khi chạy thử; ghi bằng lời thường thay vì dãy số khó hiểu.
    public static string NetworkText(string? address) =>
        address == null ? "không xác định"
        : System.Net.IPAddress.TryParse(address, out var ip) && System.Net.IPAddress.IsLoopback(ip) ? "mạng nội bộ" : address;

    public static string AlertText(string label, DateTime now, string? address, string location, bool withAttribution = true) =>
        $"Đăng nhập mới vào {Brand.Name}: {AlertDetail(label, now, address, location)}"
        + (withAttribution ? $" {GeoIpLocation.Attribution}." : "");

    // Email đã có tiêu đề "Đăng nhập mới vào MindSprint AI" nên thân thư bắt đầu thẳng bằng thiết bị.
    public static string AlertDetail(string label, DateTime now, string? address, string location) =>
        $"{label}, lúc {VnTime.Format(now)} (giờ Việt Nam), {(NetworkText(address) is "mạng nội bộ" ? "mạng nội bộ" : "mạng Internet " + NetworkText(address))}, vị trí ước tính {location}. Nếu không phải bạn, hãy đổi mật khẩu.";

    public async Task<long?> SeenAsync(ApplicationUser user, HttpContext context)
    {
        long? deviceId = null;
        try
        {
            var raw = LearnDevice.Read(context);
            if (raw?.Length != 64 || !raw.All(Uri.IsHexDigit)) return null;
            var hash = DeviceLabel.Hash(raw);
            var now = clock.GetUtcNow().UtcDateTime;
            var label = DeviceLabel.Parse(context.Request.Headers.UserAgent.ToString());
            var ip = GeoIpLocation.Normalize(context.Connection.RemoteIpAddress)?.ToString();
            var location = geo.Estimate(context.Connection.RemoteIpAddress);
            var hint = signals?.ReadLight(context) ?? new DeviceSignalResult(NetworkPrefix: DeviceSignalService.Prefix(context.Connection.RemoteIpAddress));
            bool alert;
            await using (var tx = await db.Database.BeginTransactionAsync())
            {
                await Tang7cLock.AcquireAsync(db, "device:" + user.Id);
                var known = await db.KnownDevices.SingleOrDefaultAsync(d => d.UserId == user.Id && d.DeviceHash == hash);
                // (F8a) dùng lại id hàng để claim 7c2 vẫn trỏ đúng thiết bị; thiếu dấu hiệu thì không gộp.
                if (known == null && hint.LightHash != null && hint.NetworkPrefix != null)
                    known = await db.KnownDevices.Where(d => d.UserId == user.Id && d.LightHash == hint.LightHash
                        && d.NetworkPrefix == hint.NetworkPrefix && d.LastSeenAt >= now.AddDays(-30))
                        .OrderByDescending(d => d.LastSeenAt).FirstOrDefaultAsync();
                alert = known == null && await db.KnownDevices.AnyAsync(d => d.UserId == user.Id);
                if (known == null)
                {
                    known = new() { UserId = user.Id, DeviceHash = hash, FirstSeenAt = now };
                    db.KnownDevices.Add(known);
                }
                // (T5) LastSeenAt đã là lần đăng nhập thành công, không cập nhật bởi request thường.
                known.LastSeenAt = now; known.Label = label;
                known.LastSignInIp = ip; known.LastLocation = location;
                known.DeviceHash = hash;
                known.LightHash = hint.LightHash ?? known.LightHash;
                known.NetworkPrefix = hint.NetworkPrefix;
                if (alert) await notifications.AddAsync(user.Id, NotificationType.NewDevice, new(Text(), "/Account"));
                await db.SaveChangesAsync(); await tx.CommitAsync();
                deviceId = known.Id;
            }
            if (!alert) return deviceId; // (T3, T5) thiết bị đầu tiên cũng lưu IP/vị trí, không cảnh báo.
            // Push và email độc lập: một kênh hỏng không làm mất kênh còn lại.
            try { await notifications.PushAsync(user.Id); }
            catch { logger.LogWarning("Không đẩy được cảnh báo thiết bị mới."); }
            var account = links.To("/Account");
            await email.SendAsync(user.Email!, $"Đăng nhập mới vào {Brand.Name}",
                $"<p>{WebUtility.HtmlEncode(AlertDetail(label, now, ip, location))}</p>"
                // Chỉ ghi nguồn khi thật sự dùng dữ liệu DB-IP (có vị trí).
                + (location == GeoIpLocation.Unknown ? "" : $"<p>{GeoIpLocation.AttributionHtml}</p>")
                + $"<p><a href=\"{WebUtility.HtmlEncode(account)}\">Tài khoản</a> · <a href=\"{WebUtility.HtmlEncode(account)}#protective-lock\">Khóa tài khoản</a></p>");

            // Chuông là chữ thuần nên kèm nguồn trong câu; email đã có link nguồn riêng (CC BY 4.0).
            string Text(bool withAttribution = true) => AlertText(label, now, ip, location, withAttribution);
        }
        catch
        {
            // (T4) không log exception vì provider có thể kèm email/IP/tham số SQL.
            logger.LogWarning("Không ghi nhận hoặc gửi được cảnh báo thiết bị mới.");
            foreach (var entry in db.ChangeTracker.Entries().Where(e => e.Entity is KnownDevice || e.Entity is Notification && e.State == EntityState.Added).ToList())
                entry.State = EntityState.Detached;
        }
        return deviceId; // (T7) email lỗi vẫn có phiên; DB lỗi không được cấp cookie thiếu claim.
    }
}

internal static class Tang7cLock
{
    // Khóa transaction tại SQL Server, dùng chung giữa các instance ứng dụng.
    public static Task AcquireAsync(AppDbContext db, string key) => db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @r int; EXEC @r = sp_getapplock @Resource={key}, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000; IF @r < 0 THROW 51000, 'Cannot acquire application lock', 1;");
}
