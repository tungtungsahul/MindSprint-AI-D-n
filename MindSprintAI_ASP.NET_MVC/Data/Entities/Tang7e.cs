namespace MindSprintAI_ASP.NET_MVC.Data.Entities;

public enum DeviceChangeStatus { Pending = 1, Approved = 2, Denied = 3, Cancelled = 4 }
public enum ScreenLeaveKind { Hidden = 1, OtherWindow = 2, PageExit = 3, SignalLoss = 4 }

// (F2, F7) chỉ lưu kết quả có khóa; không có cột chứa thông số gốc.
public class DeviceChangeRequest
{
    public long Id { get; set; }
    public long AttemptId { get; set; }
    public Attempt Attempt { get; set; } = null!;
    public string RequestingDeviceId { get; set; } = "";
    public string? Reason { get; set; }
    public DeviceChangeStatus Status { get; set; } = DeviceChangeStatus.Pending;
    public DateTime CreatedAt { get; set; }
    public DateTime? DecidedAt { get; set; }
    public string? DecidedById { get; set; }
    public string? LightHash { get; set; }
    public string? FullHash { get; set; }
    public string? NetworkPrefix { get; set; }
}

// (H2, H5) thời điểm và thời lượng do máy chủ quyết định.
public class ScreenLeave
{
    public long Id { get; set; }
    public long AttemptId { get; set; }
    public Attempt Attempt { get; set; } = null!;
    public ScreenLeaveKind Kind { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public int DurationSeconds { get; set; }
    public bool Counted { get; set; }
    public bool ReportedOffline { get; set; } // (H10) xảy ra lúc mất mạng, trình duyệt báo lại khi có mạng; giờ chỉ là khoảng.
}
