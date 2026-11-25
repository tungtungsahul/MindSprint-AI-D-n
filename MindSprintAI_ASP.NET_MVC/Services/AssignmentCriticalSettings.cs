using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MindSprintAI_ASP.NET_MVC.Data.Entities;

namespace MindSprintAI_ASP.NET_MVC.Services;

public record CriticalQuestionOption(int Id, int Number, string Prompt);
public record CriticalSettingsView(CriticalSettings Settings, Dictionary<int, List<CriticalQuestionOption>> Questions);

public partial class AssignmentService
{
    internal async Task<Dictionary<int, List<CriticalQuestionOption>>> CriticalChoicesAsync(List<int> quizIds)
    {
        var items = await db.Questions.AsNoTracking().Where(q => quizIds.Contains(q.QuizId) && q.DeletedAt == null
            && q.Type != QuestionType.SurveySingle && q.Type != QuestionType.SurveyMultiple && q.Type != QuestionType.SurveyText)
            .OrderBy(q => q.OrderIndex).ToListAsync();
        return items.GroupBy(q => q.QuizId).ToDictionary(g => g.Key, g => g.Select(q => new CriticalQuestionOption(q.Id, q.OrderIndex + 1, q.Prompt)).ToList());
    }
    private async Task ApplyCriticalSettingsAsync(Assignment a, CriticalSettings s)
    {
        var questions = await db.Questions.Where(q => q.QuizId == a.QuizId && q.DeletedAt == null).ToListAsync();
        var gradable = questions.Where(q => !SurveyRules.IsSurvey(q.Type)).ToList();
        var max = a.ScoreScale switch { ScoreScale.Ten => 10m, ScoreScale.Hundred => 100m, _ => gradable.Sum(q => q.Points) };
        if (!Enum.IsDefined(a.ScoreScale) || !Enum.IsDefined(a.Rounding)) throw new BusinessException("Thang điểm hoặc làm tròn không hợp lệ.");
        if (s.ThresholdEnabled && (s.CriticalThreshold == null || s.CriticalThreshold < 0 || s.CriticalThreshold > max || s.CriticalThreshold > 99999999999999.9999m
            || gradable.Count == 0)) throw new BusinessException("Ngưỡng liệt phải nằm trong thang điểm của bài có chấm điểm.");
        var ids = (s.CriticalQuestionIds ?? []).Distinct().Order().ToList();
        // (E1) luôn kiểm cả ids gửi kèm, không tin checklist hay công tắc phía trình duyệt.
        if (ids.Except(gradable.Select(q => q.Id)).Any()) throw new BusinessException("Câu điểm liệt phải là câu chấm điểm thuộc quiz của bài giao.");
        if (s.CriticalQuestionsEnabled && ids.Count == 0) throw new BusinessException("Chọn ít nhất một câu điểm liệt.");
        a.CriticalThreshold = s.ThresholdEnabled ? s.CriticalThreshold : null;
        a.CriticalQuestionsEnabled = s.CriticalQuestionsEnabled;
        a.CriticalQuestionIdsJson = JsonSerializer.Serialize(ids);
    }

