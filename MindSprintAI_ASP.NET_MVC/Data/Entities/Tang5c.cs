namespace MindSprintAI_ASP.NET_MVC.Data.Entities;

public enum NotificationType { Assistant = 1, Transfer = 2, Promotion = 3, Retake = 4, Regrade = 5, AccountLock = 6, ExtraMinutes = 7, ExtraAttempts = 8, Reminder = 9, NewDevice = 10, DeviceChange = 11, ScreenLeave = 12, ContentReview = 13 }
public enum ClassTransferStatus { Pending = 1, Accepted = 2, Cancelled = 3, Declined = 4, Expired = 5 }

public class Notification
{
    public long Id { get; set; }
    public string UserId { get; set; } = "";
    public NotificationType Type { get; set; }
    public string PayloadJson { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime? ReadAt { get; set; }
}

public class ClassTransfer
{
    public int Id { get; set; }
    public int ClassroomId { get; set; }
    public Classroom Classroom { get; set; } = null!;
    public string FromUserId { get; set; } = "";
    public string ToUserId { get; set; } = "";
    public ClassTransferStatus Status { get; set; } = ClassTransferStatus.Pending;
    public bool FromUserStays { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? AcceptedAt { get; set; }
    public DateTime? ExpiresAt { get; set; } // (L2) dòng cũ dùng CreatedAt + 7 ngày
    public string? InvitedEmail { get; set; }
}
