using MindSprintAI_ASP.NET_MVC.Data.Entities;

namespace MindSprintAI_ASP.NET_MVC.Services;

public partial class AssignmentService
{
    public static void ApplyDeviceSettings(Assignment a, bool deviceLock, bool monitorScreen, int? autoSubmitLeaves)
    {
        if (autoSubmitLeaves is < 1 or > 20) throw new BusinessException("Số lần rời màn hình phải từ 1 đến 20.");
        if (!monitorScreen && autoSubmitLeaves != null) throw new BusinessException("Hãy bật Giám sát rời màn hình trước khi chọn tự nộp bài.");
        a.DeviceLock = deviceLock; a.MonitorScreen = monitorScreen; a.AutoSubmitLeaves = autoSubmitLeaves;
    }

    public async Task ChangeDeviceSettingsAsync(int id, bool deviceLock, bool monitorScreen, int? autoSubmitLeaves, string userId)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        var a = await AttemptLock.AssignmentAsync(db, id);
        await GetForLecturerAsync(id, userId);
        var before = $"{a.DeviceLock}/{a.MonitorScreen}/{a.AutoSubmitLeaves}";
        ApplyDeviceSettings(a, deviceLock, monitorScreen, autoSubmitLeaves);
        db.AssignmentChanges.Add(new() { AssignmentId = id, ChangedById = userId, ChangedAt = Now,
            Field = "DeviceMonitoring", OldValue = before, NewValue = $"{deviceLock}/{monitorScreen}/{autoSubmitLeaves}" });
        await new UserSettingPresetService(db, clock).SaveAssignmentAsync(a, userId);
        db.Audit?.Add(AuditAction.AssignmentSettingsChanged, "Assignment", id, userId); // (NK3)
        await db.SaveChangesAsync(); await tx.CommitAsync();
        await notifier.LecturersAsync(id);
    }
}
