using Microsoft.EntityFrameworkCore;
using MindSprintAI_ASP.NET_MVC.Data;
using MindSprintAI_ASP.NET_MVC.Data.Entities;

namespace MindSprintAI_ASP.NET_MVC.Services;

public enum TrashKind { StudySet = 1, Quiz = 2, Flashcard = 3, Report = 4, AiConversation = 5 }
public enum TrashRestoreMode { Move = 1, NewStudySet = 2, WithOldStudySet = 3 }
public record TrashItem(TrashKind Kind, int Id, string Title, DateTime DeletedAt, bool Archived,
    bool ParentDeleted, bool CanRestore)
{
    public bool CanRestoreWithParent { get; init; }
    public bool CanRelocate { get; init; }
    public bool RetainedWithParent { get; init; }
    public DateTime? PurgeAt => Archived || RetainedWithParent || Kind == TrashKind.Report ? null : DeletedAt.AddDays(30);
    public string KindLabel => Kind switch { TrashKind.StudySet => "Bộ học", TrashKind.Quiz => "Quiz", TrashKind.Flashcard => "Thẻ", TrashKind.AiConversation => "Hội thoại trợ lý", _ => "Báo cáo bài giao" };
}

public class TrashService(AppDbContext db, ClassroomService classrooms, TimeProvider clock)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<List<TrashItem>> ListAsync(string userId, string? ownerId = null)
    {
        if (ownerId != null && ownerId != userId) throw new ForbiddenException();
        var result = new List<TrashItem>();
        var sets = await db.StudySets.AsNoTracking().Where(s => s.OwnerId == userId && (s.DeletedAt != null || s.ArchivedAt != null)).ToListAsync();
        foreach (var s in sets)
        {
            var archived = s.ArchivedAt != null || s.EverAssignedAt != null || await UsedSet(s.Id).AnyAsync();
            var deleted = s.DeletedAt ?? s.ArchivedAt!.Value;
            result.Add(Item(TrashKind.StudySet, s.Id, s.Title, deleted, archived));
        }
        var quizzes = await db.Quizzes.AsNoTracking().Include(q => q.StudySet)
            .Where(q => q.StudySet.OwnerId == userId && (q.DeletedAt != null || q.ArchivedAt != null
                || q.StudySet.DeletedAt != null || q.StudySet.ArchivedAt != null)).ToListAsync();
        foreach (var q in quizzes)
        {
            var used = q.ArchivedAt != null || await UsedQuizAsync(q);
            result.Add(ChildItem(TrashKind.Quiz, q.Id, q.Title, q.DeletedAt ?? q.ArchivedAt ?? q.StudySet.DeletedAt ?? q.StudySet.ArchivedAt!.Value,
                used, q.StudySet, q.DeletedAt != null || q.ArchivedAt != null));
        }
        var cards = await db.Flashcards.AsNoTracking().Include(c => c.StudySet)
            .Where(c => c.StudySet.OwnerId == userId && (c.DeletedAt != null
                || c.StudySet.DeletedAt != null || c.StudySet.ArchivedAt != null)).ToListAsync();
        foreach (var c in cards)
            result.Add(ChildItem(TrashKind.Flashcard, c.Id, c.Front, c.DeletedAt ?? c.StudySet.DeletedAt ?? c.StudySet.ArchivedAt!.Value,
                c.StudySet.EverAssignedAt != null || c.StudySet.ArchivedAt != null || await UsedSet(c.StudySetId).AnyAsync(),
                c.StudySet, c.DeletedAt != null));
        var reports = await db.Assignments.AsNoTracking().Include(a => a.Quiz).Where(a => a.ReportDeletedAt != null
            && db.ClassMembers.Any(m => m.ClassroomId == a.ClassroomId && m.UserId == userId && m.Classroom.DeletedAt == null
                && m.Status == ClassMemberStatus.Active && (m.Role == ClassRole.MainLecturer || m.Role == ClassRole.AssistantLecturer))).ToListAsync();
        result.AddRange(reports.Select(a => Item(TrashKind.Report, a.Id, a.Quiz?.Title ?? a.PaperTitle ?? "Thẻ ghi nhớ", a.ReportDeletedAt!.Value, false)));
        var conversations = await db.AiConversations.AsNoTracking().Where(c => c.UserId == userId && c.DeletedAt != null
            && !db.Attempts.Any(a => a.Participant.UserId == userId && a.Participant.Assignment.Mode == AssignmentMode.Test
                && a.Participant.Assignment.StudySetId == c.StudySetId && (a.Status == AttemptStatus.InProgress || a.Status == AttemptStatus.Paused)))
            .ToListAsync();
        result.AddRange(conversations.Select(c => Item(TrashKind.AiConversation, c.Id, c.Title, c.DeletedAt!.Value, false)));
        return result.OrderByDescending(x => x.DeletedAt).ToList();
    }

    private TrashItem Item(TrashKind kind, int id, string title, DateTime deleted, bool archived, bool parent = false) =>
        new(kind, id, title, deleted, archived, parent, !parent && (archived || deleted.AddDays(30) > Now));

    private TrashItem ChildItem(TrashKind kind, int id, string title, DateTime deleted, bool used, StudySet parent, bool deletedSeparately)
    {
        var parentDeleted = parent.DeletedAt != null || parent.ArchivedAt != null;
        var retained = !deletedSeparately && parent.ArchivedAt != null;
        var fresh = used || retained || deleted.AddDays(30) > Now;
        var parentFresh = parent.ArchivedAt != null || parent.EverAssignedAt != null
            || (parent.DeletedAt?.AddDays(30) > Now);
        return Item(kind, id, title, deleted, used, parentDeleted) with
        {
            CanRestoreWithParent = parentDeleted && fresh && parentFresh,
            CanRelocate = parentDeleted && fresh && !used && parent.HiddenByAdminAt == null,
            RetainedWithParent = retained
        };
    }

    public Task<List<StudySet>> GetRestoreTargetsAsync(string userId) => db.StudySets.AsNoTracking()
        .Where(s => s.OwnerId == userId && s.DeletedAt == null && s.ArchivedAt == null && s.HiddenByAdminAt == null)
        .OrderBy(s => s.Title).ToListAsync();

    private IQueryable<Assignment> UsedSet(int id) => db.Assignments.Where(a => a.StudySetId == id || a.Quiz != null && a.Quiz.StudySetId == id);

    private Task<bool> UsedQuizAsync(Quiz quiz) => quiz.EverAssignedAt != null ? Task.FromResult(true)
        : db.Quizzes.AnyAsync(q => q.Id == quiz.Id && (db.Assignments.Any(a => a.QuizId == q.Id)
            || db.AttemptQuestions.IgnoreQueryFilters().Any(aq => aq.Question.QuizId == q.Id)));

    public async Task RestoreAsync(TrashKind kind, int id, string userId, TrashRestoreMode? restoreMode = null, int? targetStudySetId = null)
    {
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        switch (kind)
        {
            case TrashKind.AiConversation:
                var conversation = await db.AiConversations.SingleOrDefaultAsync(c => c.Id == id && c.UserId == userId) ?? throw new ForbiddenException();
                await StudySetLearningAccess.EnsureNotTakingTestAsync(db, conversation.StudySetId, userId);
                CheckRestore(conversation.DeletedAt, false);
                conversation.DeletedAt = null;
                break;
            case TrashKind.StudySet:
                var set = await db.StudySets.SingleOrDefaultAsync(s => s.Id == id) ?? throw new ForbiddenException();
                if (set.OwnerId != userId) throw new ForbiddenException();
                await RestoreSetAsync(set);
                break;
            case TrashKind.Quiz:
                var quiz = await db.Quizzes.Include(q => q.StudySet).SingleOrDefaultAsync(q => q.Id == id) ?? throw new ForbiddenException();
                if (quiz.StudySet.OwnerId != userId) throw new ForbiddenException();
                await StudyLibraryService.EnsureQuizNotTransferredAsync(db, quiz);
                var usedQuiz = quiz.ArchivedAt != null || await UsedQuizAsync(quiz);
                CheckRestore(quiz.DeletedAt ?? quiz.ArchivedAt ?? quiz.StudySet.DeletedAt ?? quiz.StudySet.ArchivedAt,
                    usedQuiz || quiz.DeletedAt == null && quiz.StudySet.ArchivedAt != null);
                var quizTarget = await RestoreParentAsync(quiz.StudySet, userId, usedQuiz, restoreMode, targetStudySetId);
                if (quizTarget != null) { quiz.StudySet = quizTarget; quiz.StudySetId = quizTarget.Id; }
                quiz.DeletedAt = null; quiz.ArchivedAt = null; // Giữ nguyên Nháp/Sẵn sàng giao.
                break;
            case TrashKind.Flashcard:
                var card = await db.Flashcards.Include(c => c.StudySet).SingleOrDefaultAsync(c => c.Id == id) ?? throw new ForbiddenException();
                if (card.StudySet.OwnerId != userId) throw new ForbiddenException();
                var usedCard = card.StudySet.EverAssignedAt != null || card.StudySet.ArchivedAt != null || await UsedSet(card.StudySetId).AnyAsync();
                CheckRestore(card.DeletedAt ?? card.StudySet.DeletedAt ?? card.StudySet.ArchivedAt,
                    usedCard || card.DeletedAt == null && card.StudySet.ArchivedAt != null);
                var cardTarget = await RestoreParentAsync(card.StudySet, userId, usedCard, restoreMode, targetStudySetId);
                if (cardTarget != null)
                {
                    card.StudySet = cardTarget; card.StudySetId = cardTarget.Id;
                }
                card.DeletedAt = null;
                break;
            case TrashKind.Report:
                var a = await ReportAsync(id, userId);
                CheckRestore(a.ReportDeletedAt, false);
                a.ReportDeletedAt = null;
                break;
            default: throw new BusinessException("Loại dữ liệu thùng rác không hợp lệ.");
        }
        // (NK3) Chỉ các loại thuộc danh sách NK3; không ghi nội dung dữ liệu khôi phục.
        var action = kind switch { TrashKind.StudySet => AuditAction.StudySetRestored, TrashKind.Quiz => AuditAction.QuizRestored,
            TrashKind.Flashcard => AuditAction.FlashcardRestored, _ => (AuditAction?)null };
        if (action != null) db.Audit?.Add(action.Value, kind.ToString(), id, userId);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    private async Task RestoreSetAsync(StudySet set)
    {
        CheckRestore(set.DeletedAt ?? set.ArchivedAt, set.ArchivedAt != null || set.EverAssignedAt != null || await UsedSet(set.Id).AnyAsync());
        // Xóa bộ chỉ ẩn cha: mọi mục còn trong bộ trở lại; mục đã xóa riêng giữ nguyên DeletedAt.
        set.DeletedAt = null; set.ArchivedAt = null;
    }

    private async Task<StudySet?> RestoreParentAsync(StudySet parent, string userId, bool everAssigned,
        TrashRestoreMode? mode, int? targetStudySetId)
    {
        if (parent.DeletedAt == null && parent.ArchivedAt == null) return null;
        if (mode == null) throw new BusinessException("Hãy chọn nơi khôi phục: bộ học trước, bộ khác hoặc bộ mới.");
        if (!Enum.IsDefined(mode.Value)) throw new BusinessException("Cách khôi phục không hợp lệ.");
        if (mode == TrashRestoreMode.WithOldStudySet)
        {
            await RestoreSetAsync(parent);
            return null;
        }
        if (everAssigned) throw new BusinessException("Mục đã từng giao chỉ được khôi phục cùng bộ học cũ.");
        if (parent.HiddenByAdminAt != null) throw new BusinessException("Bộ học đang bị ẩn; không thể chuyển nội dung sang bộ khác.");
        if (mode == TrashRestoreMode.Move)
        {
            var target = await db.StudySets.SingleOrDefaultAsync(s => s.Id == targetStudySetId) ?? throw new ForbiddenException();
            if (target.OwnerId != userId) throw new ForbiddenException();
            if (target.DeletedAt != null || target.ArchivedAt != null || target.HiddenByAdminAt != null)
                throw new BusinessException("Chỉ chuyển vào bộ học đang hoạt động và không bị ẩn của bạn.");
            return target;
        }
        const string suffix = " (khôi phục)";
        var created = new StudySet { OwnerId = userId, Title = parent.Title[..Math.Min(parent.Title.Length, 200 - suffix.Length)] + suffix, CreatedAt = Now };
        db.StudySets.Add(created);
        await db.SaveChangesAsync(); // Cần Id trước khi chuyển mục; cùng giao dịch với khôi phục.
        return created;
    }

    private void CheckRestore(DateTime? deleted, bool archived)
    {
        if (deleted == null) throw new BusinessException("Mục này không nằm trong thùng rác.");
        if (!archived && deleted.Value.AddDays(30) <= Now) throw new BusinessException("Đã quá 30 ngày nên không khôi phục được.");
    }

    private async Task<Assignment> ReportAsync(int id, string userId)
    {
        var a = await db.Assignments.SingleOrDefaultAsync(a => a.Id == id) ?? throw new ForbiddenException();
        if (a.ClassroomId == null) throw new ForbiddenException();
        await classrooms.EnsureLecturerAsync(a.ClassroomId.Value, userId);
        return a;
    }

    public async Task DeleteReportAsync(int id, string userId)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        await AttemptLock.AssignmentAsync(db, id);
        var a = await ReportAsync(id, userId);
        if (!await CanDeleteReportAsync(id, userId))
            throw new BusinessException("Chỉ xóa báo cáo khi bài giao đã kết thúc và không còn lượt đang làm.");
        a.ReportDeletedAt ??= Now;
        db.Audit?.Add(AuditAction.ReportDeleted, "Assignment", id, userId); // (NK3)
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    public async Task<bool> CanDeleteReportAsync(int id, string userId)
    {
        var a = await ReportAsync(id, userId);
        var now = Now;
        return a.ReportDeletedAt == null && (a.Status == AssignmentStatus.Closed || (a.Mode == AssignmentMode.Test ? a.TestEndsAt : a.DueAt) <= now)
            && !await db.Attempts.AnyAsync(x => x.Participant.AssignmentId == id
                && x.Kind != AttemptKind.SelfReview
                && (x.Status == AttemptStatus.InProgress || x.Status == AttemptStatus.Paused) && (x.DeadlineAt == null || x.DeadlineAt > now));
    }
}
