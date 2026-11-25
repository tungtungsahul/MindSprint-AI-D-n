using Microsoft.EntityFrameworkCore;
using MindSprintAI_ASP.NET_MVC.Data;
using MindSprintAI_ASP.NET_MVC.Data.Entities;

namespace MindSprintAI_ASP.NET_MVC.Services;

internal static class AssignmentAccess
{
    internal static bool Approved(Participant? p) => p?.IsStranger == true
        && p.ApprovalStatus is ApprovalStatus.NotRequired or ApprovalStatus.Approved;

    internal static bool CanStart(Assignment a, ClassMember? m, Participant? p) =>
        m?.Status is not (ClassMemberStatus.Left or ClassMemberStatus.Removed)
        && (m?.Status == ClassMemberStatus.Active && AssignmentService.IsAssignedTo(a, m) || Approved(p));

    internal static async Task EnsureStartAsync(AppDbContext db, Assignment a, ClassMember? m, string userId)
    {
        var p = await db.Participants.SingleOrDefaultAsync(p => p.AssignmentId == a.Id && p.UserId == userId);
        if (p?.ApprovalStatus == ApprovalStatus.Pending) throw new BusinessException("Chờ giảng viên duyệt");
        if (p?.ApprovalStatus == ApprovalStatus.Rejected) throw new ForbiddenException();
        if (!CanStart(a, m, p)) throw new ForbiddenException();
    }

    internal static void EnsureJoinable(Assignment a, DateTime now)
    {
        if (a.Status == AssignmentStatus.Closed || (a.Mode == AssignmentMode.Test ? a.TestEndsAt : a.DueAt) <= now)
            throw new BusinessException("Bài đã kết thúc.");
    }
}
