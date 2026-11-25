using System.ComponentModel.DataAnnotations.Schema;

namespace MindSprintAI_ASP.NET_MVC.Data.Entities;

public enum AuditActorKind { User = 1, Guest = 2, System = 3 }
public enum AuditResult { Success = 1, Denied = 2, Failed = 3 }

// (NK3) Giá trị đã phát hành chỉ được thêm ở cuối, không đổi số hoặc dùng lại.
public enum AuditAction
{
    Registered = 1, EmailConfirmed = 2, LoginSucceeded = 3, LoginFailed = 4, LoggedOut = 5,
    PasswordRequested = 6, PasswordReset = 7, PasswordChanged = 8, PasswordSet = 9,
    TwoFactorEnabled = 10, TwoFactorDisabled = 11, RecoveryRegenerated = 12, AuthenticatorReset = 13,
    PasskeyAdded = 14, PasskeyDeleted = 15, PasskeyRenamed = 16, ExternalLinked = 17, ExternalRemoved = 18,
    DeviceDeleted = 19, OtherDevicesSignedOut = 20, AccountSelfLocked = 21,
    GeminiSaved = 22, GeminiDeleted = 23, NotificationSettingsChanged = 24,
    ClassroomCreated = 25, ClassroomCodeChanged = 26, ClassroomDeleted = 27, ClassroomRestored = 28,
    MemberApproved = 29, MemberRejected = 30, MemberRemoved = 31, ClassroomJoined = 32, ClassroomLeft = 33,
    AssistantInvited = 34, InvitationRevoked = 35, AssistantAccepted = 36, AssistantDeclined = 37, AssistantRemoved = 38,
    TransferOffered = 39, TransferAccepted = 40, TransferDeclined = 41, TransferCancelled = 42,
    StudySetCreated = 43, StudySetEdited = 44, StudySetDeleted = 45, StudySetRestored = 46,
    QuizCreated = 47, QuizEdited = 48, QuizDeleted = 49, QuizRestored = 50,
    QuestionCreated = 51, QuestionEdited = 52, QuestionDeleted = 53, QuestionRestored = 54,
    FlashcardCreated = 55, FlashcardEdited = 56, FlashcardDeleted = 57, FlashcardRestored = 58,
    ExcelImported = 59, StudySetCloned = 60,
    AssignmentCreated = 61, AssignmentSettingsChanged = 62, AssignmentDueChanged = 63, AssignmentClosed = 64,
    TestStarted = 65, ScoresPublished = 66, AnswersPublished = 67, Regraded = 68,
    ExtraTimeGranted = 69, ExtraAttemptsGranted = 70, StrangerApproved = 71, StrangerRejected = 72,
    RetakeRequested = 73, RetakeApproved = 74, RetakeRejected = 75, RetakeCancelled = 76,
    DeviceChangeApproved = 77, DeviceChangeRejected = 78, AttemptStarted = 79, DeviceConsentGiven = 80,
    AttemptSubmitted = 81, AttemptTimedOut = 82, AttemptScreenSubmitted = 83, DeviceChangeRequested = 84,
    AdminLockedUser = 85, AdminUnlockedUser = 86, AdminGranted = 87, AdminRevoked = 88,
    ContentHidden = 89, ReportDismissed = 90, ContentReported = 91, AdminTransferOffered = 92,
    AdminViewedAudit = 93, ActionRejected = 94, DeviceChangeCancelled = 95, ReportDeleted = 96,
    LecturerPromoted = 97, AdminViewedReport = 98, AdminListedUsers = 99, AdminViewedUser = 100,
    AdminViewedRoleHistory = 101, AdminListedReports = 102, AdminListedTransfers = 103,
    ReportHandled = 104, TwoFactorEmailEnabled = 105, TwoFactorEmailDisabled = 106,
    AttemptStopped = 107, AttemptCompleted = 108,
    UnhideRequested = 109, ContentUnhidden = 110, UnhideRejected = 111,
    ClassroomJoinPolicyChanged = 112, AccountDeleted = 113,
    PaperGraded = 114, // GV chấm tay bài làm theo đề
    DisplayNameChanged = 115 // tự đổi tên hiển thị (ProfileService)
}

// (NK3) Không FK vào dữ liệu sống: vẫn tra được sau khi đối tượng bị xóa; không lưu địa chỉ mạng.
public sealed class AuditEntry
{
    public long Id { get; set; }
    public DateTime At { get; set; }
    public string? ActorUserId { get; set; }
    public string? ActorGuestId { get; set; }
    public AuditActorKind ActorKind { get; set; }
    public AuditAction Action { get; set; }
    public string TargetType { get; set; } = "";
    public string TargetId { get; set; } = "";
    public AuditResult Result { get; set; }
    public string Note { get; set; } = "";
    public string RequestId { get; set; } = "";
    [NotMapped] internal Func<string>? ResolveTarget { get; set; }
}
