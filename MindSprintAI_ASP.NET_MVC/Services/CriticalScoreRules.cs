using System.Text.Json;
using MindSprintAI_ASP.NET_MVC.Data.Entities;

namespace MindSprintAI_ASP.NET_MVC.Services;

public record CriticalSettings(bool ThresholdEnabled = false, decimal? CriticalThreshold = null,
    bool CriticalQuestionsEnabled = false, List<int>? CriticalQuestionIds = null);

public static class CriticalScoreRules
{
    public static List<int> QuestionIds(Assignment a) => string.IsNullOrWhiteSpace(a.CriticalQuestionIdsJson)
        ? [] : JsonSerializer.Deserialize<List<int>>(a.CriticalQuestionIdsJson) ?? [];

    public static string? Text(CriticalLabel label, string? reasons) => label switch
    {
        CriticalLabel.Pending => "Chờ chấm",
        CriticalLabel.Failed => "Liệt" + (string.IsNullOrEmpty(reasons) ? "" : ": " + reasons),
        _ => null
    };

    // (E3, E4) chỉ dùng bản chụp và câu trả lời cuối; không thay bất kỳ con số điểm nào.
    public static void Recompute(Attempt attempt)
    {
        attempt.CriticalLabel = CriticalLabel.None; attempt.CriticalReasons = null; attempt.FailedCriticalQuestion = false;
        if (attempt.Status != AttemptStatus.Ended) return;
        var a = attempt.Participant.Assignment;
        if (a.CriticalThreshold == null && !a.CriticalQuestionsEnabled) return;
        var ids = a.CriticalQuestionsEnabled ? QuestionIds(a) : [];
        var reasons = new List<string>();
        var pending = false;
        foreach (var q in attempt.Questions)
        {
            var snap = QuestionSnapshot.FromJson(q.SnapshotJson);
            if (SurveyRules.IsSurvey(snap.Type)) continue;
            var effective = RegradeRules.Effective(q, a.Regrades);
            if (effective.Cancelled) continue;
            var critical = ids.Contains(snap.QuestionId) || ids.Contains(q.QuestionId);
            var final = q.Submissions.Where(s => s.IsFinal).OrderByDescending(s => s.SubmittedAt).ThenByDescending(s => s.Id).FirstOrDefault();
            var fullCredit = a.Regrades.Any(r => r.QuestionId == snap.QuestionId && r.Method == RegradeMethod.FullCredit);
            var awaiting = final != null && final.Score == null && !fullCredit;
            if (awaiting && (critical || a.CriticalThreshold != null)) pending = true;
            if (critical && !awaiting && (final == null && !fullCredit || effective.Latest != effective.Max))
            {
                reasons.Add($"Câu {q.DisplayOrder + 1} chưa đúng hoàn toàn");
                attempt.FailedCriticalQuestion = true;
            }
        }
        if (pending) { attempt.CriticalLabel = CriticalLabel.Pending; return; }
        var raw = a.Mode == AssignmentMode.Practice ? attempt.PracticeScore ?? attempt.FirstScore : attempt.FirstScore;
        if (a.CriticalThreshold is { } threshold && Displayed(raw, attempt.MaxScore, a) <= threshold)
            reasons.Insert(0, "Điểm không vượt ngưỡng liệt");
        if (reasons.Count > 0) { attempt.CriticalLabel = CriticalLabel.Failed; attempt.CriticalReasons = string.Join("; ", reasons); }
    }

    public static decimal Displayed(decimal raw, decimal max, Assignment a) => ScoreFormatter.Round(ScoreFormatter.Scale(raw, max, a.ScoreScale), a.Rounding);
}

// (E6a–E6c) quy tắc chọn nhãn/đạt theo quyết định của người dùng.
public static class CriticalSelectionRules
{
    public static SelectedScore FromAttempt(Attempt a) => new(a.FirstScore, a.MaxScore, a.PracticeScore,
        a.CriticalLabel, a.CriticalReasons, a.FailedCriticalQuestion);

    public static SelectedScore Average(SelectedScore score, IReadOnlyList<Attempt> done, Assignment a)
    {
        // (E6c) một lượt chưa chấm ngăn kết luận; B chỉ liệt khi mọi lượt đều sai câu điểm liệt.
        if (done.Any(x => x.CriticalLabel == CriticalLabel.Pending)) return score with
            { CriticalLabel = CriticalLabel.Pending, CriticalReasons = null, FailedCriticalQuestion = false };
        var questionFailed = done.Count > 0 && done.All(x => x.FailedCriticalQuestion);
        var thresholdFailed = a.CriticalThreshold is { } threshold && CriticalScoreRules.Displayed(score.Raw, score.Max, a) <= threshold;
        var reasons = new List<string>();
        if (thresholdFailed) reasons.Add("Điểm trung bình không vượt ngưỡng liệt");
        if (questionFailed) reasons.Add("Mọi lượt được tính đều sai câu điểm liệt");
        return score with { CriticalLabel = questionFailed || thresholdFailed ? CriticalLabel.Failed : CriticalLabel.None,
            FailedCriticalQuestion = questionFailed,
            CriticalReasons = reasons.Count == 0 ? null : string.Join("; ", reasons) };
    }

    public static bool? Passed(SelectedScore? score, Assignment a)
    {
        if (a.PassMark is not { } pass || score == null || score.CriticalLabel == CriticalLabel.Pending) return null;
        return score.CriticalLabel != CriticalLabel.Failed && CriticalScoreRules.Displayed(score.Raw, score.Max, a) >= pass;
    }
}
