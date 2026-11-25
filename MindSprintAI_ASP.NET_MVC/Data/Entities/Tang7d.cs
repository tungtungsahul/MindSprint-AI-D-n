namespace MindSprintAI_ASP.NET_MVC.Data.Entities;

public enum CriticalLabel { None = 0, Pending = 1, Failed = 2 }

// (D3b) mỗi đánh giá cùng giao dịch với tiến độ, kể cả khi chưa đến hạn.
public class FlashcardReview
{
    public long Id { get; set; }
    public int StudySetId { get; set; }
    public StudySet StudySet { get; set; } = null!;
    public int FlashcardId { get; set; }
    public Flashcard Flashcard { get; set; } = null!;
    public int? ParticipantId { get; set; }
    public Participant? Participant { get; set; }
    public string UserId { get; set; } = "";
    public FlashcardMode Mode { get; set; }
    public FlashcardRating Rating { get; set; }
    public FlashcardRating? BeforeRating { get; set; }
    public FlashcardRating? AfterRating { get; set; }
    public int BeforeBox { get; set; }
    public int AfterBox { get; set; }
    public DateOnly? BeforeDueDate { get; set; }
    public DateOnly? AfterDueDate { get; set; }
    public bool WasDue { get; set; }
    public DateTime ReviewedAt { get; set; }
}
