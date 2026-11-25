using Microsoft.EntityFrameworkCore;
using MindSprintAI_ASP.NET_MVC.Data;
using MindSprintAI_ASP.NET_MVC.Data.Entities;

namespace MindSprintAI_ASP.NET_MVC.Services;

public record SelectedScore(decimal Raw, decimal Max, decimal? Practice = null,
    CriticalLabel CriticalLabel = CriticalLabel.None, string? CriticalReasons = null, bool FailedCriticalQuestion = false)
{
    public string? CriticalText => CriticalScoreRules.Text(CriticalLabel, CriticalReasons);
}
public record InputReview(IReadOnlyList<string> Values, IReadOnlyList<string> Answers, IReadOnlyList<bool>? Matches);

public record GradebookRow(
    string UserId,
    string DisplayName,
    CompletionStatus Status,
    CompletionLabels Labels,
    int AttemptsUsed,
    SelectedScore? Score,
    long? LatestAttemptId,
    CardCounts? Cards = null,
    string? MissingCards = null,
    string? Mastery = null,
    string? Progress = null,
    bool BlankRegradeCredit = false,
    bool? Passed = null,
    int ExtraMinutes = 0,
    int ExtraAttempts = 0,
    bool CanAdjust = false,
    bool IsStranger = false,
    bool IsGuest = false,
    int? ParticipantId = null,
    ApprovalStatus ApprovalStatus = ApprovalStatus.NotRequired,
    string? Warning = null,
    bool CanKeepGuest = false,
    Models.DeviceChangeCard? DeviceChange = null,
    ScreenSummary? Screen = null,
    string? EndText = null,
    int SelfReviewCount = 0,
    long? LatestSelfReviewId = null);

public partial class GradebookService(AppDbContext db, AssignmentService assignments, TimeProvider clock)
{
    public static Dictionary<long, InputReview> InputReviews(List<(AttemptQuestion Question, QuestionSnapshot Snapshot)> items) =>
        items.ToDictionary(x => x.Question.Id, x =>
        {
            var json = x.Question.Submissions.Where(s => s.IsFinal).OrderByDescending(s => s.SubmittedAt).ThenByDescending(s => s.Id).FirstOrDefault()?.PayloadJson;
            return new InputReview(InputFeedback.Values(x.Snapshot, json), InputFeedback.Answers(x.Snapshot), InputFeedback.Matches(x.Snapshot, json));
        });
    // Chọn điểm theo cách của bài giao, chỉ dùng lượt đã kết thúc, điểm lần đầu (26).
    public static SelectedScore? Select(IEnumerable<Attempt> attempts, ScoreSelection mode)
    {
        var done = attempts.Where(a => a.Status == AttemptStatus.Ended && !a.ExcludeFromGradebook).OrderBy(a => a.AttemptNumber).ToList();
        if (done.Count == 0) return null;
        var counted = ScoredAttempts(done, mode);
        var pick = mode == ScoreSelection.Average ? null : counted[0];
        return pick != null
            ? CriticalSelectionRules.FromAttempt(pick)
            : new SelectedScore(done.Average(a => a.FirstScore), done.Average(a => a.MaxScore), done.Average(a => a.PracticeScore));
    }

    // Cùng một lựa chọn cho bảng điểm và ma trận; trung bình giữ tất cả lượt đóng góp.
    public static List<Attempt> ScoredAttempts(IEnumerable<Attempt> attempts, ScoreSelection mode)
    {
        var done = attempts.Where(a => a.Status == AttemptStatus.Ended && !a.ExcludeFromGradebook).OrderBy(a => a.AttemptNumber).ToList();
        if (done.Count == 0 || mode == ScoreSelection.Average) return done;
        return [mode switch
        {
            ScoreSelection.First => done[0],
            ScoreSelection.Latest => done[^1],
            // (E6b) ưu tiên đã chấm không liệt, rồi đã chấm liệt; chỉ lấy chờ chấm khi không còn lượt đã chấm.
            _ => done.OrderBy(a => a.CriticalLabel == CriticalLabel.Pending ? 2 : a.CriticalLabel == CriticalLabel.Failed ? 1 : 0)
                .ThenByDescending(a => Ratio(a)).ThenByDescending(a => a.AttemptNumber).First()
        }];
    }

    private static decimal Ratio(Attempt a) => a.MaxScore <= 0 ? 0 : a.FirstScore / a.MaxScore;

