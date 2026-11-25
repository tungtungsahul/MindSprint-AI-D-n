using Microsoft.EntityFrameworkCore;
using MindSprintAI_ASP.NET_MVC.Data.Entities;

namespace MindSprintAI_ASP.NET_MVC.Services;

public partial class AssignmentService
{
    public async Task ChangeExtraMinutesAsync(int id, string learnerId, int minutes, string? reason, string userId)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        var a = await AttemptLock.AssignmentAsync(db, id);
        await GetForLecturerAsync(id, userId);
        if (a.AnswersPublishedAt != null)
            throw new BusinessException("Đáp án đã công bố, học viên có thể đã xem đáp án. Không thể cộng giờ riêng.");
        if (a.Status == AssignmentStatus.Closed) throw new BusinessException("Bài đã đóng.");
        if (minutes is < 0 or > 600) throw new BusinessException("Giờ cộng riêng phải từ 0 đến 600 phút; 0 là bỏ giờ cộng.");
        var why = AdjustmentReason(reason);
        var p = await AdjustmentParticipantAsync(a, learnerId);
        var changed = new List<Attempt>();
        foreach (var at in p.Attempts.Where(AttemptRules.IsOpen).OrderBy(x => x.Id).ToList())
        {
            var current = await AttemptLock.LoadAsync(db, at.Id);
            // (B3) lượt làm lại có khung và thời lượng riêng, không cộng vào khung đó.
            if (current.Kind != AttemptKind.Normal || !AttemptRules.IsOpen(current)) continue;
            var deadline = (a.Mode == AssignmentMode.Test ? a.TestEndsAt : a.DueAt)?.AddMinutes(minutes);
            if (deadline != null && (current.DeadlineAt == null || deadline < current.DeadlineAt)
                && deadline < Now + MinShortenNotice)
                throw new BusinessException("Hạn mới phải cách thời điểm hiện tại ít nhất 15 phút. Muốn dừng ngay hãy dùng \"Đóng bài giao\".");
            if (!AttemptRules.ExpireIfDue(current, p, Now)) changed.Add(current);
        }
        RecordAdjustment(a, $"Participants[{p.Id}].ExtraMinutes", p.ExtraMinutes, minutes, why, userId);
        p.ExtraMinutes = minutes;
        p.ExtraMinutesReason = why;
        foreach (var at in changed) at.DeadlineAt = AttemptRules.DeadlineFor(a, p);
        await Notifications.AddAsync(learnerId, NotificationType.ExtraMinutes,
            new($"Giảng viên đã cộng thêm {minutes} phút cho bạn ở bài {a.Quiz?.Title ?? a.PaperTitle ?? "Thẻ ghi nhớ"}: {why}", $"/Learn/Assignment/{id}", AssignmentId: id));
        db.Audit?.Add(AuditAction.ExtraTimeGranted, "Assignment", id, userId); // (NK3)
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        await NotifyDeadlinesAsync(changed);
        await Notifications.PushAsync(learnerId);
        await notifier.LecturersAsync(id);
    }

    public async Task ChangeMaxAttemptsAsync(int id, int maxAttempts, string? reason, string userId)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        var a = await AttemptLock.AssignmentAsync(db, id);
        await GetForLecturerAsync(id, userId);
        EnsurePracticeAttempts(a);
        var why = AdjustmentReason(reason);
        if (maxAttempts is < 1 or > MaxAttemptLimit) throw new BusinessException("Số lượt tối đa phải từ 1 đến 100.");
        var people = new List<Participant>();
        foreach (var participantId in await db.Participants.Where(p => p.AssignmentId == id).OrderBy(p => p.Id).Select(p => p.Id).ToListAsync())
            people.Add(await AttemptLock.ParticipantAsync(db, participantId));
        var largestExtra = people.Select(p => p.ExtraAttempts).DefaultIfEmpty(0).Max();
        if ((long)maxAttempts + largestExtra > MaxAttemptLimit)
            throw new BusinessException($"Có học viên đã được cộng riêng {largestExtra} lượt, nên số lượt tối đa chung chỉ đặt được tới {MaxAttemptLimit - largestExtra}.");
        var needed = people.Select(p => UsedAttempts(p) - p.ExtraAttempts).DefaultIfEmpty(1).Max();
        if (maxAttempts < needed)
            throw new BusinessException($"Đã có học viên dùng hết {needed} lượt, nên số lượt tối đa phải từ {needed} trở lên.");
        var extra = (long)maxAttempts - (a.MaxAttempts ?? maxAttempts);
        RecordAdjustment(a, nameof(a.MaxAttempts), a.MaxAttempts, maxAttempts, why, userId);
        a.MaxAttempts = maxAttempts;
        // Cả học viên chưa mở bài cũng nhận được thông báo tăng lượt.
        var users = await db.ClassMembers.Where(m => m.ClassroomId == a.ClassroomId && m.Role == ClassRole.Student
            && m.Status == ClassMemberStatus.Active).ToListAsync();
        var recipients = users.Where(m => IsAssignedTo(a, m)).Select(m => m.UserId).ToArray();
        if (extra > 0)
            foreach (var learner in recipients) await AddAttemptsNoticeAsync(a, learner, extra);
        db.Audit?.Add(AuditAction.AssignmentSettingsChanged, "Assignment", id, userId); // (NK3)
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        if (extra > 0) await Notifications.PushAsync(recipients);
        await notifier.LecturersAsync(id);
    }

    public async Task ChangeExtraAttemptsAsync(int id, string learnerId, int extraAttempts, string? reason, string userId)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        var a = await AttemptLock.AssignmentAsync(db, id);
        await GetForLecturerAsync(id, userId);
        EnsurePracticeAttempts(a);
        var why = AdjustmentReason(reason);
        if (extraAttempts < 0) throw new BusinessException("Số lượt cộng riêng không được âm.");
        var p = await AdjustmentParticipantAsync(a, learnerId);
        var limit = (long?)a.MaxAttempts + extraAttempts;
        if (limit > MaxAttemptLimit)
            throw new BusinessException($"Tổng số lượt của học viên này (chung + cộng riêng) tối đa 100, nên lượt cộng riêng tối đa {MaxAttemptLimit - a.MaxAttempts}.");
        if (limit < UsedAttempts(p))
            throw new BusinessException($"Học viên này đã dùng {UsedAttempts(p)} lượt, nên lượt cộng riêng phải từ {UsedAttempts(p) - a.MaxAttempts} trở lên.");
        var extra = (long)extraAttempts - p.ExtraAttempts;
        RecordAdjustment(a, $"Participants[{p.Id}].ExtraAttempts", p.ExtraAttempts, extraAttempts, why, userId);
        p.ExtraAttempts = extraAttempts;
        p.ExtraAttemptsReason = why;
        if (extra > 0) await AddAttemptsNoticeAsync(a, learnerId, extra);
        db.Audit?.Add(AuditAction.ExtraAttemptsGranted, "Assignment", id, userId); // (NK3)
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        if (extra > 0) await Notifications.PushAsync(learnerId);
        await notifier.LecturersAsync(id);
    }

    private static void EnsurePracticeAttempts(Assignment a)
    {
        if (a.Mode != AssignmentMode.Practice || a.Form == AssignmentForm.FlashcardOnly)
            throw new BusinessException("Chỉ cộng lượt cho quiz Ôn tập. Kiểm tra dùng quy tắc cho làm lại.");
    }

    private static string AdjustmentReason(string? reason) => !string.IsNullOrWhiteSpace(reason) && reason.Trim().Length <= 500
        ? reason.Trim() : throw new BusinessException("Phải ghi lý do (tối đa 500 ký tự).");

    private async Task<Participant> AdjustmentParticipantAsync(Assignment a, string learnerId)
    {
        var member = await AttemptLock.MemberAsync(db, a.ClassroomId, learnerId);
        var p = await db.Participants.SingleOrDefaultAsync(p => p.AssignmentId == a.Id && p.UserId == learnerId);
        if (!AssignmentAccess.CanStart(a, member, p)) throw new ForbiddenException();
        if (p == null)
        {
            p = new Participant { Assignment = a, UserId = learnerId, CreatedAt = Now };
            db.Participants.Add(p);
            await db.SaveChangesAsync();
        }
        return await AttemptLock.ParticipantAsync(db, p.Id);
    }

    private void RecordAdjustment(Assignment a, string field, int? old, int value, string why, string userId) =>
        db.AssignmentChanges.Add(new() { AssignmentId = a.Id, ChangedById = userId, ChangedAt = Now,
            Field = field, OldValue = old?.ToString(), NewValue = value.ToString(), Reason = why });

    private Task AddAttemptsNoticeAsync(Assignment a, string userId, long count) => Notifications.AddAsync(userId,
        NotificationType.ExtraAttempts, new($"Bạn được thêm {count} lượt ở bài {a.Quiz?.Title ?? a.PaperTitle ?? "Thẻ ghi nhớ"}",
            $"/Learn/Assignment/{a.Id}", AssignmentId: a.Id));

    public async Task<int> CountExtraTimeOpenAsync(int id, string userId)
    {
        await GetForLecturerAsync(id, userId);
        return await db.Attempts.CountAsync(at => at.Participant.AssignmentId == id && at.Participant.ExtraMinutes > 0
            && at.Kind == AttemptKind.Normal
            && (at.Status == AttemptStatus.InProgress || at.Status == AttemptStatus.Paused)
            && (at.DeadlineAt == null || at.DeadlineAt > Now));
    }

    public async Task<JoinCode> ChangeCodeAsync(int id, string userId, bool undo = false)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        await AttemptLock.AssignmentAsync(db, id);
        await GetForLecturerAsync(id, userId);
        var code = undo ? await codes.UndoAsync(JoinCodeTarget.Assignment, id) : await codes.ReplaceAsync(JoinCodeTarget.Assignment, id);
        db.Audit?.Add(AuditAction.AssignmentSettingsChanged, "Assignment", id, userId); // (NK3)
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return code;
    }

    private static int UsedAttempts(Participant p) => p.Attempts.Count(at => at.Status != AttemptStatus.Cancelled && at.Kind != AttemptKind.SelfReview);
}
