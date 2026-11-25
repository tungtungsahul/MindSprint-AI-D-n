using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MindSprintAI_ASP.NET_MVC.Data.Entities;

namespace MindSprintAI_ASP.NET_MVC.Services;

public record TextAnswer(IReadOnlyList<string> Blanks);
public record NumberAnswer(string Value);

public static class NumberParser
{
    // (5, 15b) không đoán đơn vị, dấu hàng nghìn hay biểu thức học viên nhập.
    public static decimal Parse(string? input)
    {
        var text = (input ?? "").Trim().Replace('−', '-');
        if (text.Any(c => !char.IsAsciiDigit(c) && !char.IsWhiteSpace(c) && c is not ('.' or ',' or '+' or '-')))
            throw new BusinessException("Chỉ nhập số");
        if (!Regex.IsMatch(text, @"^[+-]?(?:[0-9]+(?:[.,][0-9]*)?|[.,][0-9]+)$")
            || !decimal.TryParse(text.Replace(',', '.'), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var value))
            throw new BusinessException("Số không hợp lệ");
        return value;
    }
}

public static class TextMatching
{
    public static string Normalize(string? value, QuestionSnapshot q)
    {
        var text = (value ?? "").Normalize(NormalizationForm.FormC);
        if (q.AcceptNoDiacritics)
        {
            text = string.Concat(text.Normalize(NormalizationForm.FormD)
                .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark))
                .Replace('đ', 'd').Replace('Đ', 'D').Normalize(NormalizationForm.FormC);
        }
        if (q.IgnorePunctuation)
            text = string.Concat(text.Select(c => ".,;:!?-…'\"()".Contains(c) ? ' ' : c));
        text = Regex.Replace(text, @"\s+", " ").Trim();
        return q.CaseSensitive ? text : text.ToUpperInvariant();
    }

    public static IReadOnlyList<bool> Match(QuestionSnapshot q, IReadOnlyList<string> values)
    {
        if (values.Count != q.Blanks.Count) throw new BusinessException("Số ô trả lời không đúng.");
        for (var i = 0; i < values.Count; i++)
            if (Normalize(values[i], q).Length == 0) throw new BusinessException($"Chỗ trống {i + 1} chưa điền.");
        bool Fits(int input, int blank) => q.Blanks[blank].Answers.Any(a => Normalize(a, q) == Normalize(values[input], q));
        if (!q.AcceptAnyBlankOrder) return Enumerable.Range(0, values.Count).Select(i => Fits(i, i)).ToArray();

        // (15b(c)) đường tăng luân phiên cho ghép cặp tối đa; một câu trả lời chỉ dùng cho một ô.
        var owner = Enumerable.Repeat(-1, q.Blanks.Count).ToArray();
        bool Augment(int input, bool[] visited)
        {
            for (var blank = 0; blank < owner.Length; blank++)
            {
                if (visited[blank] || !Fits(input, blank)) continue;
                visited[blank] = true;
                if (owner[blank] < 0 || Augment(owner[blank], visited))
                {
                    owner[blank] = input;
                    return true;
                }
            }
            return false;
        }
        for (var i = 0; i < values.Count; i++) Augment(i, new bool[owner.Length]);
        return Enumerable.Range(0, values.Count).Select(owner.Contains).ToArray();
    }
}

public class TextInputGrader : IQuestionGrader
{
    public IReadOnlyCollection<QuestionType> Types { get; } = [QuestionType.TextInput];
    public GradeResult Grade(QuestionSnapshot question, string payloadJson)
    {
        var values = JsonSerializer.Deserialize<TextAnswer>(payloadJson)?.Blanks ?? [];
        var matches = TextMatching.Match(question, values);
        var count = matches.Count(x => x);
        var all = count == question.Blanks.Count;
        return new GradeResult(all, all ? question.Points : question.BlankScoring == BlankScoring.PerBlank
            ? question.Points * count / question.Blanks.Count : 0); // (65C) không làm tròn điểm câu.
    }
}

public class NumberInputGrader : IQuestionGrader
{
    public IReadOnlyCollection<QuestionType> Types { get; } = [QuestionType.NumberInput];
    public GradeResult Grade(QuestionSnapshot question, string payloadJson)
    {
        var value = NumberParser.Parse(JsonSerializer.Deserialize<NumberAnswer>(payloadJson)?.Value);
        bool ok;
        try { ok = question.NumberAnswer != null && Math.Abs(value - question.NumberAnswer.Value) <= question.NumberTolerance; }
        catch (OverflowException) { ok = false; }
        return new GradeResult(ok, ok ? question.Points : 0);
    }
}

// Dùng chung cho bản nháp, phần xem lại và phản hồi; chỉ gọi sau khi service đã quyết định quyền xem.
public static class InputFeedback
{
    public static IReadOnlyList<string> Values(QuestionSnapshot q, string? json) => json == null ? [] : q.Type switch
    {
        QuestionType.TextInput => JsonSerializer.Deserialize<TextAnswer>(json)?.Blanks ?? [],
        QuestionType.NumberInput => [JsonSerializer.Deserialize<NumberAnswer>(json)?.Value ?? ""],
        QuestionType.SurveyText => [JsonSerializer.Deserialize<SurveyAnswer>(json)?.Value ?? ""],
        _ => []
    };

    public static IReadOnlyList<string> Answers(QuestionSnapshot q) => q.Type switch
    {
        QuestionType.TextInput => q.Blanks.Select(b => b.Answers.First()).ToArray(),
        QuestionType.NumberInput => [q.NumberAnswer?.ToString(CultureInfo.GetCultureInfo("vi-VN"))
            + (q.NumberTolerance > 0 ? " ± " + q.NumberTolerance.ToString(CultureInfo.GetCultureInfo("vi-VN")) : "")],
        _ => []
    };

    public static IReadOnlyList<bool>? Matches(QuestionSnapshot q, string? json)
    {
        if (q.Type != QuestionType.TextInput || json == null) return null;
        try { return TextMatching.Match(q, Values(q, json)); }
        catch (BusinessException) { return Enumerable.Repeat(false, q.Blanks.Count).ToArray(); }
    }
}
