namespace MindSprintAI_ASP.NET_MVC.Data.Entities;

public enum InvitationKind { Assistant = 1, Transfer = 2 }
public enum InvitationStatus { Pending = 1, Accepted = 2, Declined = 3, Revoked = 4, Expired = 5 }
public enum ReminderKind { Due = 1, ClassApproval = 2, ParticipantApproval = 3, Retake = 4, Invitation = 5, Transfer = 6, SrsDue = 7 }

// (L3, L6) sổ lời mời giữ email gốc và hạn gốc, kể cả trước khi có tài khoản.
public class Invitation
{
    public long Id { get; set; }
    public InvitationKind Kind { get; set; }
    public int ClassroomId { get; set; }
    public Classroom Classroom { get; set; } = null!;
    public string InviterId { get; set; } = "";
    public string Email { get; set; } = "";
    public string NormalizedEmail { get; set; } = "";
    public string? RecipientId { get; set; }
    public int? MemberId { get; set; }
    public int? TransferId { get; set; }
    public string? TransferFromUserId { get; set; }
    public bool FromUserStays { get; set; }
    public InvitationStatus Status { get; set; } = InvitationStatus.Pending;
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? RecipientNotifiedAt { get; set; }
}

public class Reminder
{
    public long Id { get; set; }
    public string UserId { get; set; } = "";
    public ReminderKind Kind { get; set; }
    public long TargetId { get; set; }
    public string Text { get; set; } = "";
    public string Url { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime? EmailedAt { get; set; }
}

// (N4) khóa duy nhất theo ngày Việt Nam, cả khi hai worker chạy đồng thời.
public class ReminderDigest
{
    public long Id { get; set; }
    public string UserId { get; set; } = "";
    public DateTime VietnamDate { get; set; }
}

public class KnownDevice
{
    public string? LightHash { get; set; }
    public string? NetworkPrefix { get; set; }
    public long Id { get; set; }
    public string UserId { get; set; } = "";
    public string DeviceHash { get; set; } = "";
    public string Label { get; set; } = "";
    public DateTime FirstSeenAt { get; set; }
    public DateTime LastSeenAt { get; set; }
    // (T5) chỉ ghi đè lần đăng nhập cuối, không giữ lịch sử IP.
    public string? LastSignInIp { get; set; }
    public string? LastLocation { get; set; }
}
