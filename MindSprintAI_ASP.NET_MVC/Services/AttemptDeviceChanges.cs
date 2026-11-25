using Microsoft.EntityFrameworkCore;
using MindSprintAI_ASP.NET_MVC.Data.Entities;

namespace MindSprintAI_ASP.NET_MVC.Services;

public record DeviceChangeState(string Status, bool Active, bool Ended, string? Message);
public partial class AttemptService
{
    private readonly HashSet<string> bellUsers = [];
    public static bool NeedsConsent(Assignment a) => a.DeviceLock || a.MonitorScreen;
    internal static void EnsureConsent(Assignment a, bool consent)
    {
        if (NeedsConsent(a) && !consent) throw new BusinessException("Bạn cần bấm Tôi đồng ý trước khi bắt đầu bài này.");
    }

    internal static void ApplyDeviceSignals(Attempt a, DeviceSignalResult? signals)
    {
        a.LightHash = signals?.LightHash;
        a.FullHash = signals?.FullHash;
        a.NetworkPrefix = signals?.NetworkPrefix;
    }

    public async Task ConsentAsync(long id, string userId, DeviceSignalResult? signals)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        var a = await LoadForLearnerAsync(id, userId);
        if (AttemptRules.IsOpen(a))
        {
            a.DeviceConsentAt ??= Now;
            if (a.ActiveDeviceId == device.Id) ApplyDeviceSignals(a, signals == null ? null : signals with
                { FullHash = NeedsConsent(a.Participant.Assignment) || a.MonitorScreen ? signals.FullHash : null });
        }
        db.Audit?.Add(AuditAction.DeviceConsentGiven, "Attempt", id, userId); // (NK3)
        await db.SaveChangesAsync(); await tx.CommitAsync(); await NotifyLiveChangesAsync();
    }

    private async Task RequestDeviceChangeAsync(Attempt a, string? reason, DeviceSignalResult? signals)
    {
        reason = reason?.Trim();
        if (reason?.Length > 200) throw new BusinessException("Lý do chuyển thiết bị tối đa 200 ký tự.");
        if (a.DeviceChanges.Any(r => r.Status == DeviceChangeStatus.Pending)) return; // (F2) dưới khóa lượt.
        a.DeviceChanges.Add(new() { CreatedAt = Now, RequestingDeviceId = device.Id, Reason = reason,
            LightHash = signals?.LightHash, FullHash = a.DeviceConsentAt != null ? signals?.FullHash : null, NetworkPrefix = signals?.NetworkPrefix });
        db.Audit?.Add(AuditAction.DeviceChangeRequested, "Attempt", a.Id); // (NK3)
        liveChanges.Add(a.Participant.AssignmentId);
        await BellLecturersAsync(a, NotificationType.DeviceChange, "Học viên xin chuyển thiết bị. Hãy xem yêu cầu để cho chuyển hoặc từ chối.");
    }

    // (F11) lọc trong SQL; cả GV chính/phụ dùng tùy chọn riêng, không ảnh hưởng live.
    private IQueryable<string> BellLecturers(int? classroomId, NotificationType type) =>
        db.ClassMembers.Where(m => m.ClassroomId == classroomId
            && m.Classroom.DeletedAt == null && m.Status == ClassMemberStatus.Active
            && (m.Role == ClassRole.MainLecturer || m.Role == ClassRole.AssistantLecturer)
            && ((type == NotificationType.DeviceChange && m.User.DeviceChangeBellEnabled)
                || (type == NotificationType.ScreenLeave && m.User.ScreenLeaveBellEnabled))).Select(m => m.UserId);

    private async Task BellLecturersAsync(Attempt a, NotificationType type, string text)
    {
        var users = await BellLecturers(a.Participant.Assignment.ClassroomId, type).ToListAsync();
        var notifications = new NotificationService(db, clock, notifier);
        foreach (var user in users)
        {
            await notifications.AddAsync(user, type, new(text, $"/Assignments/Details/{a.Participant.AssignmentId}#assignment-live",
                AssignmentId: a.Participant.AssignmentId, AttemptId: a.Id));
            bellUsers.Add(user);
        }
    }

    public async Task CancelDeviceChangeAsync(long id, string userId)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        var a = await LoadForLearnerAsync(id, userId);
        foreach (var r in a.DeviceChanges.Where(r => r.Status == DeviceChangeStatus.Pending))
        { r.Status = DeviceChangeStatus.Cancelled; r.DecidedAt = Now; }
        liveChanges.Add(a.Participant.AssignmentId);
        db.Audit?.Add(AuditAction.DeviceChangeCancelled, "Attempt", id, userId); // (NK3)
        await db.SaveChangesAsync(); await tx.CommitAsync(); await NotifyLiveChangesAsync();
    }

    public async Task<int> DecideDeviceChangeAsync(long id, long requestId, bool approve, string lecturerId)
    {
        var header = await db.Attempts.Where(a => a.Id == id).Select(a => new { a.ParticipantId, a.Participant.AssignmentId }).SingleOrDefaultAsync()
            ?? throw new ForbiddenException();
        await using var tx = await db.Database.BeginTransactionAsync();
        var assignment = await AttemptLock.AssignmentAsync(db, header.AssignmentId);
        // (F3) cùng quyền với màn hình theo dõi: GV chính/phụ đang hoạt động trong lớp còn tồn tại.
        if (assignment.ClassroomId == null || !await db.ClassMembers.AnyAsync(m => m.ClassroomId == assignment.ClassroomId
            && m.UserId == lecturerId && m.Classroom.DeletedAt == null && m.Status == ClassMemberStatus.Active
            && (m.Role == ClassRole.MainLecturer || m.Role == ClassRole.AssistantLecturer))) throw new ForbiddenException();
        await AttemptLock.ParticipantAsync(db, header.ParticipantId);
        var a = await AttemptLock.LoadAsync(db, id);
        AttemptRules.ExpireIfDue(a, a.Participant, Now);
        var request = a.DeviceChanges.SingleOrDefault(r => r.Id == requestId) ?? throw new ForbiddenException();
        string[] old = [];
        if (request.Status == DeviceChangeStatus.Pending && AttemptRules.IsOpen(a))
        {
            request.Status = approve ? DeviceChangeStatus.Approved : DeviceChangeStatus.Denied;
            request.DecidedAt = Now; request.DecidedById = lecturerId;
            if (approve)
            {
                old = a.ActiveDeviceId == null ? [] : connections.ForDevice(id, a.ActiveDeviceId);
                QuestionTimerRules.Pause(a, Now);
                a.ActiveDeviceId = request.RequestingDeviceId;
                a.DisconnectedAt = Now; a.Status = AttemptStatus.InProgress;
                ApplyDeviceSignals(a, new(request.LightHash, request.FullHash, request.NetworkPrefix));
                // (H2) máy mới bắt đầu nhịp theo dõi; không tính sự mất liên lạc của máy cũ.
                ScreenMonitoringRules.CloseAll(a, Now);
                a.MonitorHeartbeatAt = Now;
            }
        }
        liveChanges.Add(assignment.Id);
        db.Audit?.Add(approve ? AuditAction.DeviceChangeApproved : AuditAction.DeviceChangeRejected, "Attempt", id, lecturerId); // (NK3)
        await db.SaveChangesAsync(); await tx.CommitAsync(); await NotifyLiveChangesAsync();
        if (old.Length > 0) await notifier.MovedAsync(id, old);
        return assignment.Id;
    }

    public async Task<DeviceChangeState> DeviceChangeStateAsync(long id, string userId)
    {
        var a = await GetForLearnerAsync(id, userId);
        var request = a.DeviceChanges.Where(r => r.RequestingDeviceId == device.Id).OrderByDescending(r => r.Id).FirstOrDefault();
        return new(request?.Status.ToString() ?? "None", a.ActiveDeviceId == device.Id, !AttemptRules.IsOpen(a),
            request?.Status == DeviceChangeStatus.Denied ? "Giảng viên chưa cho chuyển thiết bị" : null);
    }
}
