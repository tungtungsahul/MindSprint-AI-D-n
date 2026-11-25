using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MindSprintAI_ASP.NET_MVC.Data;
using MindSprintAI_ASP.NET_MVC.Data.Entities;

namespace MindSprintAI_ASP.NET_MVC.Services;

public record RegradeInput(int QuestionId, RegradeMethod Method, string Reason, Guid ClientRequestId, IReadOnlyList<int> NewCorrect);
public record RegradeQuestion(int Id, int Number, string Prompt, bool Choice, IReadOnlyList<OptionSnapshot> Options);

public class RegradeService(AppDbContext db, TimeProvider clock, ILearnNotifier notifier)
{
    // (C3) quyền chấm lại độc lập với quyền sửa bộ học; không dựa vào chế độ màn hình.
    public async Task<Assignment> GetAuthorizedAsync(int assignmentId, string userId)
    {
        var a = await db.Assignments.Include(a => a.Quiz!).ThenInclude(q => q.StudySet)
            .SingleOrDefaultAsync(a => a.Id == assignmentId) ?? throw new BusinessException("Không tìm thấy bài giao.");
        var owner = a.Quiz != null && a.Quiz.StudySetId == (a.StudySetId ?? a.Quiz.StudySetId)
            && a.Quiz.StudySet.OwnerId == userId;
        var lecturer = await db.ClassMembers.AnyAsync(m => m.ClassroomId == a.ClassroomId && m.UserId == userId
            && m.Status == ClassMemberStatus.Active && (m.Role == ClassRole.MainLecturer || m.Role == ClassRole.AssistantLecturer));
        if (!owner && !lecturer) throw new ForbiddenException();
        return a;
    }

    public async Task<List<RegradeQuestion>> QuestionsAsync(int assignmentId, string userId)
    {
        var a = await GetAuthorizedAsync(assignmentId, userId);
        var questions = await db.Questions.Include(q => q.Options).Where(q => q.QuizId == a.QuizId).OrderBy(q => q.OrderIndex).ToListAsync();
        return questions.Where(q => !SurveyRules.IsSurvey(q.Type)).Select(q => new RegradeQuestion(q.Id, q.OrderIndex + 1,
            q.Prompt, MasteryRules.Eligible(q.Type), QuestionSnapshot.From(q).Options)).ToList();
    }

