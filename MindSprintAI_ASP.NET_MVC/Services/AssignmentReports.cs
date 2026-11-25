using Microsoft.EntityFrameworkCore;
using MindSprintAI_ASP.NET_MVC.Data.Entities;

namespace MindSprintAI_ASP.NET_MVC.Services;

public enum AssignmentReportState { Open = 1, Upcoming = 2, Closed = 3 }
public record AssignmentReportItem(int Id, string Title, AssignmentMode Mode, AssignmentReportState State,
    DateTime CreatedAt, int Participants, string? Code, int ClassroomId, string Classroom);
public record ReportClassOption(int Id, string Name);
public record AssignmentReportPage(IReadOnlyList<AssignmentReportItem> Items, int Page, int Total,
    string Status, string? Query, int? ClassId, IReadOnlyDictionary<AssignmentReportState, int> Counts,
    IReadOnlyList<ReportClassOption> Classes)
{
    public int Pages => Math.Max(1, (Total + 19) / 20);
}

public partial class AssignmentService
{
    // Same clock/window conditions as IsOpen + the shared test clock used in classroom summaries.
    public static AssignmentReportState ReportState(Assignment a, DateTime now) =>
        a.Status == AssignmentStatus.Closed || a.DueAt <= now || a.Mode == AssignmentMode.Test && a.TestStartedAt != null && a.TestEndsAt <= now
            ? AssignmentReportState.Closed
            : a.OpensAt > now || a.Mode == AssignmentMode.Test && a.TestStartedAt == null ? AssignmentReportState.Upcoming : AssignmentReportState.Open;

    public async Task<AssignmentReportPage> GetReportPageAsync(string lecturerId, int page = 1, string? status = null, string? q = null, int? classId = null)
    {
        var now = Now;
        status = status is "open" or "upcoming" or "closed" ? status : "all";
        q = string.IsNullOrWhiteSpace(q) ? null : q.Trim();
        if (q?.Length > 200) q = q[..200];
        var teaching = db.ClassMembers.Where(m => m.UserId == lecturerId && m.Classroom.DeletedAt == null
            && m.Status == ClassMemberStatus.Active && (m.Role == ClassRole.MainLecturer || m.Role == ClassRole.AssistantLecturer));
        var classes = await teaching.OrderBy(m => m.Classroom.Name).Select(m => new ReportClassOption(m.ClassroomId, m.Classroom.Name)).ToListAsync();
        var query = db.Assignments.AsNoTracking().Where(a => a.ClassroomId != null && teaching.Any(m => m.ClassroomId == a.ClassroomId)
            && a.ReportDeletedAt == null && a.ArchivedAt == null
            && (classId == null || a.ClassroomId == classId)
            && (q == null || ((a.Quiz != null ? a.Quiz.Title : a.PaperTitle ?? "Thẻ ghi nhớ")).Contains(q)))
            .Select(a => new
            {
                Assignment = a,
                State = a.Status == AssignmentStatus.Closed || a.DueAt <= now || a.Mode == AssignmentMode.Test && a.TestStartedAt != null && a.TestEndsAt <= now
                    ? AssignmentReportState.Closed
                    : a.OpensAt > now || a.Mode == AssignmentMode.Test && a.TestStartedAt == null ? AssignmentReportState.Upcoming : AssignmentReportState.Open
            });
        var counts = await query.GroupBy(a => a.State).Select(g => new { State = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.State, x => x.Count);
        var state = status switch { "open" => AssignmentReportState.Open, "upcoming" => AssignmentReportState.Upcoming, "closed" => AssignmentReportState.Closed, _ => (AssignmentReportState?)null };
        var total = state == null ? counts.Values.Sum() : counts.GetValueOrDefault(state.Value);
        page = Math.Clamp(page, 1, Math.Max(1, (total + 19) / 20));
        if (state != null) query = query.Where(a => a.State == state);
        var items = await query.OrderByDescending(x => x.Assignment.CreatedAt).ThenByDescending(x => x.Assignment.Id)
            .Skip((page - 1) * 20).Take(20)
            .Select(x => new AssignmentReportItem(x.Assignment.Id, (x.Assignment.Quiz != null ? x.Assignment.Quiz.Title : x.Assignment.PaperTitle ?? "Thẻ ghi nhớ"),
                x.Assignment.Mode, x.State, x.Assignment.CreatedAt,
                x.Assignment.Participants.Count(p => p.Attempts.Any(a => a.Status != AttemptStatus.Cancelled && !a.ExcludeFromGradebook)),
                db.JoinCodes.Where(c => c.TargetType == JoinCodeTarget.Assignment && c.TargetId == x.Assignment.Id && c.IsActive).Select(c => c.Code).FirstOrDefault(),
                x.Assignment.ClassroomId!.Value, x.Assignment.Classroom!.Name)).ToListAsync();
        return new(items, page, total, status, q, classId, counts, classes);
    }
}
