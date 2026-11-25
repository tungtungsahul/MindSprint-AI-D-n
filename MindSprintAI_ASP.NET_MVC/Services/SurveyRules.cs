using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MindSprintAI_ASP.NET_MVC.Data;
using MindSprintAI_ASP.NET_MVC.Data.Entities;

namespace MindSprintAI_ASP.NET_MVC.Services;

public record SurveyAnswer(string Value);

public static class SurveyRules
{
    public static bool IsSurvey(QuestionType type) => type is QuestionType.SurveySingle or QuestionType.SurveyMultiple or QuestionType.SurveyText;
    public static bool IsChoice(QuestionType type) => MasteryRules.Eligible(type) || type is QuestionType.SurveySingle or QuestionType.SurveyMultiple;

    internal static async Task<bool> IsUngradedAsync(AppDbContext db, int? quizId)
    {
        if (quizId == null) return false;
        var types = await db.Questions.Where(q => q.QuizId == quizId).Select(q => q.Type).ToListAsync();
        return types.Count > 0 && types.All(IsSurvey);
    }

    public static List<int> MissingRequired(Attempt a) => a.Questions.OrderBy(q => q.DisplayOrder)
        .Where(q => q.AnswerCount == 0 && QuestionSnapshot.FromJson(q.SnapshotJson) is { IsRequired: true } s && IsSurvey(s.Type))
        .Select(q => q.DisplayOrder + 1).ToList();
}

// (56, 57) kiểm tra phản hồi nhưng không gán đúng/sai hoặc thưởng điểm.
public class SurveyGrader : IQuestionGrader
{
    public IReadOnlyCollection<QuestionType> Types { get; } = [QuestionType.SurveySingle, QuestionType.SurveyMultiple, QuestionType.SurveyText];
    public GradeResult Grade(QuestionSnapshot question, string payloadJson)
    {
        if (question.Type == QuestionType.SurveyText)
        {
            var value = JsonSerializer.Deserialize<SurveyAnswer>(payloadJson)?.Value ?? "";
            if (string.IsNullOrWhiteSpace(value)) throw new BusinessException("Hãy nhập ý kiến.");
            if (value.Length > 500) throw new BusinessException("Ý kiến tối đa 500 ký tự.");
        }
        else
        {
            var selected = (JsonSerializer.Deserialize<ChoiceAnswer>(payloadJson)?.Selected ?? []).Distinct().ToHashSet();
            if (selected.Count == 0) throw new BusinessException("Hãy chọn ít nhất một lựa chọn.");
            if (!selected.IsSubsetOf(question.Options.Select(o => o.Id))) throw new BusinessException("Lựa chọn không thuộc câu hỏi này.");
            if (question.Type == QuestionType.SurveySingle && selected.Count != 1) throw new BusinessException("Câu này chỉ được chọn một lựa chọn.");
        }
        return new(null, 0);
    }
}
