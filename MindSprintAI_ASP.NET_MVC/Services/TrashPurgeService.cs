using Microsoft.EntityFrameworkCore;
using MindSprintAI_ASP.NET_MVC.Data;
using MindSprintAI_ASP.NET_MVC.Data.Entities;

namespace MindSprintAI_ASP.NET_MVC.Services;

public record PurgeCounts(int StudySets, int Quizzes, int Questions, int Flashcards, int AiConversations = 0);

public class TrashPurgeService(AppDbContext db, TimeProvider clock, ILogger<TrashPurgeService> logger)
{
    public async Task<PurgeCounts> RunAsync(CancellationToken cancellationToken = default)
    {
        var cutoff = clock.GetUtcNow().UtcDateTime.AddDays(-30);
        // Giữ khóa đọc/khoảng khóa đến commit; FK Restrict là hàng rào cuối nếu có tham chiếu mới.
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
        var conversations = db.AiConversations.Where(c => c.DeletedAt <= cutoff);
        var messages = db.AiMessages.Where(m => conversations.Any(c => c.Id == m.ConversationId));
        await db.AiCitations.Where(c => messages.Any(m => m.Id == c.AiMessageId)).ExecuteDeleteAsync(cancellationToken);
        await messages.ExecuteDeleteAsync(cancellationToken);
        var conversationCount = await conversations.ExecuteDeleteAsync(cancellationToken);
        var safeSets = db.StudySets.IgnoreQueryFilters().Where(s => s.EverAssignedAt == null && s.ArchivedAt == null
            && !db.Assignments.Any(a => a.StudySetId == s.Id || a.Quiz != null && a.Quiz.StudySetId == s.Id)
            && !db.Quizzes.Any(q => q.StudySetId == s.Id && (q.EverAssignedAt != null || q.ArchivedAt != null))
            && !db.AttemptQuestions.Any(aq => aq.Question.Quiz.StudySetId == s.Id)
            && !db.Regrades.Any(r => r.Question.Quiz.StudySetId == s.Id)
            && !db.FlashcardProgresses.Any(p => p.Flashcard.StudySetId == s.Id && p.ParticipantId != null)
            && !db.ContentReports.Any(r => r.StudySetId == s.Id)
            && !db.AiConversations.Any(c => c.StudySetId == s.Id));
        var setIds = await safeSets.Where(s => s.DeletedAt < cutoff).Select(s => s.Id).ToListAsync(cancellationToken);
        // Một quiz chưa từng giao vẫn dọn được khi bộ học còn chứa quiz khác đã giao.
        var safeQuizzes = db.Quizzes.IgnoreQueryFilters().Where(q => q.EverAssignedAt == null && q.ArchivedAt == null
            && !db.Assignments.Any(a => a.QuizId == q.Id)
            && !db.AttemptQuestions.Any(aq => aq.Question.QuizId == q.Id)
            && !db.Regrades.Any(r => r.Question.QuizId == q.Id)
            && !db.AiMessages.Any(m => db.Questions.IgnoreQueryFilters().Any(x => x.Id == m.QuestionId && x.QuizId == q.Id))
            && !db.AiConversations.Any(c => db.Questions.IgnoreQueryFilters().Any(x => x.Id == c.QuestionId && x.QuizId == q.Id)));
        var quizIds = await safeQuizzes.Where(q =>
            q.DeletedAt < cutoff || setIds.Contains(q.StudySetId)).Select(q => q.Id).ToListAsync(cancellationToken);
        var questions = db.Questions.IgnoreQueryFilters().Where(q => quizIds.Contains(q.QuizId)
            || q.DeletedAt < cutoff && safeQuizzes.Any(quiz => quiz.Id == q.QuizId));
        var questionIds = await questions.Select(q => q.Id).ToListAsync(cancellationToken);
        var blanks = db.QuestionBlanks.IgnoreQueryFilters().Where(b => questionIds.Contains(b.QuestionId));
        await db.BlankAnswers.IgnoreQueryFilters().Where(a => blanks.Any(b => b.Id == a.QuestionBlankId)).ExecuteDeleteAsync(cancellationToken);
        await blanks.ExecuteDeleteAsync(cancellationToken);
        await db.QuestionOptions.IgnoreQueryFilters().Where(o => questionIds.Contains(o.QuestionId)).ExecuteDeleteAsync(cancellationToken);
        await db.QuestionRevisions.IgnoreQueryFilters().Where(r => questionIds.Contains(r.QuestionId)).ExecuteDeleteAsync(cancellationToken);
        var questionCount = await db.Questions.IgnoreQueryFilters().Where(q => questionIds.Contains(q.Id)).ExecuteDeleteAsync(cancellationToken);
        var quizCount = await db.Quizzes.Where(q => quizIds.Contains(q.Id)).ExecuteDeleteAsync(cancellationToken);
        var cardIds = await db.Flashcards.Where(c => safeSets.Any(s => s.Id == c.StudySetId)
            && (c.DeletedAt < cutoff || setIds.Contains(c.StudySetId))).Select(c => c.Id).ToListAsync(cancellationToken);
        await db.FlashcardProgresses.Where(p => cardIds.Contains(p.FlashcardId) && p.ParticipantId == null).ExecuteDeleteAsync(cancellationToken);
        var cardCount = await db.Flashcards.Where(c => cardIds.Contains(c.Id)).ExecuteDeleteAsync(cancellationToken);
        await db.SourceTexts.Where(s => setIds.Contains(s.StudySetId)).ExecuteDeleteAsync(cancellationToken);
        var setCount = await db.StudySets.Where(s => setIds.Contains(s.Id)).ExecuteDeleteAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        logger.LogInformation("Thùng rác: xóa hẳn {StudySets} bộ học, {Quizzes} quiz, {Questions} câu, {Flashcards} thẻ.", setCount, quizCount, questionCount, cardCount);
        logger.LogInformation("Thùng rác trợ lý: xóa hẳn {Conversations} hội thoại cùng tin nhắn và trích dẫn.", conversationCount);
        return new(setCount, quizCount, questionCount, cardCount, conversationCount);
    }
}

public sealed class TrashPurgeWorker(IServiceScopeFactory scopes, TimeProvider clock, ILogger<TrashPurgeWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromDays(1), clock);
        while (!stoppingToken.IsCancellationRequested)
        {
            using var run = new WorkerRun(logger, nameof(TrashPurgeWorker)); // (NK1)
            try
            {
                using var scope = scopes.CreateScope();
                var counts = await scope.ServiceProvider.GetRequiredService<TrashPurgeService>().RunAsync(stoppingToken);
                run.Count = counts.StudySets + counts.Quizzes + counts.Questions + counts.Flashcards + counts.AiConversations;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex) { run.Failed = true; logger.LogWarning(ex, "Chưa dọn được thùng rác; sẽ thử lại sau."); }
            run.Dispose();
            try { if (!await timer.WaitForNextTickAsync(stoppingToken)) return; }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
        }
    }
}
