using Microsoft.EntityFrameworkCore;
using MindSprintAI_ASP.NET_MVC.Data;
using MindSprintAI_ASP.NET_MVC.Data.Entities;

namespace MindSprintAI_ASP.NET_MVC.Services;

public enum JoinOutcome { Joined, PendingApproval, AlreadyMember, OpenAssignment }

public record JoinResult(JoinOutcome Outcome, int? ClassroomId = null, int? AssignmentId = null);
public record MembershipConfirmation(int ClassroomId, int MemberId, bool Leaving, string Name, IReadOnlyList<string> AssignmentTitles);
public record ClassMemberName(string UserId, string DisplayName, ClassRole Role, string? Email = null);
public record ClassMemberNames(int ClassroomId, string ClassName, List<ClassMemberName> Members);
public record LecturerClassroomSummary(int ClassroomId, int Students, int PendingStudents, int OpenAssignments, int NeedsReview,
    ReviewWork? Work = null);

public record AssignmentWork(int ClassroomId, ReviewWork Work);

// Việc GV cần xem, tách theo loại để trang nói rõ là việc gì thay vì chỉ một con số.
public record ReviewWork(int JoinRequests = 0, int Strangers = 0, int Publication = 0, int Devices = 0, int Manual = 0)
{
    public int Total => JoinRequests + Strangers + Publication + Devices + Manual;

    public IEnumerable<string> Describe() => Parts().Select(p => p.Text);

    // Fragment là thẻ trong trang bài giao nơi xử lý việc đó (assignment-tabs.js mở theo #...).
    public IEnumerable<(string Text, string Fragment)> Parts()
    {
        if (JoinRequests > 0) yield return ($"{JoinRequests} học viên xin vào lớp", "class-pending");
        if (Manual > 0) yield return ($"{Manual} bài làm cần chấm tay", "theo-doi");
        if (Publication > 0) yield return ($"{Publication} học viên chờ công bố điểm", "cham-lai");
        if (Strangers > 0) yield return ($"{Strangers} người ngoài lớp chờ duyệt", "theo-doi");
        if (Devices > 0) yield return ($"{Devices} yêu cầu chuyển thiết bị", "chong-gian-lan");
    }

    public static ReviewWork operator +(ReviewWork a, ReviewWork b) => new(a.JoinRequests + b.JoinRequests,
        a.Strangers + b.Strangers, a.Publication + b.Publication, a.Devices + b.Devices, a.Manual + b.Manual);
}

