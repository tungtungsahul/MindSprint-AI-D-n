using System.Net;
using Microsoft.EntityFrameworkCore;
using MindSprintAI_ASP.NET_MVC.Data;
using MindSprintAI_ASP.NET_MVC.Data.Entities;

namespace MindSprintAI_ASP.NET_MVC.Services;

internal sealed class ReminderCandidate
{
    public string UserId { get; set; } = "";
    public long TargetId { get; set; }
    public string Text { get; set; } = "";
    public string Url { get; set; } = "";
}

public class ReminderService(AppDbContext db, TimeProvider clock, NotificationService notifications, IEmailSender email,
    EmailLinks links, InvitationService invitations, ClassTransferService transfers, ILogger<ReminderService> logger)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    private IQueryable<ReminderCandidate> Candidates(ReminderKind kind)
    {
        var now = Now; var tomorrow = now.AddHours(24); var yesterday = now.AddHours(-24);
        var lecturers = db.ClassMembers.Where(m => m.Status == ClassMemberStatus.Active
            && (m.Role == ClassRole.MainLecturer || m.Role == ClassRole.AssistantLecturer) && m.Classroom.DeletedAt == null);
        switch (kind)
        {
            case ReminderKind.SrsDue:
                // (D5c) worker tạo sau 07:00 VN; tổng hợp trên SQL trước khi lấy trang hữu hạn.
                var date = VnTime.Date(now);
                var target = date.Year * 10000L + date.Month * 100 + date.Day;
                var srs = new SrsDueService(db, clock);
                var counts = srs.PersonalCards().Concat(srs.AssignmentCards()).GroupBy(c => c.UserId)
                    .Select(g => new { UserId = g.Key, Count = g.Count() });
                return from u in db.Users.Where(u => now.AddHours(7).Hour >= 7 && !u.IsGuest && u.SrsBellEnabled
                        && (u.LockoutEnd == null || u.LockoutEnd <= clock.GetUtcNow())
                        && !db.AccountLocks.Any(l => l.UserId == u.Id && l.UnlockedAt == null && l.StartsAt <= now && (l.EndsAt == null || l.EndsAt > now)))
                    join count in counts on u.Id equals count.UserId
                    select new ReminderCandidate { UserId = u.Id, TargetId = target,
                        Text = "Hôm nay bạn có " + count.Count + " thẻ đến hạn ôn", Url = "/StudySets" };
            case ReminderKind.Due:
                // (N1) gồm người chưa mở bài (chưa có Participant), theo cùng chính sách giao bài.
                var due = db.Assignments.Where(a => a.Status == AssignmentStatus.Open && a.DueAt != null && a.DueAt > now
                    && a.DueAt <= tomorrow && (a.ClassroomId == null || a.Classroom!.DeletedAt == null));
                return (from a in due
                    from u in db.Users.Where(u => !u.IsGuest && (
                        db.Participants.Any(p => p.AssignmentId == a.Id && p.UserId == u.Id && p.GuestId == null
                            && (p.ApprovalStatus == ApprovalStatus.Approved || p.ApprovalStatus == ApprovalStatus.NotRequired)
                            && (p.CompletionStatus == CompletionStatus.NotStarted || p.CompletionStatus == CompletionStatus.InProgress || p.CompletionStatus == CompletionStatus.Paused))
                        || !db.Participants.Any(p => p.AssignmentId == a.Id && p.UserId == u.Id)
                            && db.ClassMembers.Any(m => m.ClassroomId == a.ClassroomId && m.UserId == u.Id && m.Role == ClassRole.Student
                                && m.Status == ClassMemberStatus.Active && (a.AudiencePolicy == AudiencePolicy.IncludeLateJoiners || m.JoinedAt <= a.CreatedAt))))
                    select new ReminderCandidate { UserId = u.Id, TargetId = a.Id,
                        Text = "Bài \"" + ((a.Quiz != null ? a.Quiz.Title : a.PaperTitle ?? "Thẻ ghi nhớ")) + "\" sắp đến hạn nộp (còn dưới 24 giờ).", Url = "/Learn/Assignment/" + a.Id });
            case ReminderKind.ClassApproval:
                return from request in db.ClassMembers.Where(m => m.Status == ClassMemberStatus.PendingApproval && m.CreatedAt < yesterday)
                    from lecturer in lecturers.Where(m => m.ClassroomId == request.ClassroomId)
                    select new ReminderCandidate { UserId = lecturer.UserId, TargetId = request.Id,
                        Text = "Lớp \"" + request.Classroom.Name + "\": có yêu cầu vào lớp chờ duyệt quá 24 giờ.", Url = "/Classrooms/Details/" + request.ClassroomId };
            case ReminderKind.ParticipantApproval:
                return from request in db.Participants.Where(p => p.ApprovalStatus == ApprovalStatus.Pending && p.CreatedAt < yesterday)
                    from lecturer in lecturers.Where(m => m.ClassroomId == request.Assignment.ClassroomId)
                    select new ReminderCandidate { UserId = lecturer.UserId, TargetId = request.Id,
                        Text = "Bài \"" + ((request.Assignment.Quiz != null ? request.Assignment.Quiz.Title : request.Assignment.PaperTitle ?? "Thẻ ghi nhớ")) + "\": có yêu cầu vào bài chờ duyệt quá 24 giờ.", Url = "/Assignments/Details/" + request.AssignmentId };
            case ReminderKind.Retake:
                return from request in db.RetakeRequests.Where(r => r.Status == RetakeRequestStatus.Pending && r.CreatedAt < yesterday)
                    from lecturer in lecturers.Where(m => m.ClassroomId == request.Participant.Assignment.ClassroomId)
                    select new ReminderCandidate { UserId = lecturer.UserId, TargetId = request.Id,
                        Text = "Bài \"" + ((request.Participant.Assignment.Quiz != null ? request.Participant.Assignment.Quiz.Title : request.Participant.Assignment.PaperTitle ?? "Thẻ ghi nhớ")) + "\": có yêu cầu làm lại chờ duyệt quá 24 giờ.", Url = "/Assignments/Details/" + request.Participant.AssignmentId };
            case ReminderKind.Invitation:
                return from invite in db.Invitations.Where(i => i.Status == InvitationStatus.Pending && i.CreatedAt < yesterday
                        && i.ExpiresAt > now && i.Classroom.DeletedAt == null)
                    from user in db.Users.Where(u => !u.IsGuest && u.EmailConfirmed && u.Email != null && u.Email.ToUpper() == invite.NormalizedEmail)
                    select new ReminderCandidate { UserId = user.Id, TargetId = invite.Id,
                        Text = "Lớp \"" + invite.Classroom.Name + "\": có lời mời chờ bạn trả lời quá 24 giờ.", Url = "/Invitations" };
            default:
                return from transfer in db.ClassTransfers.Where(t => t.Status == ClassTransferStatus.Pending && t.CreatedAt < yesterday
                        && (t.ExpiresAt ?? t.CreatedAt.AddDays(7)) > now && t.Classroom.DeletedAt == null && !db.Invitations.Any(i => i.TransferId == t.Id))
                    select new ReminderCandidate { UserId = transfer.ToUserId, TargetId = transfer.Id,
                        Text = "Lớp \"" + transfer.Classroom.Name + "\": có đề nghị chuyển lớp chờ bạn trả lời quá 24 giờ.", Url = "/ClassTransfers/Details/" + transfer.Id };
        }
    }

    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        var processed = 0; // (NK1) Số thông báo tạo mới + nhóm email đã xét.
        // (T8) xóa theo tập hợp, chạy lại an toàn; đồng thời thu hồi các phiên quá 180 ngày.
        var cutoff = Now.AddDays(-180);
        await db.KnownDevices.Where(d => d.LastSeenAt < cutoff).ExecuteDeleteAsync(cancellationToken);
        await invitations.ExpireAsync(); await transfers.ExpireLegacyAsync();
        // (L3) chỉ duyệt nhóm có lời mời chưa báo và email đã xác minh, không tải toàn bộ users.
        var recipients = await (from i in db.Invitations.Where(i => i.Status == InvitationStatus.Pending && i.ExpiresAt > Now && i.RecipientNotifiedAt == null
                                   && i.Classroom.DeletedAt == null)
            from u in db.Users.Where(u => !u.IsGuest && u.EmailConfirmed && u.Email != null && u.Email.ToUpper() == i.NormalizedEmail)
            select u.Id).Distinct().OrderBy(id => id).Take(200).ToListAsync(cancellationToken);
        foreach (var id in recipients) await invitations.RevealAsync(id);

        foreach (var kind in Enum.GetValues<ReminderKind>())
        {
            do
            {
                cancellationToken.ThrowIfCancellationRequested();
                List<ReminderCandidate> items;
                await using (var tx = await db.Database.BeginTransactionAsync(cancellationToken))
                {
                    await Tang7cLock.AcquireAsync(db, "reminder:create");
                    items = await Candidates(kind).Where(c => !db.Reminders.Any(r => r.UserId == c.UserId && r.Kind == kind && r.TargetId == c.TargetId))
                        .OrderBy(c => c.TargetId).ThenBy(c => c.UserId).Take(200).ToListAsync(cancellationToken);
                    foreach (var item in items)
                    {
                        // (D5c) khóa duy nhất (user, kind, ngày VN), đặt đã email và vẫn loại khỏi mọi truy vấn digest.
                        db.Reminders.Add(new() { UserId = item.UserId, Kind = kind, TargetId = item.TargetId, Text = item.Text, Url = item.Url,
                            CreatedAt = Now, EmailedAt = kind == ReminderKind.SrsDue ? Now : null });
                        await notifications.AddAsync(item.UserId, NotificationType.Reminder, new(item.Text, item.Url));
                    }
                    await db.SaveChangesAsync(cancellationToken); await tx.CommitAsync(cancellationToken);
                }
                await notifications.PushAsync(items.Select(i => i.UserId).Distinct().ToArray());
                processed += items.Count;
                if (kind != ReminderKind.SrsDue || items.Count < 200) break;
            } while (!cancellationToken.IsCancellationRequested);
        }

        var today = Now.AddHours(7).Date;
        // (N4–N6) lọc trên SQL trước Take, khóa/opt-out không chiếm chỗ các tài khoản khác.
        string? after = null;
        while (!cancellationToken.IsCancellationRequested)
        {
            var page = await EmailUsers().Where(u => (after == null || string.Compare(u.Id, after) > 0)
                && db.Reminders.Any(r => r.UserId == u.Id && r.EmailedAt == null && r.Kind != ReminderKind.SrsDue)
                && !db.ReminderDigests.Any(d => d.UserId == u.Id && d.VietnamDate == today))
                .OrderBy(u => u.Id).Select(u => u.Id).Take(100).ToListAsync(cancellationToken);
            if (page.Count == 0) break;
            foreach (var userId in page) await DigestAsync(userId, today, cancellationToken);
            processed += page.Count;
            after = page[^1];
        }
        return processed;
    }

    private IQueryable<ApplicationUser> EmailUsers() => db.Users.Where(u => !u.IsGuest && u.EmailConfirmed && u.Email != null
        && u.EmailRemindersEnabled && (u.LockoutEnd == null || u.LockoutEnd <= clock.GetUtcNow())
        && !db.AccountLocks.Any(l => l.UserId == u.Id && l.UnlockedAt == null && l.StartsAt <= Now && (l.EndsAt == null || l.EndsAt > Now)));

    private IQueryable<Reminder> RelevantReminders(ReminderKind kind, string userId)
    {
        var current = Candidates(kind);
        return db.Reminders.Where(r => r.UserId == userId && r.Kind == kind && r.Kind != ReminderKind.SrsDue && r.EmailedAt == null
            && current.Any(c => c.UserId == r.UserId && c.TargetId == r.TargetId));
    }

    private async Task DigestAsync(string userId, DateTime today, CancellationToken cancellationToken)
    {
        List<Reminder> items; string address;
        await using (var tx = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            await Tang7cLock.AcquireAsync(db, "digest:" + userId);
            var user = await EmailUsers().AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId, cancellationToken);
            if (user == null || await db.ReminderDigests.AnyAsync(d => d.UserId == userId && d.VietnamDate == today, cancellationToken)) return;
            items = [];
            foreach (var kind in Enum.GetValues<ReminderKind>().Where(k => k != ReminderKind.SrsDue))
            {
                long after = 0;
                while (true)
                {
                    var page = await RelevantReminders(kind, userId).Where(r => r.Id > after).OrderBy(r => r.Id).Take(200).ToListAsync(cancellationToken);
                    items.AddRange(page);
                    if (page.Count < 200) break;
                    after = page[^1].Id;
                }
            }
            // (D5) thẻ SRS không kích hoạt digest và không xuất hiện trong nội dung email.
            if (items.Count == 0) return;
            address = user.Email!;
            // (N4) đặt chỗ trước khi enqueue: crash/gửi lỗi không tạo email thứ hai cùng ngày.
            db.ReminderDigests.Add(new() { UserId = userId, VietnamDate = today });
            await db.SaveChangesAsync(cancellationToken); await tx.CommitAsync(cancellationToken);
        }
        try
        {
            var body = "<p>Các việc đang chờ bạn trên " + Brand.Name + ":</p><ul>" + string.Concat(items.Select(r =>
                $"<li><a href=\"{WebUtility.HtmlEncode(links.To(r.Url))}\">{WebUtility.HtmlEncode(r.Text)}</a></li>"))
                + $"</ul><p><a href=\"{WebUtility.HtmlEncode(links.To("/Account"))}\">Tài khoản — tắt email nhắc việc</a></p>";
            await email.SendAsync(address, $"Nhắc việc hôm nay · {Brand.Name}", body);
            var now = Now;
            foreach (var batch in items.Select(r => r.Id).Chunk(200))
                await db.Reminders.Where(r => batch.Contains(r.Id)).ExecuteUpdateAsync(s => s.SetProperty(r => r.EmailedAt, now), cancellationToken);
        }
        catch { logger.LogWarning("Không gửi được email nhắc việc; giữ mục chưa gửi cho ngày sau."); }
    }

    public async Task SetEmailEnabledAsync(string userId, bool enabled)
    {
        await using var tx = await db.Database.BeginTransactionAsync(); // (NK3)
        if (await db.Users.Where(u => u.Id == userId && !u.IsGuest).ExecuteUpdateAsync(s => s.SetProperty(u => u.EmailRemindersEnabled, enabled)) != 1)
            throw new ForbiddenException();
        db.Audit?.Add(AuditAction.NotificationSettingsChanged, "User", userId, userId);
        await db.SaveChangesAsync(); await tx.CommitAsync();
    }

    public async Task SetSrsBellEnabledAsync(string userId, bool enabled)
    {
        await using var tx = await db.Database.BeginTransactionAsync(); // (NK3)
        // (D5c) không thay đổi tùy chọn email.
        if (await db.Users.Where(u => u.Id == userId && !u.IsGuest).ExecuteUpdateAsync(s => s.SetProperty(u => u.SrsBellEnabled, enabled)) != 1)
            throw new ForbiddenException();
        db.Audit?.Add(AuditAction.NotificationSettingsChanged, "User", userId, userId);
        await db.SaveChangesAsync(); await tx.CommitAsync();
    }

    public async Task SetLecturerBellEnabledAsync(string userId, NotificationType type, bool enabled)
    {
        await using var tx = await db.Database.BeginTransactionAsync(); // (NK3)
        // (F11, 38A) mọi tài khoản không phải khách đều có thể chuyển sang chế độ GV.
        var users = db.Users.Where(u => u.Id == userId && !u.IsGuest);
        var count = type switch
        {
            NotificationType.DeviceChange => await users.ExecuteUpdateAsync(s => s.SetProperty(u => u.DeviceChangeBellEnabled, enabled)),
            NotificationType.ScreenLeave => await users.ExecuteUpdateAsync(s => s.SetProperty(u => u.ScreenLeaveBellEnabled, enabled)),
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };
        if (count != 1) throw new ForbiddenException();
        db.Audit?.Add(AuditAction.NotificationSettingsChanged, "User", userId, userId);
        await db.SaveChangesAsync(); await tx.CommitAsync();
    }
}

public sealed class ReminderWorker(IServiceScopeFactory scopes, TimeProvider clock, IConfiguration config, ILogger<ReminderWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var minutes = Math.Clamp(config.GetValue("Reminders:IntervalMinutes", 15), 1, 1440);
        while (!stoppingToken.IsCancellationRequested)
        {
            using var run = new WorkerRun(logger, nameof(ReminderWorker)); // (NK1)
            try
            {
                using var scope = scopes.CreateScope();
                scope.ServiceProvider.GetRequiredService<AuditLog>().Attach();
                run.Count = await scope.ServiceProvider.GetRequiredService<ReminderService>().RunAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch { run.Failed = true; logger.LogWarning("Chưa xử lý được lượt nhắc việc; sẽ thử ở lượt sau."); }
            run.Dispose();
            try { await Task.Delay(TimeSpan.FromMinutes(minutes), clock, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
