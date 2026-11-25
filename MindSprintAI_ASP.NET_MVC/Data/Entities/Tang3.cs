namespace MindSprintAI_ASP.NET_MVC.Data.Entities;

public class QuestionBlank
{
    public int Id { get; set; }
    public int QuestionId { get; set; }
    public Question Question { get; set; } = null!;
    public int BlankIndex { get; set; }
    public List<BlankAnswer> Answers { get; set; } = [];
}

public class BlankAnswer
{
    public int Id { get; set; }
    public int QuestionBlankId { get; set; }
    public QuestionBlank QuestionBlank { get; set; } = null!;
    public int OrderIndex { get; set; }
    public string Text { get; set; } = "";
}

public class Flashcard
{
    public int Id { get; set; }
    public int StudySetId { get; set; }
    public StudySet StudySet { get; set; } = null!;
    public int OrderIndex { get; set; }
    public string Front { get; set; } = "";
    public string Back { get; set; } = "";
    // "Ví dụ minh họa" của thẻ nhóm (không bắt buộc), hiện dưới mặt sau.
    public string? Example { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? EditedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
}

public enum FlashcardRating { Remembered = 1, NotRemembered = 2 }
public enum FlashcardMode { Traditional = 0, SpacedRepetition = 1, LearnerChoice = 2 }

public class FlashcardProgress
{
    // (D3a) null = dòng cũ đã đánh giá truyền thống; false = chỉ mới học SRS.
    public bool? HasTraditionalRating { get; set; }
    public DateTime? TraditionalReviewedAt { get; set; }
    public int? SrsBox { get; set; }
    public DateOnly? SrsDueDate { get; set; }
    public long Id { get; set; }
    public int? ParticipantId { get; set; }
    public Participant? Participant { get; set; }
    public string? UserId { get; set; }
    public ApplicationUser? User { get; set; }
    public int FlashcardId { get; set; }
    public Flashcard Flashcard { get; set; } = null!;
    public FlashcardRating LastRating { get; set; }
    public DateTime FirstReviewedAt { get; set; }
    public DateTime LastReviewedAt { get; set; }
    public int ReviewCount { get; set; }
}
