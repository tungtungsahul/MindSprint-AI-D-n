using Microsoft.EntityFrameworkCore;
using MindSprintAI_ASP.NET_MVC.Data;
using MindSprintAI_ASP.NET_MVC.Data.Entities;

namespace MindSprintAI_ASP.NET_MVC.Services;

public record StudySlotInput(DayOfWeek? DayOfWeek, TimeOnly? StartTime, TimeOnly? EndTime, StudyTopic? Topic, string? Note);

// Thời khóa biểu (Lịch học) của nhóm: quy tắc giữ nguyên MindSprint-AI-D-n (script.js timetableForm + StudyStatsController).
public sealed class StudyScheduleService(AppDbContext db)
{
    public const string NotFound = "Không tìm thấy giờ học.";
    public static readonly TimeOnly Earliest = new(6, 0), Latest = new(23, 0);
    public const int MaxSlots = 100; // chặn lạm dụng; một tuần học thật không cần quá 100 giờ học

    public Task<List<StudySlot>> ListAsync(string userId) => db.StudySlots.AsNoTracking().Where(s => s.UserId == userId)
        .OrderBy(s => s.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)s.DayOfWeek).ThenBy(s => s.StartTime).ThenBy(s => s.Id).ToListAsync();

    public async Task<StudySlot> CreateAsync(string userId, StudySlotInput input)
    {
        var slot = new StudySlot { UserId = userId };
        Apply(slot, input);
        await using var tx = await db.Database.BeginTransactionAsync();
        await AttemptLock.StudyPlanAsync(db, userId);
        if (await db.StudySlots.CountAsync(s => s.UserId == userId) >= MaxSlots)
            throw new BusinessException($"Mỗi người có tối đa {MaxSlots} giờ học trong thời khóa biểu.");
        db.StudySlots.Add(slot);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return slot;
    }

    public async Task<StudySlot> UpdateAsync(string userId, int id, StudySlotInput input)
    {
        var slot = await db.StudySlots.SingleOrDefaultAsync(s => s.Id == id && s.UserId == userId) ?? throw new BusinessException(NotFound);
        Apply(slot, input);
        await db.SaveChangesAsync();
        return slot;
    }

    public async Task DeleteAsync(string userId, int id)
    {
        if (await db.StudySlots.Where(s => s.Id == id && s.UserId == userId).ExecuteDeleteAsync() == 0) throw new BusinessException(NotFound);
    }

    // Giờ học tiếp theo tính từ thời điểm VN hiện tại (thẻ "Lịch học tiếp theo" của nhóm): hôm nay còn giờ chưa bắt đầu, rồi các ngày sau.
    public static (StudySlot Slot, int DaysAhead)? Next(IReadOnlyList<StudySlot> slots, DateTime vnNow)
    {
        var now = TimeOnly.FromDateTime(vnNow);
        for (var ahead = 0; ahead < 8; ahead++)
        {
            var day = vnNow.AddDays(ahead).DayOfWeek;
            var hit = slots.Where(s => s.DayOfWeek == day && (ahead > 0 || s.EndTime > now)).OrderBy(s => s.StartTime).FirstOrDefault();
            if (hit != null) return (hit, ahead);
        }
        return null;
    }

    private static void Apply(StudySlot slot, StudySlotInput input)
    {
        if (input.DayOfWeek is not { } day || !Enum.IsDefined(day)) throw new BusinessException("Chọn ngày trong tuần.");
        if (input.StartTime is not { } start || input.EndTime is not { } end) throw new BusinessException("Nhập giờ bắt đầu và giờ kết thúc.");
        start = new TimeOnly(start.Hour, start.Minute); end = new TimeOnly(end.Hour, end.Minute);
        if (start < Earliest || end > Latest) throw new BusinessException("Giờ học phải nằm trong khoảng 06:00 đến 23:00.");
        if (end <= start) throw new BusinessException("Giờ kết thúc phải muộn hơn giờ bắt đầu học.");
        var topic = input.Topic ?? StudyTopic.Mixed;
        if (!Enum.IsDefined(topic)) throw new BusinessException("Chủ đề học tập không hợp lệ.");
        var note = string.IsNullOrWhiteSpace(input.Note) ? null : input.Note.Trim();
        if (note?.Length > 40) throw new BusinessException("Ghi chú tối đa 40 ký tự.");
        (slot.DayOfWeek, slot.StartTime, slot.EndTime, slot.Topic, slot.Note) = (day, start, end, topic, note);
    }
}

