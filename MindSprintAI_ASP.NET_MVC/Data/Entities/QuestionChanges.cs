namespace MindSprintAI_ASP.NET_MVC.Data.Entities;

public enum QuestionEditKind { Minor = 1, Content = 2 }
public enum RegradeMethod { NewCorrect = 1, FullCredit = 2, Cancel = 3 }

public class QuestionRevision
{
    public long Id { get; set; }
    public int QuestionId { get; set; }
    public Question Question { get; set; } = null!;
    public int Version { get; set; }
    public QuestionEditKind EditKind { get; set; }
    public string SnapshotJson { get; set; } = "";
    public string EditedById { get; set; } = "";
    public DateTime EditedAt { get; set; }
}

public class Regrade
{
    public long Id { get; set; }
    public int AssignmentId { get; set; }
    public Assignment Assignment { get; set; } = null!;
    public int QuestionId { get; set; }
    public Question Question { get; set; } = null!;
    public RegradeMethod Method { get; set; }
    public string? NewCorrectJson { get; set; }
    public string Reason { get; set; } = "";
    public string ById { get; set; } = "";
    public DateTime At { get; set; }
    // (19) gửi lại cùng yêu cầu không ghi hai lần lịch sử.
    public Guid ClientRequestId { get; set; }
}

public class ScoreHistory
{
    public long Id { get; set; }
    public long AttemptId { get; set; }
    public Attempt Attempt { get; set; } = null!;
    public long RegradeId { get; set; }
    public Regrade Regrade { get; set; } = null!;
    public decimal OldFirstScore { get; set; }
    public decimal NewFirstScore { get; set; }
    public decimal? OldPracticeScore { get; set; }
    public decimal? NewPracticeScore { get; set; }
    public decimal OldMaxScore { get; set; }
    public decimal NewMaxScore { get; set; }
}
