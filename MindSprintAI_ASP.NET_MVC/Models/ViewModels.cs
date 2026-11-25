using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MindSprintAI_ASP.NET_MVC.Data.Entities;
using MindSprintAI_ASP.NET_MVC.Services;

namespace MindSprintAI_ASP.NET_MVC.Models;

public class RegisterModel
{
    [Required(ErrorMessage = "Nhập email."), EmailAddress(ErrorMessage = "Email không hợp lệ.")]
    public string Email { get; set; } = "";

    [Required(ErrorMessage = "Nhập tên hiển thị."), StringLength(100, ErrorMessage = "Tên tối đa 100 ký tự.")]
    [Display(Name = "Tên hiển thị")]
    public string DisplayName { get; set; } = "";

    // Pháp lý C1: hai ô không tick sẵn, đều bắt buộc.
    [Range(typeof(bool), "true", "true", ErrorMessage = "Hãy tick vào ô này để tiếp tục.")]
    public bool AcceptTerms { get; set; }
    [Range(typeof(bool), "true", "true", ErrorMessage = "Hãy tick vào ô này để tiếp tục.")]
    public bool AcceptPrivacy { get; set; }
}

public class AcceptPolicyModel
{
    [Range(typeof(bool), "true", "true", ErrorMessage = "Hãy tick vào ô này để tiếp tục.")]
    public bool AcceptTerms { get; set; }
    [Range(typeof(bool), "true", "true", ErrorMessage = "Hãy tick vào ô này để tiếp tục.")]
    public bool AcceptPrivacy { get; set; }
    public string? ReturnUrl { get; set; }
    public bool Updated { get; set; } // đã đồng ý phiên bản cũ → đoạn mở đầu "vừa cập nhật"
}

public class SetPasswordModel
{
    public string UserId { get; set; } = "";
    public string Code { get; set; } = "";

    [Required(ErrorMessage = "Nhập mật khẩu."), DataType(DataType.Password), Display(Name = "Mật khẩu")]
    public string Password { get; set; } = "";

    [DataType(DataType.Password), Display(Name = "Nhập lại mật khẩu")]
    [Compare(nameof(Password), ErrorMessage = "Hai mật khẩu không khớp.")]
    public string ConfirmPassword { get; set; } = "";

    // Pháp lý C1: chủ email (người mở link trong thư) tự đồng ý; người gõ form Đăng ký có thể không phải chủ email.
    [Range(typeof(bool), "true", "true", ErrorMessage = "Hãy tick vào ô này để tiếp tục.")]
    public bool AcceptTerms { get; set; }
    [Range(typeof(bool), "true", "true", ErrorMessage = "Hãy tick vào ô này để tiếp tục.")]
    public bool AcceptPrivacy { get; set; }
}

public class LoginModel
{
    [Required(ErrorMessage = "Nhập email."), EmailAddress(ErrorMessage = "Email không hợp lệ.")]
    public string Email { get; set; } = "";

    [Required(ErrorMessage = "Nhập mật khẩu."), DataType(DataType.Password), Display(Name = "Mật khẩu")]
    public string Password { get; set; } = "";

    [Display(Name = "Ghi nhớ đăng nhập")]
    public bool RememberMe { get; set; }

    public string? ReturnUrl { get; set; }
}

public class ResetPasswordModel
{
    public string UserId { get; set; } = "";
    public string Code { get; set; } = "";
    public string Nonce { get; set; } = "";

    [Required(ErrorMessage = "Nhập mật khẩu mới."), DataType(DataType.Password), Display(Name = "Mật khẩu mới")]
    public string Password { get; set; } = "";

    [DataType(DataType.Password), Display(Name = "Nhập lại mật khẩu mới")]
    [Compare(nameof(Password), ErrorMessage = "Hai mật khẩu không khớp.")]
    public string ConfirmPassword { get; set; } = "";
}

