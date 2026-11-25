using MindSprintAI_ASP.NET_MVC.Data.Entities;
using MindSprintAI_ASP.NET_MVC.Data;
using Microsoft.EntityFrameworkCore;

namespace MindSprintAI_ASP.NET_MVC.Services;

// Quy tắc vòng đời lượt dùng chung cho AttemptService và AssignmentService.
public static class AttemptRules
{
    internal static async Task FinalizeEndedAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        // (F5, H6) trước khi lưu trạng thái cuối, kể cả Hủy lượt hoặc bên chưa tải nhật ký/yêu cầu.
        var ended = db.ChangeTracker.Entries<Attempt>().Where(e => e.State == EntityState.Modified
            && e.Property(x => x.Status).IsModified && !IsOpen(e.Entity)).Select(e => e.Entity).ToList();
        foreach (var attempt in ended)
        {
            // (NK3) Điểm chốt chung của service: cả hết giờ khi mở trang và worker H8 đều đi qua đây.
            var action = attempt.EndReason switch
            {
                AttemptEndReason.DueDate => AuditAction.AttemptTimedOut,
                AttemptEndReason.ScreenLeaveLimit => AuditAction.AttemptScreenSubmitted,
                AttemptEndReason.Submitted => AuditAction.AttemptSubmitted,
                AttemptEndReason.FinishedAfterMastery => AuditAction.AttemptCompleted,
                _ => AuditAction.AttemptStopped
            };
            if (attempt.Status == AttemptStatus.Ended) db.Audit?.Add(action, "Attempt", attempt.Id);
            await db.Entry(attempt).Collection(a => a.DeviceChanges).LoadAsync(cancellationToken);
            await db.Entry(attempt).Collection(a => a.ScreenLeaves).LoadAsync(cancellationToken);
            var at = attempt.EndedAt ?? attempt.CancelledAt ?? DateTime.UtcNow;
            foreach (var request in attempt.DeviceChanges.Where(r => r.Status == DeviceChangeStatus.Pending))
            { request.Status = DeviceChangeStatus.Cancelled; request.DecidedAt = at; }
            ScreenMonitoringRules.CloseAll(attempt, at);
        }
    }
    // (69) chỉ đổi trạng thái hiển thị, không đổi hạn hay đồng hồ.
    public static readonly TimeSpan PauseAfter = TimeSpan.FromSeconds(30);

    public static bool IsOpen(Attempt a) => a.Status is AttemptStatus.InProgress or AttemptStatus.Paused;

    internal static void RecordAnswer(Attempt a, AttemptQuestion q, AnswerSubmission submission, DateTime now)
    {
        foreach (var old in q.Submissions) old.IsFinal = false;
        q.Submissions.Add(submission);
        q.AnswerCount++;
        q.DraftJson = null;
        q.DraftSavedAt = null;
        // (24A, 25A, 59) timeout và câu trả lời dùng chung cách giữ điểm lần đầu/sau luyện.
        if (a.Participant.Assignment.Mode == AssignmentMode.Test || submission.IsFirstAnswer) q.FirstScore = submission.Score;
        q.LatestScore = submission.Score;
        RegradeRules.Recompute(a);
        MasteryRules.Record(a, q, submission.IsCorrect, now);
    }

    // (54B) Kiểm tra chờ công bố điểm, kể cả khi thiếu khảo sát bắt buộc (B5b chỉ thêm nhãn); quiz toàn khảo sát không có điểm để chờ.
    public static bool AwaitsScores(Attempt a) => a.Participant.Assignment.Mode == AssignmentMode.Test
        && a.Participant.Assignment.ScoresPublishedAt == null && a.MaxScore > 0;

    public static AttemptStatus DisplayStatus(Attempt a, DateTime now) =>
        a.Status == AttemptStatus.InProgress && a.DisconnectedAt < now - PauseAfter
            ? AttemptStatus.Paused
            : a.Status;

    // Kết thúc lượt; câu chưa làm giữ 0 điểm (61, 66).
    public static void End(Attempt a, Participant p, AttemptEndReason reason, DateTime at)
    {
        // (C2, 61, 66) yêu cầu lặp không chấm lại và không hồi sinh lượt đã hủy.
        if (!IsOpen(a)) return;
        QuestionTimerRules.Expire(a, at);
        a.Status = AttemptStatus.Ended;
        a.EndReason = reason;
        a.EndedAt = at;
        ScreenMonitoringRules.CloseAll(a, at); // (F5, H6) kết thúc dừng nhật ký cùng lúc.
        foreach (var request in a.DeviceChanges.Where(r => r.Status == DeviceChangeStatus.Pending))
        { request.Status = DeviceChangeStatus.Cancelled; request.DecidedAt = at; }
        if (p.Assignment.Mode == AssignmentMode.Test) GradeUnconfirmedDrafts(a, reason == AttemptEndReason.Submitted);
        RegradeRules.Recompute(a);
        foreach (var q in a.Questions) QuestionTimerRules.Stop(q, at);
        if (a.Kind == AttemptKind.SelfReview) return;
        if (!CardCompletion.HasCards(p.Assignment))
            p.CompletionStatus = AwaitsScores(a)
                ? CompletionStatus.SubmittedAwaitingScore : CompletionStatus.Completed;
        RefreshCompletionLabels(p, a);
    }

    internal static void RefreshCompletionLabels(Participant p, Attempt? current = null)
    {
        // (61, 66, C2) nhãn thuộc lượt còn hiệu lực mới nhất; hủy lượt phải trả lại nhãn của lượt trước.
        var attempts = current == null ? p.Attempts : p.Attempts.Append(current);
        var latest = attempts.Where(a => a.Status != AttemptStatus.Cancelled && a.Kind != AttemptKind.SelfReview)
            .OrderByDescending(a => a.AttemptNumber).FirstOrDefault();
        p.CompletionLabels &= ~(CompletionLabels.Expired | CompletionLabels.ClosedByLecturer | CompletionLabels.HiddenByAdmin | CompletionLabels.MissingRequiredSurvey | CompletionLabels.RemovedFromClass | CompletionLabels.StoppedByLock);
        if (latest?.Status == AttemptStatus.Ended && SurveyRules.MissingRequired(latest).Count > 0)
            p.CompletionLabels |= CompletionLabels.MissingRequiredSurvey; // (B5b) giữ Hoàn thành, không bịa phản hồi.
        p.CompletionLabels |= latest?.EndReason switch
        {
            AttemptEndReason.DueDate => CompletionLabels.Expired,
            AttemptEndReason.ClosedByLecturer => CompletionLabels.ClosedByLecturer,
            AttemptEndReason.HiddenByAdmin => CompletionLabels.HiddenByAdmin,
            AttemptEndReason.RemovedFromClass => CompletionLabels.RemovedFromClass,
            AttemptEndReason.AccountLocked => CompletionLabels.StoppedByLock,
            _ => CompletionLabels.None
        };
    }

    // Lượt mở mà đã quá giờ chốt thì chốt ngay tại mốc giờ đó (không cần job nền).
    public static bool ExpireIfDue(Attempt a, Participant p, DateTime now)
    {
        QuestionTimerRules.Expire(a, now);
        if (!IsOpen(a) || a.DeadlineAt == null || a.DeadlineAt > now) return false;
        End(a, p, AttemptEndReason.DueDate, a.DeadlineAt.Value);
        return true;
    }

    public static DateTime? DeadlineFor(Assignment assignment, Participant p) =>
        (assignment.Mode == AssignmentMode.Test ? assignment.TestEndsAt : assignment.DueAt)?.AddMinutes(p.ExtraMinutes);

    private static void GradeUnconfirmedDrafts(Attempt a, bool lockedOnly = false)
    {
        foreach (var q in a.Questions.Where(q => q.AnswerCount == 0 && q.DraftJson != null
            && (!lockedOnly || q.State == AttemptQuestionState.LockedTimeout)))
        {
            var snapshot = QuestionSnapshot.FromJson(q.SnapshotJson);
            IQuestionGrader? grader = snapshot.Type switch
            {
                QuestionType.MultipleChoice => new ChoiceGrader(),
                QuestionType.TextInput => new TextInputGrader(),
                QuestionType.NumberInput => new NumberInputGrader(),
                QuestionType.SurveyMultiple or QuestionType.SurveyText => new SurveyGrader(),
                _ => null
            };
            if (grader == null) continue;
            GradeResult grade;
            try { grade = grader.Grade(snapshot, q.DraftJson!); }
            catch (BusinessException) { continue; } // (15b(1)) nháp trống/không hợp lệ giữ 0 điểm.
            q.Submissions.Add(new AnswerSubmission
            {
                ClientRequestId = Guid.NewGuid(), PayloadJson = q.DraftJson!, IsCorrect = grade.IsCorrect,
                Score = grade.Score, IsFirstAnswer = true, IsFinal = true, SubmittedAt = a.EndedAt!.Value
            });
            if (q.State != AttemptQuestionState.LockedTimeout) q.State = AttemptQuestionState.Answered;
            q.AnswerCount = 1;
            q.FirstScore = q.LatestScore = grade.Score;
        }
    }
}
