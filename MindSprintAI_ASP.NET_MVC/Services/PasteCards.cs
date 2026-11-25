using System.Text.Json;

namespace MindSprintAI_ASP.NET_MVC.Services;

// "Nhập hàng loạt flashcards" của nhóm (frontend/js/flashcards.js parseBulkInput + backend/app/routers/flashcards_bulk.py):
// dán chữ, mỗi dòng "Câu hỏi : Câu trả lời" (tách ở dấu : đầu tiên) hoặc "mặt trước | mặt sau"; hoặc dán danh sách
// [{"front": "...", "back": "..."}] (nhận cả question/answer, cả {"cards": [...]} / {"flashcards": [...]}).
// Khác nhóm: không lưu thẳng — thành dòng sheet Flashcard rồi đi qua cùng trang xem trước + kiểm tra như nhập Excel.
public static class PasteCards
{
    public const int MaxChars = 200_000;

    public static List<ImportEditRow> Rows(string? text)
    {
        text = text?.Trim() ?? "";
        if (text.Length > MaxChars) throw new BusinessException($"Nội dung dán tối đa {MaxChars:N0} ký tự.");
        var pairs = (text.StartsWith('[') || text.StartsWith('{') ? FromList(text) : null) ?? FromLines(text);
        if (pairs.Count == 0) throw new BusinessException("Chưa nhận ra thẻ nào. Mỗi dòng ghi: Câu hỏi : Câu trả lời (hoặc mặt trước | mặt sau).");
        if (pairs.Count > ExcelTemplate.MaxRows) throw new BusinessException($"Mỗi lần dán tối đa {ExcelTemplate.MaxRows} thẻ.");
        return pairs.Select((p, i) => new ImportEditRow { Sheet = "Flashcard", Row = ExcelTemplate.FirstDataRow + i, Values = [p.Front, p.Back] }).ToList();
    }

    private static List<(string Front, string Back)> FromLines(string text) => text.Split('\n')
        .Select(l => l.Trim()).Where(l => l.Length > 0)
        .Select(l => l.IndexOf(':') is var c and > 0 ? (l[..c].Trim(), l[(c + 1)..].Trim())
            : l.Split('|') is { Length: >= 2 } p ? (p[0].Trim(), p[1].Trim()) : ("", ""))
        .Where(p => p.Item1.Length > 0 && p.Item2.Length > 0).ToList();

    // Dán sai cú pháp danh sách thì trả null để đọc như các dòng chữ (như nhóm: JSON.parse lỗi → tách dòng).
    private static List<(string Front, string Back)>? FromList(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            var items = root.ValueKind == JsonValueKind.Array ? root.EnumerateArray().ToList()
                : root.TryGetProperty("cards", out var c) && c.ValueKind == JsonValueKind.Array ? c.EnumerateArray().ToList()
                : root.TryGetProperty("flashcards", out var f) && f.ValueKind == JsonValueKind.Array ? f.EnumerateArray().ToList()
                : [root];
            string Str(JsonElement e, params string[] names) => names.Select(n => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(n, out var v)
                && v.ValueKind == JsonValueKind.String ? v.GetString()!.Trim() : "").FirstOrDefault(s => s.Length > 0) ?? "";
            return items.Select(e => (Str(e, "front", "question"), Str(e, "back", "answer"))).Where(p => p.Item1.Length > 0 && p.Item2.Length > 0).ToList();
        }
        catch (JsonException) { return null; }
    }
}
