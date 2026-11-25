using Microsoft.EntityFrameworkCore;
using MindSprintAI_ASP.NET_MVC.Data.Entities;

namespace MindSprintAI_ASP.NET_MVC.Services;

public record QuizUsage(int Id, string Title, string? Classroom, bool RunningTest, DateTime? CreatedAt = null);

public partial class StudyLibraryService
{
    private DateTime EditNow => (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;
    private ILearnNotifier EditNotifier => notifier ?? new NullLearnNotifier();

    public async Task<List<QuizUsage>> GetUsageAsync(int quizId, string userId)
    {
        await GetOwnedQuizAsync(quizId, userId);
        var now = EditNow;
        return await db.Assignments.Where(a => a.QuizId == quizId).OrderBy(a => a.Id)
            .Select(a => new QuizUsage(a.Id, a.Quiz!.Title, a.Classroom == null ? null : a.Classroom.Name,
                a.Mode == AssignmentMode.Test && a.Status == AssignmentStatus.Open && a.TestStartedAt != null && a.TestEndsAt > now, a.CreatedAt)).ToListAsync();
    }

    private async Task<Question> SaveQuestionAsync(int quizId, int? questionId, int? version, Question candidate,
        string userId, QuestionEditKind kind, int extraMinutes)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        await QuizChangeLock.AcquireAsync(db, quizId);
        var ids = await db.Assignments.Where(a => a.QuizId == quizId).OrderBy(a => a.Id).Select(a => a.Id).ToListAsync();
        var assignments = new List<Assignment>();
        foreach (var id in ids) assignments.Add(await AttemptLock.AssignmentAsync(db, id));
        var quiz = await GetOwnedQuizAsync(quizId, userId);
        if (!Enum.IsDefined(kind)) throw new BusinessException("Kiểu sửa không hợp lệ.");
        if (extraMinutes < 0) throw new BusinessException("Số phút cộng thêm phải từ 0 trở lên.");
        if (quiz.Status == QuizStatus.Ready && QuestionRules.Validate(candidate) is { } invalid)
            throw new BusinessException($"Quiz đang ở trạng thái Sẵn sàng giao nên câu phải hợp lệ: {invalid}");
        if (extraMinutes > 0 && kind == QuestionEditKind.Content && assignments.Any(a => a.Mode == AssignmentMode.Test
            && a.Status == AssignmentStatus.Open && a.TestStartedAt != null && a.TestEndsAt > EditNow && a.AnswersPublishedAt != null))
            throw new BusinessException("Đã công bố đáp án nên không cộng giờ được.");
        var hasAttempts = await HasAttemptsAsync(quizId);
        Question question;
        if (questionId == null)
        {
            if (hasAttempts) throw new BusinessException(QuestionEditRules.StructureBlocked);
            if (ids.Count > 0) throw new BusinessException("Quiz đã được giao nên không thêm câu được. Muốn sửa nhiều, hãy nhân bản bộ học rồi sửa bản sao.");
            question = candidate;
            question.QuizId = quizId;
            question.OrderIndex = quiz.Questions.Count(q => q.DeletedAt == null);
            db.Questions.Add(question);
        }
        else
        {
            question = quiz.Questions.SingleOrDefault(q => q.Id == questionId && q.DeletedAt == null) ?? throw new BusinessException("Không tìm thấy câu hỏi.");
            // (19, C3) đọc lại cả thành phần nếu DbContext từng mở trình soạn trước khi lấy khóa.
            await db.Entry(question).ReloadAsync();
            foreach (var o in question.Options) await db.Entry(o).ReloadAsync();
            if (version == null || question.Version != version) throw new BusinessException(QuestionEditRules.Stale);
            if (kind == QuestionEditKind.Minor && !QuestionEditRules.IsMinor(question, candidate))
                throw new BusinessException("Sửa nhỏ chỉ đổi chữ nội dung/lựa chọn/giải thích; hãy chọn Sửa nội dung/đáp án khi đổi dạng, điểm, đáp án hoặc cách chấm.");
            db.QuestionRevisions.Add(new QuestionRevision
            {
                QuestionId = question.Id, Version = question.Version, EditKind = kind,
                SnapshotJson = QuestionSnapshot.From(question).ToJson(), EditedById = userId, EditedAt = EditNow
            });
            CopyQuestion(question, candidate);
            db.Entry(question).State = EntityState.Modified;
        }
        var affected = new List<Attempt>();
        // (19) lấy khóa theo bài → thành viên → người làm → từng lượt, trước khi ghi bản mới.
        foreach (var assignment in assignments)
            affected.AddRange(await QuizChangeLock.AttemptsAsync(db, assignment));
        foreach (var a in affected) AttemptRules.ExpireIfDue(a, a.Participant, EditNow);
        await SaveWithConcurrencyAsync(); // lấy Id lựa chọn mới và Version đã tăng, vẫn trong cùng giao dịch.
        if (questionId != null)
        {
            var snap = QuestionSnapshot.From(question);
            foreach (var a in affected.Where(AttemptRules.IsOpen))
                if (a.Questions.SingleOrDefault(q => QuestionSnapshot.FromJson(q.SnapshotJson).OrderIndex == question.OrderIndex) is { } q)
                    QuestionEditRules.Apply(a, q, snap, question.Version, kind);
        }
        var extended = new List<Attempt>();
        if (kind == QuestionEditKind.Content && extraMinutes > 0)
            foreach (var a in assignments.Where(a => a.Mode == AssignmentMode.Test && a.Status == AssignmentStatus.Open
                && a.TestStartedAt != null && a.TestEndsAt > EditNow))
            {
                AssignmentService.ChangeDeadline(db, a, a.TestEndsAt!.Value.AddMinutes(extraMinutes), userId, EditNow, "Cộng giờ khi sửa đề (60, 63)");
                foreach (var attempt in affected.Where(x => x.Participant.AssignmentId == a.Id && AttemptRules.IsOpen(x) && x.Kind == AttemptKind.Normal))
                {
                    attempt.DeadlineAt = AttemptRules.DeadlineFor(a, attempt.Participant);
                    extended.Add(attempt);
                }
            }
        foreach (var p in affected.Select(a => a.Participant).Distinct()) await CardCompletion.RefreshAsync(db, p, EditNow);
        if (questionId == null)
            await new UserSettingPresetService(db, clock ?? TimeProvider.System).SaveQuestionAsync(question, userId);
        db.Audit?.Add(questionId == null ? AuditAction.QuestionCreated : AuditAction.QuestionEdited, "Question", question.Id, userId, resolveTarget: () => question.Id.ToString()); // (NK3)
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        if (questionId != null)
        {
            foreach (var a in affected.Where(AttemptRules.IsOpen))
                await EditNotifier.AttemptAsync(a.Id, "question-edited", new LearnNotice(AttemptId: a.Id, Url: $"/Learn/Play/{a.Id}"));
            await AssignmentService.NotifyDeadlinesAsync(EditNotifier, extended);
            foreach (var id in ids) await EditNotifier.LecturersAsync(id);
        }
        return question;
    }

