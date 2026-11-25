using System.Net;
using System.Text.RegularExpressions;

namespace MindSprintAI_ASP.NET_MVC.Services;

// Khung email mang phong cách MindSprint AI (nền Acid nhạt, thẻ trắng viền mực, bóng cứng, nút "nhấn thật").
// Email không tải được CSS/phông của web, nên mọi kiểu ghi thẳng vào thẻ (inline) và dùng bảng cho Outlook.
// Không dùng phông hay ảnh từ bên ngoài (nguyên tắc tự chủ): chữ rơi về Segoe UI/Arial nếu máy không có Space Grotesk.
// Bóng cứng dùng viền dưới/phải dày thay vì box-shadow vì Gmail/Outlook bỏ box-shadow.
// Giao diện tối: Apple Mail, Outlook (Mac/iPhone), Thunderbird đọc @media prefers-color-scheme; Outlook.com đọc [data-ogsc]/[data-ogsb].
// Gmail bỏ qua cả hai và tự đảo màu theo cách riêng, nên bản sáng phải tự đọc được khi bị đảo (chữ mực trên nền sáng, không ảnh).
public static class EmailLayout
{
    private const string Ink = "#09090B", Acid = "#D2E823", Paper = "#F3F7DC", Muted = "#3F3D38", Blue = "#2F4BFF";
    private const string Font = "'Space Grotesk','Segoe UI','Helvetica Neue',Arial,sans-serif";
    private const string Mark = "<!--mindsprint-email-->";

    // Màu tối lấy đúng bảng [data-bs-theme="dark"] trong wwwroot/css/mindsprint.css để email khớp với web.
    private const string DarkBg = "#131317", DarkSurface = "#1C1C22", DarkMuted = "#26262E", DarkText = "#E6E1D6",
        DarkTextMuted = "#B9B4A7", DarkLine = "#4A4740", DarkAcid = "#AEBD3A", DarkBlue = "#8E98D6";

    private static readonly (string Css, string Rule)[] DarkRules =
    [
        ("rk-bg", $"background:{DarkBg}!important;color:{DarkText}!important;"),
        ("rk-logo", $"background:{DarkMuted}!important;color:{DarkAcid}!important;border-color:{DarkLine}!important;"),
        ("rk-card", $"background:{DarkSurface}!important;border-color:{DarkLine}!important;"),
        ("rk-band", $"background:{DarkAcid}!important;border-color:{DarkLine}!important;"),
        ("rk-text", $"color:{DarkText}!important;"),
        ("rk-mark", $"border-color:{DarkAcid}!important;"),
        ("rk-muted", $"color:{DarkTextMuted}!important;"),
        ("rk-link", $"color:{DarkBlue}!important;"),
        ("rk-btn", $"background:{DarkAcid}!important;border-color:{DarkLine}!important;"),
        ("rk-on-acid", $"color:{Ink}!important;"),
        ("rk-code", $"background:{DarkAcid}!important;color:{Ink}!important;border-color:{DarkLine}!important;"),
    ];

    private static readonly string DarkStyle =
        "@media (prefers-color-scheme:dark){" + string.Concat(DarkRules.Select(r => $".{r.Css}{{{r.Rule}}}")) + "}"
        + string.Concat(DarkRules.Select(r => $"[data-ogsc] .{r.Css},[data-ogsb] .{r.Css}{{{r.Rule}}}"));

    private static readonly Regex Cta = new(@"<p>\s*<a href=""([^""]+)"">([^<]+)</a>\s*</p>", RegexOptions.Compiled);
    private static readonly Regex Link = new(@"<a href=""([^""]*)"">", RegexOptions.Compiled);
    private static readonly Regex Paragraph = new(@"<p>", RegexOptions.Compiled);
    private static readonly Regex Code = new(@"<b>([0-9]{6})</b>", RegexOptions.Compiled);

