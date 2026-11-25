namespace MindSprintAI_ASP.NET_MVC.Data.Entities;

// Bảng có Version được tăng tự động khi lưu, dùng chống ghi đè (ý Codex 18).
public interface IVersioned
{
    int Version { get; set; }
}

public class StudySet
{
    public FlashcardMode FlashcardMode { get; set; }
    public int Id { get; set; }
    public string OwnerId { get; set; } = "";
    public ApplicationUser Owner { get; set; } = null!;
    public string Title { get; set; } = "";
    // Danh mục tự đặt (Tiếng Anh, Lập trình… như bộ lọc danh mục FR-04 của nhóm); null = chưa xếp.
    public string? Category { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ArchivedAt { get; set; }
    public DateTime? EverAssignedAt { get; set; }
    public DateTime? HiddenByAdminAt { get; set; }
    public string? HiddenReason { get; set; }
    public DateTime? DeletedAt { get; set; }
    public int? ClonedFromId { get; set; }

    public List<Quiz> Quizzes { get; set; } = [];
}

public class Quiz : IVersioned
{
    public DateTime? EverAssignedAt { get; set; }
    public DateTime? ArchivedAt { get; set; }
    public int Id { get; set; }
    public int StudySetId { get; set; }
    public StudySet StudySet { get; set; } = null!;
    public string Title { get; set; } = "";
    public QuizStatus Status { get; set; } = QuizStatus.Draft;
    public int Version { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DeletedAt { get; set; }

    public List<Question> Questions { get; set; } = [];
}

public class Question : IVersioned
{
    public DateTime? DeletedAt { get; set; }
    public int Id { get; set; }
    public int QuizId { get; set; }
    public Quiz Quiz { get; set; } = null!;

    // Thứ tự gốc; báo cáo GV luôn theo thứ tự này (B4c).
    public int OrderIndex { get; set; }
    public QuestionType Type { get; set; }
    public string Prompt { get; set; } = "";
    public string? Explanation { get; set; }
    public decimal Points { get; set; } = 1;
    public int? TimeLimitSeconds { get; set; }
    public bool KeepOptionOrder { get; set; }

    // Setting chấm của câu điền chữ/điền số (tầng 3), tạo sẵn cột.
    public bool CaseSensitive { get; set; }
    public bool AcceptNoDiacritics { get; set; }
    public bool IgnorePunctuation { get; set; }
    public bool AcceptAnyBlankOrder { get; set; }
    public BlankScoring BlankScoring { get; set; } = BlankScoring.AllOrNothing;
    private decimal? _numberAnswer;
    private decimal _numberTolerance;
    public decimal? NumberAnswerExact { get; set; }
    public decimal? NumberToleranceExact { get; set; }
    // (5) cột thêm giữ đầy đủ decimal; cột cũ 18,6 giữ tương thích, không làm tròn đáp án chấm.
    public decimal? NumberAnswer
    {
        get => NumberAnswerExact ?? _numberAnswer;
        set { NumberAnswerExact = value; _numberAnswer = value is { } v ? Math.Clamp(v, -999999999999.999999m, 999999999999.999999m) : null; }
    }
    public decimal NumberTolerance
    {
        get => NumberToleranceExact ?? _numberTolerance;
        set { NumberToleranceExact = value; _numberTolerance = Math.Clamp(value, -999999999999.999999m, 999999999999.999999m); }
    }

    // Khảo sát (tầng 4).
    public bool IsRequired { get; set; }
    public int? MaxLength { get; set; }

    public int Version { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<QuestionOption> Options { get; set; } = [];
    public List<QuestionBlank> Blanks { get; set; } = [];
}

public class QuestionOption
{
    public int Id { get; set; }
    public int QuestionId { get; set; }
    public Question Question { get; set; } = null!;
    public int OrderIndex { get; set; }
    public string Text { get; set; } = "";
    public bool IsCorrect { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
