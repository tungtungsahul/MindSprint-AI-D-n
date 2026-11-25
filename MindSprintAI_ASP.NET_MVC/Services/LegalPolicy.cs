using System.Security.Claims;

namespace MindSprintAI_ASP.NET_MVC.Services;

// Chữ chính thức ở docs/PHAP_LY_VAN_BAN.md. Sửa nội dung lớn của Chính sách/Điều khoản thì tăng CurrentVersion:
// mọi người dùng sẽ được hỏi đồng ý lại ở lần mở trang kế tiếp.
public static class LegalPolicy
{
    public const int CurrentVersion = 1;
    public const string Claim = "qem_policy";

    public static int? AcceptedVersion(ClaimsPrincipal user) =>
        int.TryParse(user.FindFirstValue(Claim), out var v) ? v : null;

    public static bool NeedsAcceptance(ClaimsPrincipal user) =>
        user.Identity?.IsAuthenticated == true && (AcceptedVersion(user) ?? 0) < CurrentVersion;
}

// Người dùng tự điền trên máy chạy thật (Legal__OwnerName…); còn trống thì hiện đúng chỗ cần điền.
public sealed record LegalInfo(string OwnerName, string ContactEmail, string EffectiveDate, string Domain)
{
    public static LegalInfo From(IConfiguration config)
    {
        static string Or(string? value, string blank) => string.IsNullOrWhiteSpace(value) ? blank : value.Trim();
        var host = Uri.TryCreate(config["App:BaseUrl"], UriKind.Absolute, out var uri) ? uri.Host : null;
        return new(Or(config["Legal:OwnerName"], "[ĐIỀN: họ tên]"),
            Or(config["Legal:ContactEmail"], "[ĐIỀN: email liên hệ]"),
            Or(config["Legal:EffectiveDate"], "[ĐIỀN: ngày hiệu lực]"),
            Or(host, "[ĐIỀN: tên miền]"));
    }
}

// Trang vẫn mở được khi người dùng chưa đồng ý (đọc chính sách, đồng ý, đăng xuất, trang lỗi).
[AttributeUsage(AttributeTargets.Method)]
public sealed class AllowWithoutPolicyAttribute : Attribute;
