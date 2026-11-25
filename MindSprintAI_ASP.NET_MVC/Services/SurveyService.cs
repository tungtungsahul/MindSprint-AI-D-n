using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MindSprintAI_ASP.NET_MVC.Data;
using MindSprintAI_ASP.NET_MVC.Data.Entities;

namespace MindSprintAI_ASP.NET_MVC.Services;

public record SurveyOptionResult(string Text, int Count, decimal Percent);
public record SurveyOpinion(long SubmissionId, string DisplayName, string Text, bool Published);
public record LecturerSurveyQuestion(int QuestionId, string Prompt, QuestionType Type, int Respondents,
    IReadOnlyList<SurveyOptionResult> Options, IReadOnlyList<SurveyOpinion> Opinions);
public record LecturerSurveyResults(int AssignmentId, DateTime? PublishedAt, IReadOnlyList<LecturerSurveyQuestion> Questions);

// (56) model phía học viên không có trường tên hay Id của phản hồi, kể cả khi serialize thành JSON.
public record LearnerSurveyQuestion(string Prompt, bool Suppressed, int? Respondents,
    IReadOnlyList<SurveyOptionResult> Options, IReadOnlyList<string> Opinions);
public record LearnerSurveyResults(int AssignmentId, IReadOnlyList<LearnerSurveyQuestion> Questions);

public static class SurveyAggregation
{
    public static List<LecturerSurveyQuestion> Build(IEnumerable<Attempt> attempts, IEnumerable<QuestionSnapshot> questions)
    {
        // (56) mỗi người chỉ lấy lượt kết thúc gần nhất, bỏ hoàn toàn lượt hủy và lượt dở.
        var latest = attempts.Where(a => a.Status == AttemptStatus.Ended).GroupBy(a => a.ParticipantId)
            .Select(g => g.OrderByDescending(a => a.EndedAt).ThenByDescending(a => a.AttemptNumber).First()).ToList();
        return questions.Where(q => SurveyRules.IsSurvey(q.Type)).OrderBy(q => q.OrderIndex).Select(q =>
        {
            // (49B) bản chụp trước bàn giao giữ Id cũ; tổng hợp theo vị trí gốc và vị trí lựa chọn.
            var answers = latest.SelectMany(a => a.Questions.Select(aq => (Question: aq, Snapshot: QuestionSnapshot.FromJson(aq.SnapshotJson)))
                .Where(x => x.Snapshot.OrderIndex == q.OrderIndex)
                .Select(x => (Submission: x.Question.Submissions.Where(s => s.IsFinal).OrderByDescending(s => s.SubmittedAt).ThenByDescending(s => s.Id).FirstOrDefault(), x.Snapshot))
                .Where(x => x.Submission != null).Select(x => (Attempt: a, Submission: x.Submission!, x.Snapshot))).ToList();
            var options = q.Options.OrderBy(o => o.OrderIndex).Select(o =>
            {
                var count = answers.Count(x => (JsonSerializer.Deserialize<ChoiceAnswer>(x.Submission.PayloadJson)?.Selected ?? [])
                    .Any(id => x.Snapshot.Options.Any(old => old.Id == id && old.OrderIndex == o.OrderIndex)));
                return new SurveyOptionResult(o.Text, count, answers.Count == 0 ? 0 : count * 100m / answers.Count);
            }).ToList();
            var opinions = q.Type != QuestionType.SurveyText ? [] : answers.Select(x => new SurveyOpinion(x.Submission.Id,
                x.Attempt.Participant.User?.DisplayName ?? "Học viên", JsonSerializer.Deserialize<SurveyAnswer>(x.Submission.PayloadJson)?.Value ?? "",
                x.Submission.PublishedToClass)).ToList();
            return new LecturerSurveyQuestion(q.QuestionId, q.Prompt, q.Type, answers.Count, options, opinions);
        }).ToList();
    }

    public static LearnerSurveyResults ForLearner(LecturerSurveyResults source) => new(source.AssignmentId,
        source.Questions.Select(q => q.Respondents < 5
            ? new LearnerSurveyQuestion(q.Prompt, true, null, [], [])
            : new LearnerSurveyQuestion(q.Prompt, false, q.Respondents, q.Options,
                q.Opinions.Where(o => o.Published).Select(o => o.Text).ToList())).ToList());
}

public class SurveyService(AppDbContext db, AssignmentService assignments, TimeProvider clock)
{
    // (48A) GV chính và GV phụ của lớp đều xem/công khai khảo sát; không giới hạn theo người tạo bài giao.
    private Task<Assignment> OwnedAsync(int id, string userId) => assignments.GetForLecturerAsync(id, userId);

    private async Task<LecturerSurveyResults> BuildAsync(Assignment a)
    {
        var questions = await db.Questions.AsNoTracking().Include(q => q.Options)
            .Where(q => q.QuizId == a.QuizId).OrderBy(q => q.OrderIndex).ToListAsync();
        var attempts = await db.Attempts.AsNoTracking().Include(x => x.Participant).ThenInclude(p => p.User)
            .Include(x => x.Questions).ThenInclude(q => q.Submissions)
            .Where(x => x.Participant.AssignmentId == a.Id && x.Status == AttemptStatus.Ended && x.Kind != AttemptKind.SelfReview).ToListAsync();
        return new(a.Id, a.SurveyResultsPublishedAt, SurveyAggregation.Build(attempts, questions.Select(QuestionSnapshot.From)));
    }

    public async Task<LecturerSurveyResults> GetForLecturerAsync(int id, string userId) => await BuildAsync(await OwnedAsync(id, userId));

    public async Task<LearnerSurveyResults> GetForLearnerAsync(int id, string userId)
    {
        var a = await db.Assignments.AsNoTracking().SingleOrDefaultAsync(a => a.Id == id);
        if (a?.SurveyResultsPublishedAt == null || !await db.Attempts.AnyAsync(x => x.Participant.AssignmentId == id
            && x.Participant.UserId == userId && x.Kind != AttemptKind.SelfReview && x.Status == AttemptStatus.Ended)) throw new ForbiddenException();
        var hiddenNotice = ContentVisibility.LearnerNotice(await ContentVisibility.HiddenReasonAsync(db, a.StudySetId));
        if (hiddenNotice != null) throw new BusinessException(hiddenNotice);
        return SurveyAggregation.ForLearner(await BuildAsync(a));
    }

    public async Task PublishAsync(int id, bool publish, string userId)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        var a = await AttemptLock.AssignmentAsync(db, id);
        await OwnedAsync(id, userId);
        a.SurveyResultsPublishedAt = publish ? clock.GetUtcNow().UtcDateTime : null;
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    public async Task PublishOpinionAsync(int id, long submissionId, bool publish, string userId)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        await AttemptLock.AssignmentAsync(db, id);
        var a = await OwnedAsync(id, userId);
        // (56) chỉ công khai ý kiến nằm trong tập thống kê của chính bài giao này.
        var results = await BuildAsync(a);
        if (!results.Questions.SelectMany(q => q.Opinions).Any(o => o.SubmissionId == submissionId)) throw new ForbiddenException();
        var submission = await db.AnswerSubmissions.SingleAsync(s => s.Id == submissionId);
        submission.PublishedToClass = publish;
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }
}
