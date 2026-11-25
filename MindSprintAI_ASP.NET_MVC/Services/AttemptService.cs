using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using MindSprintAI_ASP.NET_MVC.Data;
using MindSprintAI_ASP.NET_MVC.Data.Entities;
using MindSprintAI_ASP.NET_MVC.Models;

namespace MindSprintAI_ASP.NET_MVC.Services;

public record AnswerResult(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? IsCorrect = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? Score = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<int>? CorrectOptionIds = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Explanation = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? CorrectAnswers = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<bool>? BlankCorrect = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Survey = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool CanRetry = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] int AnswerCount = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] MasteryProgress? Mastery = null);

public record TestStatus(bool Started, bool CanStart, DateTime? EndsAt, string? Reason = null, bool Approved = true);
public record LearnState(bool Ended, bool Moved, DateTime? DeadlineAt, string Time, string ReviewUrl,
    IReadOnlyList<QuestionClockState>? QuestionTimers = null, IReadOnlyDictionary<long, int>? QuestionVersions = null);

public partial class AttemptService(AppDbContext db, TimeProvider clock, IEnumerable<IQuestionGrader> graders,
    ILearnDevice device, ILearnNotifier notifier, LearnConnectionTracker connections)
{
    private readonly Dictionary<QuestionType, IQuestionGrader> _graders =
        graders.SelectMany(g => g.Types.Select(t => (t, g))).ToDictionary(x => x.t, x => x.g);

    private readonly HashSet<int> liveChanges = [];

    internal async Task NotifyLiveChangesAsync()
    {
        // (E) chỉ gọi sau commit, không đẩy dữ liệu học viên qua hub.
        foreach (var id in liveChanges) await notifier.LecturersAsync(id);
        liveChanges.Clear();
        if (bellUsers.Count > 0) await new NotificationService(db, clock, notifier).PushAsync(bellUsers.ToArray());
        bellUsers.Clear();
    }

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    // Trả về lượt dở nếu đã có (tải lại / hai tab), nếu không thì tạo lượt mới kèm bản chụp đề.
    public async Task<Attempt> StartAsync(int assignmentId, string userId, bool consent = false, DeviceSignalResult? signals = null)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        var assignment = await AttemptLock.LearnerAsync(db, assignmentId, userId);
        if (assignment.Form == AssignmentForm.FlashcardOnly) throw new BusinessException("Bài này chỉ học thẻ ghi nhớ.");
        var member = await AttemptLock.MemberForLearnerAsync(db, assignment.ClassroomId, userId);
        await AccountLockState.EnsureAllowedAsync(db, userId, Now);
        await AssignmentAccess.EnsureStartAsync(db, assignment, member, userId);
        if (assignment.QuizId != null) // bài làm theo đề (Form = Document) không có quiz
        {
            await db.Entry(assignment).Reference(a => a.Quiz).Query()
                .Include(q => q!.Questions).ThenInclude(q => q.Options).LoadAsync();
            await db.Entry(assignment).Reference(a => a.Quiz).Query()
                .Include(q => q!.Questions).ThenInclude(q => q.Blanks).ThenInclude(b => b.Answers).LoadAsync();
        }
        await EnsurePracticeAccessAsync(assignment, userId);
        var participant = await JoinParticipantAsync(assignment, userId);
        participant = await AttemptLock.ParticipantAsync(db, participant.Id);
        var open = participant.Attempts.FirstOrDefault(AttemptRules.IsOpen);
        if (open?.Kind == AttemptKind.SelfReview)
        {
            if (!AssignmentService.IsOpen(assignment, Now, participant)) throw new BusinessException("Bài đã kết thúc. Hãy chọn Tự ôn quiz (không tính điểm).");
            open = await AttemptLock.LoadAsync(db, open.Id);
            AttemptRules.End(open, participant, AttemptEndReason.Submitted, Now);
            await db.SaveChangesAsync(); // giải phóng lượt dở trước khi bắt đầu lượt tính điểm.
            open = null;
        }
        if (open != null)
        {
            open = await AttemptLock.LoadAsync(db, open.Id);
            var before = LiveState(open);
            var expired = AttemptRules.ExpireIfDue(open, participant, Now);
            if (before != LiveState(open)) liveChanges.Add(assignmentId);
            if (!expired)
            {
                if (NeedsConsent(assignment) && open.DeviceConsentAt == null)
                { EnsureConsent(assignment, consent); open.DeviceConsentAt = Now; }
                AdoptDevice(open);
                await db.SaveChangesAsync();
                await transaction.CommitAsync();
                await NotifyLiveChangesAsync();
                return open;
            }
            await db.SaveChangesAsync();
        }
        var used = participant.Attempts.Count(a => a.Status != AttemptStatus.Cancelled && a.Kind != AttemptKind.SelfReview);
        var limit = assignment.MaxAttempts + participant.ExtraAttempts;
        // (C2) lượt thường chỉ một lần; hủy mới giải phóng chỗ trong trần hai lượt.
        string? reason = !AssignmentService.IsOpen(assignment, Now, participant) ? "Bài chưa mở hoặc đã kết thúc."
            : assignment.Mode == AssignmentMode.Test && assignment.TestStartedAt == null ? "Chờ giảng viên bắt đầu."
            : assignment.Mode == AssignmentMode.Test && AttemptRules.DeadlineFor(assignment, participant) <= Now ? "Bài kiểm tra đã hết giờ."
            : assignment.Mode == AssignmentMode.Test && (used >= 2 || participant.Attempts.Any(a =>
                a.Kind == AttemptKind.Normal && a.Status != AttemptStatus.Cancelled))
                ? "Bạn đã làm lượt đầu. Hãy xin giảng viên duyệt làm lại."
            : limit != null && used >= limit ? "Bạn đã dùng hết số lượt của bài này." : null;
        if (reason != null)
        {
            // (61, 66) từ chối lượt mới vẫn phải giữ việc chốt lượt cũ đã hết giờ.
            if (open != null)
            {
                liveChanges.Add(assignmentId);
                await transaction.CommitAsync();
                await NotifyLiveChangesAsync();
            }
            throw new BusinessException(reason);
        }
        EnsureConsent(assignment, consent);
        var attempt = CreateAttempt(assignment, participant, AttemptRules.DeadlineFor(assignment, participant), consent, signals);
        attempt.MadeAsGuest = await db.Users.AnyAsync(u => u.Id == userId && u.IsGuest);
        if (attempt.MadeAsGuest) participant.GuestStartedAt ??= Now;
        db.Attempts.Add(attempt);
        liveChanges.Add(assignmentId);
        if (participant.CompletionStatus != CompletionStatus.Completed) participant.CompletionStatus = CompletionStatus.InProgress;
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        await NotifyLiveChangesAsync();
        return attempt;
    }

    private async Task<Participant> JoinParticipantAsync(Assignment assignment, string userId)
    {
        var p = await db.Participants.SingleOrDefaultAsync(p => p.AssignmentId == assignment.Id && p.UserId == userId);
        if (p != null) return p;
        p = new Participant { Assignment = assignment, UserId = userId, CreatedAt = Now };
        db.Participants.Add(p);
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException)
        {
            // (B4a, C2) chỉ nhận đúng dòng thắng unique index khi hai yêu cầu cùng tạo người làm.
            db.Entry(p).State = EntityState.Detached;
            p = await db.Participants.SingleOrDefaultAsync(x => x.AssignmentId == assignment.Id && x.UserId == userId);
            if (p == null) throw;
        }
        return p;
    }

    internal Attempt CreateAttempt(Assignment assignment, Participant participant, DateTime? deadline, bool consent = false, DeviceSignalResult? signals = null) =>
        CreateAttemptCore(assignment, participant, deadline, consent, signals, AttemptKind.Normal);

    private Attempt CreateAttemptCore(Assignment assignment, Participant participant, DateTime? deadline, bool consent, DeviceSignalResult? signals, AttemptKind kind)
    {
        var selfReview = kind == AttemptKind.SelfReview;
        if (!selfReview) EnsureConsent(assignment, consent); // (F7b) cả lượt làm lại phải đi qua đây.
        // Bài làm theo đề: không có câu hỏi; MaxScore = 0 tới khi GV chấm (PaperTestService.GradeAsync).
        var questions = assignment.Form == AssignmentForm.Document ? []
            : assignment.Quiz!.Questions.Where(q => q.DeletedAt == null).OrderBy(q => q.OrderIndex).ToArray();
        if (assignment.ShuffleQuestions) RandomNumberGenerator.Shuffle(questions.AsSpan());
        var attempt = new Attempt
        {
            Kind = kind, ExcludeFromGradebook = selfReview,
            Participant = participant, AttemptNumber = participant.Attempts.Select(a => a.AttemptNumber).DefaultIfEmpty().Max() + 1,
            StartedAt = Now, LastSeenAt = Now, CreatedAt = Now, DeadlineAt = deadline,
            ActiveDeviceId = device.Id, DisconnectedAt = Now,
            DeviceConsentAt = !selfReview && NeedsConsent(assignment) && consent ? Now : null,
            MonitorScreen = !selfReview && assignment.MonitorScreen, AutoSubmitLeaves = selfReview ? null : assignment.AutoSubmitLeaves,
            MonitorHeartbeatAt = !selfReview && assignment.MonitorScreen ? Now : null,
            LightHash = signals?.LightHash, FullHash = NeedsConsent(assignment) && consent ? signals?.FullHash : null, NetworkPrefix = signals?.NetworkPrefix,
            MaxScore = questions.Where(q => !SurveyRules.IsSurvey(q.Type)).Sum(q => q.Points),
            PracticeScore = assignment.MasteryThreshold != null && assignment.ScoreRetries ? 0 : null,
            Questions = questions.Select((q, i) =>
            {
                var options = q.Options.OrderBy(o => o.OrderIndex).Select(o => o.Id).ToArray();
                if (assignment.ShuffleOptions && !q.KeepOptionOrder
                    && SurveyRules.IsChoice(q.Type))
                    RandomNumberGenerator.Shuffle(options.AsSpan());
                return new AttemptQuestion
                {
                    QuestionId = q.Id, QuestionVersion = q.Version, DisplayOrder = i,
                    OptionOrderJson = JsonSerializer.Serialize(options),
                    SnapshotJson = (QuestionSnapshot.From(q) with
                    { TimeLimitSeconds = assignment.IgnoreQuestionTimers ? null : q.TimeLimitSeconds, IgnoreQuestionTimers = assignment.IgnoreQuestionTimers }).ToJson()
                };
            }).ToList()
        };
        RegradeRules.Recompute(attempt);
        db.Audit?.Add(AuditAction.AttemptStarted, "Attempt", 0, participant.UserId, resolveTarget: () => attempt.Id.ToString()); // (NK3) cả làm lại.
        if (attempt.DeviceConsentAt != null)
            db.Audit?.Add(AuditAction.DeviceConsentGiven, "Attempt", 0, participant.UserId, resolveTarget: () => attempt.Id.ToString());
        return attempt;
    }

    public async Task<TestStatus> GetTestStatusAsync(int assignmentId, string userId)
    {
        var a = await db.Assignments.AsNoTracking().FirstOrDefaultAsync(a => a.Id == assignmentId)
            ?? throw new BusinessException("Không tìm thấy bài.");
        var member = await db.ClassMembers.FirstOrDefaultAsync(m => m.ClassroomId == a.ClassroomId && m.UserId == userId);
        var p = await db.Participants.AsNoTracking().SingleOrDefaultAsync(p => p.AssignmentId == assignmentId && p.UserId == userId);
        if (p?.IsStranger != true && !AssignmentAccess.CanStart(a, member, p)) throw new ForbiddenException();
        var approved = AssignmentAccess.CanStart(a, member, p) && p?.ApprovalStatus != ApprovalStatus.Rejected;
        var reason = p?.ApprovalStatus == ApprovalStatus.Pending ? "Chờ giảng viên duyệt"
            : p?.ApprovalStatus == ApprovalStatus.Rejected ? "Giảng viên đã từ chối yêu cầu tham gia." : null;
        p ??= new Participant();
        var deadline = AttemptRules.DeadlineFor(a, p);
        return new TestStatus(a.TestStartedAt != null, approved && AssignmentService.IsOpen(a, Now, p)
            && (a.Mode != AssignmentMode.Test || a.TestStartedAt != null && deadline > Now), deadline, reason, approved);
    }

    public async Task<Attempt> GetForLearnerAsync(long attemptId, string userId)
    {
        await using var transaction = db.Database.CurrentTransaction == null ? await db.Database.BeginTransactionAsync() : null;
        var attempt = await LoadForLearnerAsync(attemptId, userId);
        if (transaction != null)
        {
            await transaction.CommitAsync();
            await NotifyLiveChangesAsync();
        }
        return attempt;
    }

    public async Task<LearnState> GetStateAsync(long id, string userId)
    {
        var a = await GetForLearnerAsync(id, userId);
        return new(!AttemptRules.IsOpen(a), a.Kind != AttemptKind.SelfReview && a.ActiveDeviceId != device.Id, a.DeadlineAt,
            VnTime.Format(a.DeadlineAt), $"/Learn/Review/{id}", a.Questions.Where(q =>
                QuestionSnapshot.FromJson(q.SnapshotJson).TimeLimitSeconds != null).Select(q =>
                {
                    var timer = QuestionTimerRules.Read(a, q, Now);
                    return new QuestionClockState(q.Id, timer.SecondsLeft, timer.Locked, timer.Expired, q.TimerStartedAt != null);
                }).ToList(), a.Questions.ToDictionary(q => q.Id, q => q.QuestionVersion));
    }

    internal async Task<Attempt> LoadForLearnerAsync(long attemptId, string userId, bool writing = false, string? deviceOverride = null)
    {
        var header = await db.Attempts.Where(a => a.Id == attemptId)
            .Select(a => new { a.ParticipantId, a.Participant.AssignmentId, a.Participant.UserId }).SingleOrDefaultAsync()
            ?? throw new BusinessException("Không tìm thấy lượt làm.");
        if (header.UserId != userId) throw new ForbiddenException();
        var assignment = await AttemptLock.LearnerAsync(db, header.AssignmentId, userId);
        var member = writing ? await AttemptLock.MemberForLearnerAsync(db, assignment.ClassroomId, userId) : null;
        if (writing) await AccountLockState.EnsureAllowedAsync(db, userId, Now);
        await AttemptLock.ParticipantAsync(db, header.ParticipantId);
        var attempt = await AttemptLock.LoadAsync(db, attemptId);
        // Kiểm lại sau khóa: gộp tài khoản có thể vừa chuyển quyền sở hữu trong lúc request chờ.
        if (attempt.Participant.UserId != userId) throw new ForbiddenException();
        await EnsurePracticeAccessAsync(assignment, userId);
        var before = LiveState(attempt);
        if (AttemptRules.ExpireIfDue(attempt, attempt.Participant, Now)) await db.SaveChangesAsync();
        await ObserveScreenAsync(attempt); // (H8) mọi lần đọc/ghi cũng chốt ngưỡng, không chỉ chờ worker.
        if (attempt.Kind != AttemptKind.SelfReview) await CardCompletion.RefreshAsync(db, attempt.Participant, Now);
        await db.SaveChangesAsync();
        if (writing)
        {
            if (attempt.Kind != AttemptKind.SelfReview && (NeedsConsent(assignment) || attempt.MonitorScreen) && attempt.DeviceConsentAt == null)
                throw new BusinessException("Bạn cần bấm Tôi đồng ý trước khi bắt đầu bài này.");
            var hiddenNotice = await GetHiddenContentNoticeAsync(assignment);
            if (hiddenNotice != null) throw new BusinessException(hiddenNotice);
            var currentDevice = deviceOverride ?? device.Id;
            EnsureDevice(attempt, currentDevice);
            // (33A, B3a, 41A) làm lại đã duyệt được kiểm thành viên lúc bắt đầu, vẫn được làm khi bài đóng.
            var retake = attempt.Kind == AttemptKind.Retake && assignment.Mode == AssignmentMode.Test
                && attempt.RetakeRequest?.Status == RetakeRequestStatus.Used
                && attempt.RetakeRequest.ParticipantId == attempt.ParticipantId
                && attempt.RetakeRequest.WindowEndsAt > Now && attempt.DeadlineAt > Now;
            var selfReview = attempt.Kind == AttemptKind.SelfReview && assignment.Mode == AssignmentMode.Practice;
            if (selfReview && !await IsSelfReviewMemberAsync(assignment, userId)) throw new ForbiddenException();
            if (!selfReview && !retake && !AssignmentAccess.CanStart(assignment, member, attempt.Participant)) throw new ForbiddenException();
            if (!selfReview && !retake && assignment.Status == AssignmentStatus.Closed) throw new BusinessException("Bài đã đóng.");
        }
        if (before != LiveState(attempt)) liveChanges.Add(attempt.Participant.AssignmentId);
        return attempt;
    }

    private async Task EnsurePracticeAccessAsync(Assignment assignment, string userId)
    {
        if (assignment.Mode != AssignmentMode.Practice) return;
        var setId = assignment.StudySetId ?? await db.Quizzes.Where(q => q.Id == assignment.QuizId)
            .Select(q => q.StudySetId).SingleAsync();
        await StudySetLearningAccess.EnsureNotTakingTestAsync(db, setId, userId);
    }

    internal static (AttemptStatus, int, CompletionStatus, CompletionLabels) LiveState(Attempt a) =>
        (a.Status, a.Questions.Sum(q => q.AnswerCount), a.Participant.CompletionStatus, a.Participant.CompletionLabels);

    private void AdoptDevice(Attempt attempt)
    {
        // (B4a) lượt cũ chưa có thiết bị nhận máy đầu tiên mở nó, dưới khóa hàng.
        if (AttemptRules.IsOpen(attempt) && (attempt.ActiveDeviceId == null || attempt.Kind == AttemptKind.SelfReview)) attempt.ActiveDeviceId = device.Id;
    }

    internal static void EnsureDevice(Attempt attempt, string? deviceId)
    {
        if (attempt.Kind == AttemptKind.SelfReview) return;
        if (string.IsNullOrWhiteSpace(deviceId) || attempt.ActiveDeviceId != deviceId)
            throw new BusinessException("Bài đang mở trên thiết bị khác. Hãy mở lại bài để chuyển sang thiết bị này.");
    }

    public async Task<(Attempt Attempt, bool NeedsTakeover)> OpenPlayAsync(long id, string userId)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        var a = await LoadForLearnerAsync(id, userId);
        AdoptDevice(a);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        await NotifyLiveChangesAsync();
        return (a, a.Kind != AttemptKind.SelfReview && AttemptRules.IsOpen(a) && a.ActiveDeviceId != device.Id);
    }

    public async Task TakeoverAsync(long id, string userId, string? reason = null, DeviceSignalResult? signals = null)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        var a = await LoadForLearnerAsync(id, userId, writing: false);
        if (!AttemptRules.IsOpen(a))
        {
            await tx.CommitAsync();
            await NotifyLiveChangesAsync();
            throw new BusinessException("Lượt này đã kết thúc.");
        }
        if (a.ActiveDeviceId == device.Id)
        {
            await tx.CommitAsync();
            await NotifyLiveChangesAsync();
            return;
        }
        if (a.Kind != AttemptKind.SelfReview && a.Participant.Assignment.DeviceLock)
        {
            await RequestDeviceChangeAsync(a, reason, signals); // (F2) không đổi máy trước khi GV duyệt.
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            await NotifyLiveChangesAsync();
            return;
        }
        var oldConnections = a.ActiveDeviceId == null
            ? [] : connections.ForDevice(id, a.ActiveDeviceId);
        QuestionTimerRules.Pause(a, Now);
        foreach (var pending in a.DeviceChanges.Where(r => r.Status == DeviceChangeStatus.Pending))
        { pending.Status = DeviceChangeStatus.Cancelled; pending.DecidedAt = Now; }
        ScreenMonitoringRules.CloseAll(a, Now);
        a.MonitorHeartbeatAt = a.MonitorScreen ? Now : null;
        a.ActiveDeviceId = device.Id;
        a.DisconnectedAt = Now;
        a.Status = AttemptStatus.InProgress;
        ApplyDeviceSignals(a, signals == null ? null : signals with { FullHash = a.DeviceConsentAt != null && (NeedsConsent(a.Participant.Assignment) || a.MonitorScreen) ? signals.FullHash : null });
        liveChanges.Add(a.Participant.AssignmentId);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        await NotifyLiveChangesAsync();
        if (oldConnections.Length > 0) await notifier.MovedAsync(id, oldConnections);
    }

    public async Task PauseAsync(long id, string userId)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        var a = await LoadForLearnerAsync(id, userId, writing: true);
        if (AttemptRules.IsOpen(a))
        {
            QuestionTimerRules.Pause(a, Now); // (59) chỉ dừng giờ riêng Ôn tập, hạn lượt vẫn chạy.
            a.Status = AttemptStatus.Paused;
            liveChanges.Add(a.Participant.AssignmentId);
        }
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        await NotifyLiveChangesAsync();
    }

    public async Task<OpenQuestionResult> OpenQuestionAsync(long id, long questionId, string userId, bool retry = false, int? questionVersion = null)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        var a = await LoadForLearnerAsync(id, userId, writing: true);
        var q = a.Questions.SingleOrDefault(q => q.Id == questionId)
            ?? throw new BusinessException("Câu hỏi không thuộc lượt này.");
        EnsureQuestionVersion(q, questionVersion);
        if (!AttemptRules.IsOpen(a))
        {
            await tx.CommitAsync();
            await NotifyLiveChangesAsync();
            throw new BusinessException("Lượt này đã kết thúc.");
        }
        var before = LiveState(a);
        QuestionTimerRules.Open(a, q, Now, retry);
        if (before != LiveState(a)) liveChanges.Add(a.Participant.AssignmentId);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        await NotifyLiveChangesAsync();
        var timer = QuestionTimerRules.Read(a, q, Now);
        var questions = ProjectQuestions(a, false, q.Id); // Lượt đang mở: cùng bộ lọc nội dung/đáp án với trang Play.
        return new(questions.Single(x => x.Id == q.Id), a.Questions.Count, timer.SecondsLeft, timer.Locked, timer.Expired);
    }

    public async Task<AnswerResult> AnswerAsync(long attemptId, long attemptQuestionId, Guid clientRequestId,
        IReadOnlyList<int> selectedOptionIds, string userId, IReadOnlyList<string>? blanks = null, string? value = null, int? questionVersion = null)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        var attempt = await LoadForLearnerAsync(attemptId, userId, writing: true);
        var currentQuestion = attempt.Questions.SingleOrDefault(q => q.Id == attemptQuestionId)
            ?? throw new BusinessException("Câu hỏi không thuộc lượt này.");
        EnsureQuestionVersion(currentQuestion, questionVersion);
        // Gửi lại cùng yêu cầu (mạng chậm, bấm hai lần): trả kết quả cũ, không ghi lần thứ hai.
        var previous = await db.AnswerSubmissions.Include(s => s.AttemptQuestion).ThenInclude(q => q.Attempt)
            .ThenInclude(a => a.Participant).ThenInclude(p => p.Assignment)
            .FirstOrDefaultAsync(s => s.ClientRequestId == clientRequestId);
        if (previous != null)
        {
            if (previous.AttemptQuestion.Attempt.Participant.UserId != userId) throw new ForbiddenException();
            if (previous.AttemptQuestion.AttemptId != attemptId || previous.AttemptQuestionId != attemptQuestionId)
                throw new BusinessException("Câu trả lời này đã được gửi. Hãy tải lại trang rồi làm tiếp.");
            await transaction.CommitAsync();
            await NotifyLiveChangesAsync();
            return ToResult(previous.AttemptQuestion, previous);
        }

        if (!AttemptRules.IsOpen(attempt))
        {
            await transaction.CommitAsync();
            await NotifyLiveChangesAsync();
            throw new BusinessException("Lượt này đã kết thúc.");
        }
        var aq = attempt.Questions.FirstOrDefault(q => q.Id == attemptQuestionId)
            ?? throw new BusinessException("Câu hỏi không thuộc lượt này.");
        if (QuestionTimerRules.RejectAnswer(attempt, aq, Now))
        {
            await transaction.CommitAsync();
            await NotifyLiveChangesAsync(); // (58B, 59) giữ khóa/timeout dù từ chối câu trả lời đến muộn.
            throw new BusinessException("Câu này đã hết giờ.");
        }
        // Ôn tập không làm chủ: mỗi câu trả lời một lần, chọn là chốt.
        var test = attempt.Participant.Assignment.Mode == AssignmentMode.Test;
        if (!test && aq.AnswerCount > 0 && !MasteryRules.CanRetry(attempt, aq))
            throw new BusinessException("Câu này không được trả lời lại hoặc đã hết số lần trả lời.");

        var snapshot = QuestionSnapshot.FromJson(aq.SnapshotJson);
        if (snapshot.TimeLimitSeconds != null && aq.TimerStartedAt == null)
            throw new BusinessException("Hãy mở câu hỏi trước khi trả lời.");
        var payload = AnswerPayload(snapshot.Type, selectedOptionIds, blanks, value);
        var grade = GraderFor(snapshot.Type).Grade(snapshot, payload);

        var submission = new AnswerSubmission
        {
            ClientRequestId = clientRequestId, PayloadJson = payload, IsCorrect = grade.IsCorrect, Score = grade.Score,
            IsFirstAnswer = aq.AnswerCount == 0, IsFinal = true, SubmittedAt = Now
        };
        AttemptRules.RecordAnswer(attempt, aq, submission, Now);
        if (aq.State != AttemptQuestionState.LockedTimeout) aq.State = AttemptQuestionState.Answered;
        if (!test) QuestionTimerRules.Stop(aq, Now);
        attempt.LastSeenAt = Now;
        liveChanges.Add(attempt.Participant.AssignmentId);

        try
        {
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            await NotifyLiveChangesAsync();
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync();
            db.ChangeTracker.Clear();
            var winner = await db.AnswerSubmissions.Include(s => s.AttemptQuestion).ThenInclude(q => q.Attempt)
                .ThenInclude(a => a.Participant).ThenInclude(p => p.Assignment)
                .FirstOrDefaultAsync(s => s.ClientRequestId == clientRequestId)
                ?? throw new BusinessException("Không lưu được câu trả lời, hãy thử lại.");
            if (winner.AttemptQuestion.Attempt.Participant.UserId != userId) throw new ForbiddenException();
            if (winner.AttemptQuestion.AttemptId != attemptId || winner.AttemptQuestionId != attemptQuestionId)
                throw new BusinessException("Câu trả lời này đã được gửi. Hãy tải lại trang rồi làm tiếp.");
            EnsureDevice(winner.AttemptQuestion.Attempt, device.Id);
            return ToResult(winner.AttemptQuestion, winner);
        }
        return ToResult(aq, submission);
    }

    public async Task SaveDraftAsync(long attemptId, long questionId, IReadOnlyList<int> selected, string userId,
        IReadOnlyList<string>? blanks = null, string? value = null, int? questionVersion = null)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        var a = await LoadForLearnerAsync(attemptId, userId, writing: true);
        if (!AttemptRules.IsOpen(a))
        {
            await transaction.CommitAsync();
            await NotifyLiveChangesAsync();
            throw new BusinessException("Lượt này đã kết thúc.");
        }
        var q = a.Questions.SingleOrDefault(q => q.Id == questionId)
            ?? throw new BusinessException("Câu hỏi không thuộc lượt này.");
        EnsureQuestionVersion(q, questionVersion);
        if (QuestionTimerRules.RejectAnswer(a, q, Now))
        {
            await transaction.CommitAsync();
            await NotifyLiveChangesAsync();
            throw new BusinessException("Câu này đã hết giờ.");
        }
        var snap = QuestionSnapshot.FromJson(q.SnapshotJson);
        if (snap.TimeLimitSeconds != null && q.TimerStartedAt == null)
            throw new BusinessException("Hãy mở câu hỏi trước khi trả lời.");
        if (a.Participant.Assignment.Mode != AssignmentMode.Test || snap.Type is not
            (QuestionType.MultipleChoice or QuestionType.TextInput or QuestionType.NumberInput or QuestionType.SurveyMultiple or QuestionType.SurveyText))
            throw new BusinessException("Dạng câu này không lưu nháp.");
        var payload = AnswerPayload(snap.Type, selected, blanks, value);
        if (snap.Type == QuestionType.MultipleChoice && selected.Count > 0) GraderFor(snap.Type).Grade(snap, payload);
        if (SurveyRules.IsSurvey(snap.Type) && (selected.Count > 0 || !string.IsNullOrWhiteSpace(value)))
            GraderFor(snap.Type).Grade(snap, payload);
        q.DraftJson = payload;
        q.DraftSavedAt = Now;
        a.LastSeenAt = Now;
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        await NotifyLiveChangesAsync();
    }

    public async Task ToggleFlagAsync(long attemptId, long attemptQuestionId, string userId)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        var attempt = await LoadForLearnerAsync(attemptId, userId, writing: true);
        if (!AttemptRules.IsOpen(attempt))
        {
            await transaction.CommitAsync();
            await NotifyLiveChangesAsync();
            throw new BusinessException("Lượt này đã kết thúc.");
        }
        var aq = attempt.Questions.FirstOrDefault(q => q.Id == attemptQuestionId)
            ?? throw new BusinessException("Câu hỏi không thuộc lượt này.");
        aq.Flagged = !aq.Flagged;
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        await NotifyLiveChangesAsync();
    }

    // Nộp bài: câu bỏ trống giữ 0 điểm.
    public async Task FinishAsync(long attemptId, string userId)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        var attempt = await LoadForLearnerAsync(attemptId, userId, writing: true);
        if (!AttemptRules.IsOpen(attempt))
        {
            await transaction.CommitAsync();
            await NotifyLiveChangesAsync();
            return;
        }
        var missing = SurveyRules.MissingRequired(attempt).Where(number => attempt.Kind != AttemptKind.SelfReview &&
            attempt.Questions.Single(q => q.DisplayOrder + 1 == number).State != AttemptQuestionState.LockedTimeout).ToList();
        if (missing.Count > 0) throw new BusinessException("Còn câu khảo sát bắt buộc chưa trả lời: câu " + string.Join(", ", missing));
        var mastery = MasteryRules.Progress(attempt);
        if (mastery is { CanFinish: false }) throw new BusinessException("Phải trả lời mọi câu ít nhất một lần và đạt mốc hoặc dùng hết số lần trả lời lại.");
        AttemptRules.End(attempt, attempt.Participant, mastery?.Reached == true ? AttemptEndReason.FinishedAfterMastery : AttemptEndReason.Submitted, Now);
        if (attempt.Kind != AttemptKind.SelfReview) await CardCompletion.RefreshAsync(db, attempt.Participant, Now);
        liveChanges.Add(attempt.Participant.AssignmentId);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        await NotifyLiveChangesAsync();
    }

    // Trang bài giao phía học viên: các lượt của mình và có bắt đầu lượt mới được không.
    public async Task<(Assignment Assignment, List<Attempt> Attempts, bool CanStart, string? Reason)> GetOverviewAsync(
        int assignmentId, string userId)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        await AttemptLock.LearnerAsync(db, assignmentId, userId);
        var participantId = await db.Participants.Where(p => p.AssignmentId == assignmentId && p.UserId == userId)
            .Select(p => (int?)p.Id).SingleOrDefaultAsync();
        if (participantId != null) await AttemptLock.ParticipantAsync(db, participantId.Value);
        var assignment = await db.Assignments.Include(a => a.Quiz).Include(a => a.Classroom)
            .FirstOrDefaultAsync(a => a.Id == assignmentId)
            ?? throw new BusinessException("Không tìm thấy bài.");
        var participant = await db.Participants.Include(p => p.Attempts).ThenInclude(a => a.Questions).ThenInclude(q => q.Submissions)
            .Include(p => p.RetakeRequests)
            .FirstOrDefaultAsync(p => p.AssignmentId == assignmentId && p.UserId == userId);
        var member = assignment.ClassroomId == null ? null : await db.ClassMembers.FirstOrDefaultAsync(m =>
            m.ClassroomId == assignment.ClassroomId && m.UserId == userId);
        var assigned = AssignmentAccess.CanStart(assignment, member, participant);
        if (!assigned && participant == null && !await CanSelfReviewAsync(assignment, userId)) throw new ForbiddenException(); // đã rời lớp vẫn xem được bài mình đã làm (41A)
        if (participant != null && RetakeService.ExpireUnused(participant, Now))
        {
            liveChanges.Add(assignmentId);
            await db.SaveChangesAsync();
        }

        var attempts = participant?.Attempts.Where(a => a.Status != AttemptStatus.Cancelled).OrderBy(a => a.AttemptNumber).ToList() ?? [];
        if (attempts.FirstOrDefault(AttemptRules.IsOpen) is { } open)
        {
            open = await AttemptLock.LoadAsync(db, open.Id);
            var before = LiveState(open);
            if (AttemptRules.ExpireIfDue(open, participant!, Now)) await db.SaveChangesAsync();
            if (before != LiveState(open)) liveChanges.Add(assignmentId);
        }
        if (participant != null)
        {
            var before = participant.CompletionStatus;
            await CardCompletion.RefreshAsync(db, participant, Now);
            if (before != participant.CompletionStatus) liveChanges.Add(assignmentId);
            await db.SaveChangesAsync();
        }

        var limit = assignment.MaxAttempts + (participant?.ExtraAttempts ?? 0);
        string? reason = participant?.ApprovalStatus == ApprovalStatus.Pending ? "Chờ giảng viên duyệt"
            : participant?.ApprovalStatus == ApprovalStatus.Rejected ? "Giảng viên đã từ chối yêu cầu tham gia."
            : assignment.Form == AssignmentForm.FlashcardOnly ? "Bài này chỉ học thẻ ghi nhớ."
            : !assigned ? "Bạn không còn ở trong lớp này."
            : attempts.Any(x => x.Kind != AttemptKind.SelfReview && AttemptRules.IsOpen(x)) ? null
            : !AssignmentService.IsOpen(assignment, Now, participant) ? (assignment.OpensAt > Now ? $"Bài mở lúc {VnTime.Format(assignment.OpensAt)}." : "Bài đã kết thúc.")
            : assignment.Mode == AssignmentMode.Test && assignment.TestStartedAt == null ? "Chờ giảng viên bắt đầu."
            : assignment.Mode == AssignmentMode.Test && AttemptRules.DeadlineFor(assignment, participant ?? new Participant()) <= Now ? "Bài kiểm tra đã hết giờ."
            : assignment.Mode == AssignmentMode.Test && attempts.Any(a => a.Kind == AttemptKind.Normal) ? "Bạn đã làm lượt đầu. Có thể xin giảng viên duyệt làm lại."
            : limit != null && attempts.Count(x => x.Kind != AttemptKind.SelfReview) >= limit ? "Bạn đã dùng hết số lượt của bài này."
            : null;
        await transaction.CommitAsync();
        await NotifyLiveChangesAsync();
        return (assignment, attempts, reason == null, reason);
    }

    public async Task<List<Participant>> ListMyParticipationsAsync(string userId)
    {
        var ids = await db.Participants.Where(p => p.UserId == userId && p.Assignment.Form != AssignmentForm.QuizOnly)
            .Select(p => p.AssignmentId).ToListAsync();
        foreach (var id in ids) await GetOverviewAsync(id, userId);
        return await db.Participants.Include(p => p.Attempts).Where(p => p.UserId == userId).ToListAsync();
    }

    private IQuestionGrader GraderFor(QuestionType type) =>
        _graders.TryGetValue(type, out var g) ? g : throw new BusinessException("Dạng câu này chưa hỗ trợ.");

    private static AnswerResult ToResult(AttemptQuestion aq, AnswerSubmission s)
    {
        if (aq.Attempt.Participant.Assignment.Mode == AssignmentMode.Test) return new AnswerResult();
        var snap = QuestionSnapshot.FromJson(aq.SnapshotJson);
        if (SurveyRules.IsSurvey(snap.Type)) return new AnswerResult(Survey: true, AnswerCount: aq.AnswerCount, Mastery: MasteryRules.Progress(aq.Attempt));
        var effective = RegradeRules.Effective(aq, aq.Attempt.Participant.Assignment.Regrades);
        return new AnswerResult(effective.IsCorrect == true, effective.Latest,
            snap.Options.Where(o => o.IsCorrect).Select(o => o.Id).ToList(), snap.Explanation,
            InputFeedback.Answers(snap), InputFeedback.Matches(snap, s.PayloadJson),
            CanRetry: MasteryRules.CanRetry(aq.Attempt, aq), AnswerCount: aq.AnswerCount, Mastery: MasteryRules.Progress(aq.Attempt));
    }

    public static bool CanShowScores(Participant p) =>
        p.Assignment.Mode == AssignmentMode.Practice || p.Assignment.ScoresPublishedAt != null;

    public async Task<LearnerAssignmentModel> GetAssignmentPageAsync(int assignmentId, string userId)
    {
        var (a, list, canStart, reason) = await GetOverviewAsync(assignmentId, userId);
        var hiddenNotice = await GetHiddenContentNoticeAsync(a);
        var waiting = reason == "Chờ giảng viên duyệt" || a.Mode == AssignmentMode.Test && a.TestStartedAt == null && AssignmentService.IsOpen(a, Now)
            && reason == "Chờ giảng viên bắt đầu.";
        if (waiting && reason != "Chờ giảng viên duyệt")
        {
            // (B1) chỉ mở trang mới vào phòng chờ; endpoint thăm dò trạng thái không tạo dữ liệu.
            await using var transaction = await db.Database.BeginTransactionAsync();
            a = await AttemptLock.LearnerAsync(db, assignmentId, userId);
            var member = await AttemptLock.MemberForLearnerAsync(db, a.ClassroomId, userId);
            if (member?.Status == ClassMemberStatus.Active && AssignmentService.IsAssignedTo(a, member)
                && AssignmentService.IsOpen(a, Now) && a.TestStartedAt == null)
            {
                var exists = await db.Participants.AnyAsync(p => p.AssignmentId == assignmentId && p.UserId == userId);
                await JoinParticipantAsync(a, userId);
                if (!exists) liveChanges.Add(assignmentId);
            }
            await transaction.CommitAsync();
            await NotifyLiveChangesAsync();
        }
        var p = await db.Participants.Include(p => p.Guest).Include(p => p.Attempts).Include(p => p.RetakeRequests)
            .SingleOrDefaultAsync(p => p.AssignmentId == assignmentId && p.UserId == userId);
        var showScores = a.Mode == AssignmentMode.Practice || a.ScoresPublishedAt != null;
        var ungraded = await SurveyRules.IsUngradedAsync(db, a.QuizId);
        var membership = await db.ClassMembers.SingleOrDefaultAsync(m => m.ClassroomId == a.ClassroomId && m.UserId == userId);
        var accessible = AssignmentAccess.CanStart(a, membership, p);
        var cards = CardCompletion.HasCards(a) ? await CardCompletion.CountsAsync(db, a, p?.Id) : null;
        // (D5b) bộ đếm đến hạn hiển thị theo chế độ thực tế, không để view tính lịch SRS.
        if (cards != null && hiddenNotice == null && accessible
            && (a.FlashcardMode == FlashcardMode.SpacedRepetition
                || a.FlashcardMode == FlashcardMode.LearnerChoice && p?.FlashcardMode == FlashcardMode.SpacedRepetition))
            cards = cards with { DueCount = await new SrsDueService(db, clock).CountAsync(a.StudySetId!.Value, userId, p?.Id) };
        var changedParts = await db.AssignmentChanges.AnyAsync(c => c.AssignmentId == a.Id && c.Field == nameof(Assignment.RequiredParts));
        return new LearnerAssignmentModel
        {
            CanSelfReview = await CanSelfReviewAsync(a, userId),
            CanSelfReviewCards = await CanSelfReviewCardsAsync(a, userId),
            Assignment = a, Attempts = list, CanStart = canStart, CannotStartReason = reason,
            HiddenContentNotice = hiddenNotice, IsClassMember = membership?.Status == ClassMemberStatus.Active,
            RemainingAttempts = a.Mode != AssignmentMode.Practice || a.MaxAttempts == null ? null
                : Math.Max(0, a.MaxAttempts.Value + (p?.ExtraAttempts ?? 0) - list.Count(x => x.Kind != AttemptKind.SelfReview)),
            PersonalDeadline = AttemptRules.DeadlineFor(a, p ?? new Participant()),
            IsWaiting = waiting, ShowScores = showScores, Ungraded = ungraded,
            AttemptCriticalTexts = showScores ? list.ToDictionary(x => x.Id, x => CriticalScoreRules.Text(x.CriticalLabel, x.CriticalReasons)) : [],
            Mastery = MasteryRules.Report(a, list.Where(x => x.Kind != AttemptKind.SelfReview), p?.MasteryReached == true),
            AttemptMastery = list.Where(x => x.Status == AttemptStatus.Ended).ToDictionary(x => x.Id,
                x => MasteryRules.Report(a, [x], x.MasteryReachedAt != null)),
            CanSeeSurveyResults = hiddenNotice == null && a.SurveyResultsPublishedAt != null && list.Any(x => x.Kind != AttemptKind.SelfReview && x.Status == AttemptStatus.Ended)
                && !await db.Users.AnyAsync(u => u.Id == userId && u.IsGuest),
            Score = showScores ? GradebookService.SelectForAssignment(list, a) : null,
            RetakeRequests = p?.RetakeRequests.OrderByDescending(r => r.CreatedAt).ToList() ?? [],
            CanRequestRetake = hiddenNotice == null && accessible && p != null && RetakeService.CanRequest(p),
            Cards = cards,
            Completion = p?.CompletionStatus, Labels = p?.CompletionLabels ?? CompletionLabels.None,
            MissingCards = p?.CompletionStatus == CompletionStatus.Incomplete && CardCompletion.RequiresCards(a)
                ? (await CardCompletion.CountsAsync(db, a, p.Id, beforeEnd: true)).MissingLabel : null,
            CanStudyCards = hiddenNotice == null && accessible && p?.ApprovalStatus != ApprovalStatus.Pending && p?.ApprovalStatus != ApprovalStatus.Rejected
                && CardCompletion.HasCards(a) && (a.OpensAt == null || a.OpensAt <= Now)
                && (!CardCompletion.Ended(a, Now) || a.AllowFlashcardsAfterDue),
            // (75b) thông báo lưu trên trang bài giao, đọc lại được sau khi học viên quay lại.
            RequirementsNotice = !changedParts ? null : p?.CompletionLabels.HasFlag(CompletionLabels.CompletedBeforeCardsRequired) == true
                ? "Giảng viên đã thay đổi phần bắt buộc. Bạn giữ trạng thái Hoàn thành; khuyến khích học thêm thẻ."
                : $"Giảng viên đã thay đổi phần bắt buộc thành {DisplayText.Of(a.RequiredParts)}. Hãy hoàn thành yêu cầu mới."
        };
    }

    public async Task<List<PlayQuestion>> GetPlayQuestionsAsync(Attempt attempt, string userId)
    {
        attempt = await GetForLearnerAsync(attempt.Id, userId);
        var a = attempt.Participant.Assignment;
        if (await GetHiddenContentNoticeAsync(a) != null) return [];
        var retaking = false;
        if (a.Mode == AssignmentMode.Test)
        {
            var retakes = await db.Attempts.Include(x => x.Questions).ThenInclude(q => q.Submissions).Where(x =>
                x.ParticipantId == attempt.ParticipantId && x.Kind == AttemptKind.Retake
                && (x.Status == AttemptStatus.InProgress || x.Status == AttemptStatus.Paused)).ToListAsync();
            foreach (var retake in retakes) await GetForLearnerAsync(retake.Id, userId);
            // (68) ẩn đáp án từ lúc được duyệt làm lại cho đến khi nộp lượt làm lại, không chỉ lúc đang làm.
            retaking = retakes.Any(AttemptRules.IsOpen) || await db.RetakeRequests.AnyAsync(r =>
                r.ParticipantId == attempt.ParticipantId && r.Status == RetakeRequestStatus.Approved && r.WindowEndsAt > Now);
        }
        var projected = ProjectQuestions(attempt, retaking);
        if (!AttemptRules.IsOpen(attempt))
        {
            var questionIds = await db.Questions.Where(q => q.QuizId == a.QuizId).Select(q => q.Id).ToListAsync();
            var revisions = await db.QuestionRevisions.AsNoTracking().Where(r => questionIds.Contains(r.QuestionId)
                && r.EditKind == QuestionEditKind.Content).Select(r => new { r.QuestionId, r.Version }).ToListAsync();
            var current = await db.Questions.AsNoTracking().Include(q => q.Options).Include(q => q.Blanks).ThenInclude(b => b.Answers)
                .Where(q => questionIds.Contains(q.Id)).ToDictionaryAsync(q => q.OrderIndex);
            projected = projected.Select(q =>
            {
                var aq = attempt.Questions.Single(x => x.Id == q.Id);
                var original = current.GetValueOrDefault(QuestionSnapshot.FromJson(aq.SnapshotJson).OrderIndex);
                var edited = original != null && revisions.Any(r => r.QuestionId == original.Id && r.Version >= aq.QuestionVersion);
                return q with { ReviewNotices = RegradeRules.ReviewNotes(attempt, aq, q.CorrectIds != null,
                    edited && original != null ? QuestionSnapshot.From(original) : null, edited) };
            }).ToList();
        }
        return projected;
    }

    // only: chỉ dựng đúng câu đó (mở câu), số thứ tự vẫn theo cả lượt — không giải mã đề của 19 câu kia.
    public static List<PlayQuestion> ProjectQuestions(Attempt attempt, bool hasOpenRetake, long? only = null)
    {
        var a = attempt.Participant.Assignment;
        if (a.Quiz?.StudySet?.HiddenByAdminAt != null) return [];
        return attempt.Questions.OrderBy(q => q.DisplayOrder).Select((aq, i) => (aq, i)).Where(x => only == null || x.aq.Id == only).Select(x =>
        {
            var (aq, i) = x;
            var snap = QuestionSnapshot.FromJson(aq.SnapshotJson);
            var bodyHidden = AttemptRules.IsOpen(attempt) && snap.TimeLimitSeconds != null
                && aq.TimerStartedAt == null && aq.TimeLeftSeconds == null && aq.State != AttemptQuestionState.LockedTimeout;
            var answered = aq.AnswerCount > 0;
            // Ôn tập: chỉ câu đã trả lời (câu bỏ trống không hiện đáp án, kể cả sau khi lượt kết thúc — chốt 2026-11-05). Kiểm tra: chỉ sau công bố (54B, 68).
            var reveal = !bodyHidden && !SurveyRules.IsSurvey(snap.Type) && (a.Mode == AssignmentMode.Practice ? answered
                : !AttemptRules.IsOpen(attempt) && a.AnswersPublishedAt != null && !hasOpenRetake);
            var last = aq.Submissions.Where(s => s.IsFinal).OrderByDescending(s => s.SubmittedAt).ThenByDescending(s => s.Id).FirstOrDefault();
            var selectedJson = AttemptRules.IsOpen(attempt) && aq.DraftJson != null ? aq.DraftJson : last?.PayloadJson;
            var choice = SurveyRules.IsChoice(snap.Type);
            var selected = selectedJson == null || !choice ? [] : JsonSerializer.Deserialize<ChoiceAnswer>(selectedJson)?.Selected ?? [];
            var order = aq.OptionOrderJson == null ? snap.Options.Select(o => o.Id).ToArray()
                : JsonSerializer.Deserialize<int[]>(aq.OptionOrderJson)!;
            var effective = RegradeRules.Effective(aq, a.Regrades);
            return new PlayQuestion(aq.Id, i + 1, snap.Type, bodyHidden ? "" : snap.Prompt,
                bodyHidden ? [] : order.Select(id => snap.Options.Single(o => o.Id == id)).Select(o => new PlayOption(o.Id, o.Text)).ToList(),
                answered, aq.Flagged, bodyHidden ? [] : selected,
                reveal ? snap.Options.Where(o => o.IsCorrect).Select(o => o.Id).ToList() : null,
                reveal && answered ? effective.IsCorrect : null, reveal ? snap.Explanation : null,
                bodyHidden ? 0 : snap.Blanks.Count, bodyHidden ? null : InputFeedback.Values(snap, selectedJson),
                reveal && !choice ? InputFeedback.Answers(snap) : null,
                reveal ? InputFeedback.Matches(snap, last?.PayloadJson) : null,
                snap.IsRequired, bodyHidden ? null : snap.MaxLength, aq.AnswerCount, aq.IsMastered, MasteryRules.CanRetry(attempt, aq),
                snap.TimeLimitSeconds, aq.State == AttemptQuestionState.LockedTimeout, bodyHidden, aq.QuestionVersion,
                AttemptRules.IsOpen(attempt) ? null : RegradeRules.ReviewNotes(attempt, aq, reveal));
        }).ToList();
    }

    public async Task<string?> GetHiddenContentNoticeAsync(Assignment a)
    {
        var setId = a.StudySetId ?? a.Quiz?.StudySetId
            ?? await db.Quizzes.Where(q => q.Id == a.QuizId).Select(q => (int?)q.StudySetId).SingleOrDefaultAsync();
        return ContentVisibility.LearnerNotice(await ContentVisibility.HiddenReasonAsync(db, setId));
    }

    private static string AnswerPayload(QuestionType type, IReadOnlyList<int> selected, IReadOnlyList<string>? blanks, string? value)
    {
        if (type == QuestionType.SurveyText && value?.Length > 500) throw new BusinessException("Ý kiến tối đa 500 ký tự.");
        if (blanks?.Count > 5 || blanks?.Any(b => b?.Length > 10000) == true || value?.Length > (type == QuestionType.SurveyText ? 500 : 200))
            throw new BusinessException("Câu trả lời quá dài.");
        return type switch
        {
            QuestionType.TextInput => JsonSerializer.Serialize(new TextAnswer(blanks ?? [])),
            QuestionType.NumberInput => JsonSerializer.Serialize(new NumberAnswer(value ?? "")),
            QuestionType.SurveyText => JsonSerializer.Serialize(new SurveyAnswer(value ?? "")),
            _ => JsonSerializer.Serialize(new ChoiceAnswer(selected))
        };
    }

    internal static void EnsureQuestionVersion(AttemptQuestion q, int? version)
    {
        // (19) phản hồi đến sau reset không được chấm theo đề cũ, kể cả gửi lại ClientRequestId cũ.
        if (version != null && version != q.QuestionVersion)
            throw new BusinessException("Hãy tải lại bài để nhận bản câu hỏi mới.");
    }
}
