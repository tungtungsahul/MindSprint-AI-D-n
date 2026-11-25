using Microsoft.EntityFrameworkCore;
using MindSprintAI_ASP.NET_MVC.Data;
using MindSprintAI_ASP.NET_MVC.Data.Entities;

namespace MindSprintAI_ASP.NET_MVC.Services;

public record CloneSelection(IReadOnlyList<int> QuizIds, bool Flashcards, bool MatchingSettings, bool SourceText = false);
internal record ClonedStudySet(StudySet Set, Dictionary<int, Quiz> Quizzes, Dictionary<int, Flashcard> Cards);

public class StudySetCloneService(AppDbContext db, TimeProvider clock)
{
    public async Task<StudySet> GetOwnedAsync(int id, string userId)
    {
        var set = await db.StudySets.AsNoTracking().Include(s => s.Quizzes.Where(q => q.DeletedAt == null))
            .SingleOrDefaultAsync(s => s.Id == id && s.DeletedAt == null && s.ArchivedAt == null) ?? throw new BusinessException("Không tìm thấy bộ học.");
        if (set.OwnerId != userId) throw new ForbiddenException();
        if (set.HiddenByAdminAt != null) throw new BusinessException("Bộ học bị quản trị viên ẩn nên không nhân bản được.");
        return set;
    }

    public async Task<StudySet> CloneAsync(int id, CloneSelection selection, string userId)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        await GetOwnedAsync(id, userId);
        var quizIds = await db.Quizzes.Where(q => q.StudySetId == id).OrderBy(q => q.Id).Select(q => q.Id).ToListAsync();
        foreach (var quizId in quizIds) await QuizChangeLock.AcquireAsync(db, quizId);
        await LockSetAsync(id);
        await GetOwnedAsync(id, userId);
        var clone = await CopyAsync(id, userId, selection, transfer: false);
        db.Audit?.Add(AuditAction.StudySetCloned, "StudySet", clone.Set.Id, userId, resolveTarget: () => clone.Set.Id.ToString()); // (NK3)
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return clone.Set;
    }

    internal Task LockSetAsync(int id) => db.Database.ExecuteSqlInterpolatedAsync(
        $"SELECT [Id] FROM [StudySets] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {id}");

    // (49B, 50) dựng cây mới, không gắn lại entity gốc hoặc sao chép dữ liệu làm bài.
    internal async Task<ClonedStudySet> CopyAsync(int id, string ownerId, CloneSelection? selection, bool transfer)
    {
        var source = await db.StudySets.AsNoTracking().Include(s => s.Quizzes).ThenInclude(q => q.Questions).ThenInclude(q => q.Options)
            .Include(s => s.Quizzes).ThenInclude(q => q.Questions).ThenInclude(q => q.Blanks).ThenInclude(b => b.Answers)
            .AsSplitQuery().SingleAsync(s => s.Id == id);
        if (!transfer && source.HiddenByAdminAt != null) throw new BusinessException("Bộ học bị quản trị viên ẩn nên không nhân bản được.");
        var quizzes = source.Quizzes.Where(q => q.DeletedAt == null).OrderBy(q => q.Id).ToList();
        if (!transfer && selection != null && selection.QuizIds.Except(quizzes.Select(q => q.Id)).Any())
            throw new BusinessException("Quiz được chọn không thuộc bộ học.");
        var set = new StudySet { OwnerId = ownerId, Title = source.Title[..Math.Min(source.Title.Length, 190)] + " (bản sao)",
            ClonedFromId = id, CreatedAt = clock.GetUtcNow().UtcDateTime,
            HiddenByAdminAt = transfer ? source.HiddenByAdminAt : null, HiddenReason = transfer ? source.HiddenReason : null,
            EverAssignedAt = transfer ? source.EverAssignedAt : null };
        db.StudySets.Add(set);
        if (transfer || selection?.SourceText == true)
        {
            var text = await db.SourceTexts.AsNoTracking().SingleOrDefaultAsync(s => s.StudySetId == id);
            if (text != null) db.SourceTexts.Add(new() { StudySet = set, Content = text.Content, Version = text.Version });
        }
        var quizMap = new Dictionary<int, Quiz>();
        foreach (var q in quizzes.Where(q => selection == null || selection.QuizIds.Contains(q.Id)))
        {
            var copy = new Quiz { Title = q.Title, Status = transfer ? q.Status : QuizStatus.Draft,
                CreatedAt = set.CreatedAt,
                EverAssignedAt = transfer ? q.EverAssignedAt : null, ArchivedAt = transfer ? q.ArchivedAt : null };
            set.Quizzes.Add(copy); quizMap.Add(q.Id, copy);
            foreach (var question in q.Questions.Where(x => x.DeletedAt == null).OrderBy(x => x.OrderIndex))
            {
                var copied = CopyQuestion(question, selection?.MatchingSettings ?? true);
                if (transfer) copied.Version = question.Version;
                copy.Questions.Add(copied);
            }
        }
        var cardMap = new Dictionary<int, Flashcard>();
        if (selection == null || selection.Flashcards)
            foreach (var c in await db.Flashcards.AsNoTracking().Where(c => c.StudySetId == id && c.DeletedAt == null).ToListAsync())
            {
                var card = new Flashcard { StudySet = set, Front = c.Front, Back = c.Back, Example = c.Example, OrderIndex = c.OrderIndex,
                    // (49B, B5d) giữ mốc thẻ để bài đã giao không mất phần bắt buộc/tiến độ.
                    CreatedAt = transfer ? c.CreatedAt : set.CreatedAt, EditedAt = transfer ? c.EditedAt : null };
                db.Flashcards.Add(card); cardMap.Add(c.Id, card);
            }
        return new(set, quizMap, cardMap);
    }

    internal static Question CopyQuestion(Question q, bool matching) => new()
    {
        OrderIndex = q.OrderIndex, Type = q.Type, Prompt = q.Prompt, Explanation = q.Explanation, Points = q.Points,
        TimeLimitSeconds = q.TimeLimitSeconds, KeepOptionOrder = q.KeepOptionOrder, IsRequired = q.IsRequired, MaxLength = q.MaxLength,
        NumberAnswer = q.NumberAnswer, NumberTolerance = matching ? q.NumberTolerance : 0,
        CaseSensitive = matching && q.CaseSensitive, AcceptNoDiacritics = matching && q.AcceptNoDiacritics,
        IgnorePunctuation = matching && q.IgnorePunctuation, AcceptAnyBlankOrder = matching && q.AcceptAnyBlankOrder,
        BlankScoring = matching ? q.BlankScoring : BlankScoring.AllOrNothing,
        Options = q.Options.OrderBy(o => o.OrderIndex).Select(o => new QuestionOption { OrderIndex = o.OrderIndex, Text = o.Text, IsCorrect = o.IsCorrect }).ToList(),
        Blanks = q.Blanks.OrderBy(b => b.BlankIndex).Select(b => new QuestionBlank { BlankIndex = b.BlankIndex,
            Answers = b.Answers.OrderBy(a => a.OrderIndex).Select(a => new BlankAnswer { OrderIndex = a.OrderIndex, Text = a.Text }).ToList() }).ToList()
    };
}
