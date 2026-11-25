using Microsoft.EntityFrameworkCore;
using MindSprintAI_ASP.NET_MVC.Data;
using MindSprintAI_ASP.NET_MVC.Data.Entities;

namespace MindSprintAI_ASP.NET_MVC.Services;

public record PracticeSettings(
    DateTime? OpensAt,
    DateTime? DueAt,
    int? MaxAttempts,
    ScoreSelection ScoreSelection = ScoreSelection.Highest,
    ScoreScale ScoreScale = ScoreScale.Ten,
    RoundingMode Rounding = RoundingMode.TwoDecimals,
    AudiencePolicy AudiencePolicy = AudiencePolicy.IncludeLateJoiners,
    bool ShuffleQuestions = false,
    bool ShuffleOptions = false,
    AssignmentForm Form = AssignmentForm.QuizOnly,
    int? StudySetId = null,
    RequiredParts RequiredParts = RequiredParts.Quiz,
    bool AllowFlashcardsAfterDue = true,
    int? MasteryThreshold = null,
    int? AnswersPerQuestion = null,
    bool ScoreRetries = false,
    bool IgnoreQuestionTimers = false,
    StrangerPolicy StrangerPolicy = StrangerPolicy.Reject,
    FlashcardMode FlashcardMode = FlashcardMode.Traditional,
    CriticalSettings? CriticalSettings = null,
    bool DeviceLock = false, bool MonitorScreen = false, int? AutoSubmitLeaves = null,
    bool AllowQuizSelfReview = false, bool AllowCardSelfReview = false, bool AllowOfflineCards = false);

public record MasterySettings(int? MasteryThreshold, int? AnswersPerQuestion = null, bool ScoreRetries = false);

public record TestSettings(
    DateTime? OpensAt,
    int DurationMinutes,
    ScoreScale ScoreScale = ScoreScale.Ten,
    RoundingMode Rounding = RoundingMode.TwoDecimals,
    AudiencePolicy AudiencePolicy = AudiencePolicy.IncludeLateJoiners,
    bool ShuffleQuestions = true,
    bool ShuffleOptions = true,
    bool IgnoreQuestionTimers = false,
    StrangerPolicy StrangerPolicy = StrangerPolicy.Reject,
    CriticalSettings? CriticalSettings = null,
    bool DeviceLock = false, bool MonitorScreen = false, int? AutoSubmitLeaves = null);

public partial class AssignmentService(AppDbContext db, ClassroomService classrooms, JoinCodeService codes, TimeProvider clock, ILearnNotifier notifier)
{
    public const int MaxAttemptLimit = 100;
    public static readonly TimeSpan MinShortenNotice = TimeSpan.FromMinutes(15); // 63 ý 2

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public static bool IsOpen(Assignment a, DateTime now, Participant? participant = null) =>
        a.Status == AssignmentStatus.Open
        && (a.OpensAt == null || a.OpensAt <= now)
        && ((participant == null || a.Mode == AssignmentMode.Test ? a.DueAt : AttemptRules.DeadlineFor(a, participant)) is not { } due || due > now);

    // Bài đã đóng/hết hạn lúc học viên mới vào lớp thì không giao cho người đó (40C, 67).
    public static bool IsAssignedTo(Assignment a, ClassMember m)
    {
        if (m.Role != ClassRole.Student || m.JoinedAt == null) return false;
        if (a.AudiencePolicy == AudiencePolicy.OnlyCurrentMembers) return m.JoinedAt <= a.CreatedAt;
        var endedAt = a.ClosedAt ?? (a.Mode == AssignmentMode.Test ? a.TestEndsAt : a.DueAt);
        return endedAt == null || m.JoinedAt < endedAt;
    }

    public Task<Assignment> CreatePracticeAsync(int classroomId, int quizId, PracticeSettings s, string userId) =>
        CreateAsync(classroomId, quizId, s, AssignmentMode.Practice, null, userId);

