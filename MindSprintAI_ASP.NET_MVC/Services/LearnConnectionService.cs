using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using MindSprintAI_ASP.NET_MVC.Data;
using MindSprintAI_ASP.NET_MVC.Data.Entities;
using MindSprintAI_ASP.NET_MVC.Hubs;

namespace MindSprintAI_ASP.NET_MVC.Services;

public sealed class LearnConnectionService(AppDbContext db, AttemptService attempts, TimeProvider clock,
    LearnConnectionTracker tracker, IHubContext<LearnHub> hub, ClassroomService classrooms, ILearnNotifier notifier)
{
    public async Task JoinLecturerAsync(string connectionId, int assignmentId, string userId)
    {
        var classroomId = await db.Assignments.Where(a => a.Id == assignmentId).Select(a => a.ClassroomId).SingleOrDefaultAsync();
        if (classroomId == null || !await classrooms.IsLecturerAsync(classroomId.Value, userId)) throw new ForbiddenException();
        await hub.Groups.AddToGroupAsync(connectionId, SignalRLearnNotifier.LecturersGroup(assignmentId));
    }

    public async Task JoinAttemptAsync(string connectionId, long attemptId, string userId, string? deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId)) throw new BusinessException("Không nhận dạng được thiết bị. Hãy tải lại trang.");
        await using var tx = await db.Database.BeginTransactionAsync();
        var a = await attempts.LoadForLearnerAsync(attemptId, userId, writing: true, deviceOverride: deviceId);
        if (!AttemptRules.IsOpen(a))
        {
            await tx.CommitAsync();
            await attempts.NotifyLiveChangesAsync();
            throw new BusinessException("Lượt này đã kết thúc.");
        }
        // (69, B4a) cùng khóa hàng với chuyển máy/ngắt kết nối để không ghi đè trạng thái mới.
        tracker.Add(connectionId, a.Id, deviceId!);
        try
        {
            await hub.Groups.AddToGroupAsync(connectionId, SignalRLearnNotifier.AttemptGroup(a.Id));
            a.DisconnectedAt = null;
            a.Status = AttemptStatus.InProgress;
            await db.SaveChangesAsync();
            await tx.CommitAsync();
        }
        catch
        {
            tracker.Remove(connectionId);
            await hub.Groups.RemoveFromGroupAsync(connectionId, SignalRLearnNotifier.AttemptGroup(a.Id));
            throw;
        }
        await notifier.LecturersAsync(a.Participant.AssignmentId);
    }

    public async Task JoinWaitingAsync(string connectionId, int assignmentId, string userId)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        var a = await AttemptLock.LearnerAsync(db, assignmentId, userId);
        var member = await AttemptLock.MemberForLearnerAsync(db, a.ClassroomId, userId);
        await AccountLockState.EnsureAllowedAsync(db, userId, clock.GetUtcNow().UtcDateTime);
        var p = await db.Participants.SingleOrDefaultAsync(p => p.AssignmentId == assignmentId && p.UserId == userId);
        if (p == null || !(AssignmentAccess.CanStart(a, member, p) || p.IsStranger))
            throw new ForbiddenException();
        await hub.Groups.AddToGroupAsync(connectionId, SignalRLearnNotifier.WaitingGroup(assignmentId));
        await tx.CommitAsync();
        await notifier.LecturersAsync(assignmentId);
    }

    public Task DisconnectAsync(string connectionId) => DisconnectAsync(connectionId, retry: true);

    private async Task DisconnectAsync(string connectionId, bool retry)
    {
        var tracked = tracker.Find(connectionId);
        if (tracked == null) return;
        await using var tx = await db.Database.BeginTransactionAsync();
        var header = await db.Attempts.Where(a => a.Id == tracked.AttemptId)
            .Select(a => new { a.ParticipantId, a.Participant.AssignmentId, a.Participant.UserId }).SingleAsync();
        await AttemptLock.LearnerAsync(db, header.AssignmentId, header.UserId ?? "participant:" + header.ParticipantId);
        await AttemptLock.ParticipantAsync(db, header.ParticipantId);
        var attempt = await AttemptLock.LoadAsync(db, tracked.AttemptId);
        // Đọc người làm trước khi khóa: khách vừa gộp vào tài khoản thì lượt đã sang người khác → thả khóa, đọc lại (13/11/2026).
        if (attempt.ParticipantId != header.ParticipantId) { await tx.RollbackAsync(); await tx.DisposeAsync(); if (retry) await DisconnectAsync(connectionId, false); return; }
        var now = clock.GetUtcNow().UtcDateTime;
        AttemptRules.ExpireIfDue(attempt, attempt.Participant, now);
        tracker.Remove(connectionId);
        if (AttemptRules.IsOpen(attempt) && attempt.ActiveDeviceId == tracked.DeviceId
            && tracker.ForDevice(attempt.Id, tracked.DeviceId).Length == 0)
        {
            QuestionTimerRules.Pause(attempt, now);
            attempt.DisconnectedAt = now;
        }
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        await notifier.LecturersAsync(header.AssignmentId, 31);
    }

    // (69) kết nối trước lần khởi động này đều đã mất; không chạm lượt đã chốt.
    public static async Task<int> ResetDisconnectedAsync(AppDbContext db, DateTime now)
    {
        var ids = await db.Attempts.Where(a => (a.Status == AttemptStatus.InProgress || a.Status == AttemptStatus.Paused)
            && (a.DisconnectedAt == null || a.Questions.Any(q => q.TimerStartedAt != null)
                && a.Participant.Assignment.Mode == AssignmentMode.Practice))
            .Select(a => new { a.Id, a.ParticipantId, a.Participant.AssignmentId, a.Participant.UserId }).ToListAsync();
        var count = 0;
        foreach (var id in ids)
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            await AttemptLock.LearnerAsync(db, id.AssignmentId, id.UserId ?? "participant:" + id.ParticipantId);
            await AttemptLock.ParticipantAsync(db, id.ParticipantId);
            var a = await AttemptLock.LoadAsync(db, id.Id);
            if (a.ParticipantId != id.ParticipantId) { await tx.RollbackAsync(); continue; } // lượt vừa đổi người làm (gộp khách)
            if (AttemptRules.IsOpen(a))
            {
                AttemptRules.ExpireIfDue(a, a.Participant, now);
                QuestionTimerRules.Pause(a, now); // (59) khởi động lại không để giờ Ôn tập chạy ngầm.
                if (a.DisconnectedAt == null) { a.DisconnectedAt = now; count++; }
                await db.SaveChangesAsync();
            }
            await tx.CommitAsync();
        }
        return count;
    }
}
