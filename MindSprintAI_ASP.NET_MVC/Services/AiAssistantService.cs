using Microsoft.EntityFrameworkCore;
using MindSprintAI_ASP.NET_MVC.Data;
using MindSprintAI_ASP.NET_MVC.Data.Entities;
using MindSprintAI_ASP.NET_MVC.Models;

namespace MindSprintAI_ASP.NET_MVC.Services;

public record AiAccessState(bool Allowed, string? Reason, bool HasKey, bool SourceAvailable, bool Locked = false);
public record AiQuestionContext(string Prompt, IReadOnlyList<PlayOption> Options, AnswerResult VisibleAnswer);

public sealed class AiAssistantService(AppDbContext db, TimeProvider clock, UserApiKeyService keys,
    AttemptService attempts, ClassroomService classrooms, ILearnNotifier notifier, GeminiClient gemini)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    private async Task<Assignment> AssignmentAsync(int id, string userId)
    {
        await keys.EnsureAccountAsync(userId);
        await AccountLockState.EnsureAllowedAsync(db, userId, Now);
        var a = await db.Assignments.Include(a => a.Quiz).ThenInclude(q => q!.StudySet).SingleOrDefaultAsync(a => a.Id == id)
            ?? throw new ForbiddenException();
        var member = await db.ClassMembers.SingleOrDefaultAsync(m => m.ClassroomId == a.ClassroomId && m.UserId == userId);
        await AssignmentAccess.EnsureStartAsync(db, a, member, userId);
        return a;
    }

    private static int SetId(Assignment a) => a.StudySetId ?? a.Quiz?.StudySetId ?? throw new ForbiddenException();

    private async Task EnsureContentAsync(Assignment a, string userId)
    {
        await StudySetLearningAccess.EnsureNotTakingTestAsync(db, SetId(a), userId);
        var reason = await attempts.GetHiddenContentNoticeAsync(a);
        if (reason != null) throw new BusinessException(reason);
        if (a.OpensAt > Now) throw new BusinessException("Bài chưa mở.");
    }

    private async Task<string?> RefusalAsync(Assignment a, string userId)
    {
        if (a.Mode == AssignmentMode.Practice) return a.AllowAiAssistant ? null : "Giảng viên đã tắt trợ lý AI cho bài này. Bạn vẫn đọc được lịch sử.";
        if (a.AnswersPublishedAt == null) return "Trợ lý của bài kiểm tra chỉ mở sau khi giảng viên công bố đáp án.";
        if (!a.AllowAiAfterTest) return "Giảng viên chưa cho dùng trợ lý ở bài này.";
        if (await db.Attempts.AnyAsync(x => x.Participant.AssignmentId == a.Id
            && (x.Status == AttemptStatus.InProgress || x.Status == AttemptStatus.Paused))) return "Chờ mọi lượt Kiểm tra kết thúc để dùng trợ lý.";
        if (await db.RetakeRequests.AnyAsync(r => r.Participant.AssignmentId == a.Id && r.Participant.UserId == userId
            && r.Status == RetakeRequestStatus.Approved && r.WindowEndsAt > Now)) return "Bạn đang có một lượt làm lại đã được duyệt nên trợ lý tạm đóng. Làm xong lượt đó là dùng được.";
        return null;
    }

    public async Task<AiAccessState> StateAsync(int id, string userId)
    {
        var a = await AssignmentAsync(id, userId);
        var hasKey = await keys.LastFourAsync(userId) != null;
        try { await EnsureContentAsync(a, userId); }
        catch (BusinessException ex) { return new(false, ex.Message, hasKey, false, true); }
        var source = await db.SourceTexts.AnyAsync(s => s.StudySetId == SetId(a) && s.Content != "");
        var refusal = await RefusalAsync(a, userId);
        // Pháp lý C5: key lưu trước khi có thông báo thì ẩn ô hỏi và nhắc đồng ý (UseAsync cũng chặn ở bước gửi).
        if (refusal == null && await keys.NoticeAcceptedAsync(userId) == false)
            return new(false, UserApiKeyService.AiNoticeMissing, hasKey, source);
        // Allowed = quyền hỏi theo bài giao; thiếu key báo riêng qua HasKey để nút vẫn là "Hỏi trợ lý" kèm hướng dẫn lấy key.
        return new(refusal == null && source, refusal ?? (!source ? "Bộ học chưa có văn bản nguồn." : !hasKey ? "Nhập API key Gemini để dùng trợ lý" : null), hasKey, source);
    }

    public async Task<SourceReadModel> ReadSourceAsync(int id, string userId)
    {
        var a = await AssignmentAsync(id, userId); await EnsureContentAsync(a, userId);
        var source = await db.SourceTexts.AsNoTracking().Include(s => s.StudySet).SingleOrDefaultAsync(s => s.StudySetId == SetId(a))
            ?? throw new BusinessException("Bộ học chưa có văn bản nguồn.");
        return new(a.Id, source.StudySetId, source.StudySet.Title, source.Content, source.Version);
    }

    public static AiQuestionContext VisibleContext(PlayQuestion question) => new(question.Prompt, question.Options,
        new(CorrectOptionIds: question.CorrectIds, Explanation: question.Explanation, CorrectAnswers: question.CorrectAnswers));

    public async Task<AiConversation> AskAsync(int id, string question, int? conversationId, long? attemptId,
        int? questionId, string userId, CancellationToken cancellationToken = default)
    {
        question = question?.Trim() ?? "";
        if (question.Length is 0 or > 1000) throw new BusinessException("Câu hỏi cần từ 1 đến 1.000 ký tự.");
        var a = await AssignmentAsync(id, userId); await EnsureContentAsync(a, userId);
        var refusal = await RefusalAsync(a, userId); if (refusal != null) throw new BusinessException(refusal);
        var source = await db.SourceTexts.AsNoTracking().SingleOrDefaultAsync(s => s.StudySetId == SetId(a) && s.Content != "", cancellationToken)
            ?? throw new BusinessException("Bộ học chưa có văn bản nguồn.");
        var conversation = conversationId == null ? null : await ConversationAsync(conversationId.Value, userId);
        if (conversation != null && (conversation.AssignmentId != id || conversation.StudySetId != SetId(a))) throw new ForbiddenException();
        AiQuestionContext? context = null;
        if (questionId != null)
        {
            if (attemptId == null) throw new BusinessException("Hãy chọn một câu trong lượt làm bài của bạn để hỏi.");
            var attempt = await attempts.GetForLearnerAsync(attemptId.Value, userId);
            if (attempt.Participant.AssignmentId != id) throw new ForbiddenException();
            var aq = attempt.Questions.SingleOrDefault(q => q.QuestionId == questionId) ?? throw new ForbiddenException();
            // Dùng chính projection trả cho Razor, kể cả 68, câu có giờ riêng và câu chưa trả lời.
            var projected = (await attempts.GetPlayQuestionsAsync(attempt, userId)).SingleOrDefault(q => q.Id == aq.Id);
            if (projected == null || projected.BodyHidden) throw new BusinessException("Câu này chưa mở nên chưa hỏi được. Hãy mở câu đó trong bài trước.");
            context = VisibleContext(projected);
        }
        var chunks = SourceGrounding.Select(source.Content, question, context?.Prompt);
        var key = await keys.UseAsync(userId);
        var answer = await gemini.AskAsync(key, chunks, question, context, cancellationToken);
        // Không để một dịch vụ ngoài phản chiếu key vào lịch sử hay phản hồi.
        if (answer.Answer.Contains(key, StringComparison.Ordinal) || answer.Citations.Any(c => c.Quote?.Contains(key, StringComparison.Ordinal) == true))
            throw new BusinessException("Gemini trả về dữ liệu không hợp lệ. Vui lòng thử lại.");
        var grounded = SourceGrounding.Validate(answer, chunks, question);
        // Kiểm lại sau mạng: setting/khóa thi/quyền có thể đổi trong 30 giây chờ Gemini.
        db.ChangeTracker.Clear();
        a = await AssignmentAsync(id, userId); await EnsureContentAsync(a, userId);
        refusal = await RefusalAsync(a, userId); if (refusal != null) throw new BusinessException(refusal);
        if (SetId(a) != source.StudySetId) throw new BusinessException("Bài vừa đổi bộ học. Hãy tải lại.");
        conversation = conversationId == null ? new() { UserId = userId, StudySetId = SetId(a), AssignmentId = id,
            QuestionId = questionId, Title = question[..Math.Min(question.Length, 120)], CreatedAt = Now }
            : await ConversationAsync(conversationId.Value, userId);
        if (conversationId == null) db.AiConversations.Add(conversation);
        conversation.Messages.Add(new() { Role = "user", Content = question, QuestionId = questionId, SourceVersion = source.Version, CreatedAt = Now });
        conversation.Messages.Add(new() { Role = "assistant", Content = grounded.Answer, QuestionId = questionId,
            SourceVersion = source.Version, CreatedAt = Now, Citations = grounded.Citations });
        await db.SaveChangesAsync(cancellationToken);
        return conversation;
    }

    public async Task<AiConversation> ConversationAsync(int id, string userId)
    {
        await keys.EnsureAccountAsync(userId);
        var row = await db.AiConversations.Include(c => c.Messages.OrderBy(m => m.Id)).ThenInclude(m => m.Citations)
            .SingleOrDefaultAsync(c => c.Id == id && c.UserId == userId && c.DeletedAt == null) ?? throw new ForbiddenException();
        await StudySetLearningAccess.EnsureNotTakingTestAsync(db, row.StudySetId, userId);
        return row;
    }

    public async Task<List<AiConversation>> HistoryAsync(int id, string userId)
    {
        var a = await AssignmentAsync(id, userId); await EnsureContentAsync(a, userId);
        return await db.AiConversations.AsNoTracking().Where(c => c.UserId == userId && c.AssignmentId == id && c.DeletedAt == null)
            .OrderByDescending(c => c.Id).ToListAsync();
    }

    public async Task<List<AiConversation>> ListAsync(string userId, bool trash = false)
    {
        await keys.EnsureAccountAsync(userId);
        var cutoff = Now.AddDays(-30);
        return await db.AiConversations.AsNoTracking().Where(c => c.UserId == userId
            && (trash ? c.DeletedAt != null && c.DeletedAt > cutoff : c.DeletedAt == null)
            && !db.Attempts.Any(a => a.Participant.UserId == userId && a.Participant.Assignment.Mode == AssignmentMode.Test
                && a.Participant.Assignment.StudySetId == c.StudySetId && (a.Status == AttemptStatus.InProgress || a.Status == AttemptStatus.Paused)))
            .OrderByDescending(c => c.Id).ToListAsync();
    }

    public async Task DeleteAsync(int id, string userId)
    {
        var row = await ConversationAsync(id, userId); row.DeletedAt = Now; await db.SaveChangesAsync();
    }

    public async Task RestoreAsync(int id, string userId)
    {
        await keys.EnsureAccountAsync(userId);
        var row = await db.AiConversations.SingleOrDefaultAsync(c => c.Id == id && c.UserId == userId) ?? throw new ForbiddenException();
        await StudySetLearningAccess.EnsureNotTakingTestAsync(db, row.StudySetId, userId);
        if (row.DeletedAt == null || row.DeletedAt <= Now.AddDays(-30)) throw new BusinessException("Hội thoại đã quá hạn khôi phục 30 ngày.");
        row.DeletedAt = null; await db.SaveChangesAsync();
    }

    public async Task SettingsAsync(int id, bool allowAiAssistant, bool allowAiAfterTest, string userId)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        var a = await AttemptLock.AssignmentAsync(db, id);
        if (a.ClassroomId == null) throw new ForbiddenException();
        await classrooms.EnsureLecturerAsync(a.ClassroomId.Value, userId);
        db.AssignmentChanges.Add(new() { AssignmentId = id, ChangedById = userId, ChangedAt = Now, Field = "AllowAiAssistant/AllowAiAfterTest",
            OldValue = $"{a.AllowAiAssistant}/{a.AllowAiAfterTest}", NewValue = $"{allowAiAssistant}/{allowAiAfterTest}" });
        a.AllowAiAssistant = allowAiAssistant; a.AllowAiAfterTest = allowAiAfterTest;
        await db.SaveChangesAsync(); await tx.CommitAsync();
        await notifier.AiSettingsAsync(id);
    }

    public async Task<List<AiQuestionStat>> StatsAsync(int id, string userId)
    {
        var a = await db.Assignments.SingleOrDefaultAsync(a => a.Id == id) ?? throw new ForbiddenException();
        if (a.ClassroomId == null) throw new ForbiddenException();
        await classrooms.EnsureLecturerAsync(a.ClassroomId.Value, userId);
        var rows = await (from c in db.AiConversations
            join m in db.AiMessages on c.Id equals m.ConversationId
            where c.AssignmentId == id && m.Role == "user"
            select new { m.QuestionId, c.UserId }).ToListAsync();
        var ids = rows.Where(r => r.QuestionId != null).Select(r => r.QuestionId!.Value).Distinct().ToList();
        var orders = await db.Questions.IgnoreQueryFilters().Where(q => ids.Contains(q.Id)).Select(q => new { q.Id, q.OrderIndex }).ToDictionaryAsync(q => q.Id, q => q.OrderIndex + 1);
        // 76C, cách C (người dùng chốt 2026-11-06): đếm chính xác, không ngưỡng; không trả tên/nội dung.
        return rows.GroupBy(r => r.QuestionId).OrderBy(g => g.Key).Select(g => new AiQuestionStat(g.Key,
            g.Key == null ? "không gắn câu" : $"Câu {orders.GetValueOrDefault(g.Key.Value)}", g.Count().ToString())).ToList();
    }
}
