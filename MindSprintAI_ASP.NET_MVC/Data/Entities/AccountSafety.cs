namespace MindSprintAI_ASP.NET_MVC.Data.Entities;

public enum AccountLockKind { Protective = 1, Violation = 2 }
public enum ReportTarget { StudySet = 1, Quiz = 2, Question = 3, User = 4, AiConversation = 5 }
public enum ReportStatus { Pending = 1, Handled = 2, Dismissed = 3 }

public class AccountLock
{
    public int Id { get; set; }
    public string UserId { get; set; } = "";
    public AccountLockKind Kind { get; set; }
    public string? Reason { get; set; }
    public string? LockedById { get; set; }
    public DateTime StartsAt { get; set; }
    public DateTime? EndsAt { get; set; }
    public DateTime? UnlockedAt { get; set; }
    public string? UnlockedById { get; set; }
}

public class JoinCodeFailure
{
    public long Id { get; set; }
    // (46A, 73) 1 = tài khoản, 2 = cookie; không dùng IP để nhận dạng người học.
    public int KeyType { get; set; }
    public string KeyHash { get; set; } = "";
    public int FailCount { get; set; }
    public DateTime FirstFailedAt { get; set; }
    public DateTime LastFailedAt { get; set; }
}

public class ContentReport
{
    public int Id { get; set; }
    public string ReporterId { get; set; } = "";
    public ReportTarget TargetType { get; set; }
    public string TargetId { get; set; } = "";
    public string TargetOwnerId { get; set; } = "";
    public int? StudySetId { get; set; }
    public string Reason { get; set; } = "";
    public string? Note { get; set; }
    // (51C) chỉ lưu nội dung bị báo cáo, không lưu bài làm/điểm hay phần khác của chủ sở hữu.
    public string ContentJson { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public ReportStatus Status { get; set; } = ReportStatus.Pending;
    public string? HandledById { get; set; }
    public DateTime? HandledAt { get; set; }
}

public class AdminAccessLog
{
    public long Id { get; set; }
    public string AdminId { get; set; } = "";
    public int ContentReportId { get; set; }
    public DateTime ViewedAt { get; set; }
}

public class AdminRoleChange
{
    public long Id { get; set; }
    public string AdminId { get; set; } = "";
    public string TargetUserId { get; set; } = "";
    public bool Granted { get; set; }
    public string Reason { get; set; } = "";
    public DateTime At { get; set; }
}
