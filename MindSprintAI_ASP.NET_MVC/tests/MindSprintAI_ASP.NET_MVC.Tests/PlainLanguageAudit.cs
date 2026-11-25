using System.Net;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MindSprintAI_ASP.NET_MVC.Tests;

internal static class PlainLanguageAudit
{
    // (G0) only explained network addresses; "(cookie)" only as the explanation after a plain phrase (pháp lý 2026-11-12); licensed DB-IP attribution; the Gemini product name;
    // exact Google button labels (explained beside them); physical biometrics, not browser fingerprints.
    // Never allow a whole view/service or a bare technical term.
    private static readonly Regex Allowed = new(
        // Người dùng chốt 2026-11-08: giữ "Passkey" (hộp thoại Windows/Google/iPhone ghi đúng chữ này);
        // JavaScript chỉ được dùng trong câu hướng dẫn bật lại có giải thích kèm.
        @"địa chỉ mạng(?: lần cuối)?\s*\(IP\)|\(cookie\)|DB-IP\.com|API key Gemini|Create API key|Copy key|vân tay, khuôn mặt|\bpasskey\b"
        + @"|Trình duyệt đang tắt JavaScript \(phần giúp trang web hoạt động\)\. Hãy bật lại JavaScript trong cài đặt trình duyệt",
        RegexOptions.IgnoreCase);
    private static readonly Regex Banned = new(
        @"\b(cookie|IP|user[ -]?agent|session|token|hash|HMAC|SignalR|fingerprint|script|API|key|JSON|server|database|DB|migration|request|endpoint|HTTP|HTTPS|localhost|passkey|JavaScript|2FA|chunks?)\b|mã băm|vân tay|máy chủ|phiên (đăng nhập|xác thực|nối đăng nhập)|(?:lỗi|mã lỗi|trạng thái)\s*[1-5]\d\d\b|\b(400|401|403|404|408|409|419|422|429|500|502|503|504)\b(?!\s*(ký tự|dòng|người|câu|thẻ|giây|phút))",
        RegexOptions.IgnoreCase);
    private static readonly Regex Tokens = new(
        "(?<comment>//[^\\r\\n]*|/\\*[\\s\\S]*?\\*/)|(?<literal>@\"(?:\"\"|[^\"])*\"|\"(?:\\\\.|[^\"\\\\])*\"|'(?:\\\\.|[^'\\\\\\r\\n])*'|`(?:\\\\.|[^`\\\\])*`)");

    internal static bool HasJargon(string text) => Banned.IsMatch(Allowed.Replace(
        Regex.Replace(WebUtility.HtmlDecode(text), "<[^>]*>", " "), ""));

    internal static IEnumerable<string> Sources(string root)
    {
        // (G0) every application source, including all partials and root Program.cs;
        // generated migrations, tests, vendor libraries and reference UI are developer inputs.
        foreach (var dir in new[] { "Views", "Controllers", "Services", "Models", "Hubs", "Data/Entities", "wwwroot/js" })
            foreach (var path in Directory.EnumerateFiles(Path.Combine(root, dir), "*", SearchOption.AllDirectories)
                .Where(p => new[] { ".cshtml", ".cs", ".js", ".html" }.Contains(Path.GetExtension(p))))
                yield return path;
        foreach (var path in Directory.EnumerateFiles(root, "*.cs")) yield return path;
    }

