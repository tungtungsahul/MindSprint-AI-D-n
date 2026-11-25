using System.Text.Json;
using System.Security.Cryptography;
using MindSprintAI_ASP.NET_MVC.Data.Entities;

namespace MindSprintAI_ASP.NET_MVC.Services;

public record EffectiveQuestionScore(decimal First, decimal Latest, decimal Max, bool Cancelled, bool BlankCredit, bool? IsCorrect = null);

public static class RegradeRules
{
    public const string BlankCreditLabel = "Được tính đúng khi chấm lại (không trả lời)";

    public static IReadOnlyList<string> ReviewNotes(Attempt a, AttemptQuestion q, bool reveal, QuestionSnapshot? current = null, bool edited = false)
    {
        var snap = QuestionSnapshot.FromJson(q.SnapshotJson);
        var notes = new List<string>();
        var number = q.DisplayOrder + 1;
        if (edited)
            notes.Add(reveal && current != null && !SurveyRules.IsSurvey(current.Type)
                ? "Giảng viên đã đổi nội dung/đáp án. Đáp án đúng hiện tại: " + CorrectText(current)
                : $"Câu {number} đã được sửa");
        foreach (var r in a.Participant.Assignment.Regrades.Where(r => r.QuestionId == snap.QuestionId).OrderBy(r => r.At).ThenBy(r => r.Id))
        {
            notes.Add($"Câu {number} đã được chấm lại: {r.Reason}");
            // (B1, 54B, 68) chỉ dựng chuỗi đáp án sau cùng một cổng lộ đáp án của Review.
            if (reveal && r.Method == RegradeMethod.NewCorrect)
            {
                var ids = JsonSerializer.Deserialize<int[]>(r.NewCorrectJson ?? "[]") ?? [];
                notes.Add("Đáp án đúng khi chấm lại: " + string.Join("; ", snap.Options.Where(o => ids.Contains(o.Id)).Select(o => o.Text)));
            }
        }
        return notes;
    }

    private static string CorrectText(QuestionSnapshot q) => MasteryRules.Eligible(q.Type)
        ? string.Join("; ", q.Options.Where(o => o.IsCorrect).Select(o => o.Text)) : string.Join("; ", InputFeedback.Answers(q));

    // (19, 28A, C1) duy nhất một phép tính điểm: lịch sử gốc giữ nguyên, chấm lại áp theo thứ tự.
    public static EffectiveQuestionScore Effective(AttemptQuestion q, IEnumerable<Regrade> regrades)
    {
        var snap = QuestionSnapshot.FromJson(q.SnapshotJson);
        if (SurveyRules.IsSurvey(snap.Type)) return new(0, 0, 0, false, false);
        var final = q.Submissions.Where(s => s.IsFinal).OrderByDescending(s => s.SubmittedAt).ThenByDescending(s => s.Id).FirstOrDefault();
        var firstSubmission = q.Submissions.Where(s => s.IsFirstAnswer).OrderBy(s => s.SubmittedAt).ThenBy(s => s.Id).FirstOrDefault();
        var test = q.Attempt?.Participant?.Assignment?.Mode == AssignmentMode.Test;
        var first = test ? final?.Score ?? q.FirstScore ?? 0 : firstSubmission?.Score ?? q.FirstScore ?? 0;
        // (25A, 28A) điểm lần đầu xét đúng lần trả lời đầu; lần luyện sau chỉ đổi điểm gần nhất.
        var firstAnswer = test ? final : firstSubmission;
        var latest = final?.Score ?? q.LatestScore ?? 0;
        var correct = final?.IsCorrect;
        var cancelled = false;
        var blankCredit = false;
        foreach (var r in regrades.Where(r => r.QuestionId == snap.QuestionId).OrderBy(r => r.At).ThenBy(r => r.Id))
        {
            if (r.Method == RegradeMethod.Cancel) { cancelled = true; first = latest = 0; blankCredit = false; correct = null; }
            if (cancelled) continue; // (C1) hủy câu loại cả tử và mẫu, không phục hồi bằng cách chấm khác.
            if (r.Method == RegradeMethod.FullCredit)
            {
                first = latest = snap.Points;
                correct = true;
                blankCredit = q.AnswerCount == 0;
            }
            if (r.Method == RegradeMethod.NewCorrect && MasteryRules.Eligible(snap.Type))
            {
                var key = JsonSerializer.Deserialize<int[]>(r.NewCorrectJson ?? "[]") ?? [];
                bool Matches(AnswerSubmission? s) => s != null && (s.IsCorrect == true || key.Length > 0
                    && (JsonSerializer.Deserialize<ChoiceAnswer>(s.PayloadJson)?.Selected ?? []).ToHashSet().SetEquals(key));
                if (Matches(firstAnswer)) first = snap.Points;
                if (Matches(final)) { latest = snap.Points; correct = true; }
            }
        }
        return new(first, latest, cancelled ? 0 : snap.Points, cancelled, blankCredit, correct);
    }

