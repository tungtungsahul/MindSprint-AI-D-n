using Microsoft.EntityFrameworkCore;
using MindSprintAI_ASP.NET_MVC.Data;
using MindSprintAI_ASP.NET_MVC.Data.Entities;

namespace MindSprintAI_ASP.NET_MVC.Services;

public class LecturerPromotionService(AppDbContext db, NotificationService notifications, TimeProvider clock, InvitationService invitations)
{
    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow().UtcDateTime; var cutoff = now.AddDays(-30); var count = 0;
        var ids = await db.Classrooms.Where(c => c.DeletedAt == null && c.Members.Any(m => m.Role == ClassRole.MainLecturer
            && m.Status == ClassMemberStatus.Active && db.AccountLocks.Any(l => l.UserId == m.UserId && l.UnlockedAt == null
                && l.StartsAt < cutoff && (l.EndsAt == null || l.EndsAt > now))))
            .OrderBy(c => c.Id).Select(c => c.Id).ToListAsync(cancellationToken);
        foreach (var id in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
            var room = await ClassroomLock.RoomAsync(db, id);
            await ClassroomLock.AssignmentsAsync(db, id);
            var members = await ClassroomLock.MembersAsync(db, id);
            var main = members.Single(m => m.Role == ClassRole.MainLecturer && m.Status == ClassMemberStatus.Active);
            // (53, B5c) đọc lại khóa sau khi đã tuần tự hóa chuyển lớp; mở khóa không tự lấy lại vai chính.
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT [Id] FROM [AspNetUsers] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {main.UserId}", cancellationToken);
            if (!await db.AccountLocks.AnyAsync(l => l.UserId == main.UserId && l.UnlockedAt == null && l.StartsAt < cutoff
                && (l.EndsAt == null || l.EndsAt > now), cancellationToken)) continue;
            var next = members.Where(m => m.Role == ClassRole.AssistantLecturer && m.Status == ClassMemberStatus.Active)
                .OrderBy(m => m.AddedAt).ThenBy(m => m.Id).FirstOrDefault();
            if (next == null) continue;
            main.Role = ClassRole.AssistantLecturer;
            await db.SaveChangesAsync(cancellationToken);
            next.Role = ClassRole.MainLecturer;
            var recipients = members.Where(m => m.Status == ClassMemberStatus.Active && m.Role != ClassRole.Student).Select(m => m.UserId).ToHashSet();
            foreach (var uid in recipients)
                await notifications.AddAsync(uid, NotificationType.Promotion, new(uid == next.UserId
                    ? $"Lớp \"{room.Name}\": bạn đã được nâng làm giảng viên chính."
                    : $"Lớp \"{room.Name}\": đã nâng giảng viên phụ được thêm sớm nhất làm giảng viên chính.", $"/Classrooms/Details/{id}", id));
            var cancelled = await invitations.CancelForClassAsync(id);
            foreach (var pending in await db.ClassTransfers.Where(t => t.ClassroomId == id && t.Status == ClassTransferStatus.Pending
                && !db.Invitations.Any(i => i.TransferId == t.Id)).ToListAsync(cancellationToken))
            {
                pending.Status = ClassTransferStatus.Cancelled;
                foreach (var uid in new[] { pending.FromUserId, pending.ToUserId }.Distinct())
                {
                    await notifications.AddAsync(uid, NotificationType.Transfer,
                        new("Đề nghị chuyển lớp đã hủy vì giảng viên chính thay đổi.", $"/ClassTransfers/Details/{pending.Id}", id, pending.Id));
                    recipients.Add(uid);
                }
            }
            db.Audit?.Add(AuditAction.LecturerPromoted, "Classroom", id, null); // (NK3)
        await db.SaveChangesAsync(cancellationToken); await tx.CommitAsync(cancellationToken);
            await notifications.PushAsync(recipients.ToArray()); count++;
            foreach (var invitation in cancelled) await invitations.AfterFinishAsync(invitation);
        }
        return count;
    }
}

public sealed class LecturerPromotionWorker(IServiceScopeFactory scopes, TimeProvider clock, ILogger<LecturerPromotionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1), clock);
        while (!stoppingToken.IsCancellationRequested)
        {
            using var run = new WorkerRun(logger, nameof(LecturerPromotionWorker)); // (NK1)
            try
            {
                using var scope = scopes.CreateScope();
                scope.ServiceProvider.GetRequiredService<AuditLog>().Attach();
                run.Count = await scope.ServiceProvider.GetRequiredService<LecturerPromotionService>().RunAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex) { run.Failed = true; logger.LogWarning(ex, "Chưa kiểm tra được bàn giao GV bị khóa; sẽ thử lại sau."); }
            run.Dispose();
            try { if (!await timer.WaitForNextTickAsync(stoppingToken)) return; }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
        }
    }
}