public class OptionForm
{
    public string? Text { get; set; }
    public bool IsCorrect { get; set; }
}

public class QuestionForm
{
    public int QuizId { get; set; }
    public int? QuestionId { get; set; }
    public int? Version { get; set; }
    public QuestionEditKind EditKind { get; set; } = QuestionEditKind.Content;
    public int ExtraSessionMinutes { get; set; }
    public QuestionType Type { get; set; } = QuestionType.SingleChoice;
    public string Prompt { get; set; } = "";
    public string? Explanation { get; set; }
    public decimal Points { get; set; } = 1;
    public bool KeepOptionOrder { get; set; }
    public List<OptionForm> Options { get; set; } = [];
    public List<string> BlankAnswers { get; set; } = [""];
    public bool CaseSensitive { get; set; }
    public bool AcceptNoDiacritics { get; set; }
    public bool IgnorePunctuation { get; set; }
    public bool AcceptAnyBlankOrder { get; set; }
    public BlankScoring BlankScoring { get; set; } = BlankScoring.AllOrNothing;
    public string? NumberAnswer { get; set; }
    public string? NumberTolerance { get; set; }
    public bool IsRequired { get; set; }
    public int? MaxLength { get; set; }
    public int? TimeLimitSeconds { get; set; }

    public InputQuestionInput ToInputQuestion() => new(Type, Prompt ?? "", Explanation, Points,
        BlankAnswers.Select(b => (IReadOnlyList<string>)(b ?? "").Split('|').ToList()).ToList(), CaseSensitive,
        AcceptNoDiacritics, IgnorePunctuation, AcceptAnyBlankOrder, BlankScoring, NumberAnswer, NumberTolerance, TimeLimitSeconds);

    public ChoiceQuestionInput ToInput() => new(Type, Prompt ?? "", Explanation, Points, KeepOptionOrder,
        Options.Where(o => !string.IsNullOrWhiteSpace(o.Text)).Select(o => new OptionInput(o.Text!, o.IsCorrect)).ToList(), IsRequired, TimeLimitSeconds);

    public static QuestionForm From(Question q) => new()
    {
        QuizId = q.QuizId, QuestionId = q.Id, Version = q.Version, Type = q.Type, Prompt = q.Prompt,
        Explanation = q.Explanation, Points = q.Points, KeepOptionOrder = q.KeepOptionOrder, IsRequired = q.IsRequired, MaxLength = q.MaxLength,
        TimeLimitSeconds = q.TimeLimitSeconds,
        BlankAnswers = q.Blanks.Count == 0 ? [""] : q.Blanks.OrderBy(b => b.BlankIndex).Select(b => string.Join("|", b.Answers.OrderBy(a => a.OrderIndex).Select(a => a.Text))).ToList(),
        CaseSensitive = q.CaseSensitive, AcceptNoDiacritics = q.AcceptNoDiacritics, IgnorePunctuation = q.IgnorePunctuation,
        AcceptAnyBlankOrder = q.AcceptAnyBlankOrder, BlankScoring = q.BlankScoring,
        NumberAnswer = q.NumberAnswer?.ToString(System.Globalization.CultureInfo.InvariantCulture),
        NumberTolerance = q.NumberTolerance.ToString(System.Globalization.CultureInfo.InvariantCulture),
        Options = q.Options.OrderBy(o => o.OrderIndex).Select(o => new OptionForm { Text = o.Text, IsCorrect = o.IsCorrect }).ToList()
    };
}

public class QuizEditorModel
{
    public Dictionary<int, QuestionPreset> Presets { get; init; } = [];
    public required Quiz Quiz { get; init; }
    public required QuestionForm Form { get; init; }
    public bool WasAssigned { get; init; }
    public bool HasAttempts { get; init; }
    public List<QuizUsage> Usage { get; init; } = [];
}

public record RegradePageModel(int AssignmentId, List<RegradeQuestion> Questions);
public record RegradeFormModel(int AssignmentId, RegradeQuestion Question);

