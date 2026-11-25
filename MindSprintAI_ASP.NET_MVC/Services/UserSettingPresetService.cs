using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MindSprintAI_ASP.NET_MVC.Data;
using MindSprintAI_ASP.NET_MVC.Data.Entities;
using MindSprintAI_ASP.NET_MVC.Models;

namespace MindSprintAI_ASP.NET_MVC.Services;

// Chỉ các option này được lưu; tuyệt đối không serialize nguyên form/bài giao/câu hỏi.
public record AssignmentPreset
{
    public bool DeviceLock { get; init; }
    public bool MonitorScreen { get; init; }
    public int? AutoSubmitLeaves { get; init; }
    public FlashcardMode FlashcardMode { get; init; }
    public bool ThresholdEnabled { get; init; }
    public decimal? CriticalThreshold { get; init; }
    public bool CriticalQuestionsEnabled { get; init; }
    public AssignmentForm Form { get; init; } = AssignmentForm.QuizOnly;
    public RequiredParts RequiredParts { get; init; } = RequiredParts.Quiz;
    public bool AllowFlashcardsAfterDue { get; init; } = true;
    public bool AllowQuizSelfReview { get; init; }
    public bool AllowCardSelfReview { get; init; }
    public bool AllowOfflineCards { get; init; }
    public int? DurationMinutes { get; init; }
    public int? MaxAttempts { get; init; }
    public ScoreSelection ScoreSelection { get; init; } = ScoreSelection.Highest;
    public ScoreScale ScoreScale { get; init; } = ScoreScale.Ten;
    public RoundingMode Rounding { get; init; } = RoundingMode.TwoDecimals;
    public bool ShuffleQuestions { get; init; }
    public bool ShuffleOptions { get; init; }
    public int? MasteryThreshold { get; init; }
    public int? AnswersPerQuestion { get; init; }
    public bool ScoreRetries { get; init; }
    public bool IgnoreQuestionTimers { get; init; }
    public bool AllowAiAssistant { get; init; } = true;
    public bool AllowAiAfterTest { get; init; }
    public decimal? PassMark { get; init; }

    public AssignmentInput ToInput(int classroomId, AssignmentMode mode) => new()
    {
        ClassroomId = classroomId, Mode = mode, Form = Form, RequiredParts = RequiredParts,
        DeviceLock = DeviceLock, MonitorScreen = MonitorScreen, AutoSubmitLeaves = AutoSubmitLeaves,
        FlashcardMode = FlashcardMode, ThresholdEnabled = ThresholdEnabled, CriticalThreshold = CriticalThreshold,
        CriticalQuestionsEnabled = CriticalQuestionsEnabled,
        AllowFlashcardsAfterDue = AllowFlashcardsAfterDue, DurationMinutes = DurationMinutes,
        AllowQuizSelfReview = mode == AssignmentMode.Practice && AllowQuizSelfReview,
        AllowCardSelfReview = mode == AssignmentMode.Practice && AllowCardSelfReview,
        AllowOfflineCards = mode == AssignmentMode.Practice && AllowOfflineCards,
        MaxAttempts = MaxAttempts, ScoreSelection = ScoreSelection, ScoreScale = ScoreScale, Rounding = Rounding,
        ShuffleQuestions = ShuffleQuestions, ShuffleOptions = ShuffleOptions, MasteryThreshold = MasteryThreshold,
        AnswersPerQuestion = AnswersPerQuestion, ScoreRetries = ScoreRetries, IgnoreQuestionTimers = IgnoreQuestionTimers
    };

    public static AssignmentPreset From(Assignment a) => new()
    {
        Form = a.Form, RequiredParts = a.RequiredParts, AllowFlashcardsAfterDue = a.AllowFlashcardsAfterDue,
        AllowQuizSelfReview = a.AllowQuizSelfReview, AllowCardSelfReview = a.AllowCardSelfReview, AllowOfflineCards = a.AllowOfflineCards,
        DeviceLock = a.DeviceLock, MonitorScreen = a.MonitorScreen, AutoSubmitLeaves = a.AutoSubmitLeaves,
        FlashcardMode = a.FlashcardMode, ThresholdEnabled = a.CriticalThreshold != null, CriticalThreshold = a.CriticalThreshold,
        CriticalQuestionsEnabled = a.CriticalQuestionsEnabled,
        DurationMinutes = a.DurationMinutes, MaxAttempts = a.MaxAttempts, ScoreSelection = a.ScoreSelection,
        ScoreScale = a.ScoreScale, Rounding = a.Rounding, ShuffleQuestions = a.ShuffleQuestions,
        ShuffleOptions = a.ShuffleOptions, MasteryThreshold = a.MasteryThreshold, AnswersPerQuestion = a.AnswersPerQuestion,
        ScoreRetries = a.ScoreRetries, IgnoreQuestionTimers = a.IgnoreQuestionTimers,
        AllowAiAssistant = a.AllowAiAssistant, AllowAiAfterTest = a.AllowAiAfterTest, PassMark = a.PassMark
    };
}