    public async Task ChangeCriticalSettingsAsync(int id, CriticalSettings settings, string userId)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        var a = await AttemptLock.AssignmentAsync(db, id);
        await GetForLecturerAsync(id, userId);
        var old = JsonSerializer.Serialize(new { a.CriticalThreshold, a.CriticalQuestionsEnabled, a.CriticalQuestionIdsJson });
        await ApplyCriticalSettingsAsync(a, settings);
        var value = JsonSerializer.Serialize(new { a.CriticalThreshold, a.CriticalQuestionsEnabled, a.CriticalQuestionIdsJson });
        if (old != value)
        {
            db.AssignmentChanges.Add(new() { AssignmentId = id, ChangedById = userId, ChangedAt = Now,
                Field = "CriticalSettings", OldValue = old, NewValue = value });
            // (E5) cùng thứ tự khóa bài → người làm → lượt, gồm mọi lượt đã nộp.
            foreach (var at in await QuizChangeLock.AttemptsAsync(db, a)) CriticalScoreRules.Recompute(at);
        }
        await new UserSettingPresetService(db, clock).SaveAssignmentAsync(a, userId);
        db.Audit?.Add(AuditAction.AssignmentSettingsChanged, "Assignment", id, userId); // (NK3)
        await db.SaveChangesAsync(); await tx.CommitAsync(); await notifier.LecturersAsync(id);
    }

    public async Task ChangeFlashcardModeAsync(int id, FlashcardMode flashcardMode, string userId)
    {
        if (!Enum.IsDefined(flashcardMode)) throw new BusinessException("Chế độ thẻ không hợp lệ.");
        await using var tx = await db.Database.BeginTransactionAsync();
        var a = await AttemptLock.AssignmentAsync(db, id);
        await GetForLecturerAsync(id, userId);
        if (!CardCompletion.HasCards(a)) throw new BusinessException("Bài này không có thẻ ghi nhớ.");
        if (a.FlashcardMode != flashcardMode) db.AssignmentChanges.Add(new() { AssignmentId = id, ChangedById = userId,
            ChangedAt = Now, Field = nameof(a.FlashcardMode), OldValue = a.FlashcardMode.ToString(), NewValue = flashcardMode.ToString() });
        a.FlashcardMode = flashcardMode;
        await new UserSettingPresetService(db, clock).SaveAssignmentAsync(a, userId);
        db.Audit?.Add(AuditAction.AssignmentSettingsChanged, "Assignment", id, userId); // (NK3)
        await db.SaveChangesAsync(); await tx.CommitAsync(); await notifier.LecturersAsync(id);
    }

    public async Task ManualGradeAsync(long id, long attemptQuestionId, decimal score, string userId)
    {
        var header = await db.Attempts.Where(at => at.Id == id).Select(at => new { at.Participant.AssignmentId, at.ParticipantId }).SingleOrDefaultAsync()
            ?? throw new ForbiddenException();
        await using var tx = await db.Database.BeginTransactionAsync();
        await AttemptLock.AssignmentAsync(db, header.AssignmentId);
        await GetForLecturerAsync(header.AssignmentId, userId);
        await AttemptLock.ParticipantAsync(db, header.ParticipantId);
        var at = await AttemptLock.LoadAsync(db, id);
        var q = at.Questions.SingleOrDefault(q => q.Id == attemptQuestionId) ?? throw new ForbiddenException();
        var snap = QuestionSnapshot.FromJson(q.SnapshotJson);
        var last = q.Submissions.Where(s => s.IsFinal).OrderByDescending(s => s.SubmittedAt).ThenByDescending(s => s.Id).FirstOrDefault();
        if (at.Status != AttemptStatus.Ended || snap.Type is not (QuestionType.TextInput or QuestionType.NumberInput)
            || last == null || last.Score != null)
            throw new BusinessException("Câu này không còn chờ chấm (đã được chấm, hoặc học viên chưa nộp bài). Hãy tải lại trang để xem điểm hiện tại.");
        // Nói rõ phạm vi bằng con số thay vì "trong phạm vi điểm câu".
        if (score < 0 || score > snap.Points)
            throw new BusinessException($"Điểm phải từ 0 đến {snap.Points.ToString("0.##", System.Globalization.CultureInfo.GetCultureInfo("vi-VN"))}.");
        last.Score = score; last.IsCorrect = score == snap.Points;
        q.LatestScore = score;
        if (last.IsFirstAnswer || at.Participant.Assignment.Mode == AssignmentMode.Test) q.FirstScore = score;
        RegradeRules.Recompute(at);
        db.AssignmentChanges.Add(new() { AssignmentId = header.AssignmentId, ChangedById = userId, ChangedAt = Now,
            Field = "ManualGrade", NewValue = $"Lượt {id}, câu {attemptQuestionId}: {score}" });
        db.Audit?.Add(AuditAction.Regraded, "Attempt", id, userId); // (NK3)
        await db.SaveChangesAsync(); await tx.CommitAsync(); await notifier.LecturersAsync(header.AssignmentId);
    }
}
