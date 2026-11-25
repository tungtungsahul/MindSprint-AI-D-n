using Microsoft.EntityFrameworkCore;
using MindSprintAI_ASP.NET_MVC.Data;
using MindSprintAI_ASP.NET_MVC.Data.Entities;

namespace MindSprintAI_ASP.NET_MVC.Services;

public record RankRow(int Rank, string Key, string Name, decimal Percent, TimeSpan? Time, bool Me, int? Done = null, int? Total = null);

// Rows: GV thấy đủ; học viên thấy top 10 (Mine = hạng của chính mình nếu ngoài top 10). Hidden: lý do học viên chưa xem được.
public record Leaderboard(List<RankRow> Rows, RankRow? Mine, int Count, string? Hidden);

// Xếp hạng (người dùng chốt 17/11/2026, kiểu Quizizz; 18/11: mỗi người một hạng, không đồng hạng):
// - Từng bài: điểm (đúng điểm bảng điểm đang tính) cao trước → làm nhanh hơn → nộp sớm hơn. Ôn tập: học viên thấy ngay;
//   Kiểm tra / bài theo đề: chỉ sau khi GV công bố điểm. Bài chỉ có thẻ ghi nhớ không có điểm nên không xếp.
// - Cả lớp: trung bình % các bài đã giao cho học viên đó và đã hiện điểm; bài chưa làm tính 0. Bằng nhau thì xét
//   làm được nhiều bài hơn → tổng thời gian làm ít hơn → nộp bài cuối sớm hơn; cuối cùng theo tên để thứ tự luôn cố định.
// - Học viên thấy top 10 + hạng của mình; GV luôn thấy cả bảng và tắt được bảng của học viên theo từng lớp.
public sealed class RankingService(AppDbContext db, ClassroomService classrooms)
{
    public const int TopForLearners = 10;

    public static bool Ranked(Assignment a) => a.Form != AssignmentForm.FlashcardOnly;
    public static bool VisibleToLearners(Assignment a) => a.Mode == AssignmentMode.Practice || a.ScoresPublishedAt != null;

    public async Task<Leaderboard> ForAssignmentAsync(int assignmentId, string userId)
    {
        var a = await db.Assignments.AsNoTracking().Include(x => x.Classroom).SingleOrDefaultAsync(x => x.Id == assignmentId)
            ?? throw new BusinessException("Không tìm thấy bài.");
        var lecturer = a.ClassroomId != null && await classrooms.IsLecturerAsync(a.ClassroomId.Value, userId);
        var people = await db.Participants.AsNoTracking().Include(p => p.Attempts).Include(p => p.User)
            .Where(p => p.AssignmentId == assignmentId).ToListAsync();
        if (!lecturer && !people.Any(p => p.UserId == userId)
            && !(a.ClassroomId != null && await classrooms.GetActiveMembershipAsync(a.ClassroomId.Value, userId) != null))
            throw new ForbiddenException();
        if (!lecturer && Hidden(a) is { } reason) return new([], null, 0, reason);
        var rows = Order(people.Select(p => Score(p, a) is { } s
            ? (Key: p.UserId ?? "g" + p.Id, Name: p.User?.DisplayName ?? p.GuestDisplayName ?? "Khách", s.Percent, s.Time, s.SubmittedAt, Me: p.UserId == userId)
            : default).Where(r => r.Key != null).ToList());
        return Cut(rows, lecturer);
    }