    public static SelectedScore? SelectForAssignment(IEnumerable<Attempt> attempts, Assignment assignment)
    {
        if (assignment.Mode == AssignmentMode.Practice)
        {
            var done = attempts.Where(a => a.Status == AttemptStatus.Ended && !a.ExcludeFromGradebook).ToList();
            var score = Select(done, assignment.ScoreSelection);
            return score != null && assignment.ScoreSelection == ScoreSelection.Average ? CriticalSelectionRules.Average(score, done, assignment) : score;
        }
        var latest = attempts.Where(a => a.Status != AttemptStatus.Cancelled && !a.ExcludeFromGradebook).OrderByDescending(a => a.AttemptNumber).FirstOrDefault();
        // Bài làm theo đề chưa chấm (MaxScore = 0): chưa có điểm, không phải 0 điểm.
        if (assignment.Form == AssignmentForm.Document && latest?.MaxScore == 0) return null;
        return latest?.Status == AttemptStatus.Ended ? CriticalSelectionRules.FromAttempt(latest) : null;
    }

    public static bool? Passed(SelectedScore? score, Assignment a) => CriticalSelectionRules.Passed(score, a);

    public async Task<(Assignment Assignment, List<GradebookRow> Rows)> GetReportAsync(int assignmentId, string lecturerId)
    {
        var head = await assignments.GetForLecturerAsync(assignmentId, lecturerId);
        // Thử tải 14/11/2026: mỗi lần làm mới nạp cả đề + câu trả lời của mọi lượt (650 người × 20 câu). Lượt đã xong chỉ cần câu
        // khi tính mốc thuộc bài hoặc có câu "cho điểm tối đa" (đánh dấu chấm lại câu bỏ trống); ngoài ra chỉ nạp câu của lượt đang mở.
        var allQuestions = NeedsEndedQuestions(head);
        var now = clock.GetUtcNow().UtcDateTime;
        // Dữ liệu đã nằm sẵn trong DbContext trước khi mở trang (thường không có: mỗi request là DbContext mới) — đọc lại ở bước (3).
        var preexisting = db.ChangeTracker.Entries().Where(IsReportEntity).Select(e => e.Entity).ToList();
        // Thử tải 13/11/2026: trang theo dõi của GV tự làm mới ~mỗi giây khi học viên thao tác. Trước đây mỗi lần làm mới khóa độc quyền
        // cả bài rồi khóa từng người → 650 học viên đứng hình theo nhịp đó. Nay: (1) chạy thử các quy tắc dọn dẹp trên bản đọc KHÔNG khóa,
        // (2) chỉ người nào thật sự cần thay đổi mới được dọn, mỗi người một giao dịch ngắn bằng khóa học viên (như công việc nền),
        // (3) đọc lại để hiển thị. Kết quả dọn dẹp giống hệt cách cũ, chỉ không bắt cả lớp chờ.
        var snapshot = await ParticipantsQuery(assignmentId, allQuestions).AsNoTrackingWithIdentityResolution().Include(p => p.Assignment).ToListAsync();
        var needUpkeep = new List<(int Id, string UserId)>();
        foreach (var p in snapshot)
            if (await UpkeepAsync(p, now)) needUpkeep.Add((p.Id, p.UserId!));
        foreach (var (id, userId) in needUpkeep)
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            await AttemptLock.LearnerAsync(db, assignmentId, userId);
            var p = await AttemptLock.ParticipantAsync(db, id);
            foreach (var attempt in p.Attempts.Where(AttemptRules.IsOpen).ToList()) await AttemptLock.LoadAsync(db, attempt.Id);
            await db.Entry(p).Collection(x => x.RetakeRequests).Query().LoadAsync();
            await UpkeepAsync(p, now);
            await db.SaveChangesAsync();
            await tx.CommitAsync();
        }
        var a = await assignments.GetForLecturerAsync(assignmentId, lecturerId);
        var changed = needUpkeep.Count > 0;
        var members = await db.ClassMembers.Include(m => m.User)
            .Where(m => m.ClassroomId == a.ClassroomId && m.Role == ClassRole.Student
                && m.Status != ClassMemberStatus.PendingApproval && m.Status != ClassMemberStatus.Invited)
            .ToListAsync();
        // Như trước đây (mỗi người được đọc lại khi khóa). Không đọc lại thứ vừa nạp khi dọn (đã mới), tránh hàng nghìn lệnh lẻ.
        foreach (var entity in preexisting)
            if (db.Entry(entity).State == EntityState.Unchanged) await db.Entry(entity).ReloadAsync();
        var participants = needUpkeep.Count == 0
            ? snapshot // không ai cần dọn (thường gặp nhất): hiển thị luôn bản vừa đọc, không đọc cả bảng điểm lần 2
            : await ParticipantsQuery(assignmentId, allQuestions).ToListAsync();

