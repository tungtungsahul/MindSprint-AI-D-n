using System.Text;
using System.Text.RegularExpressions;
using MindSprintAI_ASP.NET_MVC.Data.Entities;

namespace MindSprintAI_ASP.NET_MVC.Services;

public record SourceChunk(int Id, string Text, int StartOffset, int EndOffset);
public record GeminiCitation(int ChunkId, string Quote);
public record GeminiAnswer(bool Answerable, string Answer, List<GeminiCitation> Citations);
public record GroundedAnswer(string Answer, List<AiCitation> Citations);

public static class SourceGrounding
{
    public static List<SourceChunk> Chunks(string source)
    {
        var result = new List<SourceChunk>();
        foreach (Match paragraph in Regex.Matches(source, @"\S(?:[^\r\n]|\r?\n(?!\s*\r?\n))*"))
        {
            var end = paragraph.Index + paragraph.Length;
            while (end > paragraph.Index && char.IsWhiteSpace(source[end - 1])) end--;
            for (var start = paragraph.Index; start < end;)
            {
                var stop = Math.Min(start + 800, end);
                // Tránh cắt giữa hai UTF-16 code units của một ký tự.
                if (stop < end && char.IsHighSurrogate(source[stop - 1])) stop--;
                result.Add(new(result.Count + 1, source[start..stop], start, stop));
                start = stop;
            }
        }
        return result;
    }

    public static List<SourceChunk> Select(string source, string question, string? prompt)
    {
        var words = Regex.Matches((question + " " + prompt).ToLowerInvariant(), @"[\p{L}\p{N}]+")
            .Select(m => m.Value).Where(w => w.Length > 1).ToHashSet();
        // shortcut: keyword overlap, no embeddings; upgrade to vector search if sources get large
        return Chunks(source).OrderByDescending(c => Regex.Matches(c.Text.ToLowerInvariant(), @"[\p{L}\p{N}]+")
            .Select(m => m.Value).Distinct().Count(words.Contains)).ThenBy(c => c.Id).Take(12).ToList();
    }

    // Ánh xạ từng ký tự đã chuẩn hóa về offset gốc, không dùng IndexOf trên văn bản đã đổi độ dài.
    private static (string Text, List<int> Map) Normalize(string value)
    {
        var text = new StringBuilder(); var map = new List<int>();
        for (var i = 0; i < value.Length; i++)
        {
            if (char.IsWhiteSpace(value[i]))
            {
                if (text.Length == 0 || text[^1] == ' ') continue;
                text.Append(' '); map.Add(i);
            }
            else { text.Append(value[i]); map.Add(i); }
        }
        return (text.ToString().TrimEnd(), map);
    }

    // Chủ đề ngắn: bỏ "là gì/như thế nào" và dấu câu cuối, cắt theo từ; tránh "… là gì?." hoặc chép cả đoạn dài.
    public static string Missing(string question)
    {
        var topic = Regex.Replace(question.Trim(), @"\s+", " ");
        topic = Regex.Replace(topic, @"\s*(là gì|là sao|như thế nào|thế nào|ra sao|không)?\s*[?？.!…]*\s*$", "", RegexOptions.IgnoreCase);
        if (topic.Length > 60) topic = topic[..(topic.LastIndexOf(' ', 60) is > 20 and var cut ? cut : 60)] + "…";
        return topic.Length == 0 ? "Văn bản nguồn chưa có thông tin về câu hỏi này." : $"Văn bản nguồn chưa có thông tin về “{topic}”.";
    }

    public static GroundedAnswer Validate(GeminiAnswer answer, IReadOnlyList<SourceChunk> chunks, string question)
    {
        var citations = new List<AiCitation>();
        if (answer.Answerable && !string.IsNullOrWhiteSpace(answer.Answer))
            foreach (var item in answer.Citations ?? [])
            {
                var chunk = chunks.SingleOrDefault(c => c.Id == item.ChunkId);
                if (chunk == null || string.IsNullOrWhiteSpace(item.Quote)) continue;
                var normalized = Normalize(chunk.Text); var quote = Normalize(item.Quote).Text;
                if (quote.Length == 0) continue;
                var index = normalized.Text.IndexOf(quote, StringComparison.Ordinal);
                if (index < 0) continue;
                var start = normalized.Map[index]; var end = normalized.Map[index + quote.Length - 1] + 1;
                if (citations.Any(c => c.StartOffset == chunk.StartOffset + start && c.EndOffset == chunk.StartOffset + end)) continue;
                citations.Add(new() { ExcerptText = chunk.Text[start..end], StartOffset = chunk.StartOffset + start, EndOffset = chunk.StartOffset + end });
                if (citations.Count == 3) break;
            }
        return citations.Count == 0 ? new(Missing(question), []) : new(answer.Answer, citations);
    }
}