    public static string Wrap(string subject, string body)
    {
        if (body.Contains(Mark, StringComparison.Ordinal)) return body;
        // Chỉ mã hóa 4 ký tự đặc biệt: WebUtility.HtmlEncode đổi cả chữ có dấu thành &#225; làm email khó đọc khi xem mã nguồn.
        var title = subject.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        var html = Cta.Replace(body, Button);
        html = Link.Replace(html, m => $"<a href=\"{m.Groups[1].Value}\" class=\"rk-link\" style=\"color:{Blue};font-weight:700;text-decoration:underline;\">");
        html = Code.Replace(html, m => $"<b class=\"rk-code\" style=\"font-family:'JetBrains Mono',Consolas,'Courier New',monospace;font-size:24px;letter-spacing:4px;background:{Acid};color:{Ink};border:2px solid {Ink};padding:2px 10px;\">{m.Groups[1].Value}</b>");
        html = Paragraph.Replace(html, $"<p class=\"rk-text\" style=\"margin:0 0 16px;font-size:16px;line-height:1.65;color:{Ink};\">");
        return $"""
{Mark}<!doctype html>
<html lang="vi"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><meta name="color-scheme" content="light dark"><meta name="supported-color-schemes" content="light dark"><title>{title}</title>
<style>:root{"{"}color-scheme:light dark;supported-color-schemes:light dark;{"}"}{DarkStyle}</style></head>
<body class="rk-bg" style="margin:0;padding:0;background:{Paper};font-family:{Font};color:{Ink};">
<table role="presentation" width="100%" cellpadding="0" cellspacing="0" class="rk-bg" style="background:{Paper};"><tr><td align="center" style="padding:28px 12px;">
<table role="presentation" width="560" cellpadding="0" cellspacing="0" style="width:100%;max-width:560px;">
<tr><td style="padding:0 0 14px;">
<span class="rk-logo" style="display:inline-block;background:{Ink};color:{Acid};font-family:{Font};font-size:24px;font-weight:700;letter-spacing:-0.5px;padding:8px 18px;border:2px solid {Ink};border-bottom:5px solid {Ink};border-right:5px solid {Ink};">{Brand.Name}</span>
</td></tr>
<tr><td class="rk-card" style="background:#FFFFFF;border:2px solid {Ink};border-bottom:7px solid {Ink};border-right:7px solid {Ink};padding:0;">
<table role="presentation" width="100%" cellpadding="0" cellspacing="0"><tr><td class="rk-band" style="background:{Acid};height:10px;line-height:10px;font-size:0;border-bottom:2px solid {Ink};">&nbsp;</td></tr>
<tr><td style="padding:28px 28px 12px;font-family:{Font};">
<h1 class="rk-text" style="margin:0 0 20px;font-family:{Font};font-size:26px;line-height:1.25;font-weight:700;letter-spacing:-0.5px;color:{Ink};"><span class="rk-mark" style="border-bottom:6px solid {Acid};">{title}</span></h1>
{html}
</td></tr></table>
</td></tr>
<tr><td class="rk-muted" style="padding:18px 4px 0;font-family:{Font};font-size:13px;line-height:1.5;color:{Muted};">
Thư tự động từ {Brand.Name} — nơi lớp học ôn tập và làm bài kiểm tra cùng nhau.
</td></tr>
</table></td></tr></table></body></html>
""";
    }

    // Bản chữ thường đi kèm bản HTML (multipart/alternative). Liên kết in rõ địa chỉ để bấm/chép được trong mọi ứng dụng thư.
    public static string PlainText(string body)
    {
        var s = Regex.Replace(body, @"<(style|head|title)\b.*?</\1>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"<a\b[^>]*href=""([^""]*)""[^>]*>(.*?)</a>", m =>
        {
            var url = WebUtility.HtmlDecode(m.Groups[1].Value);
            var text = WebUtility.HtmlDecode(Regex.Replace(m.Groups[2].Value, "<[^>]+>", "")).Trim();
            return text.Length == 0 || text == url ? url : $"{text}: {url}";
        }, RegexOptions.Singleline | RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"<br\s*/?>|</(p|div|li|h[1-6]|tr|table)>", "\n", RegexOptions.IgnoreCase);
        s = WebUtility.HtmlDecode(Regex.Replace(s, "<[^>]+>", ""));
        s = Regex.Replace(Regex.Replace(s, @"[ \t]+", " "), @" ?\n ?", "\n");
        s = Regex.Replace(s, @"\n{3,}", "\n\n");
        return s.Trim() + $"\n\n--\nThư tự động từ {Brand.Name}.";
    }

    // Đoạn chỉ có một liên kết thành nút lớn. Chữ nút là địa chỉ dài thì đổi thành "Mở liên kết" và in địa chỉ nhỏ bên dưới.
    private static string Button(Match m)
    {
        var url = m.Groups[1].Value;
        var text = m.Groups[2].Value;
        // Ghi nguồn dữ liệu vị trí (DB-IP, bắt buộc giữ) là chữ nhỏ, không phải việc người nhận cần bấm.
        if (url.Contains("db-ip.com", StringComparison.OrdinalIgnoreCase))
            return $"<p class=\"rk-muted\" style=\"margin:0 0 16px;font-size:13px;line-height:1.5;color:{Muted};\"><a href=\"{url}\" class=\"rk-muted\" style=\"color:{Muted};text-decoration:underline;\">{text}</a></p>";
        var isUrl = text.StartsWith("http", StringComparison.OrdinalIgnoreCase);
        var label = isUrl ? "Mở liên kết" : text;
        var fallback = isUrl
            ? $"<p class=\"rk-muted\" style=\"margin:0 0 16px;font-size:13px;line-height:1.5;color:{Muted};word-break:break-all;\">Nếu nút không bấm được, hãy sao chép địa chỉ này vào trình duyệt: {text}</p>"
            : "";
        return $"<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" style=\"margin:4px 0 20px;\"><tr><td class=\"rk-btn\" style=\"background:{Acid};border:2px solid {Ink};border-bottom:5px solid {Ink};border-right:5px solid {Ink};\">"
            + $"<a href=\"{url}\" class=\"rk-on-acid\" style=\"display:block;padding:13px 24px;font-family:{Font};font-size:16px;font-weight:700;color:{Ink};text-decoration:none;\">{label}</a></td></tr></table>{fallback}";
    }
}