    private async Task<Assignment> CreateAsync(int classroomId, int quizId, PracticeSettings s,
        AssignmentMode mode, int? durationMinutes, string userId)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        await ClassroomLock.RoomAsync(db, classroomId);
        if (s.Form != AssignmentForm.FlashcardOnly) await QuizChangeLock.AcquireAsync(db, quizId);
        if (s.Form != AssignmentForm.FlashcardOnly)
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT [Id] FROM [Quizzes] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {quizId}");
        await classrooms.EnsureLecturerAsync(classroomId, userId);
        if (!Enum.IsDefined(s.Form) || !Enum.IsDefined(s.RequiredParts)) throw new BusinessException("Hình thức/phần bắt buộc không hợp lệ.");
        if (!Enum.IsDefined(s.StrangerPolicy)) throw new BusinessException("Chính sách người ngoài lớp không hợp lệ.");
        if (mode == AssignmentMode.Test && s.Form != AssignmentForm.QuizOnly) throw new BusinessException("Bài Kiểm tra chỉ có quiz, không có thẻ ghi nhớ.");
        var quiz = s.Form == AssignmentForm.FlashcardOnly ? null : await db.Quizzes.Include(q => q.StudySet)
            .FirstOrDefaultAsync(q => q.Id == quizId && q.DeletedAt == null)
            ?? throw new BusinessException("Không tìm thấy quiz.");
        var set = quiz?.StudySet ?? await db.StudySets.SingleOrDefaultAsync(x => x.Id == s.StudySetId && x.DeletedAt == null)
            ?? throw new BusinessException("Không tìm thấy bộ học.");
        // (48A, người dùng chốt 2026-11-05) chỉ giao bộ học của chính mình hoặc bộ học lớp này đã dùng; không lộ bộ học riêng của GV khác.
        if (set.OwnerId != userId && !await db.Assignments.AnyAsync(a => a.ClassroomId == classroomId && a.StudySetId == set.Id)) throw new ForbiddenException();
        // (51C) tuần tự hóa giao mới với thao tác ẩn để không sót bài giao vừa tạo.
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT [Id] FROM [StudySets] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {set.Id}");
        await db.Entry(set).ReloadAsync();
        if (set.DeletedAt != null || set.ArchivedAt != null || quiz?.ArchivedAt != null)
            throw new BusinessException("Bộ học/quiz đã xóa hoặc lưu trữ nên không giao mới được.");
        if (set.HiddenByAdminAt != null) throw new BusinessException("Bộ học này bị quản trị viên ẩn nên không giao bài được. Lý do: " + set.HiddenReason);
        if (quiz != null && quiz.Status != QuizStatus.Ready) throw new BusinessException("Quiz chưa ở trạng thái Sẵn sàng giao.");
        if (s.Form != AssignmentForm.QuizOnly && !await db.Flashcards.AnyAsync(c => c.StudySetId == set.Id && c.DeletedAt == null))
            throw new BusinessException("Bộ học chưa có thẻ ghi nhớ nào. Hãy thêm thẻ trước.");
        if (s.MaxAttempts is < 1 or > MaxAttemptLimit) throw new BusinessException("Số lượt tối đa phải từ 1 đến 100.");
        if (s.DueAt != null && s.DueAt <= Now) throw new BusinessException("Hạn nộp phải ở tương lai.");
        if (s.OpensAt != null && s.DueAt != null && s.OpensAt >= s.DueAt)
            throw new BusinessException("Giờ mở bài phải trước hạn nộp.");
        var mastery = new MasterySettings(s.MasteryThreshold, s.AnswersPerQuestion, s.ScoreRetries);
        await ValidateMasteryAsync(mode, quiz?.Id, mastery);

