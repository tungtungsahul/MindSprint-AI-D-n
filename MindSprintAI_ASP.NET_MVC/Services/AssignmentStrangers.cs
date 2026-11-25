using Microsoft.EntityFrameworkCore;
using MindSprintAI_ASP.NET_MVC.Data.Entities;

namespace MindSprintAI_ASP.NET_MVC.Services;

public partial class AssignmentService
{
    public async Task<DateTimeOffset> GetGuestLifetimeAsync(int id)
    {
        var a = await db.Assignments.SingleAsync(a => a.Id == id);
        // Không hạn/phiên chưa bắt đầu: cookie dài hạn; quyền giờ bài vẫn kiểm ở service.
        return new DateTimeOffset((a.Mode == AssignmentMode.Test ? a.TestEndsAt : a.DueAt) ?? Now.AddDays(365));
    }

    public async Task ChangeStrangerPolicyAsync(int id, StrangerPolicy policy, string userId)
    {
        if (!Enum.IsDefined(policy)) throw new BusinessException("Chính sách người ngoài lớp không hợp lệ.");
        await using var tx = await db.Database.BeginTransactionAsync();
        var a = await AttemptLock.AssignmentAsync(db, id);
        await GetForLecturerAsync(id, userId);
        db.AssignmentChanges.Add(new() { AssignmentId = id, ChangedById = userId, ChangedAt = Now,
            Field = nameof(a.StrangerPolicy), OldValue = a.StrangerPolicy.ToString(), NewValue = policy.ToString() });
        a.StrangerPolicy = policy;
        db.Audit?.Add(AuditAction.AssignmentSettingsChanged, "Assignment", id, userId); // (NK3)
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        await notifier.LecturersAsync(id);
    }

    public async Task DecideStrangerAsync(int id, int participantId, bool approve, string userId)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        await AttemptLock.AssignmentAsync(db, id);
        await GetForLecturerAsync(id, userId);
        var p = await AttemptLock.ParticipantAsync(db, participantId);
        if (p.AssignmentId != id || !p.IsStranger) throw new ForbiddenException();
        if (p.ApprovalStatus != ApprovalStatus.Pending) throw new BusinessException("Yêu cầu này đã được xử lý.");
        p.ApprovalStatus = approve ? ApprovalStatus.Approved : ApprovalStatus.Rejected;
        db.Audit?.Add(approve ? AuditAction.StrangerApproved : AuditAction.StrangerRejected, "Participant", participantId, userId); // (NK3)
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        await notifier.StrangerAsync(p.UserId!, id);
        await notifier.LecturersAsync(id);
    }
}