public partial class ClassroomService(AppDbContext db, JoinCodeService codes, TimeProvider clock, ILearnNotifier notifier, InvitationService invitations)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<ClassMemberNames> MemberNamesAsync(int id, string userId)
    {
        var me = await GetActiveMembershipAsync(id, userId) ?? throw new ForbiddenException();
        // Email chỉ cho giảng viên (phân biệt hai học viên trùng tên); học viên không thấy email của nhau.
        var lecturer = me.Role != ClassRole.Student;
        var name = await db.Classrooms.Where(c => c.Id == id).Select(c => c.Name).SingleAsync();
        var members = await db.ClassMembers.Where(m => m.ClassroomId == id && m.Status == ClassMemberStatus.Active)
            .OrderBy(m => m.User.DisplayName).Select(m => new ClassMemberName(m.UserId, m.User.DisplayName, m.Role, lecturer ? m.User.Email : null)).ToListAsync();
        return new(id, name, members);
    }

    public async Task<Classroom> CreateAsync(string name, ClassJoinPolicy policy, string userId)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new BusinessException("Tên lớp không được để trống.");
        if (name.Trim().Length > 200) throw new BusinessException("Tên lớp tối đa 200 ký tự.");
        var room = new Classroom { Name = name.Trim(), JoinPolicy = policy };
        room.Members.Add(new ClassMember
        {
            UserId = userId, Role = ClassRole.MainLecturer, Status = ClassMemberStatus.Active,
            AddedAt = Now, JoinedAt = Now
        });
        await using var tx = db.Database.CurrentTransaction == null ? await db.Database.BeginTransactionAsync() : null; // (NK3)
        db.Classrooms.Add(room);
        db.Audit?.Add(AuditAction.ClassroomCreated, "Classroom", room.Id, userId, resolveTarget: () => room.Id.ToString());
        await db.SaveChangesAsync();
        await codes.IssueAsync(JoinCodeTarget.Classroom, room.Id);
        await db.SaveChangesAsync();
        if (tx != null) await tx.CommitAsync();
        return room;
    }

    public Task<bool> IsLecturerAsync(int classroomId, string userId) =>
        db.ClassMembers.AnyAsync(m => m.ClassroomId == classroomId && m.UserId == userId
            && m.Classroom.DeletedAt == null
            && m.Status == ClassMemberStatus.Active
            && (m.Role == ClassRole.MainLecturer || m.Role == ClassRole.AssistantLecturer));

    public async Task EnsureLecturerAsync(int classroomId, string userId)
    {
        if (!await IsLecturerAsync(classroomId, userId)) throw new ForbiddenException();
    }

    public Task<ClassMember?> GetActiveMembershipAsync(int classroomId, string userId) =>
        db.ClassMembers.FirstOrDefaultAsync(m => m.ClassroomId == classroomId && m.UserId == userId
            && m.Classroom.DeletedAt == null
            && m.Status == ClassMemberStatus.Active);

    public Task<List<ClassMember>> ListMyClassesAsync(string userId) =>
        db.ClassMembers.Include(m => m.Classroom)
            .Where(m => m.UserId == userId && m.Classroom.DeletedAt == null
                && (m.Status == ClassMemberStatus.Active || m.Status == ClassMemberStatus.PendingApproval || m.Status == ClassMemberStatus.Left))
            .OrderByDescending(m => m.AddedAt).ToListAsync();

    // Ba lượt SQL cho mọi lớp; không tải thành viên/lượt làm bài của từng lớp.
    public async Task<IReadOnlyDictionary<int, LecturerClassroomSummary>> GetLecturerSummariesAsync(string userId)
    {
        var now = Now;
        var teaching = db.ClassMembers.Where(m => m.UserId == userId && m.Classroom.DeletedAt == null
            && m.Status == ClassMemberStatus.Active
            && (m.Role == ClassRole.MainLecturer || m.Role == ClassRole.AssistantLecturer));
        var members = await teaching.Select(m => new
        {
            m.ClassroomId,
            Students = m.Classroom.Members.Count(s => s.Role == ClassRole.Student && s.Status == ClassMemberStatus.Active),
            Pending = m.Classroom.Members.Count(s => s.Role == ClassRole.Student && s.Status == ClassMemberStatus.PendingApproval)
        }).ToListAsync();
        var assignments = db.Assignments.AsNoTracking().Where(a => a.ClassroomId != null
            && teaching.Any(m => m.ClassroomId == a.ClassroomId) && a.ReportDeletedAt == null && a.ArchivedAt == null);
        var loaded = await LoadWorkAsync(assignments);
        var byClass = loaded.Select(x => x.Snapshot).GroupBy(a => a.ClassroomId!.Value).ToDictionary(g => g.Key,
            // Cùng IsOpen và điều kiện bắt đầu/hết giờ của AttemptService.GetTestStatusAsync, giờ chung (không cộng giờ riêng).
            g => g.Count(a => AssignmentService.IsOpen(a, now)
                && (a.Mode != AssignmentMode.Test || a.TestStartedAt != null && a.TestEndsAt > now)));
        var workByClass = loaded.GroupBy(x => x.Snapshot.ClassroomId!.Value).ToDictionary(g => g.Key, g => g.Aggregate(new ReviewWork(), (s, x) => s + x.Work));
        return members.ToDictionary(m => m.ClassroomId, m =>
        {
            var total = new ReviewWork(JoinRequests: m.Pending) + workByClass.GetValueOrDefault(m.ClassroomId, new ReviewWork());
            return new LecturerClassroomSummary(m.ClassroomId, m.Students, m.Pending, byClass.GetValueOrDefault(m.ClassroomId), total.Total, total);
        });
    }

    public Task<IReadOnlyDictionary<int, AssignmentWork>> GetAssignmentWorkAsync(int classroomId) =>
        GetAssignmentWorkAsync(db.Assignments.AsNoTracking().Where(a => a.ClassroomId == classroomId
            && a.ReportDeletedAt == null && a.ArchivedAt == null));

    // Việc chờ của từng bài giao (không gồm học viên xin vào lớp — việc đó thuộc lớp).
    public async Task<IReadOnlyDictionary<int, AssignmentWork>> GetAssignmentWorkAsync(IQueryable<Assignment> assignments) =>
        (await LoadWorkAsync(assignments)).ToDictionary(x => x.Snapshot.Id, x => new AssignmentWork(x.Snapshot.ClassroomId!.Value, x.Work));

    // Một lượt SQL cho bài giao (kèm giờ mở/hạn để tính bài đang mở) và một lượt cho câu chờ chấm tay.
    private async Task<List<(Assignment Snapshot, ReviewWork Work)>> LoadWorkAsync(IQueryable<Assignment> assignments)
    {
        var now = Now;
        var rows = await assignments.Select(a => new
        {
            Snapshot = new Assignment { Id = a.Id, ClassroomId = a.ClassroomId, Mode = a.Mode, Status = a.Status,
                OpensAt = a.OpensAt, DueAt = a.DueAt, TestStartedAt = a.TestStartedAt, TestEndsAt = a.TestEndsAt },
            Publication = a.Participants.Count(p => p.CompletionStatus == CompletionStatus.SubmittedAwaitingScore
                && a.Mode == AssignmentMode.Test && a.ScoresPublishedAt == null),
            Approval = a.Participants.Count(p => p.IsStranger && p.ApprovalStatus == ApprovalStatus.Pending),
            Devices = db.DeviceChangeRequests.Count(r => r.Attempt.Participant.AssignmentId == a.Id
                && r.Status == DeviceChangeStatus.Pending
                && (r.Attempt.Status == AttemptStatus.InProgress || r.Attempt.Status == AttemptStatus.Paused)
                && (r.Attempt.DeadlineAt == null || r.Attempt.DeadlineAt > now))
        }).ToListAsync();
        // ManualGradeAsync dùng loại câu trong bản chụp và câu trả lời cuối, không phải loại câu hiện tại.
        var pendingAnswers = await db.AnswerSubmissions.Where(s => s.IsFinal && s.ScoreExact == null && s.Score == null
            && s.AttemptQuestion.Attempt.Status == AttemptStatus.Ended
            && assignments.Any(a => a.Id == s.AttemptQuestion.Attempt.Participant.AssignmentId)
            && !(s.AttemptQuestion.Attempt.Participant.CompletionStatus == CompletionStatus.SubmittedAwaitingScore
                && s.AttemptQuestion.Attempt.Participant.Assignment.Mode == AssignmentMode.Test
                && s.AttemptQuestion.Attempt.Participant.Assignment.ScoresPublishedAt == null)
            && !s.AttemptQuestion.Submissions.Any(newer => newer.IsFinal
                && (newer.SubmittedAt > s.SubmittedAt || newer.SubmittedAt == s.SubmittedAt && newer.Id > s.Id)))
            .Select(s => new { s.AttemptQuestion.Attempt.Participant.AssignmentId,
                s.AttemptQuestion.Attempt.ParticipantId, s.AttemptQuestion.SnapshotJson }).ToListAsync();
        var manual = pendingAnswers.Where(s => QuestionSnapshot.FromJson(s.SnapshotJson).Type is QuestionType.TextInput or QuestionType.NumberInput)
            .Select(s => new { s.AssignmentId, s.ParticipantId }).Distinct().GroupBy(s => s.AssignmentId)
            .ToDictionary(g => g.Key, g => g.Count());
        return rows.Select(r => (r.Snapshot, new ReviewWork(Strangers: r.Approval, Publication: r.Publication,
            Devices: r.Devices, Manual: manual.GetValueOrDefault(r.Snapshot.Id)))).ToList();
    }

    public async Task<Classroom> GetForLecturerAsync(int classroomId, string userId)
    {
        await EnsureLecturerAsync(classroomId, userId);
        return await db.Classrooms
            .Include(c => c.Members).ThenInclude(m => m.User)
            .FirstAsync(c => c.Id == classroomId);
    }

    // Trang "Tham gia bằng mã": mã có thể là của lớp hoặc của bài giao (39).
    public async Task<JoinResult> JoinByCodeAsync(string? input, string userId)
    {
        var code = await codes.ResolveAsync(input);
        if (code.TargetType == JoinCodeTarget.Assignment)
        {
            var assignment = await db.Assignments.FirstOrDefaultAsync(a => a.Id == code.TargetId)
                ?? throw new BusinessException("Mã không tồn tại. Hãy hỏi giảng viên mã mới.");
            await JoinAssignmentAsync(assignment.Id, userId);
            return new JoinResult(JoinOutcome.OpenAssignment, assignment.ClassroomId, assignment.Id);
        }

        if (await db.Users.AnyAsync(u => u.Id == userId && u.IsGuest)) throw new ForbiddenException();

        await using var transaction = await db.Database.BeginTransactionAsync();
        var room = await ClassroomLock.RoomAsync(db, code.TargetId);
        var member = await db.ClassMembers.FirstOrDefaultAsync(m => m.ClassroomId == room.Id && m.UserId == userId);
        if (member?.Status == ClassMemberStatus.Active) return new JoinResult(JoinOutcome.AlreadyMember, room.Id);
        if (member?.Status == ClassMemberStatus.PendingApproval) return new JoinResult(JoinOutcome.PendingApproval, room.Id);

        if (member == null)
        {
            member = new ClassMember { ClassroomId = room.Id, UserId = userId, Role = ClassRole.Student, AddedAt = Now };
            db.ClassMembers.Add(member);
        }
        else
        {
            member.Role = ClassRole.Student;
            member.LeftAt = null;
        }

        if (member.Status == ClassMemberStatus.Removed || room.JoinPolicy == ClassJoinPolicy.RequireApproval)
        {
            member.Status = ClassMemberStatus.PendingApproval;
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            return new JoinResult(JoinOutcome.PendingApproval, room.Id);
        }
        member.Status = ClassMemberStatus.Active;
        member.JoinedAt = Now;
        db.Audit?.Add(AuditAction.ClassroomJoined, "Classroom", room.Id, userId); // (NK3)
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        await NotifyClassLecturersAsync(room.Id);
        return new JoinResult(JoinOutcome.Joined, room.Id);
    }

    private async Task JoinAssignmentAsync(int id, string userId)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        var a = await AttemptLock.LearnerAsync(db, id, userId);
        AssignmentAccess.EnsureJoinable(a, Now);
        var member = await AttemptLock.MemberForLearnerAsync(db, a.ClassroomId, userId);
        if (member?.Status is ClassMemberStatus.Left or ClassMemberStatus.Removed) throw new ForbiddenException();
        if (member?.Status == ClassMemberStatus.Active && AssignmentService.IsAssignedTo(a, member)) return;
        var p = await db.Participants.SingleOrDefaultAsync(p => p.AssignmentId == id && p.UserId == userId);
        // Đã tham gia giữ quyền khi mã/chính sách đổi; bị từ chối không tự xin lại bằng cùng mã.
        if (p?.ApprovalStatus == ApprovalStatus.Rejected) throw new BusinessException("Giảng viên đã từ chối yêu cầu tham gia.");
        if (p == null)
        {
            if (a.StrangerPolicy == StrangerPolicy.Reject) throw new BusinessException("Bài này chỉ dành cho thành viên lớp. Hãy vào lớp bằng mã vào lớp trước.");
            // Khách chỉ được tạo bởi GuestService, luôn có GuestId và bí mật trình duyệt.
            if (await db.Users.AnyAsync(u => u.Id == userId && u.IsGuest)) throw new ForbiddenException();
            p = new Participant { Assignment = a, UserId = userId, IsStranger = true, CreatedAt = Now,
                ApprovalStatus = a.StrangerPolicy == StrangerPolicy.RequireApproval ? ApprovalStatus.Pending : ApprovalStatus.NotRequired };
            db.Participants.Add(p);
            await db.SaveChangesAsync();
        }
        await tx.CommitAsync();
        await notifier.LecturersAsync(id);
    }

    public async Task DecideAsync(int memberId, bool approve, string lecturerId)
    {
        var member = await db.ClassMembers.FirstOrDefaultAsync(m => m.Id == memberId)
            ?? throw new BusinessException("Không tìm thấy người này.");
        await EnsureLecturerAsync(member.ClassroomId, lecturerId);
        if (member.Status != ClassMemberStatus.PendingApproval) throw new BusinessException("Người này không ở trạng thái chờ duyệt.");
        member.Status = approve ? ClassMemberStatus.Active : ClassMemberStatus.Removed;
        if (approve) member.JoinedAt = Now;
        db.Audit?.Add(approve ? AuditAction.MemberApproved : AuditAction.MemberRejected, "ClassMember", memberId, lecturerId); // (NK3)
        await db.SaveChangesAsync();
        if (approve) await NotifyClassLecturersAsync(member.ClassroomId);
    }

    public async Task<MembershipConfirmation?> RemoveMemberAsync(int memberId, string lecturerId, bool confirmed = false)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        var member = await db.ClassMembers.FirstOrDefaultAsync(m => m.Id == memberId)
            ?? throw new BusinessException("Không tìm thấy người này.");
        await LockClassAssignmentsAsync(member.ClassroomId);
        member = await AttemptLock.MemberAsync(db, member.ClassroomId, member.UserId)
            ?? throw new BusinessException("Không tìm thấy người này.");
        await EnsureLecturerAsync(member.ClassroomId, lecturerId);
        if (member.Role != ClassRole.Student) throw new BusinessException("Chỉ xóa được học viên.");
        if (member.Status == ClassMemberStatus.Removed) return null;
        var open = await LoadOpenAttemptsAsync(member);
        if (!confirmed && open.Any(a => a.Kind != AttemptKind.SelfReview && (a.DeadlineAt == null || a.DeadlineAt > Now)))
            return await ConfirmationAsync(member, open, leaving: false);
        var ended = await EndRemovedAttemptsAsync(open);
        member.Status = ClassMemberStatus.Removed;
        member.LeftAt = Now;
        db.Audit?.Add(AuditAction.MemberRemoved, "ClassMember", memberId, lecturerId); // (NK3)
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        await NotifyRemovedAsync(ended);
        await NotifyClassLecturersAsync(member.ClassroomId);
        return null;
    }

    public async Task<MembershipConfirmation?> LeaveAsync(int classroomId, string userId, bool confirmed = false)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        await LockClassAssignmentsAsync(classroomId);
        var member = await AttemptLock.MemberAsync(db, classroomId, userId)
            ?? throw new BusinessException("Bạn không ở trong lớp này.");
        if (member.Status == ClassMemberStatus.Left) return null;
        if (member.Status != ClassMemberStatus.Active) throw new BusinessException("Bạn không ở trong lớp này.");
        if (member.Role == ClassRole.MainLecturer) throw new BusinessException("Bạn là giảng viên chính nên chưa rời lớp được. Hãy chuyển lớp cho giảng viên khác trước (mục Chuyển lớp / Xóa lớp ở trang lớp).");
        var open = await LoadOpenAttemptsAsync(member);
        if (!confirmed && open.Any(a => a.Kind != AttemptKind.SelfReview && (a.DeadlineAt == null || a.DeadlineAt > Now)))
            return await ConfirmationAsync(member, open, leaving: true);
        var ended = await EndRemovedAttemptsAsync(open, leaving: true);
        member.Status = ClassMemberStatus.Left;
        member.LeftAt = Now;
        db.Audit?.Add(AuditAction.ClassroomLeft, "Classroom", classroomId, userId); // (NK3)
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        await NotifyRemovedAsync(ended);
        await NotifyClassLecturersAsync(classroomId);
        return null;
    }

    public async Task ChangeJoinPolicyAsync(int classroomId, ClassJoinPolicy policy, string lecturerId)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        var room = await ClassroomLock.RoomAsync(db, classroomId);
        await EnsureLecturerAsync(classroomId, lecturerId);
        if (!Enum.IsDefined(policy)) throw new BusinessException("Cách vào lớp không hợp lệ.");
        room.JoinPolicy = policy;
        db.Audit?.Add(AuditAction.ClassroomJoinPolicyChanged, "Classroom", classroomId, lecturerId);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    public async Task<JoinCode> ChangeCodeAsync(int classroomId, string lecturerId, bool undo = false)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        await ClassroomLock.RoomAsync(db, classroomId);
        await EnsureLecturerAsync(classroomId, lecturerId);
        var code = undo ? await codes.UndoAsync(JoinCodeTarget.Classroom, classroomId) : await codes.ReplaceAsync(JoinCodeTarget.Classroom, classroomId);
        db.Audit?.Add(AuditAction.ClassroomCodeChanged, "Classroom", classroomId, lecturerId); // (NK3)
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return code;
    }

    // (B4b) lấy mọi khóa bài trước thành viên, tránh đảo thứ tự với Start/Close.
    private async Task LockClassAssignmentsAsync(int classroomId)
    {
        await ClassroomLock.RoomAsync(db, classroomId);
        var ids = await db.Assignments.Where(a => a.ClassroomId == classroomId).OrderBy(a => a.Id).Select(a => a.Id).ToListAsync();
        foreach (var id in ids) await AttemptLock.AssignmentAsync(db, id);
    }

    private async Task<List<Attempt>> LoadOpenAttemptsAsync(ClassMember member)
    {
        var participants = await db.Participants.Where(p => p.Assignment.ClassroomId == member.ClassroomId && p.UserId == member.UserId)
            .OrderBy(p => p.Id).Select(p => p.Id).ToListAsync();
        var open = new List<Attempt>();
        var loaded = new List<Participant>();
        foreach (var id in participants)
            loaded.Add(await AttemptLock.ParticipantAsync(db, id));
        foreach (var p in loaded)
        {
            foreach (var a in p.Attempts.Where(AttemptRules.IsOpen).OrderBy(a => a.Id).ToList())
                open.Add(await AttemptLock.LoadAsync(db, a.Id));
        }
        return open;
    }

    private async Task<MembershipConfirmation> ConfirmationAsync(ClassMember member, List<Attempt> open, bool leaving)
    {
        var ids = open.Where(a => a.Kind != AttemptKind.SelfReview && (a.DeadlineAt == null || a.DeadlineAt > Now)).Select(a => a.Participant.AssignmentId).ToList();
        var titles = await db.Assignments.Where(a => ids.Contains(a.Id)).OrderBy(a => a.Id)
            .Select(a => (a.Quiz != null ? a.Quiz.Title : a.PaperTitle ?? "Thẻ ghi nhớ")).ToListAsync();
        var name = await db.Users.Where(u => u.Id == member.UserId).Select(u => u.DisplayName).SingleAsync();
        return new(member.ClassroomId, member.Id, leaving, name, titles);
    }

    private async Task<List<Attempt>> EndRemovedAttemptsAsync(List<Attempt> open, bool leaving = false)
    {
        var ended = new List<Attempt>();
        foreach (var a in open)
        {
            if (AttemptRules.ExpireIfDue(a, a.Participant, Now)) continue;
            AttemptRules.End(a, a.Participant, AttemptEndReason.RemovedFromClass, Now);
            if (a.Kind != AttemptKind.SelfReview)
            {
                if (leaving) a.Participant.CompletionLabels |= CompletionLabels.LeftClass; // (B4b)
                await CardCompletion.RefreshAsync(db, a.Participant, Now);
            }
            ended.Add(a);
        }
        return ended;
    }

    private Task NotifyRemovedAsync(IEnumerable<Attempt> attempts) => Task.WhenAll(attempts.Select(a =>
        notifier.AttemptAsync(a.Id, "removed", new LearnNotice(AttemptId: a.Id, Url: $"/Learn/Review/{a.Id}"))));

    private async Task NotifyClassLecturersAsync(int classroomId)
    {
        // (E) cả người chưa có lượt cũng biến mất khỏi bảng khi rời/bị xóa.
        foreach (var id in await db.Assignments.Where(a => a.ClassroomId == classroomId).Select(a => a.Id).ToListAsync())
            await notifier.LecturersAsync(id);
    }
}