public record StudyToday(int CardsReviewed, int Answered, int Correct, bool Noted = false) // Noted: có ghi chú Sổ tay AI hôm nay (mục tiêu ngày của nhóm)
{
    public int Accuracy => Answered == 0 ? 0 : Correct * 100 / Answered;
}
public record StudyStreak(int Current, int Longest, DateOnly? LastDay);

// Chuỗi ngày học: nhóm để trình duyệt tự gửi số thẻ đã ôn (POST /api/study/days) → dễ khai khống. Ở đây tính từ dữ liệu thật
// QuizEdu đã ghi: mỗi lần ôn thẻ (FlashcardReview) và mỗi câu tự trả lời lần đầu (AnswerSubmission, bỏ câu hết giờ "{}").
// Ngày học = ngày (giờ VN) có ôn thẻ hoặc làm quiz (người dùng chốt 15/11); chuỗi đứt khi bỏ trọn một ngày.
public sealed class StudyStatsService(AppDbContext db, TimeProvider clock)
{
    private static readonly TimeSpan Vn = TimeSpan.FromHours(7);

    public async Task<IReadOnlyList<DateOnly>> ActiveDaysAsync(string userId, int lookbackDays = 400)
    {
        var from = clock.GetUtcNow().UtcDateTime.AddDays(-lookbackDays);
        var reviews = await db.FlashcardReviews.Where(r => r.UserId == userId && r.ReviewedAt >= from).Select(r => r.ReviewedAt).ToListAsync();
        var answers = await Answers(userId, from).Select(a => a.SubmittedAt).ToListAsync();
        return reviews.Concat(answers).Select(t => DateOnly.FromDateTime(t + Vn)).Distinct().OrderBy(d => d).ToList();
    }

    public async Task<StudyStreak> StreakAsync(string userId)
    {
        var days = await ActiveDaysAsync(userId);
        return Streak(days, VnTime.Date(clock.GetUtcNow().UtcDateTime));
    }

    public static StudyStreak Streak(IReadOnlyList<DateOnly> days, DateOnly today)
    {
        int longest = 0, run = 0;
        DateOnly? prev = null;
        foreach (var d in days.Distinct().Order())
        {
            run = prev is { } p && d.DayNumber - p.DayNumber == 1 ? run + 1 : 1;
            longest = Math.Max(longest, run);
            prev = d;
        }
        var set = days.ToHashSet();
        var current = 0;
        // Người dùng chốt 15/11: chuỗi chỉ đứt khi bỏ trọn một ngày — hôm nay chưa học thì vẫn tính tiếp từ hôm qua.
        var start = set.Contains(today) ? today : today.AddDays(-1);
        for (var check = start; set.Contains(check); check = check.AddDays(-1)) current++;
        return new(current, longest, prev);
    }

    public async Task<StudyToday> TodayAsync(string userId)
    {
        var start = VnTime.Date(clock.GetUtcNow().UtcDateTime).ToDateTime(TimeOnly.MinValue) - Vn; // 00:00 giờ VN tính ra UTC
        var cards = await db.FlashcardReviews.CountAsync(r => r.UserId == userId && r.ReviewedAt >= start);
        var answers = await Answers(userId, start).Select(a => a.IsCorrect).ToListAsync();
        var noted = await db.NotebookNotes.AnyAsync(n => n.Notebook.UserId == userId && n.CreatedAt >= start);
        return new(cards, answers.Count, answers.Count(c => c == true), noted);
    }

    private IQueryable<AnswerSubmission> Answers(string userId, DateTime fromUtc) => db.AnswerSubmissions.Where(a =>
        a.IsFirstAnswer && a.PayloadJson != "{}" && a.SubmittedAt >= fromUtc && a.AttemptQuestion.Attempt.Participant.UserId == userId);
}
