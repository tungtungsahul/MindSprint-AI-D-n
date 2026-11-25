using Microsoft.EntityFrameworkCore;
using MindSprintAI_ASP.NET_MVC.Data.Entities;

namespace MindSprintAI_ASP.NET_MVC.Services;

public record LecturerOpenAssignment(int Id, string Title, string Classroom, int Working, int Submitted,
    AssignmentMode Mode = AssignmentMode.Practice, DateTime? EndsAt = null, DateTime? CreatedAt = null);
public record LecturerReviewClass(int Id, string Name, int Count, ReviewWork? Work = null);
public record LecturerDashboard(string Name, string Greeting, IReadOnlyList<LecturerOpenAssignment> Open,
    IReadOnlyList<LecturerReviewClass> Review);

public partial class AssignmentService
{
    public static string LecturerGreeting(DateTime utcNow) => utcNow.AddHours(7).Hour switch
    { >= 5 and < 12 => "Chào buổi sáng", >= 12 and < 18 => "Chào buổi chiều", _ => "Chào buổi tối" };

    public async Task<LecturerDashboard> GetLecturerDashboardAsync(string userId, string name)
    {
        var now = Now;
        var pausedBefore = now - AttemptRules.PauseAfter;
        var teaching = db.ClassMembers.Where(m => m.UserId == userId && m.Classroom.DeletedAt == null
            && m.Status == ClassMemberStatus.Active && (m.Role == ClassRole.MainLecturer || m.Role == ClassRole.AssistantLecturer));
        var ongoing = await db.Assignments.AsNoTracking().Where(a => a.ClassroomId != null && a.CreatedById == userId
            && teaching.Any(m => m.ClassroomId == a.ClassroomId) && a.ReportDeletedAt == null && a.ArchivedAt == null
            && a.Status == AssignmentStatus.Open && (a.OpensAt == null || a.OpensAt <= now) && (a.DueAt == null || a.DueAt > now)
            && (a.Mode != AssignmentMode.Test || a.TestStartedAt != null && (a.TestEndsAt == null || a.TestEndsAt > now)))
            .OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id).Take(5)
            .Select(a => new LecturerOpenAssignment(a.Id, (a.Quiz != null ? a.Quiz.Title : a.PaperTitle ?? "Thẻ ghi nhớ"), a.Classroom!.Name,
                a.Participants.Count(p => p.Attempts.Any(t => t.Status == AttemptStatus.InProgress && !t.ExcludeFromGradebook
                    && (t.DisconnectedAt == null || t.DisconnectedAt >= pausedBefore) && (t.DeadlineAt == null || t.DeadlineAt > now))),
                a.Participants.Count(p => p.Attempts.Any(t => t.Status == AttemptStatus.Ended && !t.ExcludeFromGradebook)),
                a.Mode, a.Mode == AssignmentMode.Test ? a.TestEndsAt : a.DueAt, a.CreatedAt))
            .ToListAsync();
        var summaries = await classrooms.GetLecturerSummariesAsync(userId);
        var names = await teaching.Select(m => new { m.ClassroomId, m.Classroom.Name }).ToDictionaryAsync(m => m.ClassroomId, m => m.Name);
        return new(name, LecturerGreeting(now), ongoing, summaries.Where(s => s.Value.NeedsReview > 0 && names.ContainsKey(s.Key))
            .Select(s => new LecturerReviewClass(s.Key, names[s.Key], s.Value.NeedsReview, s.Value.Work)).ToList());
    }
}
