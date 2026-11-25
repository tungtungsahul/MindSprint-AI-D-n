using Microsoft.EntityFrameworkCore;
using MindSprintAI_ASP.NET_MVC.Data;
using MindSprintAI_ASP.NET_MVC.Data.Entities;

namespace MindSprintAI_ASP.NET_MVC.Services;

public record UnhideRequestDetails(UnhideRequest Request, List<Flashcard> Cards);

public class UnhideRequestService(AppDbContext db, StudyLibraryService library, NotificationService notifications, TimeProvider clock)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<int> RequestAsync(int studySetId, string? message, string userId)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        await LockSetAsync(studySetId);
        var set = await library.GetOwnedStudySetAsync(studySetId, userId);
        await db.Entry(set).ReloadAsync();
        if (set.HiddenByAdminAt == null) throw new BusinessException("Bộ học này không bị ẩn.");
        message = message?.Trim();
        if (string.IsNullOrEmpty(message) || message.Length > 1000)
            throw new BusinessException("Hãy viết vài dòng cho quản trị viên: bạn đã sửa gì, hoặc vì sao bạn thấy việc ẩn là nhầm (tối đa 1000 ký tự).");
        if (await db.UnhideRequests.AnyAsync(r => r.StudySetId == studySetId && r.Status == UnhideRequestStatus.Pending))
            throw new BusinessException("Bạn đã gửi yêu cầu, đang chờ quản trị viên xem.");
        var row = new UnhideRequest { StudySetId = studySetId, RequesterId = userId, Message = message, CreatedAt = Now };
        db.UnhideRequests.Add(row);
        db.Audit?.Add(AuditAction.UnhideRequested, "UnhideRequest", row.Id, userId, resolveTarget: () => row.Id.ToString());
        await db.SaveChangesAsync();
        var now = Now;
        var admins = await db.Users.Where(u => u.IsSystemAdmin && !u.IsGuest && !db.AccountLocks.Any(l =>
            l.UserId == u.Id && l.UnlockedAt == null && l.StartsAt <= now && (l.EndsAt == null || l.EndsAt > now)))
            .Select(u => u.Id).ToArrayAsync();
        foreach (var admin in admins)
            await notifications.AddAsync(admin, NotificationType.ContentReview,
                new($"Có yêu cầu xem xét lại bộ học “{set.Title}”.", $"/Admin/UnhideRequest/{row.Id}"));
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        await notifications.PushAsync(admins);
        return row.Id;
    }

    public async Task<List<UnhideRequest>> PendingAsync(string adminId)
    {
        await AdminService.EnsureAsync(db, adminId, Now);
        return await db.UnhideRequests.AsNoTracking().Include(r => r.StudySet).Include(r => r.Requester)
            .Where(r => r.Status == UnhideRequestStatus.Pending).OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id).ToListAsync();
    }

    public async Task<int> PendingCountAsync(string adminId)
    {
        await AdminService.EnsureAsync(db, adminId, Now);
        return await db.UnhideRequests.CountAsync(r => r.Status == UnhideRequestStatus.Pending);
    }

    public async Task<UnhideRequestDetails> GetAsync(int id, string adminId)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        await AdminService.EnsureAsync(db, adminId, Now);
        var row = await db.UnhideRequests.AsNoTracking().Include(r => r.Requester)
            .Include(r => r.StudySet).ThenInclude(s => s.Quizzes.Where(q => q.DeletedAt == null && q.ArchivedAt == null))
                .ThenInclude(q => q.Questions.Where(q => q.DeletedAt == null)).ThenInclude(q => q.Options)
            .Include(r => r.StudySet).ThenInclude(s => s.Quizzes).ThenInclude(q => q.Questions)
                .ThenInclude(q => q.Blanks).ThenInclude(b => b.Answers)
            .SingleOrDefaultAsync(r => r.Id == id) ?? throw new BusinessException("Không tìm thấy yêu cầu.");
        var cards = await db.Flashcards.AsNoTracking().Where(c => c.StudySetId == row.StudySetId && c.DeletedAt == null)
            .OrderBy(c => c.OrderIndex).ToListAsync();
        // Bộ học được ẩn qua báo cáo vi phạm; giữ liên kết nhật ký truy cập hiện có, không sửa cột cũ.
        var reportId = await db.ContentReports.Where(r => r.StudySetId == row.StudySetId && r.Status == ReportStatus.Handled)
            .OrderByDescending(r => r.HandledAt).ThenByDescending(r => r.Id).Select(r => (int?)r.Id).FirstOrDefaultAsync()
            ?? throw new BusinessException("Không tìm thấy báo cáo đã ẩn bộ học.");
        db.AdminAccessLogs.Add(new() { AdminId = adminId, ContentReportId = reportId, ViewedAt = Now });
        db.Audit?.Add(AuditAction.AdminViewedReport, "UnhideRequest", id, adminId);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return new(row, cards);
    }

    public async Task DecideAsync(int id, bool approve, string? note, string adminId)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        await AdminService.EnsureAsync(db, adminId, Now);
        var setId = await db.UnhideRequests.Where(r => r.Id == id).Select(r => (int?)r.StudySetId).SingleOrDefaultAsync()
            ?? throw new BusinessException("Không tìm thấy yêu cầu.");
        // Cùng thứ tự khóa với gửi yêu cầu: bộ học trước, yêu cầu sau.
        await LockSetAsync(setId);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT [Id] FROM [UnhideRequests] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {id}");
        var row = await db.UnhideRequests.SingleAsync(r => r.Id == id);
        await db.Entry(row).ReloadAsync();
        if (row.Status != UnhideRequestStatus.Pending) throw new BusinessException("Yêu cầu này đã được xử lý.");
        note = note?.Trim();
        if (!approve && (string.IsNullOrEmpty(note) || note.Length > 500))
            throw new BusinessException("Hãy ghi lý do từ chối (tối đa 500 ký tự).");
        if (note?.Length > 500) throw new BusinessException("Ghi chú tối đa 500 ký tự.");
        var set = await db.StudySets.SingleAsync(s => s.Id == setId);
        await db.Entry(set).ReloadAsync();
        if (approve) { set.HiddenByAdminAt = null; set.HiddenReason = null; }
        row.Status = approve ? UnhideRequestStatus.Approved : UnhideRequestStatus.Rejected;
        row.DecidedById = adminId; row.DecidedAt = Now; row.DecisionNote = note;
        db.Audit?.Add(approve ? AuditAction.ContentUnhidden : AuditAction.UnhideRejected, "UnhideRequest", id, adminId);
        await notifications.AddAsync(row.RequesterId, NotificationType.ContentReview,
            new(approve ? $"Bộ học “{set.Title}” đã được bỏ ẩn. Bạn giao bài lại được; các bài giao đã đóng lúc ẩn vẫn đóng."
                : $"Yêu cầu bỏ ẩn bộ học “{set.Title}” chưa được chấp nhận: {note}", $"/StudySets/Details/{set.Id}"));
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        await notifications.PushAsync(row.RequesterId);
    }

    public async Task<UnhideRequest?> LatestOwnedAsync(int studySetId, string userId)
    {
        await library.GetOwnedStudySetAsync(studySetId, userId);
        return await db.UnhideRequests.AsNoTracking().Where(r => r.StudySetId == studySetId)
            .OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id).FirstOrDefaultAsync();
    }

    public Task<List<int>> PendingOwnedSetIdsAsync(string userId) => db.UnhideRequests.AsNoTracking()
        .Where(r => r.StudySet.OwnerId == userId && r.Status == UnhideRequestStatus.Pending).Select(r => r.StudySetId).ToListAsync();

    private Task LockSetAsync(int id) => db.Database.ExecuteSqlInterpolatedAsync(
        $"SELECT [Id] FROM [StudySets] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {id}");
}
