using System.Text.RegularExpressions;

namespace MindSprintAI_ASP.NET_MVC.Services;

public record FallbackCard(string Front, string Back);

// Tách thẻ dự phòng khi AI không dùng được (chưa có API key Gemini, Gemini lỗi) — chuyển từ MindSprint-AI-D-n
// frontend/script.js extractFallbackCards, giữ nguyên thứ tự và quy tắc của nhóm:
//   1) dòng "thuật ngữ: định nghĩa" / "thuật ngữ – định nghĩa";
//   2a) "Ta gọi … là X" / "… được gọi là X";  2b) "X là Y";  2c) câu có công thức ("bằng", "có công thức là");
//   3) câu điều kiện "Nếu/Khi/Cho … , …" và câu ngắn một ý.
// Bỏ dòng nhiễu (Bài tập, Câu 3, Lời giải, Trang 2…), bỏ trùng mặt trước, mỗi mặt ≥ 3 ký tự. Không gửi dữ liệu đi đâu.
// Khác nhóm: chạy ở máy chủ (kết quả đi qua cùng trang xem trước/kiểm tra như thẻ AI); trường "ví dụ" của nhóm không có chỗ trong thẻ nên bỏ.
public static partial class FallbackCards
{
    public static List<FallbackCard> Extract(string? rawText, int count)
    {
        var text = Clean(rawText);
        var drafts = new List<FallbackCard>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool Add(string question, string answer)
        {
            var q = Clean(question); var a = Clean(answer);
            if (q.Length < 3 || a.Length < 3 || !seen.Add(q)) return false;
            drafts.Add(new(q, a));
            return drafts.Count >= count;
        }

        foreach (var line in text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0))
        {
            var cleaned = LinePrefix().Replace(line, "").Trim();
            if (Noise().IsMatch(cleaned)) continue;
            var m = Colon().Match(cleaned);
            if (m.Success && m.Groups[1].Value.Length >= 2 && m.Groups[2].Value.Length >= 3 && Add(m.Groups[1].Value, m.Groups[2].Value)) return drafts;
        }

        var sentences = SentenceEnd().Split(text).Select(s => s.Trim()).Where(s => s.Length >= 10).ToList();
        foreach (var s in sentences)
        {
            var passive = Passive().Match(s);
            if (passive.Success && !passive.Groups[1].Value.StartsWith("ý chính", StringComparison.OrdinalIgnoreCase))
            {
                var term = passive.Groups[2].Value.Trim();
                var desc = TaGoi().Replace(passive.Groups[1].Value, "").Trim();
                if (Words(term) <= 6 && desc.Length >= 10 && Add($"Khái niệm: {term} là gì?", desc)) return drafts;
            }
            var def = Definition().Match(s);
            if (def.Success && Words(def.Groups[1].Value.Trim()) <= 5 && Add($"{def.Groups[1].Value.Trim()} là gì?", def.Groups[2].Value.Trim().TrimEnd('.'))) return drafts;
            var formula = Formula().Match(s);
            if (formula.Success)
            {
                var f = formula.Groups[2].Value.Trim().TrimEnd('.');
                if (Add($"Công thức tính {formula.Groups[1].Value.Trim()}?", f.Contains('=') ? $"${f}$" : $"$${f}$$")) return drafts;
            }
        }

        foreach (var s in sentences)
        {
            if (drafts.Count >= count) return drafts;
            if (Noise().IsMatch(s) || seen.Any(k => s.Contains(k, StringComparison.OrdinalIgnoreCase))) continue;
            var clause = Clause().Match(s);
            if (clause.Success)
            {
                var a = clause.Groups[3].Value.Trim().TrimEnd('.');
                if (a.Length is >= 8 and <= 160 && Add($"{clause.Groups[1].Value} {clause.Groups[2].Value.Trim()}: điều kiện / hệ quả là gì?", a)) return drafts;
            }
            else if (s.Length is >= 15 and <= 150 && Add($"Ý chính: {string.Join(' ', s.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(5))}...?", s)) return drafts;
        }
        return drafts;
    }

    // cleanOcrText của nhóm (phần sửa ký hiệu PDF đã làm lúc đọc tệp, AiGenerateService.PdfSymbols).
    private static string Clean(string? s) => string.IsNullOrWhiteSpace(s) ? ""
        : Spaces().Replace(CommaSpace().Replace(CommaGlued().Replace(AiGenerateService.Clean(s), ", $1"), ", "), " ").Trim();
    private static int Words(string s) => s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    [GeneratedRegex(@"^[*•\-\d.)\s]+")] private static partial Regex LinePrefix();
    [GeneratedRegex(@"^(?:Bài tập|Bài \d+|Câu \d+|Ví dụ \d+|Đề bài|Hướng dẫn giải|Lời giải|Lời giải chi tiết|Đáp án|Trang \d+|Mục lục|Chương \d+:|Phần \d+:)", RegexOptions.IgnoreCase)] private static partial Regex Noise();
    [GeneratedRegex(@"^([^:\-–—\n]{2,50})[:\-–—]\s*(.+)$")] private static partial Regex Colon();
    [GeneratedRegex(@"(?<=[.!?])\s+")] private static partial Regex SentenceEnd();
    [GeneratedRegex(@"^(?:Ta gọi\s+)?(.+?)\s+(?:được gọi là|là)\s+([A-Za-z0-9À-ỹ\s\-_()]{2,40})[.!?]?$", RegexOptions.IgnoreCase)] private static partial Regex Passive();
    [GeneratedRegex(@"^Ta gọi\s+", RegexOptions.IgnoreCase)] private static partial Regex TaGoi();
    [GeneratedRegex(@"^([A-Z0-9a-zÀ-ỹ\s\-_()]{2,35}?)\s+(?:là|is an?|are|refers to|means|được định nghĩa là)\s+(.+)$", RegexOptions.IgnoreCase)] private static partial Regex Definition();
    [GeneratedRegex(@"([a-zA-ZÀ-ỹ0-9\s()_,\[\]]{2,40}?)\s*(?:bằng nhau và bằng|có công thức là|bằng)\s*([a-zA-Z0-9_+\-*/^=().\s]{2,50})", RegexOptions.IgnoreCase)] private static partial Regex Formula();
    [GeneratedRegex(@"^(Cho|Giả sử|Nếu|Khi|Để|Với)\s+([^,;]{5,60})[,;]\s*(.+)$", RegexOptions.IgnoreCase)] private static partial Regex Clause();
    [GeneratedRegex(@",([^\s\d])")] private static partial Regex CommaGlued();
    [GeneratedRegex(@"\s+,\s*")] private static partial Regex CommaSpace();
    [GeneratedRegex(@"[ \t]+")] private static partial Regex Spaces();
}