        var a = new Assignment
        {
            ClassroomId = classroomId, CreatedById = userId, QuizId = quiz?.Id, StudySetId = set.Id,
            Mode = mode, DurationMinutes = durationMinutes, AllowAiAssistant = mode == AssignmentMode.Practice,
            Form = s.Form, RequiredParts = s.Form == AssignmentForm.QuizOnly ? RequiredParts.Quiz
                : s.Form == AssignmentForm.FlashcardOnly ? RequiredParts.Flashcards : s.RequiredParts,
            AllowFlashcardsAfterDue = s.AllowFlashcardsAfterDue,
            AllowQuizSelfReview = s.AllowQuizSelfReview, AllowCardSelfReview = s.AllowCardSelfReview,
            AllowOfflineCards = s.AllowOfflineCards && s.Form != AssignmentForm.QuizOnly,
            OpensAt = s.OpensAt, DueAt = s.DueAt, MaxAttempts = s.MaxAttempts,
            ScoreSelection = s.ScoreSelection, ScoreScale = s.ScoreScale, Rounding = s.Rounding,
            AudiencePolicy = s.AudiencePolicy, StrangerPolicy = s.StrangerPolicy, ShuffleQuestions = s.ShuffleQuestions,
            ShuffleOptions = s.ShuffleOptions, IgnoreQuestionTimers = s.IgnoreQuestionTimers, CreatedAt = Now
        };
        ApplyMastery(a, mastery);
        ApplyDeviceSettings(a, s.DeviceLock, s.MonitorScreen, s.AutoSubmitLeaves);
        if (!Enum.IsDefined(s.FlashcardMode)) throw new BusinessException("Chế độ thẻ không hợp lệ.");
        a.FlashcardMode = s.FlashcardMode;
        await ApplyCriticalSettingsAsync(a, s.CriticalSettings ?? new());
        set.EverAssignedAt ??= Now;
        if (quiz != null) quiz.EverAssignedAt ??= Now;
        db.Assignments.Add(a);
        await db.SaveChangesAsync();
        await codes.IssueAsync(JoinCodeTarget.Assignment, a.Id);
        await new UserSettingPresetService(db, clock).SaveAssignmentAsync(a, userId);
        db.Audit?.Add(AuditAction.AssignmentCreated, "Assignment", a.Id, userId, resolveTarget: () => a.Id.ToString()); // (NK3)
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return a;
    }

    public async Task<Assignment> CreateTestAsync(int classroomId, int quizId, TestSettings s, string userId)
    {
        if (s.DurationMinutes is < 1 or > 180)
            throw new BusinessException("Thời lượng Kiểm tra phải từ 1 đến 180 phút.");
        return await CreateAsync(classroomId, quizId, new PracticeSettings(s.OpensAt, null, null,
            ScoreSelection.Latest, s.ScoreScale, s.Rounding, s.AudiencePolicy, s.ShuffleQuestions, s.ShuffleOptions,
            IgnoreQuestionTimers: s.IgnoreQuestionTimers, StrangerPolicy: s.StrangerPolicy, CriticalSettings: s.CriticalSettings,
            DeviceLock: s.DeviceLock, MonitorScreen: s.MonitorScreen, AutoSubmitLeaves: s.AutoSubmitLeaves),
            AssignmentMode.Test, s.DurationMinutes, userId);
    }

    public async Task StartTestAsync(int assignmentId, string userId)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        var a = await AttemptLock.AssignmentAsync(db, assignmentId);
        await GetForLecturerAsync(assignmentId, userId);
        EnsureTest(a);
        if (!IsOpen(a, Now)) throw new BusinessException("Bài chưa mở hoặc đã đóng.");
        if (a.TestStartedAt != null) throw new BusinessException("Bài Kiểm tra đã bắt đầu.");
        a.TestStartedAt = Now;
        a.TestEndsAt = a.TestStartedAt.Value.AddMinutes(a.DurationMinutes!.Value);
        db.Audit?.Add(AuditAction.TestStarted, "Assignment", assignmentId, userId); // (NK3)
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateConcurrencyException) { throw new BusinessException("Bài Kiểm tra đã được bắt đầu ở trang khác. Hãy tải lại."); }
        await transaction.CommitAsync();
        await notifier.TestStartedAsync(assignmentId);
        await notifier.LecturersAsync(assignmentId);
    }

    public async Task PublishScoresAsync(int assignmentId, string userId)
    {
        var a = await GetForLecturerAsync(assignmentId, userId);
        EnsureTest(a);
        await EnsureSessionEndedAsync(a);
        // Bài làm theo đề: công bố cả lớp một lần nên phải chấm hết trước (người dùng chốt 16/11/2026).
        if (a.Form == AssignmentForm.Document)
        {
            var ungraded = await db.Attempts.CountAsync(x => x.Participant.AssignmentId == assignmentId && x.Status == AttemptStatus.Ended
                && x.Kind != AttemptKind.SelfReview && !x.ExcludeFromGradebook && x.MaxScore == 0);
            if (ungraded > 0) throw new BusinessException($"Còn {ungraded} bài chưa chấm. Hãy chấm hết rồi mới công bố điểm.");
        }
        a.ScoresPublishedAt ??= Now;
        var participants = await db.Participants.Include(p => p.Attempts)
            .Where(p => p.AssignmentId == assignmentId).ToListAsync();
        foreach (var p in participants)
            if (p.CompletionStatus == CompletionStatus.SubmittedAwaitingScore)
                p.CompletionStatus = CompletionStatus.Completed;
        db.Audit?.Add(AuditAction.ScoresPublished, "Assignment", assignmentId, userId); // (NK3)
        await db.SaveChangesAsync();
        await notifier.LecturersAsync(assignmentId);
    }

    public async Task PublishAnswersAsync(int assignmentId, bool confirmed, string userId)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        await AttemptLock.AssignmentAsync(db, assignmentId);
        var a = await GetForLecturerAsync(assignmentId, userId);
        EnsureTest(a);
        await EnsureSessionEndedAsync(a);
        if (!confirmed && await HasUnusedRetakesAsync(assignmentId))
            throw new BusinessException("Còn học viên được duyệt làm lại mà chưa làm. Hãy xác nhận trước khi công bố đáp án.");
        a.AnswersPublishedAt ??= Now;
        db.Audit?.Add(AuditAction.AnswersPublished, "Assignment", assignmentId, userId); // (NK3)
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    // (mục 10, 54B) chỉ công bố khi mọi lượt của phiên đã kết thúc; người nộp sớm không được thấy đáp án
    // trong lúc bạn khác còn thi. Lượt làm lại xử lý riêng bằng cảnh báo (54B, 68).
    private async Task EnsureSessionEndedAsync(Assignment a)
    {
        if (a.TestStartedAt == null) throw new BusinessException("Bài Kiểm tra chưa bắt đầu.");
        if (a.Status != AssignmentStatus.Closed && a.TestEndsAt > Now)
            throw new BusinessException("Bài kiểm tra chưa hết giờ. Chờ hết giờ hoặc bấm \"Đóng ngay\" rồi mới công bố.");
        if (await db.Attempts.AnyAsync(x => x.Participant.AssignmentId == a.Id && x.Kind == AttemptKind.Normal
                && (x.Status == AttemptStatus.InProgress || x.Status == AttemptStatus.Paused) && x.DeadlineAt > Now))
            throw new BusinessException("Còn học viên đang làm bài (được cộng giờ). Chờ họ nộp rồi mới công bố.");
    }

    public Task<bool> HasUnusedRetakesAsync(int assignmentId) => db.RetakeRequests.AnyAsync(r =>
        r.Participant.AssignmentId == assignmentId && r.Status == RetakeRequestStatus.Approved && r.WindowEndsAt > Now);

    public async Task<List<Participant>> GetWaitingAsync(int assignmentId, string userId)
    {
        var a = await GetForLecturerAsync(assignmentId, userId);
        return await db.Participants.Include(p => p.User).Where(p => p.AssignmentId == assignmentId && !p.Attempts.Any()
            && (p.IsStranger || db.ClassMembers.Any(m => m.ClassroomId == a.ClassroomId && m.UserId == p.UserId
                && m.Status == ClassMemberStatus.Active && m.Role == ClassRole.Student)))
            .OrderBy(p => p.CreatedAt).ToListAsync();
    }

    private static void EnsureTest(Assignment a)
    {
        if (a.Mode != AssignmentMode.Test) throw new BusinessException("Thao tác này chỉ dành cho bài Kiểm tra.");
    }

    public async Task<Assignment> GetForLecturerAsync(int assignmentId, string userId)
    {
        var a = await db.Assignments.Include(x => x.Quiz).Include(x => x.Classroom)
            .FirstOrDefaultAsync(x => x.Id == assignmentId)
            ?? throw new BusinessException("Không tìm thấy bài giao.");
        if (a.ClassroomId == null) throw new ForbiddenException();
        await classrooms.EnsureLecturerAsync(a.ClassroomId.Value, userId);
        return a;
    }

    public Task<bool> CanEditMasteryAsync(int id) => db.Attempts.AllAsync(x => x.Participant.AssignmentId != id || x.Kind == AttemptKind.SelfReview);
    public Task<string?> GetHiddenContentReasonAsync(Assignment a) =>
        ContentVisibility.HiddenReasonAsync(db, a.StudySetId ?? a.Quiz?.StudySetId);
    internal Task NotifyLecturersAsync(int id) => notifier.LecturersAsync(id);
    internal NotificationService Notifications => new(db, clock, notifier);

    public async Task ChangeQuestionTimersAsync(int id, bool ignoreQuestionTimers, string userId)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        var a = await AttemptLock.AssignmentAsync(db, id);
        await GetForLecturerAsync(id, userId);
        // (E, người dùng chọn B 2026-11-05) Kiểm tra khóa sau lượt đầu để cả lớp cùng luật; Ôn tập đổi được, chỉ áp cho lượt bắt đầu sau đó (bản chụp lượt cũ giữ nguyên).
        if (a.Mode == AssignmentMode.Test && !await CanEditMasteryAsync(id))
            throw new BusinessException("Bài Kiểm tra đã có người bắt đầu lượt nên không đổi được giờ riêng từng câu.");
        db.AssignmentChanges.Add(new AssignmentChange
        {
            AssignmentId = id, ChangedById = userId, ChangedAt = Now, Field = nameof(a.IgnoreQuestionTimers),
            OldValue = a.IgnoreQuestionTimers.ToString(), NewValue = ignoreQuestionTimers.ToString()
        });
        a.IgnoreQuestionTimers = ignoreQuestionTimers;
        db.Audit?.Add(AuditAction.AssignmentSettingsChanged, "Assignment", id, userId); // (NK3)
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    public Task<bool> IsUngradedAsync(Assignment a) => SurveyRules.IsUngradedAsync(db, a.QuizId);

    public const string MasteryWarning = "Với cài đặt này, học viên trả lời sai có thể không đạt mốc trong một lượt.";

    public async Task ChangeMasteryAsync(int id, MasterySettings settings, string userId)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        var a = await AttemptLock.AssignmentAsync(db, id);
        await GetForLecturerAsync(id, userId);
        if (!await CanEditMasteryAsync(id)) throw new BusinessException("Đã có người bắt đầu lượt nên không đổi được cài đặt làm chủ.");
        await ValidateMasteryAsync(a.Mode, a.QuizId, settings);
        foreach (var field in new[] { nameof(a.MasteryThreshold), nameof(a.AnswersPerQuestion), nameof(a.ScoreRetries) })
            db.AssignmentChanges.Add(new AssignmentChange
            {
                AssignmentId = id, ChangedById = userId, ChangedAt = Now, Field = field,
                OldValue = field == nameof(a.MasteryThreshold) ? a.MasteryThreshold?.ToString() : field == nameof(a.AnswersPerQuestion) ? a.AnswersPerQuestion?.ToString() : a.ScoreRetries.ToString(),
                NewValue = field == nameof(a.MasteryThreshold) ? settings.MasteryThreshold?.ToString() : field == nameof(a.AnswersPerQuestion) ? settings.AnswersPerQuestion?.ToString() : settings.ScoreRetries.ToString()
            });
        ApplyMastery(a, settings);
        db.Audit?.Add(AuditAction.AssignmentSettingsChanged, "Assignment", id, userId); // (NK3)
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        await notifier.LecturersAsync(id);
    }

    private async Task ValidateMasteryAsync(AssignmentMode mode, int? quizId, MasterySettings settings)
    {
        if (mode == AssignmentMode.Test && (settings.MasteryThreshold != null || settings.AnswersPerQuestion != null || settings.ScoreRetries))
            throw new BusinessException("Bài Kiểm tra không có cài đặt làm chủ.");
        if (settings.MasteryThreshold == null) return;
        if (settings.MasteryThreshold is not (80 or 90 or 100)) throw new BusinessException("Mốc làm chủ chỉ có 80%, 90%, 100%.");
        if (settings.AnswersPerQuestion is < 1) throw new BusinessException("Số lần trả lời mỗi câu phải từ 1 trở lên.");
        if (!await db.Questions.AnyAsync(q => q.QuizId == quizId && (q.Type == QuestionType.SingleChoice || q.Type == QuestionType.MultipleChoice)))
            throw new BusinessException("Không bật được làm chủ vì quiz không có câu chọn một/chọn nhiều.");
    }

    private static void ApplyMastery(Assignment a, MasterySettings settings)
    {
        a.MasteryThreshold = settings.MasteryThreshold;
        a.AnswersPerQuestion = settings.MasteryThreshold == null ? null : settings.AnswersPerQuestion;
        a.ScoreRetries = settings.MasteryThreshold != null && settings.ScoreRetries;
    }

    public Task<List<Assignment>> ListForClassAsync(int classroomId) =>
        db.Assignments.Include(a => a.Quiz).Where(a => a.ClassroomId == classroomId && a.ReportDeletedAt == null)
            .OrderByDescending(a => a.CreatedAt).ToListAsync();

    public async Task<List<Assignment>> ListForLearnerAsync(string userId)
    {
        var memberships = await db.ClassMembers.Where(m => m.UserId == userId && m.Role == ClassRole.Student
            && (m.Status == ClassMemberStatus.Active || m.Status == ClassMemberStatus.Left || m.Status == ClassMemberStatus.Removed))
            .ToListAsync();
        var classIds = memberships.Select(m => m.ClassroomId).ToList();
        var done = await db.Participants.Where(p => p.UserId == userId).Select(p => p.AssignmentId).ToListAsync();
        var all = await db.Assignments.Include(a => a.Quiz).Include(a => a.Classroom)
            .Where(a => a.ClassroomId != null && classIds.Contains(a.ClassroomId.Value) || done.Contains(a.Id))
            .OrderByDescending(a => a.CreatedAt).ToListAsync();
        // Lớp đã rời: chỉ còn thấy bài mình đã làm (41A).
        return all.Where(a =>
        {
            var m = memberships.FirstOrDefault(x => x.ClassroomId == a.ClassroomId);
            if (m == null) return done.Contains(a.Id);
            return m.Status == ClassMemberStatus.Active ? IsAssignedTo(a, m) || done.Contains(a.Id) : done.Contains(a.Id);
        }).ToList();
    }

    // Gia hạn có hiệu lực ngay, không thêm lượt; rút ngắn phải cách hiện tại ≥ 15 phút (63).
    public async Task ChangeDueAsync(int assignmentId, DateTime? newDue, string? reason, string userId)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        var a = await AttemptLock.AssignmentAsync(db, assignmentId);
        await GetForLecturerAsync(assignmentId, userId);
        if (a.Mode == AssignmentMode.Test) throw new BusinessException("Bài Kiểm tra dùng đồng hồ chung của phiên.");
        if (a.Status == AssignmentStatus.Closed) throw new BusinessException("Bài đã đóng.");
        var shorten = newDue != null && (a.DueAt == null || newDue < a.DueAt);
        if (shorten && newDue < Now + MinShortenNotice)
            throw new BusinessException("Hạn mới phải cách thời điểm hiện tại ít nhất 15 phút. Muốn dừng ngay hãy dùng \"Đóng bài giao\".");
        if (newDue != null && newDue <= Now) throw new BusinessException("Hạn mới phải ở tương lai.");

        ChangeDeadline(db, a, newDue, userId, Now, reason);

        var changed = new List<Attempt>();
        foreach (var (attempt, p) in await OpenAttemptsAsync(a.Id))
        {
            var current = await AttemptLock.LoadAsync(db, attempt.Id);
            if (AttemptRules.IsOpen(current) && current.Kind == AttemptKind.Normal)
            {
                current.DeadlineAt = AttemptRules.DeadlineFor(a, p);
                changed.Add(current);
            }
        }
        db.Audit?.Add(AuditAction.AssignmentDueChanged, "Assignment", assignmentId, userId); // (NK3)
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        await NotifyDeadlinesAsync(changed);
        await notifier.LecturersAsync(assignmentId);
    }

    // "Đóng ngay" (33A, 66): lượt dở kết thúc như hết hạn, nhãn "Kết thúc do GV đóng bài".
    public async Task<int> RequestCloseAsync(int assignmentId, bool? delayed, string userId)
    {
        if (delayed == true) { await CloseAfter15MinutesAsync(assignmentId, userId); return 0; }
        if (delayed == false) { await CloseNowAsync(assignmentId, userId); return 0; }
        await using var tx = await db.Database.BeginTransactionAsync();
        var a = await AttemptLock.AssignmentAsync(db, assignmentId);
        await GetForLecturerAsync(assignmentId, userId);
        if (a.Status == AssignmentStatus.Closed) return 0;
        var count = await CountOpenAttemptsAsync(assignmentId);
        if (count > 0) return count;
        var ended = await CloseCoreAsync(a);
        db.Audit?.Add(AuditAction.AssignmentClosed, "Assignment", assignmentId, userId); // (NK3)
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        await NotifyClosedAsync(ended);
        await notifier.LecturersAsync(assignmentId);
        return 0;
    }

    public async Task CloseNowAsync(int assignmentId, string userId)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        var a = await AttemptLock.AssignmentAsync(db, assignmentId);
        await GetForLecturerAsync(assignmentId, userId);
        if (a.Status == AssignmentStatus.Closed) return;
        var ended = await CloseCoreAsync(a);
        db.Audit?.Add(AuditAction.AssignmentClosed, "Assignment", assignmentId, userId); // (NK3)
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        await NotifyClosedAsync(ended);
        await notifier.LecturersAsync(assignmentId);
    }

    // (66) giữ nguyên khóa bài trong lúc đếm; không có lượt mở thì đóng ngay.
    public async Task CloseAfter15MinutesAsync(int assignmentId, string userId)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        var a = await AttemptLock.AssignmentAsync(db, assignmentId);
        await GetForLecturerAsync(assignmentId, userId);
        if (a.Status == AssignmentStatus.Closed) return;
        if (await CountOpenAttemptsAsync(assignmentId) == 0)
        {
            var ended = await CloseCoreAsync(a);
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            await NotifyClosedAsync(ended);
            await notifier.LecturersAsync(assignmentId);
            return;
        }
        var now = Now;
        var due = now + MinShortenNotice;
        var old = a.Mode == AssignmentMode.Test ? a.TestEndsAt : a.DueAt;
        // (63, 66) chỉ rút hạn, không vô tình gia hạn bài đã có hạn sớm hơn.
        if (old == null || old > due)
        {
            ChangeDeadline(db, a, due, userId, now, "Đóng sau 15 phút (66)");
        }
        a.CloseReason = CloseReason.LecturerClosedDelayed;
        var changed = new List<Attempt>();
        foreach (var (attempt, p) in await OpenAttemptsAsync(a.Id))
        {
            var current = await AttemptLock.LoadAsync(db, attempt.Id);
            if (!AttemptRules.IsOpen(current) || current.Kind != AttemptKind.Normal) continue;
            if (old == null || old > due) current.DeadlineAt = AttemptRules.DeadlineFor(a, p);
            changed.Add(current);
        }
        db.Audit?.Add(AuditAction.AssignmentClosed, "Assignment", assignmentId, userId); // (NK3)
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        await NotifyDeadlinesAsync(changed);
        await notifier.LecturersAsync(assignmentId);
    }

    // (60, 63) sửa đề và đổi hạn dùng chung ghi lịch sử, đồng hồ và popup sau commit.
    internal static void ChangeDeadline(AppDbContext db, Assignment a, DateTime? due, string userId, DateTime now, string? reason)
    {
        var old = a.Mode == AssignmentMode.Test ? a.TestEndsAt : a.DueAt;
        db.AssignmentChanges.Add(new AssignmentChange
        {
            AssignmentId = a.Id, ChangedById = userId, ChangedAt = now,
            Field = a.Mode == AssignmentMode.Test ? nameof(Assignment.TestEndsAt) : nameof(Assignment.DueAt),
            OldValue = old?.ToString("O"), NewValue = due?.ToString("O"), Reason = reason
        });
        if (a.Mode == AssignmentMode.Test) a.TestEndsAt = due;
        else a.DueAt = due;
    }

    private Task NotifyDeadlinesAsync(IEnumerable<Attempt> attempts) => NotifyDeadlinesAsync(notifier, attempts);
    internal static Task NotifyDeadlinesAsync(ILearnNotifier notifier, IEnumerable<Attempt> attempts) => Task.WhenAll(attempts.Select(a =>
        notifier.AttemptAsync(a.Id, "deadline-changed", new LearnNotice(AttemptId: a.Id,
            Time: VnTime.Format(a.DeadlineAt), Url: $"/Learn/State/{a.Id}"))));

    internal Task NotifyClosedAsync(IEnumerable<Attempt> attempts) => Task.WhenAll(attempts.Select(a =>
        notifier.AttemptAsync(a.Id, a.EndReason == AttemptEndReason.HiddenByAdmin ? "content-hidden" : "closed",
            new LearnNotice(AttemptId: a.Id, Url: $"/Learn/Review/{a.Id}"))));

    internal async Task<List<Attempt>> CloseCoreAsync(Assignment a, CloseReason reason = CloseReason.LecturerClosedNow)
    {
        var now = Now;
        a.Status = AssignmentStatus.Closed;
        a.ClosedAt = now;
        a.CloseReason = reason;
        var ended = new List<Attempt>();
        foreach (var (attempt, p) in await OpenAttemptsAsync(a.Id))
        {
            var current = await AttemptLock.LoadAsync(db, attempt.Id);
            if (reason == CloseReason.HiddenByAdmin)
                AttemptRules.End(current, p, AttemptEndReason.HiddenByAdmin, now);
            else if (current.Kind == AttemptKind.SelfReview)
                AttemptRules.End(current, p, AttemptEndReason.Submitted, now);
            else if (!AttemptRules.ExpireIfDue(current, p, now))
                AttemptRules.End(current, p, AttemptEndReason.ClosedByLecturer, now);
            ended.Add(current);
        }
        if (CardCompletion.HasCards(a))
        {
            await db.SaveChangesAsync(); // chốt lượt trước khi ParticipantAsync đọc lại từ DB.
            var participants = await db.Participants.Include(p => p.Attempts).Where(p => p.AssignmentId == a.Id).OrderBy(p => p.Id).ToListAsync();
            foreach (var p in participants)
            {
                await AttemptLock.ParticipantAsync(db, p.Id);
                await CardCompletion.RefreshAsync(db, p, Now);
            }
        }
        return ended;
    }

    public Task<int> CountOpenAttemptsAsync(int assignmentId) =>
        db.Attempts.CountAsync(x => x.Participant.AssignmentId == assignmentId
            && x.Kind != AttemptKind.SelfReview
            && (x.Status == AttemptStatus.InProgress || x.Status == AttemptStatus.Paused));

    public async Task<List<string>> PreviewRequiredPartsAsync(int assignmentId, RequiredParts parts, string userId)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        var a = await AttemptLock.AssignmentAsync(db, assignmentId);
        await GetForLecturerAsync(assignmentId, userId);
        EnsureCombinedOpen(a, parts);
        await RefreshCardParticipantsAsync(a.Id);
        var names = await db.Participants.Where(p => p.AssignmentId == assignmentId && p.CompletionStatus == CompletionStatus.Completed)
            .OrderBy(p => p.Id).Select(p => p.User!.DisplayName).ToListAsync();
        await tx.CommitAsync();
        await notifier.LecturersAsync(assignmentId);
        return names;
    }

    public async Task ChangeRequiredPartsAsync(int assignmentId, RequiredParts parts, string userId)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        var a = await AttemptLock.AssignmentAsync(db, assignmentId);
        await GetForLecturerAsync(assignmentId, userId);
        EnsureCombinedOpen(a, parts);
        if (a.RequiredParts == parts) return;
        var participants = await RefreshCardParticipantsAsync(a.Id);
        var old = a.RequiredParts;
        a.RequiredParts = parts;
        foreach (var p in participants)
        {
            if (p.CompletionStatus == CompletionStatus.Completed && parts is RequiredParts.Flashcards or RequiredParts.Both
                && old == RequiredParts.Quiz)
                p.CompletionLabels |= CompletionLabels.CompletedBeforeCardsRequired;
            await CardCompletion.RefreshAsync(db, p, Now);
        }
        db.AssignmentChanges.Add(new AssignmentChange
        {
            AssignmentId = a.Id, ChangedById = userId, ChangedAt = Now, Field = nameof(Assignment.RequiredParts),
            OldValue = old.ToString(), NewValue = parts.ToString()
        });
        db.Audit?.Add(AuditAction.AssignmentSettingsChanged, "Assignment", assignmentId, userId); // (NK3)
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        await notifier.LecturersAsync(assignmentId);
    }

    private async Task<List<Participant>> RefreshCardParticipantsAsync(int assignmentId)
    {
        var participants = await db.Participants.Include(p => p.Attempts).Where(p => p.AssignmentId == assignmentId).OrderBy(p => p.Id).ToListAsync();
        foreach (var p in participants)
        {
            await AttemptLock.ParticipantAsync(db, p.Id);
            foreach (var attempt in p.Attempts.Where(AttemptRules.IsOpen).ToList())
            {
                var current = await AttemptLock.LoadAsync(db, attempt.Id);
                AttemptRules.ExpireIfDue(current, p, Now);
            }
            await CardCompletion.RefreshAsync(db, p, Now);
        }
        await db.SaveChangesAsync();
        return participants;
    }

    private void EnsureCombinedOpen(Assignment a, RequiredParts parts)
    {
        if (a.Mode != AssignmentMode.Practice || a.Form != AssignmentForm.Combined || !IsOpen(a, Now))
            throw new BusinessException("Chỉ đổi phần bắt buộc của bài kết hợp đang mở.");
        if (!Enum.IsDefined(parts)) throw new BusinessException("Phải chọn phần bắt buộc.");
    }

    private async Task<List<(Attempt, Participant)>> OpenAttemptsAsync(int assignmentId)
    {
        var participantIds = await db.Participants.Where(p => p.AssignmentId == assignmentId)
            .OrderBy(p => p.Id).Select(p => p.Id).ToListAsync();
        foreach (var id in participantIds) await AttemptLock.ParticipantAsync(db, id);
        var list = await db.Attempts.Include(x => x.Participant).ThenInclude(p => p.Assignment)
            .Include(x => x.Questions).ThenInclude(q => q.Submissions)
            .Where(x => x.Participant.AssignmentId == assignmentId
                && (x.Status == AttemptStatus.InProgress || x.Status == AttemptStatus.Paused))
            .ToListAsync();
        return list.Select(x => (x, x.Participant)).ToList();
    }
}