    private void CopyQuestion(Question q, Question candidate)
    {
        // (27) giữ Id lựa chọn theo vị trí để chữ sửa nhỏ không làm mất câu trả lời cũ.
        var existing = q.Options.OrderBy(o => o.OrderIndex).ToList();
        foreach (var o in existing.Skip(candidate.Options.Count)) { db.QuestionOptions.Remove(o); q.Options.Remove(o); }
        for (var i = 0; i < candidate.Options.Count; i++)
        {
            var input = candidate.Options[i];
            if (i < existing.Count) { existing[i].Text = input.Text; existing[i].IsCorrect = input.IsCorrect; }
            else q.Options.Add(input);
        }
        db.BlankAnswers.RemoveRange(q.Blanks.SelectMany(b => b.Answers));
        db.QuestionBlanks.RemoveRange(q.Blanks);
        q.Blanks = candidate.Blanks;
        q.Type = candidate.Type; q.Prompt = candidate.Prompt; q.Explanation = candidate.Explanation; q.Points = candidate.Points;
        q.KeepOptionOrder = candidate.KeepOptionOrder; q.IsRequired = candidate.IsRequired; q.MaxLength = candidate.MaxLength;
        q.TimeLimitSeconds = candidate.TimeLimitSeconds;
        q.CaseSensitive = candidate.CaseSensitive; q.AcceptNoDiacritics = candidate.AcceptNoDiacritics;
        q.IgnorePunctuation = candidate.IgnorePunctuation; q.AcceptAnyBlankOrder = candidate.AcceptAnyBlankOrder;
        q.BlankScoring = candidate.BlankScoring; q.NumberAnswer = candidate.NumberAnswer; q.NumberTolerance = candidate.NumberTolerance;
    }
}
