using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MindSprintAI_ASP.NET_MVC.Data;
using MindSprintAI_ASP.NET_MVC.Data.Entities;

namespace MindSprintAI_ASP.NET_MVC.Services;

public record ReportContext(ReportTarget TargetType, string TargetId, int? AssignmentId = null, long? AttemptId = null, int? ClassroomId = null);
public record ReportCard(string Front, string Back, string? Example = null);
public record ReportedAiMessage(string Role, string Content, List<string> Citations);
public record ReportedContent(string Title, List<QuestionSnapshot> Questions, List<ReportCard> Cards,
    List<ReportedAiMessage>? Messages = null);
public record ReportDetails(ContentReport Report, ReportedContent Content);
public record ReportQueueRow(int Id, ReportTarget TargetType, string Reason, ReportStatus Status, DateTime CreatedAt);

public class ContentReportService(AppDbContext db, TimeProvider clock, AssignmentService assignments)
{
    public static readonly string[] Reasons = ["Nội dung không phù hợp", "Thông tin sai lệch", "Quấy rối hoặc xúc phạm", "Vi phạm bản quyền", "Khác"];
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    private async Task<(string Owner, int? Set, ReportedContent Content)> TargetAsync(ReportContext context, string reporterId)
    {
        if (!Enum.IsDefined(context.TargetType) || string.IsNullOrEmpty(context.TargetId) || context.TargetId.Length > 128) throw new ForbiddenException();
        if (context.TargetType == ReportTarget.AiConversation)
        {
            if (!int.TryParse(context.TargetId, out var cid) || cid.ToString() != context.TargetId) throw new ForbiddenException();
            var conversation = await db.AiConversations.AsNoTracking().Include(c => c.Messages.OrderBy(m => m.Id)).ThenInclude(m => m.Citations)
                .SingleOrDefaultAsync(c => c.Id == cid && c.UserId == reporterId && c.DeletedAt == null) ?? throw new ForbiddenException();
            if (await db.Users.AnyAsync(u => u.Id == reporterId && u.IsGuest)) throw new ForbiddenException();
            await StudySetLearningAccess.EnsureNotTakingTestAsync(db, conversation.StudySetId, reporterId);
            // 51C: chụp đúng hội thoại bị báo cáo, không key/hội thoại khác; mỗi lần xem đi qua ViewAsync.
            return (reporterId, null, new(conversation.Title, [], [], conversation.Messages.Select(m =>
                new ReportedAiMessage(m.Role, m.Content, m.Citations.Select(c => c.ExcerptText).ToList())).ToList()));
        }
        if (context.TargetType == ReportTarget.User)
        {
            if (context.TargetId == reporterId) throw new BusinessException("Không báo cáo tên của chính mình.");
            var room = context.ClassroomId;
            if (context.AssignmentId is { } aid)
                room = await db.Assignments.Where(a => a.Id == aid).Select(a => a.ClassroomId).SingleOrDefaultAsync();
            var requester = await db.ClassMembers.SingleOrDefaultAsync(m => m.ClassroomId == room && m.UserId == reporterId && m.Status == ClassMemberStatus.Active);
            var visible = requester != null && (await db.ClassMembers.AnyAsync(m => m.ClassroomId == room && m.UserId == context.TargetId && m.Status == ClassMemberStatus.Active)
                || requester.Role != ClassRole.Student && context.AssignmentId != null
                    && await db.Participants.AnyAsync(p => p.AssignmentId == context.AssignmentId && p.UserId == context.TargetId));
            if (!visible) throw new ForbiddenException();
            var target = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == context.TargetId) ?? throw new ForbiddenException();
            return (target.Id, null, new(target.DisplayName, [], []));
        }
        var a = await db.Assignments.Include(a => a.Quiz).ThenInclude(q => q!.StudySet)
            .SingleOrDefaultAsync(a => a.Id == context.AssignmentId) ?? throw new ForbiddenException();
        var member = await db.ClassMembers.AsNoTracking().SingleOrDefaultAsync(m => m.ClassroomId == a.ClassroomId && m.UserId == reporterId && m.Status == ClassMemberStatus.Active);
        if (!(member != null && (member.Role != ClassRole.Student || AssignmentService.IsAssignedTo(a, member)))
            && !await db.Participants.AnyAsync(p => p.AssignmentId == a.Id && p.UserId == reporterId)) throw new ForbiddenException();
        var set = a.Quiz?.StudySet ?? await db.StudySets.SingleOrDefaultAsync(s => s.Id == a.StudySetId) ?? throw new ForbiddenException();
        if (set.OwnerId == reporterId) throw new BusinessException("Không báo cáo nội dung của chính mình.");
        if (!int.TryParse(context.TargetId, out var targetId) || targetId.ToString() != context.TargetId) throw new ForbiddenException();
        if (context.TargetType == ReportTarget.Question)
        {
            // (51C, B1) đúng câu đã xem trong lượt của mình; không nhận ID câu tùy ý của quiz khác.
            var q = await db.AttemptQuestions.AsNoTracking().Include(q => q.Attempt).ThenInclude(x => x.Participant)
                .SingleOrDefaultAsync(q => q.AttemptId == context.AttemptId && q.QuestionId == targetId
                    && q.Attempt.Participant.UserId == reporterId && q.Attempt.Participant.AssignmentId == a.Id
                    && q.Attempt.Status != AttemptStatus.InProgress && q.Attempt.Status != AttemptStatus.Paused);
            if (q == null) throw new ForbiddenException();
            return (set.OwnerId, set.Id, new("Câu hỏi bị báo cáo", [QuestionSnapshot.FromJson(q.SnapshotJson)], []));
        }
        if (context.TargetType == ReportTarget.Quiz && (a.QuizId != targetId || a.Quiz == null)
            || context.TargetType == ReportTarget.StudySet && set.Id != targetId) throw new ForbiddenException();
        // (51C) chỉ phần quiz/thẻ được chia qua bài giao này, không mở các quiz khác cùng bộ học.
        var questions = a.QuizId == null ? [] : await db.Questions.AsNoTracking().Include(q => q.Options)
            .Include(q => q.Blanks).ThenInclude(b => b.Answers).Where(q => q.QuizId == a.QuizId).OrderBy(q => q.OrderIndex).ToListAsync();
        var cards = context.TargetType == ReportTarget.StudySet && CardCompletion.HasCards(a)
            ? await db.Flashcards.AsNoTracking().Where(c => c.StudySetId == set.Id && c.DeletedAt == null).OrderBy(c => c.OrderIndex).Select(c => new ReportCard(c.Front, c.Back, c.Example)).ToListAsync() : [];
        return (set.OwnerId, set.Id, new(context.TargetType == ReportTarget.StudySet ? set.Title : a.Quiz!.Title, questions.Select(QuestionSnapshot.From).ToList(), cards));
    }

    // Trả về tên nội dung bị báo (tên hiển thị, tên bộ học…) để trang báo cáo nói rõ đang báo ai/cái gì.
    public async Task<string> ValidateTargetAsync(ReportContext context, string reporterId) => (await TargetAsync(context, reporterId)).Content.Title;

    public async Task<string> PendingOwnerAsync(int id, string adminId)
    {
        await AdminService.EnsureAsync(db, adminId, Now);
        return await db.ContentReports.Where(r => r.Id == id && r.Status == ReportStatus.Pending).Select(r => r.TargetOwnerId).SingleOrDefaultAsync()
            ?? throw new BusinessException("Báo cáo đã xử lý hoặc không tồn tại.");
    }

    public async Task<int> SubmitAsync(ReportContext context, string? reason, string? note, string reporterId)
    {
        if (!Reasons.Contains(reason)) throw new BusinessException("Hãy chọn một lý do báo cáo.");
        if (note?.Length > 1000) throw new BusinessException("Ghi chú tối đa 1000 ký tự.");
        var target = await TargetAsync(context, reporterId);
        await using var tx = await db.Database.BeginTransactionAsync();
        // (51C) hai tab cùng báo cáo cũng gộp vào một dòng đang chờ.
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT [Id] FROM [ContentReports] WITH (UPDLOCK, HOLDLOCK) WHERE [ReporterId] = {reporterId} AND [TargetType] = {(int)context.TargetType} AND [TargetId] = {context.TargetId} AND [Status] = 1");
        var row = await db.ContentReports.SingleOrDefaultAsync(r => r.ReporterId == reporterId && r.TargetType == context.TargetType && r.TargetId == context.TargetId && r.Status == ReportStatus.Pending);
        if (row == null)
        {
            row = new() { ReporterId = reporterId, TargetType = context.TargetType, TargetId = context.TargetId,
                TargetOwnerId = target.Owner, StudySetId = target.Set, ContentJson = JsonSerializer.Serialize(target.Content), CreatedAt = Now };
            db.ContentReports.Add(row);
        }
        row.Reason = reason!;
        row.Note = note?.Trim();
        db.Audit?.Add(AuditAction.ContentReported, "ContentReport", row.Id, reporterId, resolveTarget: () => row.Id.ToString()); // (NK3)
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return row.Id;
    }

    public async Task<List<ReportQueueRow>> QueueAsync(string adminId, int page = 1)
    {
        await AdminService.EnsureAsync(db, adminId, Now);
        if (db.Audit != null) await db.Audit.WriteSeparateAsync(AuditAction.AdminListedReports, "ContentReport", "", adminId); // (NK3)
        return await db.ContentReports.AsNoTracking().OrderBy(r => r.Status).ThenByDescending(r => r.CreatedAt)
            .Skip((Math.Clamp(page, 1, 100000) - 1) * 20).Take(20)
            .Select(r => new ReportQueueRow(r.Id, r.TargetType, r.Reason, r.Status, r.CreatedAt)).ToListAsync();
    }

    public async Task<ReportDetails> ViewAsync(int reportId, string adminId)
    {
        await AdminService.EnsureAsync(db, adminId, Now);
        var row = await db.ContentReports.AsNoTracking().SingleOrDefaultAsync(r => r.Id == reportId) ?? throw new BusinessException("Không tìm thấy báo cáo.");
        db.AdminAccessLogs.Add(new() { AdminId = adminId, ContentReportId = reportId, ViewedAt = Now });
        db.Audit?.Add(AuditAction.AdminViewedReport, "ContentReport", reportId, adminId); // (NK3)
        await db.SaveChangesAsync();
        return new(row, JsonSerializer.Deserialize<ReportedContent>(row.ContentJson)!);
    }

    public async Task HandleAsync(int reportId, bool hide, string? reason, string adminId)
    {
        var closed = new List<Assignment>();
        var ended = new List<Attempt>();
        await using var tx = await db.Database.BeginTransactionAsync();
        await AdminService.EnsureAsync(db, adminId, Now);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT [Id] FROM [ContentReports] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {reportId}");
        var row = await db.ContentReports.SingleOrDefaultAsync(r => r.Id == reportId) ?? throw new BusinessException("Không tìm thấy báo cáo.");
        await db.Entry(row).ReloadAsync();
        if (row.Status != ReportStatus.Pending) throw new BusinessException("Báo cáo đã được xử lý.");
        if (hide)
        {
            if (row.StudySetId == null) throw new BusinessException("Tên hiển thị chỉ xử lý bằng bác bỏ hoặc khóa vi phạm.");
            if (string.IsNullOrWhiteSpace(reason) || reason.Length > 500) throw new BusinessException("Nhập lý do ẩn, tối đa 500 ký tự.");
            // Khóa bộ học TRƯỚC các bài (như giao bài / chuyển lớp): bài giao mới không lọt khỏi danh sách đóng (51C).
            // Học viên tự ôn thẻ không còn khóa dòng bộ học (AttemptLock.PersonalCardsAsync) nên thứ tự này không vướng học viên.
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT [Id] FROM [StudySets] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {row.StudySetId}");
            var set = await db.StudySets.SingleAsync(s => s.Id == row.StudySetId);
            await db.Entry(set).ReloadAsync();
            // (51C) khóa toàn bộ bài theo Id trước khi khóa người làm/lượt.
            var ids = await db.Assignments.Where(a => a.StudySetId == set.Id && a.Status != AssignmentStatus.Closed)
                .OrderBy(a => a.Id).Select(a => a.Id).ToListAsync();
            foreach (var id in ids)
            {
                var a = await AttemptLock.AssignmentAsync(db, id);
                if (a.Status != AssignmentStatus.Closed) closed.Add(a);
            }
            foreach (var a in closed)
                ended.AddRange(await assignments.CloseCoreAsync(a, CloseReason.HiddenByAdmin));
            set.HiddenByAdminAt ??= Now;
            set.HiddenReason ??= reason.Trim();
        }
        row.Status = hide ? ReportStatus.Handled : ReportStatus.Dismissed;
        row.HandledById = adminId; row.HandledAt = Now;
        db.Audit?.Add(hide ? AuditAction.ContentHidden : AuditAction.ReportDismissed, "ContentReport", reportId, adminId); // (NK3)
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        await assignments.NotifyClosedAsync(ended);
        foreach (var a in closed) await assignments.NotifyLecturersAsync(a.Id);
    }

    public Task LockReportedUserAsync(int reportId, string? reason, DateTime? endsAt, string adminId, AccountLockService locks) =>
        locks.LockAsync("", AccountLockKind.Violation, reason, endsAt, adminId, reportId);
}