    internal static IEnumerable<string> UserTexts(string path, string source)
    {
        source = Regex.Replace(source, @"@\*[\s\S]*?\*@|<!--[\s\S]*?-->", "");
        var razor = Path.GetExtension(path) is ".cshtml" or ".html";
        if (razor)
        {
            foreach (Match script in Regex.Matches(source, @"<script\b[^>]*>([\s\S]*?)</script>", RegexOptions.IgnoreCase))
                foreach (var text in UserTexts("inline.js", script.Groups[1].Value)) yield return text;
        }
        if (Path.GetExtension(path) == ".cs")
        {
            // (G0) interpolated text is checked without its C# expressions/identifiers.
            foreach (var node in CSharpSyntaxTree.ParseText(source).GetRoot().DescendantNodes())
            {
                var text = node switch
                {
                    LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.StringLiteralExpression) => literal.Token.ValueText,
                    InterpolatedStringTextSyntax part => part.TextToken.ValueText,
                    _ => null
                };
                if (text == null) continue;
                // (NK1) Mẫu sự kiện Serilog chỉ ghi ra file/console, không phải chữ giao diện.
                // Chỉ bỏ qua đúng thuộc tính trong cấu hình request logging, không bỏ cả Program.cs.
                if (node.Ancestors().OfType<AssignmentExpressionSyntax>().Any(a =>
                    a.Left is MemberAccessExpressionSyntax m && m.Name.Identifier.ValueText == "MessageTemplate")
                    && node.Ancestors().OfType<InvocationExpressionSyntax>().Any(c => c.Expression.ToString().EndsWith(".UseSerilogRequestLogging"))) continue;
                // Internal key-loading failures are caught and never shown as user messages.
                if (node.Ancestors().OfType<ObjectCreationExpressionSyntax>().Any(c => c.Type.ToString() == "CryptographicException")) continue;
                // S1/S2/S6: lỗi cấu hình chỉ ghi log/dừng khởi động, không gửi tới GV/học viên.
                if (Path.GetFileName(path) == "DeploymentSecurity.cs" &&
                    (node.Ancestors().OfType<ObjectCreationExpressionSyntax>().Any(c => c.Type.ToString() == "InvalidOperationException")
                    || node.Ancestors().OfType<InvocationExpressionSyntax>().Any(c => c.Expression.ToString() == "Fail"))) continue;
                if (node.Ancestors().OfType<InvocationExpressionSyntax>().Any(call =>
                    Regex.IsMatch(call.Expression.ToString(), @"\b(Log\w+|logger|log)\b|\.(Contains|StartsWith|EndsWith|IsMatch)$"))) continue;
                // Nội dung môn học của bộ học mẫu (thẻ, câu hỏi môn Lập trình Web/SQL) là kiến thức giảng viên soạn, không phải chữ giao diện:
                // chỉ bỏ qua đúng các mảng nội dung trong ClassDemoSeedService (17/11/2026), thông báo trong service này vẫn bị kiểm.
                if (Path.GetFileName(path) == "ClassDemoSeedService.cs" && node.Ancestors().OfType<FieldDeclarationSyntax>()
                    .Any(field => field.Declaration.Variables.Any(v => v.Identifier.ValueText is "WebCards" or "WebQuestions" or "SqlCards" or "SqlQuestions" or "EngCards" or "EngQuestions"
                        or "WebSource" or "SqlSource" or "EngSource" or "WebChats" or "SqlChats" or "EngChats" or "NotebookSamples" or "OopCards" or "OopQuestions"))) continue;
                // Internal structured Gemini instructions are not shown to users.
                if (Path.GetFileName(path) == "GeminiClient.cs" && node.Ancestors().OfType<AnonymousObjectMemberDeclaratorSyntax>()
                    .Any(member => member.NameEquals?.Name.Identifier.ValueText == "systemInstruction")) continue;
                var message = node.Ancestors().OfType<ObjectCreationExpressionSyntax>()
                    .Any(c => c.Type.ToString() is "BusinessException" or "HubException");
                if (IsHumanLiteral(text) || message || Path.GetFileName(path) == "DisplayText.cs") yield return text;
            }
            yield break;
        }
        foreach (Match token in Tokens.Matches(source))
        {
            if (!token.Groups["literal"].Success) continue;
            var lineStart = source.LastIndexOf('\n', Math.Max(0, token.Index - 1)) + 1;
            var prefix = source[lineStart..token.Index];
            // (G0) logs and the internal Gemini instruction are not sent to people.
            if (Regex.IsMatch(prefix, @"\b(Log\w+\(|logger\.|log\.)")) continue;
            if (Path.GetFileName(path) == "GeminiClient.cs" && prefix.Contains("systemInstruction =")) continue;
            var text = token.Value.Trim('"', '\'', '`', '@').Replace("\\\"", "\"");
            if (razor && !Regex.IsMatch(text, @"[À-ỹĐđ]")) continue; // HTML attributes such as CSS class names.
            if (IsHumanLiteral(text)) yield return text;
        }
        if (Path.GetExtension(path) is not (".cshtml" or ".html")) yield break;
        source = Regex.Replace(source, @"<script\b[^>]*>[\s\S]*?</script>", "", RegexOptions.IgnoreCase);
        // Visible HTML plus accessibility/help/confirmation attributes, including English template text.
        foreach (Match m in Regex.Matches(source, @">([^<>]+)<|(?:placeholder|title|aria-label|alt|data-confirm)=""([^""]+)"""))
        {
            var start = source.LastIndexOf('\n', Math.Max(0, m.Index - 1)) + 1;
            if (source[start..m.Index].TrimStart().StartsWith("var ")) continue;
            var text = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
            // A Razor code line is checked through its string literals above.
            if (text.Contains("=>") || Regex.IsMatch(text.Trim(), @"^(\}|var\b|@?(if|foreach|section)\b)")) continue;
            yield return Regex.Replace(text, @"@\([^\r\n]*\)|@[\w.]+(?:\([^)]*\))?", " ");
        }
    }

    private static bool IsHumanLiteral(string text) =>
        !Regex.IsMatch(text, @"^(SELECT |UPDATE |INSERT |DELETE |DECLARE |EXEC |https?://|/|[.#][a-zA-Z])", RegexOptions.IgnoreCase)
        && Regex.IsMatch(text, @"[À-ỹĐđ]|\s");

    internal static string[] Violations(string root) => Sources(root).SelectMany(path =>
        UserTexts(path, File.ReadAllText(path)).Where(HasJargon).Select(t => $"{Path.GetRelativePath(root, path)}: {t.Trim()}"))
        .Distinct().ToArray();
}

