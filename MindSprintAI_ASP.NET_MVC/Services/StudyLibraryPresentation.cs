using Microsoft.EntityFrameworkCore;
using MindSprintAI_ASP.NET_MVC.Data.Entities;

namespace MindSprintAI_ASP.NET_MVC.Services;

public record StudySetCounts(int Questions, int Cards);

public partial class StudyLibraryService
{
    public Task<Dictionary<int, StudySetCounts>> GetOwnedCountsAsync(string userId) => db.StudySets.AsNoTracking()
        .Where(s => s.OwnerId == userId && s.DeletedAt == null && s.ArchivedAt == null)
        .Select(s => new { s.Id,
            Questions = s.Quizzes.Where(q => q.DeletedAt == null && q.ArchivedAt == null).SelectMany(q => q.Questions).Count(q => q.DeletedAt == null),
            Cards = db.Flashcards.Count(c => c.StudySetId == s.Id && c.DeletedAt == null) })
        .ToDictionaryAsync(s => s.Id, s => new StudySetCounts(s.Questions, s.Cards));

    // Trang bộ học (18/11): độ dài văn bản nguồn để hiện "N ký tự" / "chưa có" ở mục Văn bản nguồn.
    public Task<int> SourceLengthAsync(int studySetId) =>
        db.SourceTexts.Where(s => s.StudySetId == studySetId).Select(s => s.Content.Length).FirstOrDefaultAsync();

    public async Task<StudySet> GetOwnedContentsAsync(int id, string userId)
    {
        await GetOwnedStudySetAsync(id, userId);
        return await db.StudySets.AsNoTracking().AsSingleQuery()
            .Include(s => s.Quizzes.Where(q => q.DeletedAt == null && q.ArchivedAt == null))
                .ThenInclude(q => q.Questions.Where(q => q.DeletedAt == null)).ThenInclude(q => q.Options)
            .Include(s => s.Quizzes).ThenInclude(q => q.Questions).ThenInclude(q => q.Blanks).ThenInclude(b => b.Answers)
            .SingleAsync(s => s.Id == id && s.OwnerId == userId && s.DeletedAt == null && s.ArchivedAt == null);
    }
}
