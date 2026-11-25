using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;

namespace MindSprintAI_ASP.NET_MVC.Services;

// "Tạo bằng AI" của nhóm (AiController extract-file/flashcards/quiz) gắn vào luồng nhập Excel có sẵn của QuizEdu:
// AI chỉ tạo bản nháp → trang xem trước (sửa, bỏ dòng, kiểm lại) → lưu bằng ExcelImportService.SaveAsync như nhập file.
// Dùng API key Gemini của chính người dùng (UserApiKeyService, cần đã đồng ý thông báo AI) như trợ lý AI của QuizEdu.
public sealed class AiGenerateService(ExcelImportService excel, UserApiKeyService keys, GeminiClient gemini)
{
    public const int MaxChars = 60_000, MaxCount = 100, BatchSize = 25;
    public const long MaxFileBytes = 10 * 1024 * 1024;
    public static readonly string[] FileTypes = [".pdf", ".txt", ".md", ".csv", ".json", .. OfficeText.Types];

    // Người dùng chốt 15/11: tối đa 100 như nhóm. Gemini chỉ trả ~30 mục/lần (giới hạn độ dài) → chia đợt 25, mỗi đợt gửi kèm
    // các câu đã có để AI không lặp lại; đợt nào không ra thêm gì thì dừng. newSetTitle: bản nháp cho bộ học mới (quizId = 0).
    public async Task<ImportPreview> GenerateAsync(int quizId, string? text, int count, bool cards, string userId, string? newSetTitle = null)
    {
        await excel.EnsureCanImportAsync(quizId, userId);
        var source = Clean(text);
        if (source.Length < 50) throw new BusinessException("Hãy dán hoặc tải lên tài liệu dài ít nhất 50 ký tự.");
        if (source.Length > MaxChars) source = source[..MaxChars];
        count = Math.Clamp(count, 1, MaxCount);
        var items = new List<JsonElement>();
        string? notice = null, name = cards ? "Thẻ ghi nhớ tạo bằng AI" : "Câu hỏi tạo bằng AI";
        try
        {
            var key = await keys.UseAsync(userId);
            while (items.Count < count)
            {
                var batch = await gemini.GenerateAsync(key, source, Math.Min(BatchSize, count - items.Count), cards, items.Select(i => Prompt(i, cards)).ToList());
                var fresh = batch.EnumerateArray().Where(i => !items.Any(x => string.Equals(Prompt(x, cards), Prompt(i, cards), StringComparison.OrdinalIgnoreCase))).ToList();
                if (fresh.Count == 0) break;
                items.AddRange(fresh);
            }
        }
        // Như nhóm (generateAiCards → extractFallbackCards): thẻ ghi nhớ mà AI không dùng được (chưa có key, Gemini lỗi) thì tự tách
        // thẻ từ tài liệu theo quy tắc. AI đã tạo được một phần rồi mới lỗi thì giữ phần đó. Trắc nghiệm không có dự phòng (như nhóm).
        catch (BusinessException ex) when (cards)
        {
            if (items.Count > 0) notice = $"AI dừng giữa chừng ({ex.Message}). Giữ {items.Count} thẻ AI đã tạo.";
            else
            {
                var drafts = FallbackCards.Extract(source, count);
                if (drafts.Count == 0) throw;
                items = drafts.Select(d => JsonSerializer.SerializeToElement(new { front = d.Front, back = d.Back })).ToList();
                name = "Thẻ ghi nhớ tách tự động";
                notice = $"Chưa dùng được AI ({ex.Message}). MindSprint AI đã tự tách {drafts.Count} thẻ từ tài liệu theo quy tắc, không dùng AI — hãy xem lại từng thẻ trước khi lưu.";
            }
        }
        var preview = ExcelImportService.ToPreview(quizId, name, ExcelTemplate.ValidateRows(ToRows(JsonSerializer.SerializeToElement(items), cards, count)), newSetTitle);
        preview.Notice = notice;
        return preview;
    }