public class AssignmentInput
{
    public bool DeviceLock { get; set; }
    public bool MonitorScreen { get; set; }
    [Range(1, 20)] public int? AutoSubmitLeaves { get; set; }
    public FlashcardMode FlashcardMode { get; set; }
    public bool ThresholdEnabled { get; set; }
    public decimal? CriticalThreshold { get; set; }
    public bool CriticalQuestionsEnabled { get; set; }
    public List<int> CriticalQuestionIds { get; set; } = [];
    public CriticalSettings CriticalSettings() => new(ThresholdEnabled, CriticalThreshold, CriticalQuestionsEnabled, CriticalQuestionIds);
    public StrangerPolicy StrangerPolicy { get; set; } = StrangerPolicy.Reject;
    public bool IgnoreQuestionTimers { get; set; }
    public AssignmentForm Form { get; set; } = AssignmentForm.QuizOnly;
    public RequiredParts RequiredParts { get; set; } = RequiredParts.Quiz;
    public int? StudySetId { get; set; }
    public bool AllowFlashcardsAfterDue { get; set; } = true;
    public bool AllowQuizSelfReview { get; set; }
    public bool AllowCardSelfReview { get; set; }
    public bool AllowOfflineCards { get; set; }
    public AssignmentMode Mode { get; set; } = AssignmentMode.Practice;
    public int? DurationMinutes { get; set; }
    public bool ShuffleQuestions { get; set; }
    public bool ShuffleOptions { get; set; }
    public int ClassroomId { get; set; }
    public int QuizId { get; set; }

    [Display(Name = "Mở lúc (giờ Việt Nam)")]
    public DateTime? OpensAt { get; set; }

    [Display(Name = "Hạn nộp (giờ Việt Nam)")]
    public DateTime? DueAt { get; set; }

    [Display(Name = "Số lượt tối đa (tính cả lượt đầu)")]
    public int? MaxAttempts { get; set; }
    public int? MasteryThreshold { get; set; }
    public int? AnswersPerQuestion { get; set; }
    public bool ScoreRetries { get; set; }

    public ScoreSelection ScoreSelection { get; set; } = ScoreSelection.Highest;
    public ScoreScale ScoreScale { get; set; } = ScoreScale.Ten;
    public RoundingMode Rounding { get; set; } = RoundingMode.TwoDecimals;
    public AudiencePolicy AudiencePolicy { get; set; } = AudiencePolicy.IncludeLateJoiners;
}

public class AssignmentCreateModel
{
    public Dictionary<int, List<CriticalQuestionOption>> CriticalQuestions { get; init; } = [];
    public AssignmentPreset PracticePreset { get; init; } = SettingPresetRules.Assignment(null, AssignmentMode.Practice);
    public AssignmentPreset TestPreset { get; init; } = SettingPresetRules.Assignment(null, AssignmentMode.Test);
    public required Classroom Classroom { get; init; }
    public required List<Quiz> ReadyQuizzes { get; init; }
    public required AssignmentInput Form { get; init; }
    public List<StudySet> StudySets { get; init; } = [];
}

public class AssignmentDetailsModel
{
    public bool CanReopenPractice { get; init; }
    public GradebookMatrix? Matrix { get; init; }
    public bool CanDeleteReport { get; init; }
    public DateTime? UndoCodeUntil { get; init; }
    public string? HiddenContentReason { get; init; }
    public required Assignment Assignment { get; init; }
    public required string Code { get; init; }
    public required int OpenAttempts { get; init; }
    public required List<GradebookRow> Rows { get; init; }
    public List<Participant> Waiting { get; init; } = [];
    public List<RetakeRequest> RetakeRequests { get; init; } = [];
    public bool HasUnusedRetakes { get; init; }
    public List<ForgottenCard> ForgottenCards { get; init; } = [];
    public bool CanEditMastery { get; init; }
    public LecturerSurveyResults? Surveys { get; init; }
    public bool Ungraded { get; init; }
    public List<RegradeQuestion> RegradeQuestions { get; init; } = [];
}

