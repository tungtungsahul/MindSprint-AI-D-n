using System.Text.Json;
using MindSprintAI_ASP.NET_MVC.Data.Entities;

namespace MindSprintAI_ASP.NET_MVC.Services;

// Bản chụp câu lưu trong AttemptQuestion.SnapshotJson (31A): lịch sử luôn hiện đúng đề lúc làm.
public record OptionSnapshot(int Id, int OrderIndex, string Text, bool IsCorrect);
public record BlankSnapshot(IReadOnlyList<string> Answers);

public record QuestionSnapshot(
    int QuestionId,
    int OrderIndex,
    QuestionType Type,
    string Prompt,
    string? Explanation,
    decimal Points,
    IReadOnlyList<OptionSnapshot> Options)
{
    public IReadOnlyList<BlankSnapshot> Blanks { get; init; } = [];
    public bool CaseSensitive { get; init; }
    public bool AcceptNoDiacritics { get; init; }
    public bool IgnorePunctuation { get; init; }
    public bool AcceptAnyBlankOrder { get; init; }
    public BlankScoring BlankScoring { get; init; } = BlankScoring.AllOrNothing;
    public decimal? NumberAnswer { get; init; }
    public decimal NumberTolerance { get; init; }
    public bool IsRequired { get; init; }
    public int? MaxLength { get; init; }
    public int? TimeLimitSeconds { get; init; }
    public bool KeepOptionOrder { get; init; }
    public bool? IgnoreQuestionTimers { get; init; }

    public static QuestionSnapshot From(Question q) => new(
        q.Id, q.OrderIndex, q.Type, q.Prompt, q.Explanation, q.Points,
        q.Options.OrderBy(o => o.OrderIndex).Select(o => new OptionSnapshot(o.Id, o.OrderIndex, o.Text, o.IsCorrect)).ToList())
    {
        Blanks = q.Blanks.OrderBy(b => b.BlankIndex).Select(b => new BlankSnapshot(b.Answers.OrderBy(a => a.OrderIndex).Select(a => a.Text).ToList())).ToList(),
        CaseSensitive = q.CaseSensitive, AcceptNoDiacritics = q.AcceptNoDiacritics,
        IgnorePunctuation = q.IgnorePunctuation, AcceptAnyBlankOrder = q.AcceptAnyBlankOrder,
        BlankScoring = q.BlankScoring, NumberAnswer = q.NumberAnswer, NumberTolerance = q.NumberTolerance,
        IsRequired = q.IsRequired, MaxLength = q.MaxLength, TimeLimitSeconds = q.TimeLimitSeconds, KeepOptionOrder = q.KeepOptionOrder
    };

    public string ToJson() => JsonSerializer.Serialize(this);
    public static QuestionSnapshot FromJson(string json) => JsonSerializer.Deserialize<QuestionSnapshot>(json)!;
}

public record ChoiceAnswer(IReadOnlyList<int> Selected);

public record GradeResult(bool? IsCorrect, decimal Score);

// Mỗi dạng câu một bộ chấm; thêm dạng mới = thêm class mới, không sửa bộ chấm cũ (ERD mục 3.3).
public interface IQuestionGrader
{
    IReadOnlyCollection<QuestionType> Types { get; }
    GradeResult Grade(QuestionSnapshot question, string payloadJson);
}

public class ChoiceGrader : IQuestionGrader
{
    public IReadOnlyCollection<QuestionType> Types { get; } = [QuestionType.SingleChoice, QuestionType.MultipleChoice];

    public GradeResult Grade(QuestionSnapshot question, string payloadJson)
    {
        var selected = (JsonSerializer.Deserialize<ChoiceAnswer>(payloadJson)?.Selected ?? []).Distinct().ToHashSet();
        var validIds = question.Options.Select(o => o.Id).ToHashSet();
        if (selected.Count == 0) throw new BusinessException("Hãy chọn ít nhất một đáp án.");
        if (!selected.IsSubsetOf(validIds)) throw new BusinessException("Lựa chọn không thuộc câu hỏi này.");
        if (question.Type == QuestionType.SingleChoice && selected.Count != 1)
            throw new BusinessException("Câu này chỉ được chọn một đáp án.");

        // Chọn nhiều: thiếu hoặc thừa đều sai, không có điểm thành phần.
        var correct = question.Options.Where(o => o.IsCorrect).Select(o => o.Id).ToHashSet();
        var ok = selected.SetEquals(correct);
        return new GradeResult(ok, ok ? question.Points : 0);
    }
}
