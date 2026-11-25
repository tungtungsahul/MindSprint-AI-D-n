using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using UglyToad.PdfPig;

namespace MindSprintAI_ASP.NET_MVC.Services;

public record NotebookUrlSource(string Title, string Content, string Url);

// Thêm nguồn Sổ tay từ đường link — chuyển từ MindSprint-AI-D-n NotebookUrlReader + NotebookDocumentExtractor.
// An toàn như nhóm: chỉ kết nối tới địa chỉ công khai (kiểm ngay lúc kết nối, chống đổi DNS), tự kiểm từng lần chuyển hướng,
// không cookie/proxy, giới hạn 2 MB (trang chữ) / 10 MB (PDF) và 15 giây. Bỏ phần dựng trang bằng trình duyệt ẩn của nhóm.
public static class NotebookUrlReader
{
    public const string ClientName = "notebook-url";
    public const int MaxRedirects = 5, MaxTextBytes = 2_000_000, MaxDocumentBytes = 10 * 1024 * 1024;
    private static BusinessException NotAllowed() => new("Đường link không hợp lệ hoặc không được phép. Chỉ nhận trang web công khai.");

    public static SocketsHttpHandler CreateHandler() => new()
    {
        AllowAutoRedirect = false, UseCookies = false, UseProxy = false, ConnectTimeout = TimeSpan.FromSeconds(10),
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
        ConnectCallback = async (context, token) =>
        {
            foreach (var address in await PublicAddressesAsync(context.DnsEndPoint.Host, token))
            {
                var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                try { await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), token); return new NetworkStream(socket, ownsSocket: true); }
                catch (SocketException) { socket.Dispose(); }
                catch { socket.Dispose(); throw; }
            }
            throw new HttpRequestException("Không thể kết nối đến trang nguồn.");
        }
    };

    public static async Task<NotebookUrlSource> ReadAsync(HttpClient http, string? url, CancellationToken cancellationToken = default)
    {
        if (url is null || url.Length > 4096 || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)) throw NotAllowed();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        var token = deadline.Token;
        try
        {
            for (var redirects = 0; ; redirects++)
            {
                if (uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo)) throw NotAllowed();
                uri = new UriBuilder(uri) { Fragment = "" }.Uri;
                await PublicAddressesAsync(uri.DnsSafeHost, token);
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (compatible; MindSprintAI/1.0)");
                request.Headers.Accept.ParseAdd("text/html,application/xhtml+xml,application/pdf,text/plain;q=0.9,*/*;q=0.5");
                request.Headers.AcceptLanguage.ParseAdd("vi-VN,vi;q=0.9,en;q=0.8");
                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
                var status = (int)response.StatusCode;
                if (status is 301 or 302 or 303 or 307 or 308)
                {
                    if (redirects >= MaxRedirects) throw new BusinessException("Trang nguồn chuyển hướng quá nhiều lần. Hãy dán đường link cuối cùng trong trình duyệt.");
                    if (response.Headers.Location is not { } location || !Uri.TryCreate(uri, location, out var next)) throw new BusinessException("Trang nguồn chuyển hướng không hợp lệ.");
                    uri = next;
                    continue;
                }
                if (!response.IsSuccessStatusCode)
                    throw new BusinessException(status is 401 or 403 ? "Trang nguồn yêu cầu đăng nhập hoặc chặn tải tự động. Hãy dán văn bản hoặc tải tệp."
                        : status == 404 ? "Không tìm thấy trang nguồn. Kiểm tra lại đường link." : "Trang nguồn đang lỗi. Hãy thử lại sau hoặc dán văn bản/tải tệp.");
                var media = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant() ?? "";
                // "pdf" ở đây = tài liệu nhị phân (PDF, Word, PowerPoint, Excel): giới hạn 10 MB thay vì 2 MB.
                var pdf = media == "application/pdf" || uri.AbsolutePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) || OfficeType(uri, media) != null;
                var limit = pdf ? MaxDocumentBytes : MaxTextBytes;
                if (response.Content.Headers.ContentLength > limit) throw TooLarge(pdf);
                using var stream = await response.Content.ReadAsStreamAsync(token);
                using var buffer = new MemoryStream();
                var chunk = new byte[8192];
                int read;
                while ((read = await stream.ReadAsync(chunk, token)) > 0)
                {
                    if (buffer.Length + read > limit) throw TooLarge(pdf);
                    buffer.Write(chunk, 0, read);
                }
                return Extract(uri, buffer.ToArray(), media, pdf, response.Content.Headers.ContentType?.CharSet);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new BusinessException("Trang nguồn phản hồi quá chậm. Hãy thử lại hoặc dán văn bản/tải tệp."); }
        catch (HttpRequestException error)
        {
            if (error.GetBaseException() is BusinessException blocked) throw blocked;
            throw new BusinessException("Không kết nối được trang nguồn. Kiểm tra đường link hoặc dán văn bản/tải tệp.");
        }
    }

    private static BusinessException TooLarge(bool pdf) => new($"Nguồn vượt giới hạn tải ({(pdf ? "10" : "2")} MB). Hãy chọn tài liệu nhỏ hơn hoặc dán phần cần học.");

    // DOCX/PPTX/XLSX qua đường link như NotebookDocumentExtractor của nhóm: nhận theo đuôi tệp hoặc kiểu nội dung máy chủ báo.
    public static string? OfficeType(Uri url, string media)
    {
        var ext = Path.GetExtension(url.AbsolutePath).ToLowerInvariant();
        if (OfficeText.Types.Contains(ext)) return ext;
        return media switch
        {
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => ".docx",
            "application/vnd.openxmlformats-officedocument.presentationml.presentation" => ".pptx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" => ".xlsx",
            _ => null
        };
    }

    public static NotebookUrlSource Extract(Uri url, byte[] bytes, string media, bool pdf, string? charset)
    {
        if (OfficeType(url, media) is { } office)
            return Checked(new(Path.GetFileNameWithoutExtension(url.AbsolutePath) is { Length: > 0 } name ? name : url.Host, OfficeText.Extract(bytes, office), url.ToString()));
        if (pdf)
        {
            try
            {
                using var document = PdfDocument.Open(bytes);
                var text = string.Join("\n\n", document.GetPages().Select(p => p.Text));
                return Checked(new(Path.GetFileNameWithoutExtension(url.AbsolutePath) is { Length: > 0 } n ? n : url.Host, text, url.ToString()));
            }
            catch (Exception ex) when (ex is not BusinessException) { throw new BusinessException("Không đọc được tệp PDF ở đường link này."); }
        }
        Encoding encoding;
        try { encoding = charset is { Length: > 0 } ? Encoding.GetEncoding(charset) : Encoding.UTF8; } catch (ArgumentException) { encoding = Encoding.UTF8; }
        var raw = encoding.GetString(bytes);
        // XML/RSS/Atom (nhóm đọc được): bỏ thẻ, giữ chữ; mô tả RSS thường chứa HTML đã mã hóa nên bỏ thẻ hai lượt.
        if (media.Contains("xml") && !media.Contains("html") || Regex.IsMatch(raw, @"\A\s*(<\?xml[^>]*>\s*)?<(rss|feed)\b", RegexOptions.IgnoreCase))
        {
            static string Strip(string s) => System.Net.WebUtility.HtmlDecode(Regex.Replace(Regex.Replace(s, @"<!\[CDATA\[|\]\]>", ""), "<[^>]+>", "\n"));
            return Checked(new(url.Host, Regex.Replace(Strip(Strip(raw)), @"\s*\n\s*", "\n").Trim(), url.ToString()));
        }
        if (!media.Contains("html") && !Regex.IsMatch(raw, @"<html\b|<body\b", RegexOptions.IgnoreCase))
            return Checked(new(url.Host, raw, url.ToString()));
        return Checked(Html(url, raw));
    }

    // Lấy phần nội dung chính của trang như nhóm: bỏ script/style/menu/chân trang…, ưu tiên main/article.
    public static NotebookUrlSource Html(Uri url, string html)
    {
        var document = new HtmlDocument { OptionMaxNestedChildNodes = 128 };
        document.LoadHtml(html);
        var title = HtmlEntity.DeEntitize(document.DocumentNode.SelectSingleNode("//title")?.InnerText ?? "").Trim();
        if (title.Contains("Just a moment", StringComparison.OrdinalIgnoreCase))
            throw new BusinessException("Trang đang yêu cầu xác minh người dùng. Hãy mở trang trong trình duyệt rồi dán nội dung.");
        foreach (var node in document.DocumentNode.SelectNodes("//script|//style|//noscript|//nav|//header|//footer|//aside|//svg|//canvas|//form|//button|//iframe|//*[@hidden]")?.ToList() ?? [])
            node.Remove();
        var main = document.DocumentNode.SelectSingleNode("//*[@id='mw-content-text']") ?? document.DocumentNode.SelectSingleNode("//main|//article|//*[@role='main']")
            ?? document.DocumentNode.SelectSingleNode("//body") ?? document.DocumentNode;
        var text = new StringBuilder();
        Write(main, text, 0);
        return new(title.Length > 0 ? title : url.Host, text.ToString(), url.ToString());
    }

    private static readonly HashSet<string> Blocks = ["p", "div", "section", "article", "li", "ul", "ol", "h1", "h2", "h3", "h4", "h5", "h6", "tr", "table", "blockquote", "pre"];
    private static void Write(HtmlNode node, StringBuilder output, int depth)
    {
        if (depth > 128) return;
        if (node.NodeType == HtmlNodeType.Text) { output.Append(HtmlEntity.DeEntitize(node.InnerText)); return; }
        if (node.Name == "br") output.Append('\n');
        var block = Blocks.Contains(node.Name);
        if (block) output.Append('\n');
        foreach (var child in node.ChildNodes) Write(child, output, depth + 1);
        if (block) output.Append('\n');
    }

    private static NotebookUrlSource Checked(NotebookUrlSource source)
    {
        var content = Regex.Replace(Regex.Replace(source.Content, @"[ \t]+", " "), @"\n\s*\n\s*\n+", "\n\n").Trim();
        if (content.Length < 50) throw new BusinessException("Trang có quá ít nội dung để học. Hãy dùng trang bài viết/tài liệu đầy đủ hoặc dán văn bản.");
        return source with { Content = content, Title = source.Title.Length > 200 ? source.Title[..200] : source.Title };
    }

    private static async Task<IPAddress[]> PublicAddressesAsync(string host, CancellationToken token)
    {
        IPAddress[] addresses;
        try { addresses = IPAddress.TryParse(host.Trim('[', ']'), out var ip) ? [ip] : await Dns.GetHostAddressesAsync(host, token); }
        catch (SocketException) { throw new BusinessException("Không tìm thấy địa chỉ trang nguồn. Kiểm tra lại đường link."); }
        if (addresses.Length == 0 || addresses.Any(ip => !IsPublicAddress(ip))) throw NotAllowed();
        return addresses;
    }

    // Chép nguyên quy tắc địa chỉ công khai của nhóm (chặn mạng nội bộ, loopback, link-local, dải tài liệu/thử nghiệm…).
    public static bool IsPublicAddress(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (IPAddress.IsLoopback(ip)) return false;
        var b = ip.GetAddressBytes();
        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
            return (b[0] & 0xE0) == 0x20
                && !(b[0] == 0x20 && b[1] == 0x01 && b[2] <= 1)
                && !(b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x0D && b[3] == 0xB8)
                && !(b[0] == 0x20 && b[1] == 0x02)
                && !(b[0] == 0x3F && (b[1] & 0xF0) == 0xF0);
        return ip.AddressFamily == AddressFamily.InterNetwork
            && b[0] is not (0 or 10 or 127) && b[0] < 224
            && !(b[0] == 100 && b[1] is >= 64 and <= 127)
            && !(b[0] == 169 && b[1] == 254)
            && !(b[0] == 172 && b[1] is >= 16 and <= 31)
            && !(b[0] == 192 && (b[1] == 168 || (b[1] == 0 && b[2] is 0 or 2)))
            && !(b[0] == 198 && (b[1] is 18 or 19 || (b[1] == 51 && b[2] == 100)))
            && !(b[0] == 203 && b[1] == 0 && b[2] == 113);
    }
}
