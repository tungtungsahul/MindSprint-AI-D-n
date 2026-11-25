using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MindSprintAI_ASP.NET_MVC.Data;
using MindSprintAI_ASP.NET_MVC.Data.Entities;

namespace MindSprintAI_ASP.NET_MVC.Services;

public class ClassTransferService(AppDbContext db, ClassroomService classrooms, StudySetCloneService clones,
    NotificationService notifications, TimeProvider clock, InvitationService invitations)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<ClassTransfer> GetAsync(int id, string userId)
    {
        var row = await db.ClassTransfers.AsNoTracking().Include(t => t.Classroom).SingleOrDefaultAsync(t => t.Id == id);
        if (row == null || row.FromUserId != userId && row.ToUserId != userId) throw new ForbiddenException();
        return row;
    }

    public Task<ClassTransfer> OfferAsync(int id, string email, bool stays, string userId) => OfferCoreAsync(id, email, stays, userId, false);
    public Task<ClassTransfer> AdminOfferAsync(int id, string email, string adminId) => OfferCoreAsync(id, email, true, adminId, true);

    private async Task<ClassTransfer> OfferCoreAsync(int id, string email, bool stays, string userId, bool admin)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        await ClassroomLock.RoomAsync(db, id);
        await ClassroomLock.AssignmentsAsync(db, id);
        var members = await ClassroomLock.MembersAsync(db, id);
        var main = members.Single(m => m.Role == ClassRole.MainLecturer && m.Status == ClassMemberStatus.Active);
        if (admin)
        {
            await AdminService.EnsureAsync(db, userId, Now);
            if (!await EligibleForAdminAsync(id, main.UserId)) throw new BusinessException("Lớp chưa đủ điều kiện chuyển thay giảng viên bị khóa quá 30 ngày.");
        }
        else await classrooms.EnsureMainLecturerAsync(id, userId);
        var receiver = await invitations.FindRecipientAsync(InvitationService.Normalize(email ?? ""));
        if (receiver != null)
        {
            if (receiver.Id == main.UserId) throw new BusinessException("Người nhận phải khác giảng viên chính.");
            RefuseStudent(members.SingleOrDefault(m => m.UserId == receiver.Id));
        }
        var invite = await invitations.CreateAsync(id, email ?? "", userId, InvitationKind.Transfer, stays);
        invite.TransferFromUserId = main.UserId; // Admin có thể gửi thay; vẫn giữ đúng GV chính gốc.
        await invitations.NotifySentAsync(invite);
        db.Audit?.Add(admin ? AuditAction.AdminTransferOffered : AuditAction.TransferOffered, "Classroom", id, userId); // (NK3)
        if (receiver == null)
        {
            await db.SaveChangesAsync(); await tx.CommitAsync();
            await invitations.AfterInviteAsync(invite);
            // (L3) chưa có tài khoản: chưa tạo chuyển lớp, nhận qua trang lời mời chung.
            return new ClassTransfer { ClassroomId = id };
        }
        var row = new ClassTransfer { ClassroomId = id, FromUserId = main.UserId, ToUserId = receiver.Id,
            FromUserStays = stays, CreatedAt = Now, ExpiresAt = invite.ExpiresAt, InvitedEmail = invite.NormalizedEmail };
        db.ClassTransfers.Add(row);
        await db.SaveChangesAsync();
        invite.TransferId = row.Id; invite.RecipientId = receiver.Id;
        await invitations.NotifyRecipientAsync(invite, receiver);
        await db.SaveChangesAsync(); await tx.CommitAsync();
        await invitations.AfterInviteAsync(invite);
        return row;
    }

    public async Task CancelAsync(int id, string userId)
    {
        var header = await GetAsync(id, userId);
        await using var tx = await db.Database.BeginTransactionAsync();
        await ClassroomLock.RoomAsync(db, header.ClassroomId);
        var row = await LoadPendingAsync(id);
        if (row.FromUserId != userId) throw new ForbiddenException();
        await classrooms.EnsureMainLecturerAsync(row.ClassroomId, userId);
        row.Status = ClassTransferStatus.Cancelled;
        var invitation = await FinishInvitationAsync(row, InvitationStatus.Revoked, "Đề nghị chuyển lớp đã được hủy.");
        db.Audit?.Add(AuditAction.TransferCancelled, "ClassTransfer", id, userId); // (NK3)
        await db.SaveChangesAsync(); await tx.CommitAsync();
        await notifications.PushAsync(row.FromUserId, row.ToUserId);
        if (invitation != null) await invitations.AfterFinishAsync(invitation);
    }

    public async Task DecideAsync(int id, bool accept, string userId)
    {
        var header = await GetAsync(id, userId);
        if (header.ToUserId != userId) throw new ForbiddenException();
        await using var tx = await db.Database.BeginTransactionAsync();
        await ClassroomLock.RoomAsync(db, header.ClassroomId);
        var row = await LoadPendingAsync(id);
        if (row.ToUserId != userId) throw new ForbiddenException();
        var invitation = await db.Invitations.SingleOrDefaultAsync(i => i.TransferId == row.Id);
        if (invitation != null) await invitations.EnsureRecipientAsync(invitation, userId);
        else if (!await db.Users.AnyAsync(u => u.Id == userId && !u.IsGuest && u.EmailConfirmed
            && (row.InvitedEmail == null || u.Email != null && u.Email.ToUpper() == row.InvitedEmail))) throw new BusinessException(InvitationService.WrongRecipient);
        if ((row.ExpiresAt ?? row.CreatedAt.AddDays(7)) <= Now)
        {
            row.Status = ClassTransferStatus.Expired;
            invitation = await FinishInvitationAsync(row, InvitationStatus.Expired, "Đề nghị chuyển lớp đã hết hạn sau 7 ngày.");
            await db.SaveChangesAsync(); await tx.CommitAsync();
            await notifications.PushAsync(row.FromUserId, row.ToUserId);
            if (invitation != null) await invitations.AfterFinishAsync(invitation);
            throw new BusinessException("Đề nghị chuyển lớp đã hết hạn sau 7 ngày.");
        }
        if (!accept)
        {
            row.Status = ClassTransferStatus.Declined;
            invitation = await FinishInvitationAsync(row, InvitationStatus.Declined, "Người nhận đã từ chối chuyển lớp.");
        }
        else
        {
            // (49B, C3, 51C) khóa soạn quiz/bộ học trước các bài; tránh vòng chờ với sửa đề/ẩn nội dung.
            var used = await db.Assignments.Where(a => a.ClassroomId == row.ClassroomId)
                .Select(a => new { a.Id, SetId = a.StudySetId ?? a.Quiz!.StudySetId }).ToListAsync();
            var setByAssignment = used.ToDictionary(a => a.Id, a => a.SetId);
            var setIds = used.Select(a => a.SetId).Distinct().ToArray();
            foreach (var qid in await db.Quizzes.Where(q => setIds.Contains(q.StudySetId)).OrderBy(q => q.Id).Select(q => q.Id).ToListAsync())
                await QuizChangeLock.AcquireAsync(db, qid);
            foreach (var sid in used.Select(a => a.SetId).Distinct().Order()) await clones.LockSetAsync(sid);
            var assignments = await ClassroomLock.AssignmentsAsync(db, row.ClassroomId);
            var members = await ClassroomLock.MembersAsync(db, row.ClassroomId);
            var sender = members.SingleOrDefault(m => m.UserId == row.FromUserId && m.Role == ClassRole.MainLecturer
                && m.Status == ClassMemberStatus.Active) ?? throw new BusinessException("Giảng viên chính đã thay đổi. Hãy gửi đề nghị mới.");
            var receiver = members.SingleOrDefault(m => m.UserId == userId);
            RefuseStudent(receiver);
            if (!await db.Users.AnyAsync(u => u.Id == userId && u.EmailConfirmed)) throw new BusinessException(InvitationService.WrongRecipient);
            await AccountLockState.EnsureAllowedAsync(db, userId, Now);
            foreach (var sid in used.Select(a => a.SetId).Distinct().Order())
            {
                var classAssignments = assignments.Where(a => setByAssignment[a.Id] == sid).ToList();
                var selection = new CloneSelection(classAssignments.Where(a => a.QuizId != null).Select(a => a.QuizId!.Value).Distinct().ToArray(),
                    classAssignments.Any(a => a.Form != AssignmentForm.QuizOnly), MatchingSettings: true);
                var copy = await clones.CopyAsync(sid, userId, selection, transfer: true);
                await db.SaveChangesAsync();
                foreach (var assignment in classAssignments)
                {
                    // Bài đã đóng dùng quiz đã xóa giữ tham chiếu lịch sử, không nhân bản quiz đã xóa.
                    if (assignment.QuizId is { } oldQuizId && copy.Quizzes.TryGetValue(oldQuizId, out var quiz))
                    {
                        await CopyRegradesAsync(assignment, quiz);
                        assignment.QuizId = quiz.Id; assignment.Quiz = quiz;
                    }
                    assignment.StudySetId = copy.Set.Id;
                    var progress = await db.FlashcardProgresses.Where(p => p.Participant != null && p.Participant.AssignmentId == assignment.Id).ToListAsync();
                    foreach (var p in progress)
                        if (copy.Cards.TryGetValue(p.FlashcardId, out var card)) p.FlashcardId = card.Id;
                }
            }
            // (48A) nhả vai cũ trước khi gán vai mới để không vi phạm unique GV chính.
            sender.Role = ClassRole.AssistantLecturer;
            sender.Status = row.FromUserStays ? ClassMemberStatus.Active : ClassMemberStatus.Left;
            sender.LeftAt = row.FromUserStays ? null : Now;
            await db.SaveChangesAsync();
            if (receiver == null)
            {
                receiver = new ClassMember { ClassroomId = row.ClassroomId, UserId = userId, AddedAt = Now };
                db.ClassMembers.Add(receiver);
            }
            receiver.Role = ClassRole.MainLecturer; receiver.Status = ClassMemberStatus.Active;
            receiver.JoinedAt ??= Now; receiver.LeftAt = null;
            row.Status = ClassTransferStatus.Accepted; row.AcceptedAt = Now;
            invitation = await FinishInvitationAsync(row, InvitationStatus.Accepted, "Người nhận đã nhận lớp; bài giao và kết quả được giữ nguyên.");
        }
        db.Audit?.Add(accept ? AuditAction.TransferAccepted : AuditAction.TransferDeclined, "ClassTransfer", id, userId); // (NK3)
        await db.SaveChangesAsync(); await tx.CommitAsync();
        await notifications.PushAsync(row.FromUserId, row.ToUserId);
        if (invitation != null) await invitations.AfterFinishAsync(invitation);
    }

    private async Task<Invitation?> FinishInvitationAsync(ClassTransfer row, InvitationStatus status, string text)
    {
        var invite = await db.Invitations.SingleOrDefaultAsync(i => i.TransferId == row.Id);
        if (invite != null) await invitations.FinishAsync(invite, status);
        else
        {
            await NotifyBothAsync(row, text);
            // (L2, L7) đề nghị có từ trước migration vẫn gửi email kết quả cho người mời.
            invite = new Invitation { Kind = InvitationKind.Transfer, ClassroomId = row.ClassroomId, InviterId = row.FromUserId,
                RecipientId = row.ToUserId, Status = status, CreatedAt = row.CreatedAt, ExpiresAt = row.ExpiresAt ?? row.CreatedAt.AddDays(7) };
        }
        return invite;
    }

    public async Task DecideInvitationAsync(long id, bool accept, string userId)
    {
        var header = await db.Invitations.AsNoTracking().SingleOrDefaultAsync(i => i.Id == id) ?? throw new ForbiddenException();
        int transferId;
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            await ClassroomLock.RoomAsync(db, header.ClassroomId);
            var row = await invitations.LoadAsync(id);
            await invitations.EnsureRecipientAsync(row, userId);
            if (row.Kind != InvitationKind.Transfer || row.Status != InvitationStatus.Pending) throw new BusinessException("Lời mời đã được xử lý.");
            if (row.ExpiresAt <= Now || !accept)
            {
                await invitations.FinishAsync(row, row.ExpiresAt <= Now ? InvitationStatus.Expired : InvitationStatus.Declined);
                if (row.ExpiresAt > Now) db.Audit?.Add(AuditAction.TransferDeclined, "Invitation", id, userId); // (NK3)
                await db.SaveChangesAsync(); await tx.CommitAsync(); await invitations.AfterFinishAsync(row);
                if (row.ExpiresAt <= Now) throw new BusinessException("Lời mời đã hết hạn sau 7 ngày.");
                return;
            }
            if (row.TransferId == null)
            {
                // (L3, L4) tạo đề nghị thường; phần nhận/chuyển dữ liệu chỉ có DecideAsync.
                var fromUserId = row.TransferFromUserId ?? row.InviterId;
                await invitations.EnsureMainAsync(row.ClassroomId, fromUserId);
                RefuseStudent(await db.ClassMembers.SingleOrDefaultAsync(m => m.ClassroomId == row.ClassroomId && m.UserId == userId));
                if (fromUserId == userId) throw new BusinessException("Người nhận phải khác giảng viên chính.");
                if (await db.ClassTransfers.AnyAsync(t => t.ClassroomId == row.ClassroomId && t.Status == ClassTransferStatus.Pending))
                    throw new BusinessException("Lớp đang có một yêu cầu chuyển chờ nhận.");
                var transfer = new ClassTransfer { ClassroomId = row.ClassroomId, FromUserId = fromUserId, ToUserId = userId,
                    FromUserStays = row.FromUserStays, CreatedAt = row.CreatedAt, ExpiresAt = row.ExpiresAt, InvitedEmail = row.NormalizedEmail };
                db.ClassTransfers.Add(transfer); await db.SaveChangesAsync(); row.TransferId = transfer.Id; row.RecipientId = userId;
                await db.SaveChangesAsync();
            }
            transferId = row.TransferId.Value;
            await tx.CommitAsync();
        }
        await DecideAsync(transferId, accept, userId);
    }

    public async Task ExpireLegacyAsync()
    {
        var cutoff = Now.AddDays(-7);
        foreach (var id in await db.ClassTransfers.Where(t => t.Status == ClassTransferStatus.Pending
            && (t.ExpiresAt <= Now || t.ExpiresAt == null && t.CreatedAt <= cutoff)
            && !db.Invitations.Any(i => i.TransferId == t.Id)).OrderBy(t => t.Id).Select(t => t.Id).Take(200).ToListAsync())
        {
            var header = await db.ClassTransfers.AsNoTracking().SingleAsync(t => t.Id == id);
            await using var tx = await db.Database.BeginTransactionAsync();
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT [Id] FROM [Classrooms] WITH (UPDLOCK, HOLDLOCK) WHERE [Id]={header.ClassroomId}");
            var row = await db.ClassTransfers.SingleAsync(t => t.Id == id); await db.Entry(row).ReloadAsync();
            if (row.Status != ClassTransferStatus.Pending) continue;
            row.Status = ClassTransferStatus.Expired;
            var invitation = await FinishInvitationAsync(row, InvitationStatus.Expired, "Đề nghị chuyển lớp đã hết hạn sau 7 ngày.");
            await db.SaveChangesAsync(); await tx.CommitAsync();
            if (invitation != null) await invitations.AfterFinishAsync(invitation);
        }
    }

    private async Task CopyRegradesAsync(Assignment a, Quiz quiz)
    {
        // (49B) lượt cũ dùng khóa cũ; lượt mới dùng Id câu/lựa chọn của bản sao.
        foreach (var r in a.Regrades.ToList())
        {
            var source = await db.Questions.AsNoTracking().Include(q => q.Options).SingleAsync(q => q.Id == r.QuestionId);
            if (source.QuizId != a.QuizId) continue;
            var q = quiz.Questions.Single(x => x.OrderIndex == source.OrderIndex);
            var key = JsonSerializer.Deserialize<int[]>(r.NewCorrectJson ?? "[]") ?? [];
            var mapped = q.Options.Where(o => source.Options.Any(old => key.Contains(old.Id) && old.OrderIndex == o.OrderIndex)).Select(o => o.Id).Order().ToArray();
            a.Regrades.Add(new Regrade { QuestionId = q.Id, Method = r.Method, Reason = r.Reason, ById = r.ById, At = r.At,
                ClientRequestId = Guid.NewGuid(), NewCorrectJson = r.NewCorrectJson == null ? null : JsonSerializer.Serialize(mapped) });
        }
    }

    private async Task<ClassTransfer> LoadPendingAsync(int id)
    {
        var row = await db.ClassTransfers.SingleAsync(t => t.Id == id);
        await db.Entry(row).ReloadAsync();
        if (row.Status != ClassTransferStatus.Pending) throw new BusinessException("Đề nghị chuyển lớp đã được xử lý.");
        return row;
    }

    private static void RefuseStudent(ClassMember? m)
    {
        if (m?.Role == ClassRole.Student && m.Status is ClassMemberStatus.Active or ClassMemberStatus.Invited or ClassMemberStatus.PendingApproval)
            throw new BusinessException("Người nhận đang là học viên của lớp. Hãy chọn tài khoản khác.");
    }

    private async Task NotifyBothAsync(ClassTransfer row, string text)
    {
        // (mục 14) ghi tên lớp để người nhận biết đề nghị nào.
        var room = await db.Classrooms.Where(c => c.Id == row.ClassroomId).Select(c => c.Name).SingleAsync();
        foreach (var uid in new[] { row.FromUserId, row.ToUserId }.Distinct())
            await notifications.AddAsync(uid, NotificationType.Transfer, new($"Lớp \"{room}\": {text}", $"/ClassTransfers/Details/{row.Id}", row.ClassroomId, row.Id));
    }

    internal Task<bool> EligibleForAdminAsync(int id, string mainId) => db.AccountLocks.AnyAsync(l => l.UserId == mainId
        && l.UnlockedAt == null && l.StartsAt < Now.AddDays(-30) && (l.EndsAt == null || l.EndsAt > Now)
        && !db.ClassMembers.Any(m => m.ClassroomId == id && m.Status == ClassMemberStatus.Active && m.Role == ClassRole.AssistantLecturer));

    public async Task<List<Classroom>> AdminClassesAsync(string adminId)
    {
        await AdminService.EnsureAsync(db, adminId, Now);
        if (db.Audit != null) await db.Audit.WriteSeparateAsync(AuditAction.AdminListedTransfers, "Classroom", "", adminId); // (NK3)
        var cutoff = Now.AddDays(-30); var now = Now;
        return await db.Classrooms.Where(c => c.DeletedAt == null && c.Members.Any(m => m.Role == ClassRole.MainLecturer
            && m.Status == ClassMemberStatus.Active && db.AccountLocks.Any(l => l.UserId == m.UserId && l.UnlockedAt == null
                && l.StartsAt < cutoff && (l.EndsAt == null || l.EndsAt > now)))
            && !c.Members.Any(m => m.Role == ClassRole.AssistantLecturer && m.Status == ClassMemberStatus.Active))
            .OrderBy(c => c.Id).ToListAsync();
    }
}
