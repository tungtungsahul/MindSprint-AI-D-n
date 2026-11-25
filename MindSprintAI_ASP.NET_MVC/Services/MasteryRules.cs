using MindSprintAI_ASP.NET_MVC.Data.Entities;

namespace MindSprintAI_ASP.NET_MVC.Services;

public record MasteryProgress(int Mastered, int Total, int Threshold, bool Reached, bool CanFinish, bool CanRetry)
{
    public string Label => $"Làm chủ {Mastered}/{Total} (mốc {Threshold}%)";
    public string Message => Reached ? $"Bạn đã đạt mốc {Threshold}%!"
        : CanFinish ? $"Bạn đã dùng hết số lần trả lời, chưa đạt mốc ({Mastered}/{Total}, mốc {Threshold}%)" : Label;
}

public static class MasteryRules
{
    public static bool Eligible(QuestionType type) => type is QuestionType.SingleChoice or QuestionType.MultipleChoice;
    public static int Required(int total, int threshold) => (int)Math.Ceiling(total * threshold / 100m);
    public static int Denominator(Attempt a) => a.Questions.Count(q => Eligible(QuestionSnapshot.FromJson(q.SnapshotJson).Type));

    public static bool CanRetry(Attempt a, AttemptQuestion q) => AttemptRules.IsOpen(a)
        && a.Participant.Assignment.Mode == AssignmentMode.Practice && a.Participant.Assignment.MasteryThreshold != null
        && Eligible(QuestionSnapshot.FromJson(q.SnapshotJson).Type) && q.AnswerCount > 0
        && (a.Participant.Assignment.AnswersPerQuestion == null || q.AnswerCount < a.Participant.Assignment.AnswersPerQuestion)
        && q.Submissions.Where(s => s.IsFinal).OrderByDescending(s => s.SubmittedAt).ThenByDescending(s => s.Id)
            .FirstOrDefault()?.IsCorrect == false;

    public static MasteryProgress? Progress(Attempt a)
    {
        if (a.Participant.Assignment.MasteryThreshold is not { } threshold) return null;
        var retry = a.Questions.Any(q => CanRetry(a, q));
        var reached = a.MasteryReachedAt != null;
        return new(a.MasteredCount, Denominator(a), threshold, reached,
            a.Questions.All(q => q.AnswerCount > 0 || (a.Kind == AttemptKind.SelfReview || q.State == AttemptQuestionState.LockedTimeout)
                && SurveyRules.IsSurvey(QuestionSnapshot.FromJson(q.SnapshotJson).Type)) && (reached || !retry), retry);
    }

    public static void Record(Attempt a, AttemptQuestion q, bool? correct, DateTime now)
    {
        if (a.Participant.Assignment.MasteryThreshold is not { } threshold) return;
        // (22B, 24A) mỗi câu tăng một lần; đạt mốc rồi thì không mất trạng thái.
        if (Eligible(QuestionSnapshot.FromJson(q.SnapshotJson).Type) && correct == true && !q.IsMastered)
        {
            q.IsMastered = true;
            a.MasteredCount++;
        }
        var total = Denominator(a);
        if (total > 0 && a.MasteredCount >= Required(total, threshold))
        {
            a.MasteryReachedAt ??= now;
            if (a.Kind != AttemptKind.SelfReview) a.Participant.MasteryReached = true;
        }
    }

    public static string? Report(Assignment assignment, IEnumerable<Attempt> attempts, bool reached)
    {
        if (assignment.MasteryThreshold is not { } threshold) return null;
        if (reached) return $"Đạt mốc {threshold}%";
        var best = attempts.Where(a => a.Status == AttemptStatus.Ended)
            .OrderByDescending(a => Denominator(a) == 0 ? 0 : (decimal)a.MasteredCount / Denominator(a)).FirstOrDefault();
        if (best == null) return "—"; // chưa có lượt kết thúc thì chưa có gì để báo "chưa đạt".
        return $"Chưa đạt mốc ({best?.MasteredCount ?? 0}/{(best == null ? 0 : Denominator(best))}, mốc {threshold}%)";
    }
}
