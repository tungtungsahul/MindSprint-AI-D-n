using MindSprintAI_ASP.NET_MVC.Data.Entities;

namespace MindSprintAI_ASP.NET_MVC.Services;

public record HomeSet(int Id, string Title, int Quizzes, int Due);

// Trang chủ kiểu nhóm (docs/giao-dien-nhom/01–03), chỉ hiện số liệu thật: bài cần làm, thẻ đến hạn ôn, công việc Kanban, bộ học.
// Chuỗi ngày học + thẻ đã ôn + độ chính xác hôm nay lấy từ StudyStatsService (hoạt động thật, không do trình duyệt khai).
public record HomeDashboard(string Name, string Greeting, DateOnly Today, bool Lecturer, LecturerDashboard? Teaching,
    IReadOnlyList<Assignment> Todo, int CardsDue, IReadOnlyList<KanbanTask> Tasks, int TasksTotal, int TasksDone, IReadOnlyList<HomeSet> Sets,
    StudyStreak Streak, StudyToday Study)
{
    public int TaskPercent => TasksTotal == 0 ? 0 : TasksDone * 100 / TasksTotal;
    public bool NotedToday => Study.Noted;
    // "Mục tiêu hôm nay" của nhóm: ôn ≥ 1 thẻ · làm ≥ 1 câu quiz · có ghi chú — tự tính từ hoạt động thật.
    public int GoalsDone => (Study.CardsReviewed > 0 ? 1 : 0) + (Study.Answered > 0 ? 1 : 0) + (Study.Noted ? 1 : 0);
    public int GoalPercent => GoalsDone * 100 / 3;
}

public sealed class HomeService(AssignmentService assignments, AttemptService attempts, StudyLibraryService library, KanbanService kanban,
    StudyStatsService stats, TimeProvider clock)
{
    public async Task<HomeDashboard> GetAsync(ApplicationUser user)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var lecturer = user.CurrentMode == UserMode.Lecturer;
        var teaching = lecturer ? await assignments.GetLecturerDashboardAsync(user.Id, user.DisplayName) : null;
        var todo = lecturer ? [] : LearnerTodo(await assignments.ListForLearnerAsync(user.Id), await attempts.ListMyParticipationsAsync(user.Id), now);
        var due = await library.SrsDueCountsAsync(user.Id);
        var tasks = await kanban.ListAsync(user.Id);
        var sets = (await library.ListOwnedAsync(user.Id)).Take(3)
            .Select(s => new HomeSet(s.Id, s.Title, s.Quizzes.Count, due.Sets.GetValueOrDefault(s.Id))).ToList();
        // Việc chưa xong: đang làm trước, rồi chưa bắt đầu; mỗi cột giữ thứ tự người dùng đã sắp.
        var open = tasks.Where(t => t.Status != KanbanStatus.Done).OrderByDescending(t => t.Status == KanbanStatus.Doing).ThenBy(t => t.Position).Take(3).ToList();
        return new(user.DisplayName, AssignmentService.LecturerGreeting(now), VnTime.Date(now), lecturer, teaching,
            todo, due.Total, open, tasks.Count, tasks.Count(t => t.Status == KanbanStatus.Done), sets,
            await stats.StreakAsync(user.Id), await stats.TodayAsync(user.Id));
    }

    // Cùng quy tắc nhóm "Cần làm" của Learn/Index: chưa hoàn thành, chưa nộp chờ điểm, chưa quá hạn; kiểm tra trước, hạn gần trước.
    public static List<Assignment> LearnerTodo(List<Assignment> list, List<Participant> participations, DateTime now)
    {
        CompletionStatus StatusOf(Assignment a) => participations.FirstOrDefault(x => x.AssignmentId == a.Id)?.CompletionStatus ?? CompletionStatus.NotStarted;
        DateTime? DeadlineOf(Assignment a) => a.Mode == AssignmentMode.Test ? a.TestEndsAt : a.DueAt;
        return list.Where(a => StatusOf(a) is not (CompletionStatus.Completed or CompletionStatus.SubmittedAwaitingScore) && !(DeadlineOf(a) <= now))
            .OrderBy(a => DeadlineOf(a) is null ? 2 : a.Mode == AssignmentMode.Test ? 0 : 1).ThenBy(a => DeadlineOf(a) ?? DateTime.MaxValue).ToList();
    }
}
