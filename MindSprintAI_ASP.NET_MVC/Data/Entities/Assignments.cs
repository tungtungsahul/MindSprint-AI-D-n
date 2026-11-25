namespace MindSprintAI_ASP.NET_MVC.Data.Entities;

// Một bài giao; bài Kiểm tra có đúng một phiên nên các cột phiên nằm ở đây (ERD giả định 1).
public class Assignment : IVersioned
{
    // (F1, H1, H8) mặc định tắt cho bài cũ.
    public bool DeviceLock { get; set; }
    public bool MonitorScreen { get; set; }
    public int? AutoSubmitLeaves { get; set; }
    // (D1a, E1) mặc định truyền thống; hai điều kiện liệt độc lập, không sửa con số điểm.
    public FlashcardMode FlashcardMode { get; set; }
    public decimal? CriticalThreshold { get; set; }
    public bool CriticalQuestionsEnabled { get; set; }
    public string CriticalQuestionIdsJson { get; set; } = "[]";
    public bool IgnoreQuestionTimers { get; set; }
    public int Id { get; set; }
    public int? ClassroomId { get; set; }
    public Classroom? Classroom { get; set; }
    public string CreatedById { get; set; } = "";
    public int? QuizId { get; set; }
    public Quiz? Quiz { get; set; }
    public int? StudySetId { get; set; }

    // Bài làm theo đề (Form = Document): tên bài + đề GV tải lên (luôn lưu dạng PDF; Word được đổi sang PDF khi tải lên).
    public string? PaperTitle { get; set; }
    public string? PaperFileName { get; set; }      // tên tệp gốc GV tải lên (chỉ để hiển thị)
    public string? PaperStoredName { get; set; }    // tên tệp PDF trong App_Data/assignment-files/{Id}/
    public DateTime? PaperUploadedAt { get; set; }

    public AssignmentMode Mode { get; set; } = AssignmentMode.Practice;
    public AssignmentForm Form { get; set; } = AssignmentForm.QuizOnly;
    public RequiredParts RequiredParts { get; set; } = RequiredParts.Quiz;

    public DateTime? OpensAt { get; set; }
    public DateTime? DueAt { get; set; }
    public int? DurationMinutes { get; set; }
    public DateTime? TestStartedAt { get; set; }
    public DateTime? TestEndsAt { get; set; }

    // Ôn tập: "tính cả lượt đầu"; rỗng = không giới hạn.
    public int? MaxAttempts { get; set; }
    public int? MasteryThreshold { get; set; }
    public int? AnswersPerQuestion { get; set; }
    public bool ScoreRetries { get; set; }
    public ScoreSelection ScoreSelection { get; set; } = ScoreSelection.Highest;
    public ScoreScale ScoreScale { get; set; } = ScoreScale.Ten;
    public RoundingMode Rounding { get; set; } = RoundingMode.TwoDecimals;
    public decimal? PassMark { get; set; }

    public bool ShuffleQuestions { get; set; }
    public bool ShuffleOptions { get; set; }

    public AudiencePolicy AudiencePolicy { get; set; } = AudiencePolicy.IncludeLateJoiners;
    public StrangerPolicy StrangerPolicy { get; set; } = StrangerPolicy.Reject;

    public bool AllowAiAssistant { get; set; } = true;
    public bool AllowAiAfterTest { get; set; }
    public bool AllowFlashcardsAfterDue { get; set; } = true;
    public bool AllowQuizSelfReview { get; set; }
    public bool AllowCardSelfReview { get; set; }
    // Người dùng chốt 15/11: bài giao mặc định giữ quy tắc QuizEdu "lưu xong mới chuyển thẻ"; GV bật thì học viên ôn thẻ tiếp khi mất mạng,
    // đánh giá lưu tạm ở trình duyệt và gửi bù khi có mạng (máy chủ vẫn kiểm hạn nộp… lúc nhận). Chỉ bài Ôn tập có thẻ.
    public bool AllowOfflineCards { get; set; }

    public DateTime? ScoresPublishedAt { get; set; }
    public DateTime? AnswersPublishedAt { get; set; }
    public DateTime? SurveyResultsPublishedAt { get; set; }

    public AssignmentStatus Status { get; set; } = AssignmentStatus.Open;
    public DateTime? ClosedAt { get; set; }
    public CloseReason? CloseReason { get; set; }
    public DateTime? ReportDeletedAt { get; set; }
    public DateTime? ArchivedAt { get; set; }

    public int Version { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<Participant> Participants { get; set; } = [];
    public List<Regrade> Regrades { get; set; } = [];
}

public class AssignmentChange
{
    public long Id { get; set; }
    public int AssignmentId { get; set; }
    public Assignment Assignment { get; set; } = null!;
    public string ChangedById { get; set; } = "";
    public DateTime ChangedAt { get; set; } = DateTime.UtcNow;
    public string Field { get; set; } = "";
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public string? Reason { get; set; }
}

// Người làm bài trong một bài giao. Thành viên lớp chưa mở bài thì chưa có dòng (= "Chưa làm", 67).
public class Participant
{
    public FlashcardMode FlashcardMode { get; set; }
    public int Id { get; set; }
    public int AssignmentId { get; set; }
    public Assignment Assignment { get; set; } = null!;
    public string? UserId { get; set; }
    public ApplicationUser? User { get; set; }

    public int? GuestId { get; set; }
    public Guest? Guest { get; set; }
    // Tên theo bài giao: cùng trình duyệt có thể gặp các nhóm tên trùng khác nhau (45).
    public string? GuestDisplayName { get; set; }
    public DateTime? GuestStartedAt { get; set; }

    public bool IsStranger { get; set; }
    public ApprovalStatus ApprovalStatus { get; set; } = ApprovalStatus.NotRequired;

    // Cộng thêm vào giờ chung, không phải mốc cố định (63, B3b).
    public int ExtraMinutes { get; set; }
    public string? ExtraMinutesReason { get; set; }
    public int ExtraAttempts { get; set; }
    public string? ExtraAttemptsReason { get; set; }

    public CompletionStatus CompletionStatus { get; set; } = CompletionStatus.NotStarted;
    public CompletionLabels CompletionLabels { get; set; }
    public bool MasteryReached { get; set; }
    // (B5a') học sau hạn chỉ cập nhật tiến độ, không viết lại kết quả đã chốt.
    public DateTime? CardsCompletionFinalizedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<Attempt> Attempts { get; set; } = [];
    public List<RetakeRequest> RetakeRequests { get; set; } = [];
}

public class RetakeRequest
{
    public int Id { get; set; }
    public int ParticipantId { get; set; }
    public Participant Participant { get; set; } = null!;
    public string Reason { get; set; } = "";
    public RetakeRequestStatus Status { get; set; } = RetakeRequestStatus.Pending;
    public DateTime? WindowStartsAt { get; set; }
    public DateTime? WindowEndsAt { get; set; }
    public string? DecidedById { get; set; }
    public DateTime? DecidedAt { get; set; }
    public bool AnswersWerePublished { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
