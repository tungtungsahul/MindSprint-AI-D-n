using Microsoft.EntityFrameworkCore;
using MindSprintAI_ASP.NET_MVC.Data.Entities;

namespace MindSprintAI_ASP.NET_MVC.Services;

public partial class ClassroomService
{
    public Task<bool> IsMainLecturerAsync(int id, string userId) => db.ClassMembers.AnyAsync(m => m.ClassroomId == id
        && m.Classroom.DeletedAt == null && m.UserId == userId && m.Role == ClassRole.MainLecturer && m.Status == ClassMemberStatus.Active);

    public async Task EnsureMainLecturerAsync(int id, string userId)
    {
        if (!await IsMainLecturerAsync(id, userId)) throw new ForbiddenException();
    }

    internal async Task<ApplicationUser> ConfirmedByEmailAsync(string email)
    {
        var normalized = (email ?? "").Trim().ToUpperInvariant();
        return await db.Users.SingleOrDefaultAsync(u => u.EmailConfirmed && u.Email != null && u.Email.ToUpper() == normalized)
            ?? throw new BusinessException("Không tìm thấy tài khoản với email này");
    }

    // (L1–L7) cùng sổ lời mời với chuyển lớp, chưa cấp quyền trước chấp nhận.
    public Task AddAssistantAsync(int id, string email, string userId) => invitations.InviteAssistantAsync(id, email, userId);

    // (mục 14) thông báo phải nói rõ lớp nào; người dùng có thể ở nhiều lớp.
    private Task<string> RoomNameAsync(int id) => db.Classrooms.Where(c => c.Id == id).Select(c => c.Name).SingleAsync();

    public async Task RemoveAssistantAsync(int id, int memberId, string userId)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        await ClassroomLock.RoomAsync(db, id);
        await ClassroomLock.AssignmentsAsync(db, id);
        var members = await ClassroomLock.MembersAsync(db, id);
        await EnsureMainLecturerAsync(id, userId);
        var member = members.SingleOrDefault(m => m.Id == memberId && m.Role == ClassRole.AssistantLecturer
            && m.Status == ClassMemberStatus.Active) ?? throw new BusinessException("Không tìm thấy giảng viên phụ.");
        member.Status = ClassMemberStatus.Removed; member.LeftAt = Now;
        var notices = new NotificationService(db, clock, notifier);
        await notices.AddAsync(member.UserId, NotificationType.Assistant, new($"Bạn đã được gỡ vai giảng viên phụ lớp {await RoomNameAsync(id)}.", "/Classrooms", ClassroomId: id));
        db.Audit?.Add(AuditAction.AssistantRemoved, "ClassMember", memberId, userId); // (NK3)
        await db.SaveChangesAsync(); await tx.CommitAsync();
        await notices.PushAsync(member.UserId);
    }

    public async Task DeleteAsync(int id, string userId)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        var room = await ClassroomLock.RoomAsync(db, id);
        var assignments = await ClassroomLock.AssignmentsAsync(db, id);
        await ClassroomLock.MembersAsync(db, id);
        await EnsureMainLecturerAsync(id, userId);
        // (31A, 48A) không xóa lớp còn bài mở; giữ toàn bộ kết quả qua xóa mềm.
        if (assignments.Any(a => a.Status == AssignmentStatus.Open && (a.Mode == AssignmentMode.Test
            ? a.TestEndsAt == null || a.TestEndsAt > Now : a.DueAt == null || a.DueAt > Now))
            || await db.Attempts.AnyAsync(at => at.Participant.Assignment.ClassroomId == id
                && at.Kind != AttemptKind.SelfReview
                && (at.Status == AttemptStatus.InProgress || at.Status == AttemptStatus.Paused)
                && (at.DeadlineAt == null || at.DeadlineAt > Now)))
            throw new BusinessException("Chưa xóa được lớp vì còn bài giao đang mở hoặc học viên đang làm bài (kể cả bài ôn tập không có hạn). Hãy đóng các bài đó trước.");
        room.DeletedAt = Now;
        var cancelled = await invitations.CancelForClassAsync(id);
        foreach (var pending in await db.ClassTransfers.Where(t => t.ClassroomId == id && t.Status == ClassTransferStatus.Pending
            && !db.Invitations.Any(i => i.TransferId == t.Id)).ToListAsync())
            pending.Status = ClassTransferStatus.Cancelled;
        db.Audit?.Add(AuditAction.ClassroomDeleted, "Classroom", id, userId); // (NK3)
        await db.SaveChangesAsync(); await tx.CommitAsync();
        foreach (var invitation in cancelled) await invitations.AfterFinishAsync(invitation);
    }

}
