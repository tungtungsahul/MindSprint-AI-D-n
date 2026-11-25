using MindSprintAI_ASP.NET_MVC.Data.Entities;

namespace MindSprintAI_ASP.NET_MVC.Services;

public record QuestionClock(int? SecondsLeft, bool Locked, bool Expired);
public record QuestionClockState(long Id, int? SecondsLeft, bool Locked, bool Expired, bool Running);

// (58B, 59) chỉ gọi khi đã khóa và đọc lại lượt; không dùng giờ từ trình duyệt.
public static class QuestionTimerRules
{
    public const int GraceSeconds = 2;

    private static double? Remaining(AttemptQuestion q, DateTime now) =>
        q.TimeLeftSeconds is not { } left ? null : left - (q.TimerStartedAt is { } start ? Math.Max(0, (now - start).TotalSeconds) : 0);

    public static QuestionClock Read(Attempt a, AttemptQuestion q, DateTime now)
    {
        var limit = QuestionSnapshot.FromJson(q.SnapshotJson).TimeLimitSeconds;
        double? left = limit == null ? null : Remaining(q, now) ?? limit;
        if (left != null && a.DeadlineAt is { } deadline) left = Math.Min(left.Value, Math.Max(0, (deadline - now).TotalSeconds));
        return new(left == null ? null : (int)Math.Max(0, Math.Ceiling(left.Value)),
            q.State == AttemptQuestionState.LockedTimeout, q.State == AttemptQuestionState.LockedTimeout || !AttemptRules.IsOpen(a));
    }

    public static void Open(Attempt a, AttemptQuestion q, DateTime now, bool retry = false)
    {
        if (!AttemptRules.IsOpen(a)) return;
        var practice = a.Participant.Assignment.Mode == AssignmentMode.Practice;
        if (practice) Pause(a, now, q);
        var limit = QuestionSnapshot.FromJson(q.SnapshotJson).TimeLimitSeconds;
        if (limit == null) return; // Bản chụp trước 4c không tự nhận giờ từ câu gốc.
        if (practice && retry)
        {
            if (!MasteryRules.CanRetry(a, q)) throw new BusinessException("Câu này không được trả lời lại hoặc đã hết số lần trả lời.");
            // (59) yêu cầu mở lại lặp trong cùng lần luyện không cấp thêm giờ.
            if (q.TimerStartedAt == null && q.State is AttemptQuestionState.Answered or AttemptQuestionState.LockedTimeout)
            {
                q.TimeLeftSeconds = limit;
                q.State = AttemptQuestionState.Unanswered;
            }
        }
        if (q.State == AttemptQuestionState.LockedTimeout || practice && q.State == AttemptQuestionState.Answered) return;
        q.TimeLeftSeconds ??= limit;
        q.TimerStartedAt ??= now;
    }

    public static bool RejectAnswer(Attempt a, AttemptQuestion q, DateTime now)
    {
        if (QuestionSnapshot.FromJson(q.SnapshotJson).TimeLimitSeconds == null) return false;
        // (17B) giữ mốc chạy của Kiểm tra sau khóa để nhận trễ mạng tối đa 2 giây.
        if (a.Participant.Assignment.Mode == AssignmentMode.Test)
            return Remaining(q, now) is { } left && left < -GraceSeconds;
        return q.State == AttemptQuestionState.LockedTimeout;
    }

    public static void Pause(Attempt a, DateTime now, AttemptQuestion? except = null)
    {
        if (a.Participant.Assignment.Mode != AssignmentMode.Practice) return;
        Expire(a, now);
        foreach (var q in a.Questions.Where(q => q != except && q.TimerStartedAt != null)) Stop(q, now);
    }

    public static void Stop(AttemptQuestion q, DateTime now)
    {
        if (Remaining(q, now) is { } left) q.TimeLeftSeconds = (int)Math.Max(0, Math.Ceiling(left));
        q.TimerStartedAt = null;
    }

    public static void Expire(Attempt a, DateTime now)
    {
        if (!AttemptRules.IsOpen(a)) return;
        var practice = a.Participant.Assignment.Mode == AssignmentMode.Practice;
        // Hạn cả lượt luôn thắng; không biến câu còn giờ thành một lần trả lời.
        var at = a.DeadlineAt is { } deadline && deadline < now ? deadline : now;
        foreach (var q in a.Questions.Where(q => q.TimerStartedAt != null && q.State != AttemptQuestionState.LockedTimeout))
        {
            if (QuestionSnapshot.FromJson(q.SnapshotJson).TimeLimitSeconds == null || Remaining(q, at) is not { } left) continue;
            if (left > 0 || practice && left >= -GraceSeconds) continue;
            q.State = AttemptQuestionState.LockedTimeout;
            if (!practice) continue;
            Stop(q, at);
            if (SurveyRules.IsSurvey(QuestionSnapshot.FromJson(q.SnapshotJson).Type)) continue;
            // (59, giả định 4c) timeout là một lần trả lời sai, gồm cả lần luyện lại.
            AttemptRules.RecordAnswer(a, q, new AnswerSubmission
            {
                ClientRequestId = Guid.NewGuid(), PayloadJson = "{}", IsCorrect = false, Score = 0,
                IsFirstAnswer = q.AnswerCount == 0, IsFinal = true, SubmittedAt = at
            }, at);
        }
    }
}
