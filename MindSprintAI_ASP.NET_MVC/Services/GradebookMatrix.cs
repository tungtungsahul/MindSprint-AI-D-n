using Microsoft.EntityFrameworkCore;
using MindSprintAI_ASP.NET_MVC.Data.Entities;

namespace MindSprintAI_ASP.NET_MVC.Services;

public enum MatrixState { Unanswered, Correct, Incorrect, Pending }
public record MatrixMark(int AttemptNumber, MatrixState State, string? Note = null);
public record MatrixColumn(int QuestionId, int Number, string Prompt, decimal CorrectPercent, bool Graded = true);
public record MatrixRow(GradebookRow Learner, IReadOnlyList<IReadOnlyList<MatrixMark>> Cells);
public record MasteryOverview(decimal FirstPercent, decimal CurrentPercent, int Reached, int Total);
public record GradebookMatrix(IReadOnlyList<MatrixColumn> Columns, IReadOnlyList<MatrixRow> Rows, MasteryOverview? Mastery);

public partial class GradebookService
{
    public static bool NeedsAttention(GradebookRow row) => row.DeviceChange != null || row.Screen is { Highlight: true } or { Count: > 0 } || row.EndText != null;

    public async Task<GradebookMatrix> GetMatrixAsync(int assignmentId, string lecturerId, IReadOnlyList<GradebookRow> rows)
    {
        var assignment = await assignments.GetForLecturerAsync(assignmentId, lecturerId);
        var questions = await db.Questions.AsNoTracking().Where(q => q.QuizId == assignment.QuizId)
            .OrderBy(q => q.OrderIndex).Select(q => new { q.Id, q.OrderIndex, q.Prompt, q.Type }).ToListAsync();
        var regrades = await db.Regrades.AsNoTracking().Where(r => r.AssignmentId == assignmentId).ToListAsync();
        questions = questions.Where(q => !regrades.Any(r => r.QuestionId == q.Id && r.Method == RegradeMethod.Cancel)).ToList();
        // One joined read for all learners/attempts/questions/submissions; never query in a row loop.
        var attempts = await db.Attempts.AsNoTrackingWithIdentityResolution().AsSingleQuery()
            .Include(a => a.Participant).Include(a => a.Questions).ThenInclude(q => q.Submissions)
            .Where(a => a.Participant.AssignmentId == assignmentId && !a.Participant.IsStranger && a.Kind != AttemptKind.SelfReview)
            .ToListAsync();
        foreach (var attempt in attempts) attempt.Participant.Assignment = assignment;
        var byUser = attempts.ToLookup(a => a.Participant.UserId);
        var matrixRows = rows.Where(r => !r.IsStranger).Select(row =>
        {
            var available = byUser[row.UserId].Where(a => !a.ExcludeFromGradebook && a.Status != AttemptStatus.Cancelled).ToList();
            var counted = assignment.Mode == AssignmentMode.Practice ? ScoredAttempts(available, assignment.ScoreSelection)
                : available.OrderByDescending(a => a.AttemptNumber).Take(1).Where(a => a.Status == AttemptStatus.Ended).ToList();
            return new MatrixRow(row, questions.Select(question => (IReadOnlyList<MatrixMark>)(counted.Count == 0
                ? [new MatrixMark(0, MatrixState.Unanswered)]
                : counted.Select(a => MatrixCell(a, question.Id, regrades)).ToList())).ToList());
        }).ToList();
        var columns = questions.Select((q, index) => new MatrixColumn(q.Id, q.OrderIndex + 1, q.Prompt,
            matrixRows.Count == 0 ? 0 : Math.Round(100m * matrixRows.Sum(r => (decimal)r.Cells[index].Count(c => c.State == MatrixState.Correct) / r.Cells[index].Count) / matrixRows.Count, 1),
            !SurveyRules.IsSurvey(q.Type))).ToList();
        return new(columns, matrixRows, MasterySummary(assignment, rows.Where(r => !r.IsStranger).ToList(), attempts));
    }

    private static MatrixMark MatrixCell(Attempt attempt, int questionId, IReadOnlyList<Regrade> regrades)
    {
        var question = attempt.Questions.FirstOrDefault(q => q.QuestionId == questionId);
        if (question == null) return new(attempt.AttemptNumber, MatrixState.Unanswered);
        var snapshot = QuestionSnapshot.FromJson(question.SnapshotJson);
        var effective = RegradeRules.Effective(question, regrades);
        if (effective.Cancelled || SurveyRules.IsSurvey(snapshot.Type)) return new(attempt.AttemptNumber, MatrixState.Unanswered, effective.Cancelled ? "Đã hủy câu" : "Không chấm điểm");
        var answer = attempt.Participant.Assignment.Mode == AssignmentMode.Test
            ? question.Submissions.Where(s => s.IsFinal).OrderByDescending(s => s.SubmittedAt).ThenByDescending(s => s.Id).FirstOrDefault()
            : question.Submissions.Where(s => s.IsFirstAnswer).OrderBy(s => s.SubmittedAt).ThenBy(s => s.Id).FirstOrDefault();
        if (effective.BlankCredit) return new(attempt.AttemptNumber, MatrixState.Correct, RegradeRules.BlankCreditLabel);
        if (answer == null && question.AnswerCount == 0) return new(attempt.AttemptNumber, MatrixState.Unanswered);
        if (answer is { Score: null } && effective.First < effective.Max) return new(attempt.AttemptNumber, MatrixState.Pending);
        return new(attempt.AttemptNumber, effective.First >= effective.Max ? MatrixState.Correct : MatrixState.Incorrect);
    }

    private static MasteryOverview? MasterySummary(Assignment assignment, IReadOnlyList<GradebookRow> rows, List<Attempt> attempts)
    {
        if (assignment.MasteryThreshold == null || rows.Count == 0 || !attempts.Any(a => a.Questions.Any(q => q.AnswerCount > 0))) return null;
        var byUser = attempts.ToLookup(a => a.Participant.UserId);
        decimal first = 0, current = 0;
        var reached = 0;
        foreach (var row in rows)
        {
            var all = byUser[row.UserId].ToList();
            var learner = all.Where(a => a.Status != AttemptStatus.Cancelled).OrderBy(a => a.AttemptNumber).ToList();
            // Mốc đã đạt được giữ sau hủy lượt; thiếu dữ liệu so sánh thì không vẽ tiến bộ giả.
            if (all.Any(a => a.Participant.MasteryReached) && learner.Count == 0) return null;
            if (learner.Count == 0) continue;
            var initial = learner[0];
            var eligible = initial.Questions.Where(q => MasteryRules.Eligible(QuestionSnapshot.FromJson(q.SnapshotJson).Type)).ToList();
            if (eligible.Count == 0) return null;
            first += 100m * eligible.Count(q => q.Submissions.Where(s => s.IsFirstAnswer).OrderBy(s => s.SubmittedAt).ThenBy(s => s.Id).FirstOrDefault()?.IsCorrect == true) / eligible.Count;
            var ratios = learner.Select(a => MasteryRules.Denominator(a) is var n && n > 0 ? 100m * a.MasteredCount / n : 0);
            var best = ratios.Max();
            if (learner[0].Participant.MasteryReached && best < assignment.MasteryThreshold) return null;
            current += best;
            if (learner[0].Participant.MasteryReached) reached++;
        }
        return new(Math.Round(first / rows.Count, 1), Math.Round(current / rows.Count, 1), reached, rows.Count);
    }
}