    private static string Prompt(JsonElement item, bool cards) =>
        item.TryGetProperty(cards ? "front" : "question", out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()!.Trim() : "";

    // Mỗi mục AI → một dòng đúng cột file mẫu (sheet CauHoi / Flashcard); dữ liệu sai do AI sẽ hiện lỗi ở dòng đó trong trang xem trước.
    public static List<ImportEditRow> ToRows(JsonElement items, bool cards, int max)
    {
        var rows = new List<ImportEditRow>();
        foreach (var item in items.EnumerateArray().Take(max))
        {
            string Str(string name) => item.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()!.Trim() : "";
            if (cards) { rows.Add(new() { Sheet = "Flashcard", Row = ExcelTemplate.FirstDataRow + rows.Count, Values = [Str("front"), Str("back")] }); continue; }
            var options = item.TryGetProperty("options", out var o) && o.ValueKind == JsonValueKind.Array
                ? o.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!.Trim()).Take(ExcelTemplate.OptionCount).ToList() : [];
            var correct = item.TryGetProperty("correct", out var c) && c.ValueKind == JsonValueKind.Array
                ? c.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Number && x.TryGetInt32(out _)).Select(x => x.GetInt32()).Distinct().ToList() : [];
            var values = Enumerable.Repeat("", ExcelTemplate.Headers.Length).ToList();
            values[ExcelTemplate.Prompt] = Str("question");
            values[ExcelTemplate.Type] = correct.Count > 1 ? "Chọn nhiều" : "Chọn một";
            for (var i = 0; i < options.Count; i++) values[ExcelTemplate.FirstOption + i] = options[i];
            values[ExcelTemplate.Correct] = string.Join(",", correct);
            values[ExcelTemplate.Explain] = Str("explanation");
            rows.Add(new() { Sheet = "CauHoi", Row = ExcelTemplate.FirstDataRow + rows.Count, Values = values });
        }
        if (rows.Count == 0) throw new BusinessException("AI chưa tạo được nội dung nào từ tài liệu này. Hãy thử đoạn văn rõ ràng hơn.");
        return rows;
    }

    // Đọc chữ từ tệp như nhóm: PDF (PdfPig) hoặc tệp chữ; PDF scan không có lớp chữ thì báo dùng Gia sư OCR/dán chữ.
    public static async Task<string> ExtractAsync(IFormFile file)
    {
        if (file.Length == 0) throw new BusinessException("Tệp trống.");
        if (file.Length > MaxFileBytes) throw new BusinessException("Tệp tối đa 10 MB.");
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!FileTypes.Contains(ext)) throw new BusinessException("Chỉ nhận tệp PDF, Word, PowerPoint, Excel hoặc tệp chữ (TXT, MD, CSV).");
        string text;
        try
        {
            if (OfficeText.Types.Contains(ext))
            {
                using var buffer = new MemoryStream();
                await file.CopyToAsync(buffer);
                text = OfficeText.Extract(buffer.ToArray(), ext);
            }
            else if (ext == ".pdf")
            {
                using var buffer = new MemoryStream();
                await file.CopyToAsync(buffer);
                using var pdf = PdfDocument.Open(buffer.ToArray());
                text = PdfSymbols(string.Join("\n\n", pdf.GetPages().Select(p => p.Text)));
            }
            else
            {
                using var reader = new StreamReader(file.OpenReadStream(), Encoding.UTF8);
                text = await reader.ReadToEndAsync();
            }
        }
        catch (Exception ex) when (ex is not BusinessException) { throw new BusinessException("Không đọc được tệp này."); }
        if (string.IsNullOrWhiteSpace(text)) throw new BusinessException("Không lấy được chữ từ tệp (có thể là bản scan). Hãy dán chữ vào ô bên dưới.");
        return text;
    }

    // Ký hiệu toán bị phông Symbol của PDF đọc sai (CleanExtractedText của nhóm): £ → ≤, ¥ → ∞, x!"# → x_{i-1}, x! → x_i, Dx → Δx.
    // Khác nhóm: chỉ áp cho chữ lấy từ PDF — tệp chữ gõ tay có £/¥ thật (tiền tệ) thì giữ nguyên.
    public static string PdfSymbols(string text)
    {
        var s = text.Replace("£", " ≤ ").Replace("¥", " ∞ ").Replace("x!\"#", "x_{i-1}").Replace("!\"#", "_{i-1}");
        s = Regex.Replace(s, @"(?<!\p{L})x!", "x_i"); // nhóm thay mọi "x!" → "Max!" thành "Max_i"; ở đây chỉ khi x đứng riêng
        s = Regex.Replace(s, @"\bDx\s*=", "Δx = ");
        return Regex.Replace(s, @"\bDx\b", "Δx");
    }

    // Dọn chữ trích từ PDF như nhóm (CleanExtractedText): gộp khoảng trắng, thêm cách sau dấu chấm dính chữ hoa.
    public static string Clean(string? text) => string.IsNullOrWhiteSpace(text) ? ""
        : Regex.Replace(Regex.Replace(text, @"([a-zÀ-ỹ0-9])\.([A-ZÀ-Ỹ])", "$1. $2"), @"[ \t]+", " ").Trim();
}