public record QuestionPreset
{
    public bool CaseSensitive { get; init; }
    public bool AcceptNoDiacritics { get; init; }
    public bool IgnorePunctuation { get; init; }
    public bool AcceptAnyBlankOrder { get; init; }
    public BlankScoring BlankScoring { get; init; } = BlankScoring.AllOrNothing;
    public bool KeepOptionOrder { get; init; }
    public decimal Points { get; init; } = 1;
    public int? TimeLimitSeconds { get; init; }
    public bool IsRequired { get; init; }
    public int? MaxLength { get; init; }

    public QuestionForm ToForm(int quizId, QuestionType type) => new()
    {
        QuizId = quizId, Type = type, CaseSensitive = CaseSensitive, AcceptNoDiacritics = AcceptNoDiacritics,
        IgnorePunctuation = IgnorePunctuation, AcceptAnyBlankOrder = AcceptAnyBlankOrder, BlankScoring = BlankScoring,
        KeepOptionOrder = KeepOptionOrder, Points = Points, TimeLimitSeconds = TimeLimitSeconds,
        IsRequired = IsRequired, MaxLength = MaxLength, Options = [new(), new(), new(), new()]
    };

    public static QuestionPreset From(Question q) => new()
    {
        CaseSensitive = q.CaseSensitive, AcceptNoDiacritics = q.AcceptNoDiacritics, IgnorePunctuation = q.IgnorePunctuation,
        AcceptAnyBlankOrder = q.AcceptAnyBlankOrder, BlankScoring = q.BlankScoring, KeepOptionOrder = q.KeepOptionOrder,
        Points = q.Points, TimeLimitSeconds = q.TimeLimitSeconds, IsRequired = q.IsRequired, MaxLength = q.MaxLength
    };
}

public static class SettingPresetRules
{
    public static AssignmentPreset Assignment(string? json, AssignmentMode mode)
    {
        var test = mode == AssignmentMode.Test;
        var defaults = new AssignmentPreset { DurationMinutes = test ? 45 : null,
            ScoreSelection = test ? ScoreSelection.Latest : ScoreSelection.Highest,
            ShuffleQuestions = test, ShuffleOptions = test, AllowAiAssistant = !test };
        var p = Read(json, defaults);
        return p with
        {
            Form = test ? AssignmentForm.QuizOnly : p.Form,
            AllowQuizSelfReview = !test && p.AllowQuizSelfReview,
            AllowCardSelfReview = !test && p.AllowCardSelfReview,
            AllowOfflineCards = !test && p.AllowOfflineCards,
            AutoSubmitLeaves = p.MonitorScreen && p.AutoSubmitLeaves is >= 1 and <= 20 ? p.AutoSubmitLeaves : null,
            RequiredParts = test ? RequiredParts.Quiz : p.RequiredParts,
            MasteryThreshold = test ? null : p.MasteryThreshold,
            AnswersPerQuestion = test || p.MasteryThreshold == null ? null : p.AnswersPerQuestion,
            ScoreRetries = !test && p.MasteryThreshold != null && p.ScoreRetries
        };
    }

    public static QuestionPreset Question(string? json, QuestionType type)
    {
        var defaults = new QuestionPreset { Points = SurveyRules.IsSurvey(type) ? 0 : 1,
            MaxLength = type == QuestionType.SurveyText ? 500 : null };
        var p = Read(json, defaults);
        return p with { Points = SurveyRules.IsSurvey(type) ? 0 : p.Points,
            MaxLength = defaults.MaxLength };
    }

    // Đọc từng trường để một giá trị cũ sai kiểu không làm mất các option còn hợp lệ.
    private static T Read<T>(string? json, T defaults) where T : class
    {
        T result = JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(defaults))!;
        if (string.IsNullOrWhiteSpace(json)) return result;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return result;
            foreach (var property in typeof(T).GetProperties())
            {
                if (!doc.RootElement.TryGetProperty(property.Name, out var field)) continue;
                try
                {
                    var value = field.Deserialize(property.PropertyType);
                    var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
                    if (value != null && type.IsEnum && !Enum.IsDefined(type, value)) continue;
                    if (Valid(property.Name, value)) property.SetValue(result, value);
                }
                catch (JsonException) { /* Giá trị cũ sai kiểu: giữ mặc định riêng trường đó. */ }
            }
        }
        catch (JsonException) { /* JSON cũ không đọc được: dùng mặc định hệ thống. */ }
        return result;
    }

    private static bool Valid(string name, object? value) => name switch
    {
        "DurationMinutes" => value is null or (>= 1 and <= 180),
        "MaxAttempts" or "AnswersPerQuestion" => value is null or >= 1,
        "MasteryThreshold" => value is null or 80 or 90 or 100,
        "TimeLimitSeconds" => value is null or (>= 5 and <= 3600),
        "Points" => value is decimal points && points > 0 && points <= 99999.9999m,
        "MaxLength" => value is null or 500,
        "PassMark" => value is null or (>= 0m and <= 100m),
        "CriticalThreshold" => value is null or (>= 0m and <= 99999999999999.9999m),
        _ => true
    };
}

