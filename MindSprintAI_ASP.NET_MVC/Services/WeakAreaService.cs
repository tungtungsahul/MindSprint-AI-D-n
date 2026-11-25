using Microsoft.EntityFrameworkCore;
using MindSprintAI_ASP.NET_MVC.Data;
using MindSprintAI_ASP.NET_MVC.Data.Entities;

namespace MindSprintAI_ASP.NET_MVC.Services;

public enum WeakKind { Cards, Quiz }
public record WeakArea(WeakKind Kind, string Topic, int Wrong, int Total, int? StudySetId, int? AssignmentId)
{
    public int Accuracy => Total == 0 ? 0 : (Total - Wrong) * 100 / Total;
    public string Reason => $"{Wrong}/{Total} lần sai · {Accuracy}% chính xác";
    // "Gợi ý" theo loại chủ đề như BuildSuggestedAction của nhóm (nhóm chia theo danh mục; ở đây chủ đề là bộ thẻ hoặc bài quiz).
    public string Suggestion => Kind == WeakKind.Cards
        ? "Gợi ý: ôn lại các thẻ đang \"Chưa nhớ\" của bộ này mỗi ngày, rồi tự kiểm tra lại bằng quiz."
        : "Gợi ý: xem lại các câu sai kèm giải thích, hỏi Sổ tay AI phần còn chưa rõ rồi làm lại bài.";
}
public record WeakAreaPage(IReadOnlyList<WeakArea> Areas, int CheckedTopics);

// Điểm yếu (MindSprint-AI-D-n WeakAreaService): giữ quy tắc nhóm — một chủ đề là điểm yếu khi có ≥ 3 lần thử và chính xác < 70%;
// yếu nhất (chính xác thấp, sai nhiều) lên trước. Khác nhóm: tính ngay từ dữ liệu thật mỗi lần mở trang (không cần bảng + nút
// "Phân tích lại"), chủ đề = bộ thẻ đã ôn (FlashcardReview) và bài quiz đã làm (câu trả lời lần đầu, bỏ câu hết giờ "{}").
public sealed class WeakAreaService(AppDbContext db)
{
    public const int MinAttempts = 3;
    public const int WeakBelow = 70;

    public async Task<WeakAreaPage> GetAsync(string userId)
    {
        var cards = await db.FlashcardReviews.Where(r => r.UserId == userId)
            .GroupBy(r => new { r.StudySetId, r.StudySet.Title, AssignmentId = r.Participant == null ? (int?)null : r.Participant.AssignmentId })
            .Select(g => new WeakArea(WeakKind.Cards, g.Key.Title, g.Count(r => r.Rating == FlashcardRating.NotRemembered), g.Count(), g.Key.StudySetId, g.Key.AssignmentId))
            .ToListAsync();
        var quiz = await RevealedFirstAnswers(userId)
            .GroupBy(a => new { a.AttemptQuestion.Attempt.Participant.AssignmentId, Title = a.AttemptQuestion.Attempt.Participant.Assignment.Quiz!.Title })
            .Select(g => new WeakArea(WeakKind.Quiz, g.Key.Title, g.Count(a => a.IsCorrect == false), g.Count(), null, g.Key.AssignmentId))
            .ToListAsync();
        var all = cards.Concat(quiz).ToList();
        return new(Pick(all), all.Count);
    }

    // "Tạo thẻ ôn tập từ điểm yếu" (nút của nhóm, bản nhóm chỉ mở Sổ tay AI nhờ AI tạo): ở đây lấy đúng các thẻ đang "Chưa nhớ"
    // và các câu quiz trả lời sai lần đầu của chính người dùng → một bộ học mới (ôn lặp lại ngắt quãng). Không cần AI, không bịa nội dung.
    public const int MaxReviewCards = 100;
    public async Task<StudySet> CreateReviewSetAsync(string userId, StudyLibraryService library, TimeProvider clock)
    {
        var cards = await db.FlashcardProgresses.AsNoTracking()
            .Where(p => (p.UserId == userId || p.Participant!.UserId == userId) && p.LastRating == FlashcardRating.NotRemembered && p.Flashcard.DeletedAt == null)
            .OrderByDescending(p => p.LastReviewedAt).Select(p => new { p.Flashcard.Front, p.Flashcard.Back, p.Flashcard.Example }).Take(MaxReviewCards).ToListAsync();
        var wrong = await RevealedFirstAnswers(userId).AsNoTracking().Where(a => a.IsCorrect == false)
            .OrderByDescending(a => a.SubmittedAt).Select(a => a.AttemptQuestion.SnapshotJson).Take(MaxReviewCards).ToListAsync();
        var items = cards.Select(c => (Front: c.Front, Back: c.Back, Example: c.Example)).ToList();
        foreach (var json in wrong)
        {
            var q = QuestionSnapshot.FromJson(json);
            var answer = string.Join("; ", q.Options.Where(o => o.IsCorrect).Select(o => o.Text).Concat(q.Blanks.Select(b => string.Join(" / ", b.Answers))));
            if (string.IsNullOrWhiteSpace(answer)) continue;
            items.Add((q.Prompt, answer, string.IsNullOrWhiteSpace(q.Explanation) ? null : q.Explanation)); // giải thích → ô Ví dụ của thẻ
        }
        items = items.Where(i => !string.IsNullOrWhiteSpace(i.Front)).DistinctBy(i => i.Front.Trim()).Take(MaxReviewCards).ToList();
        if (items.Count == 0) throw new BusinessException("Chưa có thẻ nào đang \"Chưa nhớ\" hay câu nào trả lời sai để tạo bộ ôn tập.");
        await using var tx = await db.Database.BeginTransactionAsync();
        var now = clock.GetUtcNow().UtcDateTime;
        var set = await library.CreateStudySetAsync($"Ôn điểm yếu {VnTime.Date(now):dd/MM/yyyy}", userId);
        set.FlashcardMode = FlashcardMode.SpacedRepetition;
        db.Flashcards.AddRange(items.Select((i, n) => new Flashcard { StudySetId = set.Id, OrderIndex = n, CreatedAt = now, Front = i.Front.Trim(), Back = i.Back.Trim(), Example = i.Example?.Trim() is { Length: > FlashcardService.MaxExample } e ? e[..FlashcardService.MaxExample] : i.Example?.Trim() }));
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return set;
    }

    // Chỉ câu học viên đã được biết đúng/sai — cùng quy tắc trang làm bài (AttemptService): Luyện tập thì câu đã trả lời;
    // Kiểm tra thì chỉ khi giảng viên đã công bố đáp án và không còn lượt nào đang làm (không lộ đáp án bài chưa công bố).
    private IQueryable<AnswerSubmission> RevealedFirstAnswers(string userId) => db.AnswerSubmissions.Where(a => a.IsFirstAnswer && a.PayloadJson != "{}"
        && a.IsCorrect != null && a.AttemptQuestion.Attempt.Participant.UserId == userId
        && (a.AttemptQuestion.Attempt.Participant.Assignment.Mode == AssignmentMode.Practice
            || a.AttemptQuestion.Attempt.Participant.Assignment.AnswersPublishedAt != null
                && !db.Attempts.Any(x => x.ParticipantId == a.AttemptQuestion.Attempt.ParticipantId
                    && (x.Status == AttemptStatus.InProgress || x.Status == AttemptStatus.Paused))));

    public static List<WeakArea> Pick(IEnumerable<WeakArea> topics) => topics
        .Where(t => t.Total >= MinAttempts && t.Accuracy < WeakBelow)
        .OrderBy(t => t.Accuracy).ThenByDescending(t => t.Wrong).ToList();
}