public class Tang7fPureTests
{
    [Theory]
    [InlineData("cookie"), InlineData("IP lần cuối"), InlineData("địa chỉ IP"), InlineData("user agent"), InlineData("session hết hạn")]
    [InlineData("token sai"), InlineData("mã băm"), InlineData("HMAC"), InlineData("SignalR"), InlineData("vân tay trình duyệt")]
    [InlineData("script"), InlineData("gọi API"), InlineData("JSON"), InlineData("máy chủ lỗi"), InlineData("database"), InlineData("DB")]
    [InlineData("migration"), InlineData("request"), InlineData("endpoint"), InlineData("HTTP 403"), InlineData("Lỗi 500")]
    [InlineData("403"), InlineData("500")]
    public void G0_BatTuCam(string text) => Assert.True(PlainLanguageAudit.HasJargon(text));

    [Theory]
    [InlineData("địa chỉ mạng (IP) 113.1.2.3"), InlineData("API key Gemini — mã riêng để dùng trợ lý")]
    [InlineData("Vị trí ước tính theo DB-IP.com"), InlineData("Dùng vân tay, khuôn mặt hoặc mã PIN")]
    [InlineData("Tối đa 500 ký tự"), InlineData("phiên bản câu hỏi"), InlineData("Không nhận dạng được thiết bị. Hãy tải lại trang.")]
    public void G0_NgoaiLeCoGiaiThichVaSoGioiHan(string text) => Assert.False(PlainLanguageAudit.HasJargon(text));

    [Theory]
    [InlineData("Views/Test/_Partial.cshtml", "<p>cookie</p>")]
    [InlineData("Views/Test/Index.cshtml", "<input title=\"token hết hạn\" />")]
    [InlineData("Views/Test/Index.cshtml", "<p>IP @Model.Address</p>")]
    [InlineData("Views/Test/Index.cshtml", "<script>alert('cookie expired');</script>")]
    [InlineData("Services/Mail.cs", "email.SendAsync(to, \"IP mới\", body);")]
    [InlineData("Services/Bell.cs", "new Notice(\"session hết hạn\");")]
    [InlineData("Services/Test.cs", "throw new BusinessException(\"JSON sai\");")]
    [InlineData("Services/Test.cs", "throw new BusinessException(\"JSON\");")]
    [InlineData("Controllers/Test.cs", "TempData[\"Error\"] = \"server lỗi\";")]
    [InlineData("Models/DisplayText.cs", "return \"request lỗi\";")]
    [InlineData("wwwroot/js/test.js", "status.textContent = 'token hết hạn';")]
    public void G0_MoiLoaiNguonDeuDuocQuet(string file, string source) =>
        Assert.Contains(PlainLanguageAudit.UserTexts(file, source), PlainLanguageAudit.HasJargon);
}