public class UserSettingPresetService(AppDbContext db, TimeProvider clock)
{
    public static readonly int[] MemberPageSizes = [20, 30, 40, 50];

    public async Task<int> MemberPageSizeAsync(string userId)
    {
        var json = await JsonAsync("Classroom.MemberPageSize", userId);
        if (json != null)
        {
            try
            {
                var size = JsonSerializer.Deserialize<int>(json);
                if (MemberPageSizes.Contains(size)) return size;
            }
            catch (JsonException) { /* Giá trị cũ không hợp lệ: dùng 20 học viên mỗi trang. */ }
        }
        return 20;
    }

    public async Task SaveMemberPageSizeAsync(int size, string userId)
    {
        if (!MemberPageSizes.Contains(size))
            throw new BusinessException("Số học viên mỗi trang chỉ chọn 20, 30, 40 hoặc 50.");
        await SaveAsync("Classroom.MemberPageSize", size, userId);
        await db.SaveChangesAsync();
    }

    // Không nhận ownerId từ form; caller chỉ truyền UserId của phiên đăng nhập.
    private async Task EnsureUserAsync(string userId)
    {
        if (!await db.Users.AnyAsync(u => u.Id == userId && !u.IsGuest)) throw new ForbiddenException();
    }

    public async Task<AssignmentPreset> AssignmentAsync(AssignmentMode mode, string userId)
    {
        if (!Enum.IsDefined(mode)) throw new BusinessException("Chế độ không hợp lệ.");
        return SettingPresetRules.Assignment(await JsonAsync($"Assignment.{mode}", userId), mode);
    }

    public async Task<QuestionPreset> QuestionAsync(QuestionType type, string userId)
    {
        if (!Enum.IsDefined(type)) throw new BusinessException("Dạng câu không hợp lệ.");
        return SettingPresetRules.Question(await JsonAsync($"Question.{type}", userId), type);
    }

    private async Task<string?> JsonAsync(string scope, string userId)
    {
        await EnsureUserAsync(userId);
        return await db.UserSettingPresets.Where(p => p.UserId == userId && p.Scope == scope).Select(p => p.ValuesJson).SingleOrDefaultAsync();
    }

    internal Task SaveAssignmentAsync(Assignment a, string userId) => SaveAsync($"Assignment.{a.Mode}", AssignmentPreset.From(a), userId);
    internal Task SaveQuestionAsync(Question q, string userId) => SaveAsync($"Question.{q.Type}", QuestionPreset.From(q), userId);

    private async Task SaveAsync<T>(string scope, T value, string userId)
    {
        await EnsureUserAsync(userId);
        // Khóa cả khoảng khóa ghép khi dòng chưa có, tránh hai tab cùng INSERT preset.
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT [UserId] FROM [UserSettingPresets] WITH (UPDLOCK, HOLDLOCK) WHERE [UserId] = {userId} AND [Scope] = {scope}");
        var row = await db.UserSettingPresets.FindAsync(userId, scope);
        if (row == null) { row = new() { UserId = userId, Scope = scope }; db.UserSettingPresets.Add(row); }
        row.ValuesJson = JsonSerializer.Serialize(value); row.UpdatedAt = clock.GetUtcNow().UtcDateTime;
    }

    public async Task ResetAsync(string scope, string userId)
    {
        var scopes = Enum.GetValues<AssignmentMode>().Select(m => $"Assignment.{m}")
            .Concat(Enum.GetValues<QuestionType>().Select(t => $"Question.{t}"));
        if (!scopes.Contains(scope)) throw new BusinessException("Cài đặt không hợp lệ.");
        await EnsureUserAsync(userId);
        await db.UserSettingPresets.Where(p => p.UserId == userId && p.Scope == scope).ExecuteDeleteAsync();
        foreach (var entry in db.ChangeTracker.Entries<UserSettingPreset>().Where(e => e.Entity.UserId == userId && e.Entity.Scope == scope).ToList())
            entry.State = EntityState.Detached;
    }
}