    public static void Recompute(Attempt a)
    {
        // Bài làm theo đề: điểm do GV chấm tay (PaperTestService.GradeAsync), không tính lại từ câu hỏi (lượt không có câu nào).
        if (a.Participant.Assignment.Form == AssignmentForm.Document) return;
        var scores = a.Questions.Select(q => Effective(q, a.Participant.Assignment.Regrades)).ToList();
        a.FirstScore = scores.Sum(s => s.First);
        a.MaxScore = scores.Sum(s => s.Max);
        a.PracticeScore = a.Participant.Assignment.MasteryThreshold != null && a.Participant.Assignment.ScoreRetries
            ? scores.Sum(s => s.Latest) : null;
        CriticalScoreRules.Recompute(a); // (E5) finish, trả lời, chấm tay và chấm lại cùng một đường tính.
        // (24A) chấm lại không thay đổi IsMastered, MasteredCount hay mốc đã đạt.
    }
}

public static class QuestionEditRules
{
    public const string StructureBlocked = "Quiz đã có người làm: muốn bỏ câu thì dùng “Hủy câu” khi chấm lại, muốn thêm câu thì giao bài mới.";
    public const string MoveBlocked = "Quiz đã có người làm nên không đổi thứ tự câu được nữa. Muốn thứ tự khác, hãy nhân bản bộ học rồi sửa bản sao.";
    public const string DeleteQuizBlocked = "Quiz đã có học viên làm bài nên không xóa được.";
    public const string Stale ="Câu này vừa được sửa ở nơi khác, hãy tải lại";

    public static bool IsMinor(Question before, Question after)
    {
        QuestionSnapshot Key(Question q) => QuestionSnapshot.From(q) with
        {
            QuestionId = 0, OrderIndex = 0, Prompt = "", Explanation = null, Points = 0, NumberAnswer = null, NumberTolerance = 0,
            Options = q.Options.OrderBy(o => o.OrderIndex).Select((o, i) => new OptionSnapshot(0, i, "", o.IsCorrect)).ToList()
        };
        return before.Points == after.Points && before.NumberAnswer == after.NumberAnswer && before.NumberTolerance == after.NumberTolerance
            && before.KeepOptionOrder == after.KeepOptionOrder && Key(before).ToJson() == Key(after).ToJson();
    }

    public static void Apply(Attempt a, AttemptQuestion q, QuestionSnapshot snapshot, int version, QuestionEditKind kind)
    {
        if (!AttemptRules.IsOpen(a)) return;
        var before = QuestionSnapshot.FromJson(q.SnapshotJson);
        q.QuestionVersion = version;
        if (kind == QuestionEditKind.Minor)
        {
            q.SnapshotJson = (before with { Prompt = snapshot.Prompt, Explanation = snapshot.Explanation,
                Options = before.Options.Select(o => o with { Text = snapshot.Options.Single(n => n.OrderIndex == o.OrderIndex).Text }).ToList() }).ToJson();
            return;
        }
        // (4d) đổi setting bỏ giờ của Ôn tập chỉ áp lượt mới; lượt cũ giữ lựa chọn lúc bắt đầu.
        var ignored = before.IgnoreQuestionTimers ?? (before.TimeLimitSeconds == null && a.Participant.Assignment.IgnoreQuestionTimers);
        q.SnapshotJson = (snapshot with { TimeLimitSeconds = ignored ? null : snapshot.TimeLimitSeconds, IgnoreQuestionTimers = ignored }).ToJson();
        var answered = q.AnswerCount > 0;
        foreach (var s in q.Submissions) { s.IsFinal = false; s.IsFirstAnswer = false; }
        q.State = answered ? AttemptQuestionState.ResetByEdit : AttemptQuestionState.Unanswered;
        q.AnswerCount = 0;
        q.FirstScore = q.LatestScore = null;
        q.DraftJson = null;
        q.DraftSavedAt = null;
        // (60) giờ câu mới chỉ bắt đầu khi học viên mở lại, đủ toàn bộ giới hạn.
        q.TimerStartedAt = null;
        q.TimeLeftSeconds = null;
        var order = snapshot.Options.Select(o => o.Id).ToArray();
        if (a.Participant.Assignment.ShuffleOptions && !snapshot.KeepOptionOrder) RandomNumberGenerator.Shuffle(order.AsSpan());
        q.OptionOrderJson = JsonSerializer.Serialize(order);
        if (q.IsMastered) { q.IsMastered = false; a.MasteredCount--; }
        // (24A, B2b) giữ MasteryReachedAt và Participant.MasteryReached.
        RegradeRules.Recompute(a);
    }
}