        var rows = new List<GradebookRow>();
        var sources = members.Select(m => (Member: m, Participant: participants.FirstOrDefault(p => p.UserId == m.UserId && !p.IsStranger), Stranger: false))
            .Concat(participants.Where(p => p.IsStranger && (p.User?.IsGuest != true || p.GuestStartedAt != null))
                .Select(p => (Member: new ClassMember { UserId = p.UserId!, User = p.User!, Status = ClassMemberStatus.Active }, Participant: (Participant?)p, Stranger: true)));
        foreach (var source in sources)
        {
            var m = source.Member;
            var p = source.Participant;
            var gone = m.Status is ClassMemberStatus.Left or ClassMemberStatus.Removed;
            // Người đã rời mà chưa làm thì không hiện; người vào sau khi bài đóng thì không được giao (67).
            if (!source.Stranger && p == null && (gone || !AssignmentService.IsAssignedTo(a, m))) continue;

            var stored = p?.CompletionLabels ?? CompletionLabels.None;
            // (67, B4b) bị xóa khi đang làm đã có nhãn riêng; không thêm "Đã rời lớp" cho người bị xóa.
            var labels = stored | (gone && !stored.HasFlag(CompletionLabels.RemovedFromClass) ? CompletionLabels.LeftClass : CompletionLabels.None);
            var counted = p?.Attempts.Where(x => x.Status != AttemptStatus.Cancelled && x.Kind != AttemptKind.SelfReview).ToList() ?? [];
            var open = counted.FirstOrDefault(AttemptRules.IsOpen);
            var latestEnded = counted.Where(x => x.Status == AttemptStatus.Ended).OrderByDescending(x => x.AttemptNumber).FirstOrDefault();
            var status = p == null ? CompletionStatus.NotStarted
                : open != null && !(open.DeadlineAt <= now)
                    ? (AttemptRules.DisplayStatus(open, now) == AttemptStatus.Paused ? CompletionStatus.Paused : CompletionStatus.InProgress)
                    : counted.Any(x => x.Status == AttemptStatus.Ended || AttemptRules.IsOpen(x))
                        ? (latestEnded != null && AttemptRules.AwaitsScores(latestEnded) ? CompletionStatus.SubmittedAwaitingScore : CompletionStatus.Completed)
                    : CompletionStatus.NotStarted;
            if (open != null && open.DeadlineAt <= now) labels |= CompletionLabels.Expired; // quá hạn nhưng chưa ai mở lại để chốt
            CardCounts? cards = null;
            string? missing = null;
            if (CardCompletion.HasCards(a))
            {
                cards = await CardCompletion.CountsAsync(db, a, p?.Id);
                if (p != null && (open == null || p.CompletionStatus != CompletionStatus.InProgress)) status = p.CompletionStatus;
                if (status == CompletionStatus.Incomplete && CardCompletion.RequiresCards(a))
                    missing = (await CardCompletion.CountsAsync(db, a, p?.Id, beforeEnd: true)).MissingLabel;
            }

            var conflict = a.Mode == AssignmentMode.Test && participants.Any(g => g.Guest?.MergedIntoUserId != null
                && g.UserId == g.Guest.UserId
                && g.Attempts.Any(at => at.Status != AttemptStatus.Cancelled)
                && participants.Any(t => t.Id != g.Id && t.UserId == g.Guest.MergedIntoUserId
                    && (t.Attempts.Count == 0 || t.Attempts.Any(at => at.Status != AttemptStatus.Cancelled)))
                && (g.Id == p?.Id || g.Guest.MergedIntoUserId == m.UserId));
            rows.Add(new GradebookRow(m.UserId, p?.User?.IsGuest == true ? p.GuestDisplayName ?? m.User.DisplayName : m.User.DisplayName, status, labels, counted.Count,
                SelectForAssignment(counted, a),
                (counted.OrderByDescending(x => x.AttemptNumber).FirstOrDefault()
                    ?? (source.Stranger ? p?.Attempts.OrderByDescending(x => x.AttemptNumber).FirstOrDefault() : null))?.Id, cards, missing,
                MasteryRules.Report(a, counted, p?.MasteryReached == true),
                // 0 câu: lượt vừa nộp giữa 2 câu đọc (chỉ nạp câu của lượt đang mở) → không hiện "0/0", lần sau tự đúng.
                open == null || open.Questions.Count == 0 ? null : $"{open.Questions.Count(q => q.AnswerCount > 0)}/{open.Questions.Count} câu đã trả lời",
                allQuestions && counted.Any(at => at.Questions.Any(q => RegradeRules.Effective(q, a.Regrades).BlankCredit)),
                Passed(SelectForAssignment(counted, a), a), p?.ExtraMinutes ?? 0, p?.ExtraAttempts ?? 0, !gone,
                source.Stranger, p?.User?.IsGuest == true, p?.Id, p?.ApprovalStatus ?? ApprovalStatus.NotRequired,
                conflict ? "có 2 lượt, 1 lượt làm khi là khách" : null,
                conflict && p?.User?.IsGuest == true && participants.Any(t => t.UserId == p.Guest!.MergedIntoUserId && t.Attempts.Count == 0),
                DeviceCard(open),
                (open ?? latestEnded) is { MonitorScreen: true } monitored ? ScreenMonitoringRules.Summary(monitored, now) : null,
                latestEnded?.EndReason == AttemptEndReason.ScreenLeaveLimit ? ScreenMonitoringRules.EndText(latestEnded) : null,
                p?.Attempts.Count(x => x.Kind == AttemptKind.SelfReview && x.Status != AttemptStatus.Cancelled) ?? 0,
                p?.Attempts.Where(x => x.Kind == AttemptKind.SelfReview).OrderByDescending(x => x.AttemptNumber).FirstOrDefault()?.Id));
        }
        if (changed) await assignments.NotifyLecturersAsync(assignmentId);
        return (a, rows.OrderBy(r => r.DisplayName).ToList());
    }

    private static bool IsReportEntity(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry e) => e.Entity is Participant or Attempt
        or AttemptQuestion or AnswerSubmission or RetakeRequest or DeviceChangeRequest or ScreenLeave;

    // "Cho điểm tối đa" là cách chấm lại duy nhất bật BlankCredit (RegradeRules.Effective); mốc thuộc bài đếm câu của lượt đã xong.
    public static bool NeedsEndedQuestions(Assignment a) => a.MasteryThreshold != null || a.Regrades.Any(r => r.Method == RegradeMethod.FullCredit);

    private IQueryable<Participant> ParticipantsQuery(int assignmentId, bool allQuestions = true) =>
        (allQuestions
            ? db.Participants.Include(p => p.Attempts).ThenInclude(x => x.Questions).ThenInclude(q => q.Submissions)
            : db.Participants.Include(p => p.Attempts).ThenInclude(x => x.Questions
                .Where(q => q.Attempt.Status == AttemptStatus.InProgress || q.Attempt.Status == AttemptStatus.Paused)).ThenInclude(q => q.Submissions))
        .AsSplitQuery().Include(p => p.RetakeRequests).Include(p => p.User).Include(p => p.Guest)
            .Include(p => p.Attempts).ThenInclude(x => x.DeviceChanges)
            .Include(p => p.Attempts).ThenInclude(x => x.ScreenLeaves)
            .Where(p => p.AssignmentId == assignmentId && p.UserId != null).OrderBy(p => p.Id);

    // Các quy tắc dọn dẹp khi GV mở bảng điểm (giữ nguyên như trước): quyền làm lại hết hạn, lượt quá giờ, trạng thái hoàn thành.
    // Trả về true nếu có gì thay đổi. Chạy được trên bản sao không theo dõi (chạy thử) lẫn bản đã khóa (dọn thật).
    private async Task<bool> UpkeepAsync(Participant p, DateTime now)
    {
        var changed = RetakeService.ExpireUnused(p, now);
        foreach (var attempt in p.Attempts.Where(AttemptRules.IsOpen).ToList())
        {
            // Kèm số câu hết giờ riêng: ở bài Kiểm tra câu hết giờ chỉ đổi trạng thái câu, LiveState không thấy (tài khoản 2 rà vòng 2).
            var before = (AttemptService.LiveState(attempt), attempt.Questions.Count(q => q.State == AttemptQuestionState.LockedTimeout));
            AttemptRules.ExpireIfDue(attempt, p, now);
            changed |= before != (AttemptService.LiveState(attempt), attempt.Questions.Count(q => q.State == AttemptQuestionState.LockedTimeout));
        }
        var completion = (p.CompletionStatus, p.CardsCompletionFinalizedAt);
        await CardCompletion.RefreshAsync(db, p, now);
        return changed | completion != (p.CompletionStatus, p.CardsCompletionFinalizedAt);
    }

    // Bài làm của một học viên cho GV xem: theo thứ tự câu gốc (B4c).
    private static Models.DeviceChangeCard? DeviceCard(Attempt? a)
    {
        var r = a?.DeviceChanges.SingleOrDefault(r => r.Status == DeviceChangeStatus.Pending);
        return r == null ? null : new(r.Id, a!.Id, r.Reason,
            DeviceHintRules.Note(a.LightHash, a.FullHash, a.NetworkPrefix, r.LightHash, r.FullHash, r.NetworkPrefix)) { RequestedAt = r.CreatedAt };
    }

    public async Task<(Attempt Attempt, List<(AttemptQuestion Question, QuestionSnapshot Snapshot)> Items)> GetAttemptForLecturerAsync(
        long attemptId, string lecturerId)
    {
        var header = await db.Attempts.Where(x => x.Id == attemptId)
            .Select(x => new { x.ParticipantId, x.Participant.AssignmentId, x.Participant.UserId }).SingleOrDefaultAsync()
            ?? throw new BusinessException("Không tìm thấy lượt làm.");
        await using var transaction = await db.Database.BeginTransactionAsync();
        await assignments.GetForLecturerAsync(header.AssignmentId, lecturerId);
        // Chỉ xem (và chốt lượt quá giờ) của một người: khóa như học viên đó, không bắt cả lớp chờ (tài khoản 2 rà vòng 2, 13/11/2026).
        // Kiểm lại bên dưới chỉ bắt trường hợp lượt sang người làm khác; gộp khách đổi UserId của chính người làm thì dòng người làm/lượt vẫn được khóa đúng.
        await AttemptLock.LearnerAsync(db, header.AssignmentId, header.UserId ?? "participant:" + header.ParticipantId);
        await AttemptLock.ParticipantAsync(db, header.ParticipantId);
        var locked = await AttemptLock.LoadAsync(db, attemptId);
        if (locked.ParticipantId != header.ParticipantId) throw new BusinessException("Lượt làm vừa được chuyển sang tài khoản khác. Hãy tải lại trang.");
        var attempt = await db.Attempts.Include(x => x.Participant).ThenInclude(p => p.User)
            .Include(x => x.Participant).ThenInclude(p => p.Attempts)
            .Include(x => x.Questions).ThenInclude(q => q.Submissions)
            .FirstOrDefaultAsync(x => x.Id == attemptId)
            ?? throw new BusinessException("Không tìm thấy lượt làm.");
        await assignments.GetForLecturerAsync(attempt.Participant.AssignmentId, lecturerId);
        var before = AttemptService.LiveState(attempt);
        AttemptRules.ExpireIfDue(attempt, attempt.Participant, clock.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync();
        var items = attempt.Questions.Select(q => (q, QuestionSnapshot.FromJson(q.SnapshotJson)))
            .OrderBy(x => x.Item2.OrderIndex).ToList();
        await transaction.CommitAsync();
        if (before != AttemptService.LiveState(attempt)) await assignments.NotifyLecturersAsync(header.AssignmentId);
        return (attempt, items);
    }

    public async Task<List<ForgottenCard>> GetForgottenCardsAsync(int assignmentId, string lecturerId)
    {
        var a = await assignments.GetForLecturerAsync(assignmentId, lecturerId);
        if (!CardCompletion.HasCards(a)) return [];
        // Đếm theo Id trên SQL rồi mới lấy mặt trước: EF không dịch được GroupBy theo cột nvarchar(max) kèm sắp xếp trên record.
        var counts = await db.FlashcardProgresses.Where(p => p.Participant != null && p.Participant.AssignmentId == a.Id
            && !p.Participant.IsStranger
            && p.Flashcard.DeletedAt == null && p.HasTraditionalRating != false && p.LastRating == FlashcardRating.NotRemembered)
            .GroupBy(p => p.FlashcardId).Select(g => new { Id = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count).ToListAsync();
        var ids = counts.Select(x => x.Id).ToList();
        var fronts = await db.Flashcards.Where(f => ids.Contains(f.Id)).ToDictionaryAsync(f => f.Id, f => f.Front);
        return counts.Select(x => new ForgottenCard(fronts[x.Id], x.Count)).ToList();
    }

}
