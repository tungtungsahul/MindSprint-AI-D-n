using Microsoft.EntityFrameworkCore;
using MindSprintAI_ASP.NET_MVC.Data;
using MindSprintAI_ASP.NET_MVC.Data.Entities;

namespace MindSprintAI_ASP.NET_MVC.Services;

public record MasteryRow(int StudySetId, string Title, int Remembered, int Studied)
{
    public int Percent => Studied == 0 ? 0 : Remembered * 100 / Studied;
}
public record OverviewPage(int MasteryScore, int CardsStudied, StudyToday Today, StudyStreak Streak, WeakArea? Improve, IReadOnlyList<MasteryRow> Mastery);

// "Tổng quan & Phân tích tiến độ" của nhóm (index.html #tab-content-overview): điểm thành thạo chung (tỷ lệ thuộc từ), số từ ôn hôm nay,
// chủ đề cần cải thiện (chính xác thấp nhất), độ thành thạo theo mảng. Ở đây "thuộc" = lần đánh giá gần nhất của thẻ là "Đã nhớ"
// (FlashcardProgress.LastRating, thẻ tự ôn và thẻ bài giao); mảng = bộ học.
public sealed class OverviewService(AppDbContext db, StudyStatsService stats, WeakAreaService weak)
{
    public async Task<OverviewPage> GetAsync(string userId)
    {
        var rows = await db.FlashcardProgresses.Where(p => (p.UserId == userId || p.Participant!.UserId == userId) && p.Flashcard.DeletedAt == null)
            .GroupBy(p => new { p.Flashcard.StudySetId, p.Flashcard.StudySet.Title })
            .Select(g => new MasteryRow(g.Key.StudySetId, g.Key.Title, g.Count(p => p.LastRating == FlashcardRating.Remembered), g.Count()))
            .ToListAsync();
        var studied = rows.Sum(r => r.Studied);
        var score = studied == 0 ? 0 : rows.Sum(r => r.Remembered) * 100 / studied;
        var weakest = (await weak.GetAsync(userId)).Areas.FirstOrDefault();
        return new(score, studied, await stats.TodayAsync(userId), await stats.StreakAsync(userId), weakest,
            rows.OrderByDescending(r => r.Studied).ThenBy(r => r.Title).Take(8).ToList());
    }
}
