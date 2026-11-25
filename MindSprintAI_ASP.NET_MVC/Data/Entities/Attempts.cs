namespace MindSprintAI_ASP.NET_MVC.Data.Entities;

public class Attempt : IVersioned
{
    public DateTime? DeviceConsentAt { get; set; }
    public string? LightHash { get; set; }
    public string? FullHash { get; set; }
    public string? NetworkPrefix { get; set; }
    // (H1, H8) chốt giám sát theo lượt đã đồng ý; đổi setting dùng cho lượt mới.
    public bool MonitorScreen { get; set; }
    public int? AutoSubmitLeaves { get; set; }
    public DateTime? MonitorHeartbeatAt { get; set; }
    public DateTime? MonitorWindowAt { get; set; }
    public int MonitorEventCount { get; set; }
    public bool LeaveBellSent { get; set; }
    public List<DeviceChangeRequest> DeviceChanges { get; set; } = [];
    public List<ScreenLeave> ScreenLeaves { get; set; } = [];
    public CriticalLabel CriticalLabel { get; set; }
    public string? CriticalReasons { get; set; }
    public bool FailedCriticalQuestion { get; set; }
    public long Id { get; set; }
    // (61, 66, C2) chặn yêu cầu cũ ghi đè lượt đã chốt hoặc đã hủy.
    public int Version { get; set; }
    public int ParticipantId { get; set; }
    public Participant Participant { get; set; } = null!;
    public int AttemptNumber { get; set; }
    public AttemptKind Kind { get; set; } = AttemptKind.Normal;
    public bool MadeAsGuest { get; set; }
    public bool ExcludeFromGradebook { get; set; }

    public int? RetakeRequestId { get; set; }
    public RetakeRequest? RetakeRequest { get; set; }

    public AttemptStatus Status { get; set; } = AttemptStatus.InProgress;
    public AttemptEndReason? EndReason { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? EndedAt { get; set; }

    // Giờ máy chủ chốt lượt; tính lại khi GV đổi hạn/cộng giờ.
    public DateTime? DeadlineAt { get; set; }
    public string? ActiveDeviceId { get; set; }
    public DateTime? LastSeenAt { get; set; }
    public DateTime? DisconnectedAt { get; set; }

    private decimal _firstScore;
    public decimal? FirstScoreExact { get; set; }
    // (65C) cột cũ giữ tương thích; cột thêm giữ số lẻ của điểm chia theo ô.
    public decimal FirstScore { get => FirstScoreExact ?? _firstScore; set { _firstScore = value; FirstScoreExact = value; } }
    public decimal? PracticeScore { get; set; }
    public decimal MaxScore { get; set; }
    public int MasteredCount { get; set; }
    public DateTime? MasteryReachedAt { get; set; }

    public DateTime? CancelledAt { get; set; }
    public string? CancelReason { get; set; }
    public string? CancelledById { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<AttemptQuestion> Questions { get; set; } = [];
}

// Đề học viên đã gặp trong lượt; bản chụp bắt buộc (31A).
public class AttemptQuestion
{
    public long Id { get; set; }
    public long AttemptId { get; set; }
    public Attempt Attempt { get; set; } = null!;
    public int QuestionId { get; set; }
    public Question Question { get; set; } = null!;
    public int QuestionVersion { get; set; }
    public int DisplayOrder { get; set; }
    public string? OptionOrderJson { get; set; }
    public string SnapshotJson { get; set; } = "";
    public AttemptQuestionState State { get; set; } = AttemptQuestionState.Unanswered;
    public bool Flagged { get; set; }
    public int AnswerCount { get; set; }
    public bool IsMastered { get; set; }
    private decimal? _firstScore, _latestScore;
    public decimal? FirstScoreExact { get; set; }
    public decimal? LatestScoreExact { get; set; }
    public decimal? FirstScore { get => FirstScoreExact ?? _firstScore; set { _firstScore = value; FirstScoreExact = value; } }
    public decimal? LatestScore { get => LatestScoreExact ?? _latestScore; set { _latestScore = value; LatestScoreExact = value; } }
    public DateTime? TimerStartedAt { get; set; }
    public int? TimeLeftSeconds { get; set; }
    public string? DraftJson { get; set; }
    public DateTime? DraftSavedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<AnswerSubmission> Submissions { get; set; } = [];
}

public class AnswerSubmission
{
    public long Id { get; set; }
    public long AttemptQuestionId { get; set; }
    public AttemptQuestion AttemptQuestion { get; set; } = null!;

    // Gửi lại cùng câu trả lời không tạo lần trả lời thứ hai (mục 15).
    public Guid ClientRequestId { get; set; }
    public string PayloadJson { get; set; } = "";
    public bool? IsCorrect { get; set; }
    private decimal? _score;
    public decimal? ScoreExact { get; set; }
    public decimal? Score { get => ScoreExact ?? _score; set { _score = value; ScoreExact = value; } }
    public bool IsFirstAnswer { get; set; }
    public bool IsFinal { get; set; }
    public bool PublishedToClass { get; set; }
    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
}
