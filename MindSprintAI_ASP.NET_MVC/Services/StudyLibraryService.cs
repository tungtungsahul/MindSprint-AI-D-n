using Microsoft.EntityFrameworkCore;
using MindSprintAI_ASP.NET_MVC.Data;
using MindSprintAI_ASP.NET_MVC.Data.Entities;

namespace MindSprintAI_ASP.NET_MVC.Services;

public record OptionInput(string Text, bool IsCorrect);

public record ChoiceQuestionInput(
    QuestionType Type,
    string Prompt,
    string? Explanation,
    decimal Points,
    bool KeepOptionOrder,
    IReadOnlyList<OptionInput> Options,
    bool IsRequired = false, int? TimeLimitSeconds = null);

public record InputQuestionInput(QuestionType Type, string Prompt, string? Explanation, decimal Points,
    IReadOnlyList<IReadOnlyList<string>> Blanks, bool CaseSensitive = false, bool AcceptNoDiacritics = false,
    bool IgnorePunctuation = false, bool AcceptAnyBlankOrder = false, BlankScoring BlankScoring = BlankScoring.AllOrNothing,
    string? NumberAnswer = null, string? NumberTolerance = null, int? TimeLimitSeconds = null);

public static class QuestionRules
{
    public const int MinOptions = 2, MaxOptions = 6;

    public static string? ValidateTimeLimit(int? seconds) => seconds is < 5 or > 3600
        ? "Giờ riêng phải từ 5 đến 3600 giây, hoặc để trống." : null;

    // Điều kiện "Sẵn sàng giao" của một câu (32A). Trả về null nếu hợp lệ.
    public static string? Validate(Question q)
    {
        if (ValidateTimeLimit(q.TimeLimitSeconds) is { } timerError) return timerError;
        if (string.IsNullOrWhiteSpace(q.Prompt)) return "Chưa nhập nội dung câu hỏi.";
        var survey = SurveyRules.IsSurvey(q.Type);
        if (!survey && q.Points <= 0) return "Điểm của câu phải lớn hơn 0.";
        if (survey && q.Points != 0) return "Khảo sát không có điểm.";
        if (q.Type == QuestionType.SurveyText) return q.MaxLength == 500 ? null : "Ý kiến tối đa 500 ký tự.";
        if (q.Type == QuestionType.TextInput)
        {
            if (q.Blanks.Count is < 1 or > 5) return "Câu điền chữ phải có từ 1 đến 5 ô.";
            var snapshot = QuestionSnapshot.From(q);
            for (var i = 0; i < snapshot.Blanks.Count; i++)
                if (!snapshot.Blanks[i].Answers.Any(a => TextMatching.Normalize(a, snapshot).Length > 0))
                    return $"Ô {i + 1} chưa có đáp án.";
            return Enum.IsDefined(q.BlankScoring) ? null : "Cách chấm theo ô không hợp lệ.";
        }
        if (q.Type == QuestionType.NumberInput)
            return q.NumberAnswer == null ? "Chưa nhập đáp án số." : q.NumberTolerance < 0 ? "Dung sai phải từ 0 trở lên." : null;
        if (q.Options.Count is < MinOptions or > MaxOptions) return "Mỗi câu có từ 2 đến 6 lựa chọn.";
        if (q.Options.Any(o => string.IsNullOrWhiteSpace(o.Text))) return "Có lựa chọn chưa nhập nội dung.";
        var correct = q.Options.Count(o => o.IsCorrect);
        return q.Type switch
        {
            QuestionType.SingleChoice when correct != 1 => "Câu chọn một phải có đúng 1 đáp án đúng.",
            QuestionType.MultipleChoice when correct < 1 => "Câu chọn nhiều phải có ít nhất 1 đáp án đúng.",
            QuestionType.SingleChoice or QuestionType.MultipleChoice => null,
            QuestionType.SurveySingle or QuestionType.SurveyMultiple when correct == 0 => null,
            _ => "Dạng câu này chưa hỗ trợ ở bản hiện tại."
        };
    }
}

public partial class StudyLibraryService(AppDbContext db, TimeProvider? clock = null, ILearnNotifier? notifier = null)
{
    // (D5b) bộ đếm cá nhân dùng chung truy vấn với chuông; view chỉ hiển thị.
    public async Task<(Dictionary<int, int> Sets, int Total)> SrsDueCountsAsync(string userId)
    {
        var counts = await new SrsDueService(db, clock ?? TimeProvider.System).PersonalCountsAsync(userId);
        return (counts, counts.Values.Sum());
    }