    public async Task<Leaderboard> ForClassAsync(int classroomId, string userId)
    {
        var room = await db.Classrooms.AsNoTracking().SingleOrDefaultAsync(c => c.Id == classroomId && c.DeletedAt == null)
            ?? throw new BusinessException("Không tìm thấy lớp.");
        var lecturer = await classrooms.IsLecturerAsync(classroomId, userId);
        var members = await db.ClassMembers.AsNoTracking().Include(m => m.User)
            .Where(m => m.ClassroomId == classroomId && m.Role == ClassRole.Student && m.Status == ClassMemberStatus.Active).ToListAsync();
        if (!lecturer && !members.Any(m => m.UserId == userId)) throw new ForbiddenException();
        if (!lecturer && !room.LeaderboardEnabled) return new([], null, 0, "Giảng viên đã tắt bảng xếp hạng của lớp này.");
        var list = await db.Assignments.AsNoTracking().Where(a => a.ClassroomId == classroomId && a.Status != AssignmentStatus.Draft).ToListAsync();
        list = list.Where(a => Ranked(a) && VisibleToLearners(a)).ToList();
        var ids = list.Select(a => a.Id).ToList();
        var people = await db.Participants.AsNoTracking().Include(p => p.Attempts)
            .Where(p => ids.Contains(p.AssignmentId) && p.UserId != null).ToListAsync();
        var rows = new List<(string Key, string Name, decimal Percent, TimeSpan Time, DateTime Last, bool Me, int Done, int Total)>();
        foreach (var m in members)
        {
            var mine = list.Where(a => AssignmentService.IsAssignedTo(a, m)).ToList();
            if (mine.Count == 0) continue;
            var scores = mine.Select(a => people.FirstOrDefault(p => p.AssignmentId == a.Id && p.UserId == m.UserId) is { } p ? Score(p, a) : null).ToList();
            var done = scores.Where(s => s != null).Select(s => s!.Value).ToList();
            rows.Add((m.UserId, m.User.DisplayName, Math.Round(scores.Sum(s => s?.Percent ?? 0) / mine.Count, 1),
                TimeSpan.FromTicks(done.Sum(s => s.Time?.Ticks ?? 0)), done.Select(s => s.SubmittedAt).DefaultIfEmpty(DateTime.MaxValue).Max(),
                m.UserId == userId, done.Count, mine.Count));
        }
        var ranked = rows.OrderByDescending(r => r.Percent).ThenByDescending(r => r.Done).ThenBy(r => r.Time).ThenBy(r => r.Last)
            .ThenBy(r => r.Name, StringComparer.CurrentCulture).ThenBy(r => r.Key, StringComparer.Ordinal)
            .Select((r, i) => new RankRow(i + 1, r.Key, r.Name, r.Percent, r.Time, r.Me, r.Done, r.Total)).ToList();
        return Cut(ranked, lecturer);
    }

    public async Task SetEnabledAsync(int classroomId, bool enabled, string userId)
    {
        await classrooms.EnsureLecturerAsync(classroomId, userId);
        var room = await db.Classrooms.SingleAsync(c => c.Id == classroomId);
        room.LeaderboardEnabled = enabled;
        await db.SaveChangesAsync();
    }

    public static string? Hidden(Assignment a) =>
        !Ranked(a) ? "Bài chỉ có thẻ ghi nhớ nên không xếp hạng."
        : a.Classroom is { LeaderboardEnabled: false } ? "Giảng viên đã tắt bảng xếp hạng của lớp này."
        : !VisibleToLearners(a) ? "Bảng xếp hạng hiện sau khi giảng viên công bố điểm." : null;

    // % điểm theo đúng lựa chọn của bảng điểm; thời gian = trung bình thời gian làm của các lượt được tính.
    internal static (decimal Percent, TimeSpan? Time, DateTime SubmittedAt)? Score(Participant p, Assignment a)
    {
        var s = GradebookService.SelectForAssignment(p.Attempts, a);
        if (s == null || s.Max <= 0) return null;
        var counted = a.Mode == AssignmentMode.Practice
            ? GradebookService.ScoredAttempts(p.Attempts, a.ScoreSelection)
            : p.Attempts.Where(x => x.Status == AttemptStatus.Ended && !x.ExcludeFromGradebook).OrderByDescending(x => x.AttemptNumber).Take(1).ToList();
        var times = counted.Where(x => x.EndedAt != null).Select(x => x.EndedAt!.Value - x.StartedAt).ToList();
        return (Math.Round(s.Raw * 100 / s.Max, 1), times.Count == 0 ? null : TimeSpan.FromTicks((long)times.Average(t => t.Ticks)),
            counted.Where(x => x.EndedAt != null).Select(x => x.EndedAt!.Value).DefaultIfEmpty(DateTime.MaxValue).Max());
    }

    // Mỗi người một hạng: hạng = vị trí sau khi xếp (1, 2, 3…).
    private static List<RankRow> Order(List<(string Key, string Name, decimal Percent, TimeSpan? Time, DateTime SubmittedAt, bool Me)> rows) =>
        rows.OrderByDescending(r => r.Percent).ThenBy(r => r.Time ?? TimeSpan.MaxValue).ThenBy(r => r.SubmittedAt)
            .ThenBy(r => r.Name, StringComparer.CurrentCulture).ThenBy(r => r.Key, StringComparer.Ordinal)
            .Select((r, i) => new RankRow(i + 1, r.Key, r.Name, r.Percent, r.Time, r.Me)).ToList();

    public static Leaderboard Cut(List<RankRow> rows, bool lecturer)
    {
        if (lecturer) return new(rows, null, rows.Count, null);
        var top = rows.Take(TopForLearners).ToList();
        var mine = rows.FirstOrDefault(r => r.Me);
        return new(top, mine != null && !top.Contains(mine) ? mine : null, rows.Count, null);
    }
}