// Một câu trên màn hình làm bài. Đáp án đúng chỉ có khi câu đã được trả lời.
public record PlayQuestion(
    long Id,
    int Number,
    QuestionType Type,
    string Prompt,
    IReadOnlyList<PlayOption> Options,
    bool Answered,
    bool Flagged,
    IReadOnlyList<int> Selected,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<int>? CorrectIds,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? IsCorrect,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Explanation,
    int BlankCount = 0,
    IReadOnlyList<string>? Values = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? CorrectAnswers = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<bool>? BlankCorrect = null,
    bool IsRequired = false, int? MaxLength = null, int AnswerCount = 0, bool IsMastered = false, bool CanRetry = false,
    int? TimeLimitSeconds = null, bool Locked = false, bool BodyHidden = false,
    int QuestionVersion = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? ReviewNotices = null);

public record OpenQuestionResult(PlayQuestion Question, int Total, int? SecondsLeft, bool Locked, bool Expired)
    : QuestionClock(SecondsLeft, Locked, Expired);

public record PlayOption(int Id, string Text);

public class LearnerAssignmentModel
{
    public bool CanSelfReview { get; init; }
    public bool CanSelfReviewCards { get; init; }
    public Dictionary<long, string?> AttemptCriticalTexts { get; init; } = [];
    public int? RemainingAttempts { get; init; }
    public DateTime? PersonalDeadline { get; init; }
    public string? HiddenContentNotice { get; init; }
    public required Assignment Assignment { get; init; }
    public required List<Attempt> Attempts { get; init; }
    public required bool CanStart { get; init; }
    public string? CannotStartReason { get; init; }
    public bool IsWaiting { get; init; }
    public bool ShowScores { get; init; }
    public SelectedScore? Score { get; init; }
    public List<RetakeRequest> RetakeRequests { get; init; } = [];
    public bool CanRequestRetake { get; init; }
    public CardCounts? Cards { get; init; }
    public CompletionStatus? Completion { get; init; }
    public CompletionLabels Labels { get; init; }
    public string? MissingCards { get; init; }
    public bool CanStudyCards { get; init; }
    public string? RequirementsNotice { get; init; }
    public string? Mastery { get; init; }
    public Dictionary<long, string?> AttemptMastery { get; init; } = [];
    public bool CanSeeSurveyResults { get; init; }
    public bool Ungraded { get; init; }
    // (42A) người ngoài lớp/khách không thấy link lớp (bấm vào sẽ 403).
    public bool IsClassMember { get; init; }
}

public record AnswerRequest(long AttemptQuestionId, Guid ClientRequestId, List<int> Selected, List<string>? Blanks = null, string? Value = null, int? QuestionVersion = null);

public record QuestionRef(long AttemptQuestionId);
public record OpenQuestionRequest(long AttemptQuestionId, bool Retry = false, int? QuestionVersion = null);

public record DraftRequest(long AttemptQuestionId, List<int> Selected, List<string>? Blanks = null, string? Value = null, int? QuestionVersion = null);

// Bài làm theo đề tài liệu (Papers/Create).
public class PaperInput
{
    public int ClassroomId { get; set; }
    [Required(ErrorMessage = "Hãy nhập tên bài."), StringLength(200)] public string? Title { get; set; }
    [Range(1, 180, ErrorMessage = "Thời lượng làm bài phải từ 1 đến 180 phút.")] public int DurationMinutes { get; set; } = 60;
    public DateTime? OpensAt { get; set; }
    public bool DeviceLock { get; set; }
    public bool MonitorScreen { get; set; } = true;
    [Range(1, 20)] public int? AutoSubmitLeaves { get; set; }
    public AudiencePolicy AudiencePolicy { get; set; } = AudiencePolicy.IncludeLateJoiners;
}