    public const int MaxCategory = 60;
    // Gợi ý sẵn = ba danh mục của nhóm; người dùng gõ tên khác thì thành danh mục mới (như "Tạo danh mục mới" của nhóm).
    public static readonly string[] SuggestedCategories = ["Tiếng Anh", "Lập trình", "Kiến thức chung"];
    public static string? CleanCategory(string? category)
    {
        category = string.IsNullOrWhiteSpace(category) ? null : string.Join(' ', category.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return category?.Length > MaxCategory ? throw new BusinessException($"Danh mục tối đa {MaxCategory} ký tự.") : category;
    }

    public async Task<StudySet> CreateStudySetAsync(string title, string userId, string? category = null)
    {
        var set = new StudySet { Title = RequireText(title, "Tên bộ học"), OwnerId = userId, Category = CleanCategory(category) };
        db.StudySets.Add(set);
        db.Audit?.Add(AuditAction.StudySetCreated, "StudySet", set.Id, userId, resolveTarget: () => set.Id.ToString()); // (NK3)
        await db.SaveChangesAsync();
        return set;
    }

    public async Task RenameStudySetAsync(int studySetId, string title, string userId, string? category = null)
    {
        var set = await GetOwnedStudySetAsync(studySetId, userId);
        set.Title = RequireText(title, "Tên bộ học");
        set.Category = CleanCategory(category);
        db.Audit?.Add(AuditAction.StudySetEdited, "StudySet", studySetId, userId); // (NK3)
        await db.SaveChangesAsync();
    }

    public Task<List<StudySet>> ListOwnedAsync(string userId) =>
        db.StudySets.Where(s => s.OwnerId == userId && s.DeletedAt == null && s.ArchivedAt == null)
            .Include(s => s.Quizzes.Where(q => q.DeletedAt == null && q.ArchivedAt == null))
            .OrderByDescending(s => s.CreatedAt).ToListAsync();

    // (48A, C3; người dùng chốt 2026-11-05) giao được bộ học của mình hoặc bộ học lớp này đã dùng; bộ học riêng của GV khác không hiện.
    public async Task<List<StudySet>> ListAssignableAsync(int classroomId, string userId)
    {
        var lecturers = db.ClassMembers.Where(m => m.ClassroomId == classroomId && m.Classroom.DeletedAt == null
            && m.Status == ClassMemberStatus.Active && (m.Role == ClassRole.MainLecturer || m.Role == ClassRole.AssistantLecturer));
        if (!await lecturers.AnyAsync(m => m.UserId == userId)) throw new ForbiddenException();
        // (5b) bộ học bị Admin ẩn không hiện trong danh sách giao bài.
        return await db.StudySets.Where(s => s.DeletedAt == null && s.ArchivedAt == null && s.HiddenByAdminAt == null && (s.OwnerId == userId
                || db.Assignments.Any(a => a.ClassroomId == classroomId && a.StudySetId == s.Id)))
            .Include(s => s.Quizzes.Where(q => q.DeletedAt == null && q.ArchivedAt == null)).OrderByDescending(s => s.CreatedAt).ToListAsync();
    }

    public async Task<StudySet> GetOwnedStudySetAsync(int studySetId, string userId)
    {
        var set = await db.StudySets.FirstOrDefaultAsync(s => s.Id == studySetId && s.DeletedAt == null && s.ArchivedAt == null)
            ?? throw new BusinessException("Không tìm thấy bộ học.");
        if (set.OwnerId != userId) throw new ForbiddenException(); // chỉ chủ bộ học (C3)
        return set;
    }

    public async Task<Quiz> CreateQuizAsync(int studySetId, string title, string userId)
    {
        await GetOwnedStudySetAsync(studySetId, userId);
        var quiz = new Quiz { StudySetId = studySetId, Title = RequireText(title, "Tên quiz") };
        db.Quizzes.Add(quiz);
        db.Audit?.Add(AuditAction.QuizCreated, "Quiz", quiz.Id, userId, resolveTarget: () => quiz.Id.ToString()); // (NK3)
        await db.SaveChangesAsync();
        return quiz;
    }

    public async Task<Quiz> GetOwnedQuizAsync(int quizId, string userId)
    {
        var quiz = await db.Quizzes
            .Include(q => q.StudySet)
            .Include(q => q.Questions.Where(x => x.DeletedAt == null).OrderBy(x => x.OrderIndex)).ThenInclude(x => x.Options.OrderBy(o => o.OrderIndex))
            .Include(q => q.Questions).ThenInclude(x => x.Blanks).ThenInclude(b => b.Answers)
            .FirstOrDefaultAsync(q => q.Id == quizId && q.DeletedAt == null && q.ArchivedAt == null
                && q.StudySet.DeletedAt == null && q.StudySet.ArchivedAt == null)
            ?? throw new BusinessException("Không tìm thấy quiz.");
        if (quiz.StudySet.OwnerId != userId) throw new ForbiddenException();
        await EnsureQuizNotTransferredAsync(db, quiz);
        return quiz;
    }

    internal static async Task EnsureQuizNotTransferredAsync(AppDbContext db, Quiz quiz)
    {
        if (await db.Assignments.AnyAsync(a => a.QuizId == quiz.Id && (a.StudySetId ?? quiz.StudySetId) != quiz.StudySetId))
            throw new BusinessException("Quiz này đang dùng trong bài giao của lớp đã chuyển cho giảng viên khác nên không khôi phục được.");
    }

    public Task<bool> HasAttemptsAsync(int quizId) =>
        // (27, 49B) sau bàn giao, bản chụp vẫn tham chiếu câu gốc dù bài giao đã trỏ sang bản sao.
        db.Attempts.AnyAsync(a => a.Participant.Assignment.QuizId == quizId || a.Questions.Any(q => q.Question.QuizId == quizId));

    public Task<bool> WasAssignedAsync(int quizId) =>
        db.Quizzes.AnyAsync(q => q.Id == quizId && (q.EverAssignedAt != null || db.Assignments.Any(a => a.QuizId == q.Id)));

    // Thêm câu (questionId = null) hoặc sửa câu. expectedVersion chống ghi đè khi hai tab cùng sửa.
    public Task<Question> SaveChoiceQuestionAsync(int quizId, int? questionId, int? expectedVersion,
        ChoiceQuestionInput input, string userId, QuestionEditKind editKind = QuestionEditKind.Content, int extraSessionMinutes = 0)
    {
        if (QuestionRules.ValidateTimeLimit(input.TimeLimitSeconds) is { } timerError) throw new BusinessException(timerError);
        if (!SurveyRules.IsChoice(input.Type) && input.Type != QuestionType.SurveyText)
            throw new BusinessException("Dạng câu không hợp lệ.");
        if (input.Type != QuestionType.SurveyText && input.Options.Count is (< QuestionRules.MinOptions or > QuestionRules.MaxOptions))
            throw new BusinessException("Mỗi câu có từ 2 đến 6 lựa chọn.");
        if (input.Type == QuestionType.SingleChoice && input.Options.Count(o => o.IsCorrect) > 1)
            throw new BusinessException("Câu chọn một chỉ có 1 đáp án đúng.");
        var survey = SurveyRules.IsSurvey(input.Type);
        var candidate = new Question
        {
            Type = input.Type, Prompt = input.Prompt.Trim(), Explanation = input.Explanation?.Trim(),
            Points = survey ? 0 : input.Points, IsRequired = survey && input.IsRequired,
            MaxLength = input.Type == QuestionType.SurveyText ? 500 : null,
            KeepOptionOrder = input.KeepOptionOrder, TimeLimitSeconds = input.TimeLimitSeconds,
            Options = (input.Type == QuestionType.SurveyText ? [] : input.Options).Select((o, i) =>
                new QuestionOption { OrderIndex = i, Text = o.Text.Trim(), IsCorrect = !survey && o.IsCorrect }).ToList()
        };
        return SaveQuestionAsync(quizId, questionId, expectedVersion, candidate, userId, editKind, extraSessionMinutes);
    }

    public async Task DeleteQuestionAsync(int quizId, int questionId, string userId)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        await QuizChangeLock.AcquireAsync(db, quizId);
        var quiz = await GetOwnedQuizAsync(quizId, userId);
        if (await HasAttemptsAsync(quizId)) throw new BusinessException(QuestionEditRules.StructureBlocked);
        if (await WasAssignedAsync(quizId))
            throw new BusinessException("Quiz đã được giao nên không xóa câu được. Muốn sửa nhiều, hãy nhân bản bộ học rồi sửa bản sao.");
        var question = quiz.Questions.FirstOrDefault(q => q.Id == questionId && q.DeletedAt == null)
            ?? throw new BusinessException("Không tìm thấy câu hỏi.");
        // Câu chưa giao chỉ xóa mềm; job 30 ngày mới dọn cả lựa chọn, ô và lịch sử nháp.
        question.DeletedAt = EditNow;
        var order = 0;
        foreach (var q in quiz.Questions.Where(q => q.Id != questionId && q.DeletedAt == null).OrderBy(q => q.OrderIndex))
            q.OrderIndex = order++;
        db.Audit?.Add(AuditAction.QuestionDeleted, "Question", questionId, userId); // (NK3)
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    public async Task MoveQuestionAsync(int quizId, int questionId, int direction, string userId)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        await QuizChangeLock.AcquireAsync(db, quizId);
        var assignments = await db.Assignments.Where(a => a.QuizId == quizId).OrderBy(a => a.Id).Select(a => a.Id).ToListAsync();
        foreach (var id in assignments) await AttemptLock.AssignmentAsync(db, id);
        var quiz = await GetOwnedQuizAsync(quizId, userId);
        if (await HasAttemptsAsync(quizId))
            throw new BusinessException(QuestionEditRules.MoveBlocked);
        var list = quiz.Questions.Where(q => q.DeletedAt == null).OrderBy(q => q.OrderIndex).ToList();
        var i = list.FindIndex(q => q.Id == questionId);
        var j = i + Math.Sign(direction);
        if (i < 0 || j < 0 || j >= list.Count) return;
        (list[i].OrderIndex, list[j].OrderIndex) = (list[j].OrderIndex, list[i].OrderIndex);
        db.Audit?.Add(AuditAction.QuestionEdited, "Question", questionId, userId); // (NK3)
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    // "Sẵn sàng giao" (32A): một câu lỗi thì cả quiz chưa giao được; trả lỗi theo số câu.
    public async Task<IReadOnlyList<string>> MarkReadyAsync(int quizId, string userId)
    {
        var quiz = await GetOwnedQuizAsync(quizId, userId);
        var errors = quiz.Questions.Where(q => q.DeletedAt == null).OrderBy(q => q.OrderIndex)
            .Select((q, i) => QuestionRules.Validate(q) is { } e ? $"Câu {i + 1}: {e}" : null)
            .OfType<string>().ToList();
        if (!quiz.Questions.Any(q => q.DeletedAt == null)) errors.Add("Quiz chưa có câu hỏi nào.");
        if (errors.Count == 0)
        {
            quiz.Status = QuizStatus.Ready;
            db.Audit?.Add(AuditAction.QuizEdited, "Quiz", quizId, userId); // (NK3)
            await db.SaveChangesAsync();
        }
        return errors;
    }

    public async Task MarkDraftAsync(int quizId, string userId)
    {
        var quiz = await GetOwnedQuizAsync(quizId, userId);
        if (await WasAssignedAsync(quizId))
            throw new BusinessException("Quiz đã được giao nên không chuyển về Nháp được.");
        quiz.Status = QuizStatus.Draft;
        db.Audit?.Add(AuditAction.QuizEdited, "Quiz", quizId, userId); // (NK3)
        await db.SaveChangesAsync();
    }

    // Xóa bộ học/quiz chưa từng giao: chỉ ẩn (DeletedAt); khôi phục và tự xóa sau 30 ngày ở tầng 5.
    public async Task DeleteQuizAsync(int quizId, string userId)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        await QuizChangeLock.AcquireAsync(db, quizId);
        var quiz = await GetOwnedQuizAsync(quizId, userId);
        if (await HasAttemptsAsync(quizId)) throw new BusinessException(QuestionEditRules.DeleteQuizBlocked);
        if (await WasAssignedAsync(quizId))
        {
            await EnsureAssignmentsEndedAsync(db.Assignments.Where(a => a.QuizId == quizId));
            quiz.ArchivedAt = EditNow;
        }
        quiz.DeletedAt = EditNow;
        db.Audit?.Add(AuditAction.QuizDeleted, "Quiz", quizId, userId); // (NK3)
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    public async Task DeleteStudySetAsync(int studySetId, string userId)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        var quizIds = await db.Quizzes.Where(q => q.StudySetId == studySetId).OrderBy(q => q.Id).Select(q => q.Id).ToListAsync();
        foreach (var quizId in quizIds) await QuizChangeLock.AcquireAsync(db, quizId);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT [Id] FROM [StudySets] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {studySetId}");
        var set = await GetOwnedStudySetAsync(studySetId, userId);
        var used = db.Assignments.Where(a => a.StudySetId == studySetId || (a.Quiz != null && a.Quiz.StudySetId == studySetId));
        await EnsureAssignmentsEndedAsync(used);
        if (set.EverAssignedAt != null || await used.AnyAsync()) set.ArchivedAt = EditNow;
        set.DeletedAt = EditNow;
        db.Audit?.Add(AuditAction.StudySetDeleted, "StudySet", studySetId, userId); // (NK3)
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    private async Task EnsureAssignmentsEndedAsync(IQueryable<Assignment> used)
    {
        var now = EditNow;
        if (await used.AnyAsync(a => a.Status != AssignmentStatus.Closed
            && (a.Mode == AssignmentMode.Test ? a.TestEndsAt == null || a.TestEndsAt > now : a.DueAt == null || a.DueAt > now))
            || await db.Attempts.AnyAsync(x => used.Any(a => a.Id == x.Participant.AssignmentId)
                && x.Kind != AttemptKind.SelfReview
                && (x.Status == AttemptStatus.InProgress || x.Status == AttemptStatus.Paused) && (x.DeadlineAt == null || x.DeadlineAt > now)))
            throw new BusinessException("Chưa xóa được vì còn bài giao đang mở dùng bộ học/quiz này. Hãy đóng các bài giao đó rồi thử lại.");
    }

    public Task<Question> SaveInputQuestionAsync(int quizId, int? questionId, int? expectedVersion,
        InputQuestionInput input, string userId, QuestionEditKind editKind = QuestionEditKind.Content, int extraSessionMinutes = 0)
    {
        var candidate = BuildInputQuestion(input);
        if (QuestionRules.Validate(candidate) is { } error) throw new BusinessException(error);
        return SaveQuestionAsync(quizId, questionId, expectedVersion, candidate, userId, editKind, extraSessionMinutes);
    }

    internal static Question BuildInputQuestion(InputQuestionInput input)
    {
        if (input.Type is not (QuestionType.TextInput or QuestionType.NumberInput)) throw new BusinessException("Dạng câu không hợp lệ.");
        var question = new Question
        {
            Type = input.Type, Prompt = input.Prompt.Trim(), Explanation = input.Explanation?.Trim(), Points = input.Points,
            TimeLimitSeconds = input.TimeLimitSeconds,
            CaseSensitive = input.CaseSensitive, AcceptNoDiacritics = input.AcceptNoDiacritics,
            IgnorePunctuation = input.IgnorePunctuation, AcceptAnyBlankOrder = input.AcceptAnyBlankOrder, BlankScoring = input.BlankScoring,
            NumberAnswer = input.Type == QuestionType.NumberInput ? NumberParser.Parse(input.NumberAnswer) : null,
            NumberTolerance = input.Type == QuestionType.NumberInput ? NumberParser.Parse(string.IsNullOrWhiteSpace(input.NumberTolerance) ? "0" : input.NumberTolerance) : 0,
            Blanks = input.Type != QuestionType.TextInput ? [] : input.Blanks.Select((answers, i) => new QuestionBlank
            {
                BlankIndex = i, Answers = answers.Select((text, j) => new BlankAnswer { OrderIndex = j, Text = text }).ToList()
            }).ToList()
        };
        var snapshot = QuestionSnapshot.From(question);
        foreach (var blank in question.Blanks)
            blank.Answers = blank.Answers.Where(a => TextMatching.Normalize(a.Text, snapshot).Length > 0).ToList();
        return question;
    }

    private async Task SaveWithConcurrencyAsync()
    {
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateConcurrencyException)
        {
            throw new BusinessException(QuestionEditRules.Stale);
        }
    }

    // Cột tên giới hạn 200 ký tự (AppDbContext); báo lỗi dễ hiểu thay vì để CSDL từ chối thành trang "Có lỗi xảy ra".
    private static string RequireText(string? value, string field) =>
        string.IsNullOrWhiteSpace(value) ? throw new BusinessException($"{field} không được để trống.")
        : value.Trim().Length > 200 ? throw new BusinessException($"{field} tối đa 200 ký tự.") : value.Trim();
}