    public async Task<Regrade> ApplyAsync(int assignmentId, RegradeInput input, string userId)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        var a = await AttemptLock.AssignmentAsync(db, assignmentId);
        await GetAuthorizedAsync(assignmentId, userId);
        if (string.IsNullOrWhiteSpace(input.Reason)) throw new BusinessException("Nhập lý do chấm lại.");
        var existing = await db.Regrades.SingleOrDefaultAsync(r => r.AssignmentId == assignmentId && r.ClientRequestId == input.ClientRequestId);
        if (existing != null)
        {
            if (existing.ById != userId) throw new ForbiddenException();
            if (existing.QuestionId != input.QuestionId || existing.Method != input.Method || existing.Reason != input.Reason.Trim()
                || existing.NewCorrectJson != (input.Method == RegradeMethod.NewCorrect ? JsonSerializer.Serialize(input.NewCorrect.Distinct().Order()) : null))
                throw new BusinessException("Câu này vừa được chấm lại rồi. Tải lại trang để xem kết quả.");
            await tx.CommitAsync();
            return existing;
        }
        if (input.ClientRequestId == Guid.Empty) throw new BusinessException("Chưa gửi được yêu cầu chấm lại. Hãy tải lại trang rồi thử lại.");
        if (!Enum.IsDefined(input.Method)) throw new BusinessException("Cách chấm lại không hợp lệ.");
        var q = await db.Questions.AsNoTracking().Include(q => q.Options).SingleOrDefaultAsync(q => q.Id == input.QuestionId && q.QuizId == a.QuizId)
            ?? throw new BusinessException("Câu hỏi không thuộc bài giao này.");
        if (SurveyRules.IsSurvey(q.Type)) throw new BusinessException("Câu khảo sát không chấm lại được.");
        if (a.Regrades.Any(r => r.QuestionId == q.Id && r.Method == RegradeMethod.Cancel))
            throw new BusinessException("Câu này đã bị hủy khỏi bài giao.");
        var key = input.NewCorrect.Distinct().Order().ToArray();
        if (input.Method == RegradeMethod.NewCorrect && (!MasteryRules.Eligible(q.Type) || key.Length == 0
            || q.Type == QuestionType.SingleChoice && key.Length != 1 || key.Except(q.Options.Select(o => o.Id)).Any()))
            throw new BusinessException("Đổi đáp án mới chỉ dùng cho câu lựa chọn; hãy chọn đúng bộ đáp án. Câu điền chữ/số hãy dùng \"Cho mọi người đúng câu này\" hoặc \"Hủy câu\".");
        var attempts = (await QuizChangeLock.AttemptsAsync(db, a)).Where(at => at.Status != AttemptStatus.Cancelled).ToList();
        var related = attempts.SelectMany(at => at.Questions).Select(aq => QuestionSnapshot.FromJson(aq.SnapshotJson))
            .Where(s => s.OrderIndex == q.OrderIndex).DistinctBy(s => s.QuestionId).ToList();
        var cancelled = a.Regrades.Where(r => r.Method == RegradeMethod.Cancel).Select(r => r.QuestionId)
            .Concat(related.Select(s => s.QuestionId)).Append(q.Id).ToList();
        if (input.Method == RegradeMethod.Cancel)
        {
            var remaining = await db.Questions.Where(x => x.QuizId == a.QuizId && !cancelled.Contains(x.Id)
                && x.Type != QuestionType.SurveySingle && x.Type != QuestionType.SurveyMultiple && x.Type != QuestionType.SurveyText).SumAsync(x => x.Points);
            if (remaining <= 0 || attempts.Where(at => at.Kind != AttemptKind.SelfReview).Any(at => at.Questions.Where(x => !cancelled.Contains(QuestionSnapshot.FromJson(x.SnapshotJson).QuestionId))
                    .Sum(x => SurveyRules.IsSurvey(QuestionSnapshot.FromJson(x.SnapshotJson).Type) ? 0 : QuestionSnapshot.FromJson(x.SnapshotJson).Points) <= 0))
                throw new BusinessException("Không thể hủy câu cuối cùng còn tính điểm; tổng điểm phải lớn hơn 0.");
        }
        var now = clock.GetUtcNow().UtcDateTime;
        // (61) lượt đã hết hạn phải chốt bằng khóa cũ trước khi áp lần chấm lại mới.
        foreach (var at in attempts) AttemptRules.ExpireIfDue(at, at.Participant, now);
        var regrade = new Regrade
        {
            Assignment = a, QuestionId = q.Id, Method = input.Method, Reason = input.Reason.Trim(), ById = userId, At = now,
            ClientRequestId = input.ClientRequestId, NewCorrectJson = input.Method == RegradeMethod.NewCorrect ? JsonSerializer.Serialize(key) : null
        };
        a.Regrades.Add(regrade);
        // (49B, B1) lượt cũ giữ Id trong bản chụp; chuyển khóa lựa chọn theo thứ tự gốc, không sửa SnapshotJson.
        foreach (var snap in related.Where(s => s.QuestionId != q.Id))
        {
            var orders = q.Options.Where(o => key.Contains(o.Id)).Select(o => o.OrderIndex).ToHashSet();
            a.Regrades.Add(new Regrade { QuestionId = snap.QuestionId, Method = input.Method, Reason = regrade.Reason,
                ById = userId, At = now, ClientRequestId = Guid.NewGuid(), NewCorrectJson = input.Method == RegradeMethod.NewCorrect
                    ? JsonSerializer.Serialize(snap.Options.Where(o => orders.Contains(o.OrderIndex)).Select(o => o.Id).Order()) : null });
        }
        await db.SaveChangesAsync(); // (19) lấy Id để thứ tự ổn định cả khi hai lần chấm cùng mốc giờ.
        var notices = new NotificationService(db, clock, notifier);
        var recipients = new HashSet<string>();
        foreach (var at in attempts)
        {
            var history = new ScoreHistory { Attempt = at, Regrade = regrade, OldFirstScore = at.FirstScore,
                OldPracticeScore = at.PracticeScore, OldMaxScore = at.MaxScore };
            RegradeRules.Recompute(at);
            history.NewFirstScore = at.FirstScore; history.NewPracticeScore = at.PracticeScore; history.NewMaxScore = at.MaxScore;
            db.ScoreHistories.Add(history);
            if ((history.OldFirstScore != at.FirstScore || history.OldPracticeScore != at.PracticeScore || history.OldMaxScore != at.MaxScore)
                && at.Participant.UserId is { } learner && at.Questions.SingleOrDefault(x => QuestionSnapshot.FromJson(x.SnapshotJson).OrderIndex == q.OrderIndex) is { } changed)
            {
                // (B1, B4c) dùng số theo lượt; không đính kèm đáp án/giải thích của Kiểm tra.
                await notices.AddAsync(learner, NotificationType.Regrade, new($"Câu {changed.DisplayOrder + 1} đã được chấm lại: {input.Reason.Trim()}",
                    $"/Learn/Review/{at.Id}", AssignmentId: a.Id, AttemptId: at.Id));
                recipients.Add(learner);
            }
        }
        foreach (var p in attempts.Select(at => at.Participant).Distinct())
        {
            var latest = p.Attempts.Where(at => at.Status != AttemptStatus.Cancelled && at.Kind != AttemptKind.SelfReview).OrderByDescending(at => at.AttemptNumber).FirstOrDefault();
            if (latest != null && !AttemptRules.IsOpen(latest) && !CardCompletion.HasCards(a))
                p.CompletionStatus = AttemptRules.AwaitsScores(latest) ? CompletionStatus.SubmittedAwaitingScore : CompletionStatus.Completed;
            await CardCompletion.RefreshAsync(db, p, now);
        }
        db.Audit?.Add(AuditAction.Regraded, "Assignment", assignmentId, userId); // (NK3)
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        await notices.PushAsync(recipients.ToArray());
        await notifier.LecturersAsync(a.Id);
        return regrade;
    }
}
