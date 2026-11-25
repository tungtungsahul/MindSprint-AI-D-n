using Microsoft.EntityFrameworkCore;
using MindSprintAI_ASP.NET_MVC.Data;
using MindSprintAI_ASP.NET_MVC.Data.Entities;
using MindSprintAI_ASP.NET_MVC.Models;

namespace MindSprintAI_ASP.NET_MVC.Services;

// (NK5) Quyền kiểm lại ở service, kể cả khi được gọi ngoài controller.
public sealed class AuditQueryService(AppDbContext db, AuditLog audit, TimeProvider clock)
{
    public async Task<AuditPage> QueryAsync(string adminId, AuditFilter filter)
    {
        await AdminService.EnsureAsync(db, adminId, clock.GetUtcNow().UtcDateTime);
        await audit.WriteSeparateAsync(AuditAction.AdminViewedAudit, "Audit", "", adminId);
        var query = db.AuditEntries.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(filter.Email))
        {
            var email = filter.Email.Trim().ToUpperInvariant();
            query = query.Where(e => db.Users.Any(u => u.Id == e.ActorUserId && u.NormalizedEmail != null && u.NormalizedEmail.Contains(email)));
        }
        if (filter.From != null) { var from = VnTime.FromInput(filter.From.Value.Date); query = query.Where(e => e.At >= from); }
        if (filter.To != null) { var until = VnTime.FromInput(filter.To.Value.Date.AddDays(1)); query = query.Where(e => e.At < until); }
        if (filter.Action != null) query = query.Where(e => e.Action == filter.Action);
        if (filter.Result != null) query = query.Where(e => e.Result == filter.Result);
        if (!string.IsNullOrWhiteSpace(filter.TargetType)) query = query.Where(e => e.TargetType == filter.TargetType.Trim());
        if (!string.IsNullOrWhiteSpace(filter.TargetId)) query = query.Where(e => e.TargetId == filter.TargetId.Trim());
        if (!string.IsNullOrWhiteSpace(filter.RequestId)) query = query.Where(e => e.RequestId == filter.RequestId.Trim());
        var total = await query.CountAsync();
        var page = Math.Clamp(filter.Page, 1, Math.Max(1, (int)Math.Ceiling(total / 50d)));
        var entries = await query.OrderByDescending(e => e.At).ThenByDescending(e => e.Id).Skip((page - 1) * 50).Take(50).ToListAsync();
        // (G0) mục tiêu là tài khoản thì hiện tên + email che, không hiện mã kỹ thuật dài.
        var ids = entries.Where(e => e.ActorUserId != null).Select(e => e.ActorUserId!)
            .Concat(entries.Where(e => e.TargetType == "User").Select(e => e.TargetId)).Distinct().ToList();
        var users = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id);
        var rows = new List<AuditRow>();
        foreach (var e in entries)
        {
            var actor = e.ActorUserId != null && users.TryGetValue(e.ActorUserId, out var user)
                ? user.DisplayName + " (" + LogRedaction.MaskEmail(user.Email) + ")"
                : e.ActorKind == AuditActorKind.Guest ? "Khách " + e.ActorGuestId : e.ActorKind == AuditActorKind.System ? "Hệ thống" : "Tài khoản đã xóa";
            var target = e.TargetType == "User"
                ? AuditDisplay.Target(e.TargetType) + " " + (users.TryGetValue(e.TargetId, out var t) ? t.DisplayName + " (" + LogRedaction.MaskEmail(t.Email) + ")" : "đã xóa")
                : AuditDisplay.Target(e.TargetType) + (e.TargetId == "" ? "" : " #" + e.TargetId);
            rows.Add(new(e, actor, target, await TargetUrlAsync(e)));
        }
        return new(filter, rows, total, page);
    }

    private async Task<string?> TargetUrlAsync(AuditEntry e)
    {
        if (e.TargetType == "User") return await db.Users.AnyAsync(u => u.Id == e.TargetId && !u.IsGuest) ? "/Admin/Lock/" + Uri.EscapeDataString(e.TargetId) : null;
        if (e.TargetType == "Audit") return "/Admin/Audit";
        if (!long.TryParse(e.TargetId, out var id)) return null;
        if (e.TargetType == "Question")
        {
            var quizId = await db.Questions.Where(q => q.Id == id && q.DeletedAt == null && q.Quiz.DeletedAt == null
                && q.Quiz.StudySet.DeletedAt == null).Select(q => (int?)q.QuizId).SingleOrDefaultAsync();
            return quizId == null ? null : $"/StudySets/Quiz/{quizId}?editId={id}";
        }
        if (e.TargetType == "Flashcard")
        {
            var setId = await db.Flashcards.Where(c => c.Id == id && c.DeletedAt == null && c.StudySet.DeletedAt == null)
                .Select(c => (int?)c.StudySetId).SingleOrDefaultAsync();
            return setId == null ? null : $"/Flashcards/Editor/{setId}";
        }
        return e.TargetType switch
        {
            "Classroom" when await db.Classrooms.AnyAsync(c => c.Id == id && c.DeletedAt == null) => $"/Classrooms/Details/{id}",
            "StudySet" when await db.StudySets.AnyAsync(s => s.Id == id && s.DeletedAt == null) => $"/Flashcards/Editor/{id}",
            "Quiz" when await db.Quizzes.AnyAsync(q => q.Id == id && q.DeletedAt == null) => $"/StudySets/Quiz/{id}",
            "Assignment" when await db.Assignments.AnyAsync(a => a.Id == id) => $"/Assignments/Details/{id}",
            "Attempt" when await db.Attempts.AnyAsync(a => a.Id == id) => $"/Assignments/Attempt/{id}",
            "ContentReport" when await db.ContentReports.AnyAsync(r => r.Id == id) => $"/Admin/Report/{id}",
            "ClassTransfer" when await db.ClassTransfers.AnyAsync(t => t.Id == id) => $"/ClassTransfers/Details/{id}",
            _ => null
        };
    }
}
