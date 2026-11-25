using Microsoft.EntityFrameworkCore;
using MindSprintAI_ASP.NET_MVC.Data.Entities;

namespace MindSprintAI_ASP.NET_MVC.Services;

public partial class AssignmentService
{
    public async Task ChangeSelfReviewSettingsAsync(int id, bool allowQuizSelfReview, bool allowCardSelfReview, string userId)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        var a = await AttemptLock.AssignmentAsync(db, id);
        await GetForLecturerAsync(id, userId);
        if (a.Mode != AssignmentMode.Practice) throw new BusinessException("Chỉ cho phép tự ôn lại ở bài Ôn tập.");
        foreach (var (field, old, value) in new[] {
            (nameof(a.AllowQuizSelfReview), a.AllowQuizSelfReview, allowQuizSelfReview),
            (nameof(a.AllowCardSelfReview), a.AllowCardSelfReview, allowCardSelfReview) })
            if (old != value) db.AssignmentChanges.Add(new() { AssignmentId = id, ChangedById = userId,
                ChangedAt = Now, Field = field, OldValue = old.ToString(), NewValue = value.ToString() });
        a.AllowQuizSelfReview = allowQuizSelfReview;
        a.AllowCardSelfReview = allowCardSelfReview;
        await new UserSettingPresetService(db, clock).SaveAssignmentAsync(a, userId);
        db.Audit?.Add(AuditAction.AssignmentSettingsChanged, "Assignment", id, userId);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        await notifier.LecturersAsync(id);
    }

    // GV bật/tắt "ôn thẻ khi mất mạng" sau khi đã giao (cùng cách ghi lịch sử như ChangeSelfReviewSettingsAsync).
    public async Task ChangeOfflineCardsAsync(int id, bool allow, string userId)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        var a = await AttemptLock.AssignmentAsync(db, id);
        await GetForLecturerAsync(id, userId);
        if (a.Mode != AssignmentMode.Practice || a.Form == AssignmentForm.QuizOnly)
            throw new BusinessException("Chỉ bài Ôn tập có thẻ ghi nhớ mới cho ôn thẻ khi mất mạng.");
        if (a.AllowOfflineCards != allow)
            db.AssignmentChanges.Add(new() { AssignmentId = id, ChangedById = userId, ChangedAt = Now,
                Field = nameof(a.AllowOfflineCards), OldValue = a.AllowOfflineCards.ToString(), NewValue = allow.ToString() });
        a.AllowOfflineCards = allow;
        await new UserSettingPresetService(db, clock).SaveAssignmentAsync(a, userId);
        db.Audit?.Add(AuditAction.AssignmentSettingsChanged, "Assignment", id, userId);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        await notifier.LecturersAsync(id);
    }

    public bool CanReopenPractice(Assignment a) => CanReopenPractice(a, Now);
    public static bool CanReopenPractice(Assignment a, DateTime now) => a.Mode == AssignmentMode.Practice
        && (a.ClosedAt <= now || a.DueAt <= now);

    public async Task ReopenPracticeAsync(int id, DateTime? dueAt, string? reason, string userId)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        // Cùng khóa với worker, lấy trước khóa bài để tránh đảo thứ tự khóa.
        await Tang7cLock.AcquireAsync(db, "reminder:create");
        var a = await AttemptLock.AssignmentAsync(db, id);
        await GetForLecturerAsync(id, userId);
        if (a.Mode != AssignmentMode.Practice) throw new BusinessException("Chỉ mở lại được bài Ôn tập.");
        if (!CanReopenPractice(a, Now)) throw new BusinessException("Chỉ mở lại được bài ôn tập đã hết hạn hoặc đã đóng. Bài này còn hạn: hãy đổi hạn hoặc đóng bài giao trước.");
        if (dueAt < Now + MinShortenNotice)
            throw new BusinessException("Hạn mới phải cách thời điểm hiện tại ít nhất 15 phút.");
        if (reason?.Length > 500) throw new BusinessException("Lý do tối đa 500 ký tự.");
        var hidden = await GetHiddenContentReasonAsync(a);
        if (hidden != null) throw new BusinessException(ContentVisibility.LearnerNotice(hidden)!);

        // Đóng bài không thu hồi mã; mã từng cấp không tái sử dụng (unique Code).
        // Nếu mã đã bị tắt, khôi phục đúng dòng cũ, không sinh mã khác.
        if (await codes.GetActiveAsync(JoinCodeTarget.Assignment, id) == null)
        {
            var old = await db.JoinCodes.Where(c => c.TargetType == JoinCodeTarget.Assignment && c.TargetId == id)
                .OrderByDescending(c => c.Id).FirstOrDefaultAsync();
            if (old == null) throw new BusinessException("Không tìm thấy mã cũ của bài nên không mở lại được. Hãy giao bài mới.");
            old.IsActive = true;
        }

        var ended = new List<Attempt>();
        var changed = new List<Attempt>();
        foreach (var (attempt, p) in await OpenAttemptsAsync(id))
        {
            var current = await AttemptLock.LoadAsync(db, attempt.Id);
            if (current.Kind == AttemptKind.SelfReview)
            {
                AttemptRules.End(current, p, AttemptEndReason.Submitted, Now);
                ended.Add(current);
            }
            else if (AttemptRules.ExpireIfDue(current, p, Now)) ended.Add(current);
            else if (AttemptRules.IsOpen(current)) changed.Add(current);
        }
        await db.SaveChangesAsync(); // các khóa/tải lại phía dưới không làm mất việc chốt lượt cũ.
        ChangeDeadline(db, a, dueAt, userId, Now, reason?.Trim());
        db.AssignmentChanges.Add(new AssignmentChange { AssignmentId = id, ChangedById = userId, ChangedAt = Now,
            Field = "ReopenPractice", OldValue = a.ClosedAt?.ToString("O"), NewValue = "Open", Reason = reason?.Trim() });
        a.ClosedAt = null;
        a.CloseReason = null;
        a.Status = AssignmentStatus.Open;
        if (CardCompletion.HasCards(a))
        {
            foreach (var p in await db.Participants.Include(p => p.Attempts).Where(p => p.AssignmentId == id).OrderBy(p => p.Id).ToListAsync())
            {
                await AttemptLock.ParticipantAsync(db, p.Id);
                p.CardsCompletionFinalizedAt = null;
                await CardCompletion.RefreshAsync(db, p, Now);
            }
        }
        foreach (var attempt in changed) attempt.DeadlineAt = AttemptRules.DeadlineFor(a, attempt.Participant);
        var members = await db.ClassMembers.Where(m => m.ClassroomId == a.ClassroomId && m.Role == ClassRole.Student
            && m.Status == ClassMemberStatus.Active && !m.User.IsGuest).ToListAsync();
        var users = members.Where(m => IsAssignedTo(a, m)).Select(m => m.UserId).ToArray();
        // Cùng khóa với worker: hạn mới cần một lần nhắc mới, không thêm cột.
        await db.Reminders.Where(r => r.Kind == ReminderKind.Due && r.TargetId == id).ExecuteDeleteAsync();
        var text = $"Bài \"{a.Quiz?.Title ?? a.PaperTitle ?? "Thẻ ghi nhớ"}\" đã được mở lại để ôn tập. "
            + (dueAt == null ? "Không có hạn." : $"Hạn {VnTime.Format(dueAt)} (giờ Việt Nam).");
        foreach (var user in users)
            await Notifications.AddAsync(user, NotificationType.ExtraMinutes, new(text, $"/Learn/Assignment/{id}", AssignmentId: id));
        db.Audit?.Add(AuditAction.AssignmentSettingsChanged, "Assignment", id, userId);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        await NotifyClosedAsync(ended);
        await NotifyDeadlinesAsync(changed);
        await Notifications.PushAsync(users);
        await notifier.LecturersAsync(id);
    }
}

public partial class AttemptService
{
    private async Task<bool> IsSelfReviewMemberAsync(Assignment a, string userId)
    {
        var member = await db.ClassMembers.FirstOrDefaultAsync(m => m.ClassroomId == a.ClassroomId && m.UserId == userId
            && m.Role == ClassRole.Student && m.Status == ClassMemberStatus.Active
            && m.Classroom.DeletedAt == null && !m.User.IsGuest);
        return a.ClassroomId != null && member != null && AssignmentService.IsAssignedTo(a, member)
            && !await db.Participants.AnyAsync(p => p.AssignmentId == a.Id && p.UserId == userId && p.IsStranger);
    }

    public async Task<bool> CanSelfReviewAsync(Assignment a, string userId)
    {
        if (!a.AllowQuizSelfReview || a.Form == AssignmentForm.FlashcardOnly) return false;
        return await CanSelfReviewContentAsync(a, userId);
    }

    public async Task<bool> CanSelfReviewCardsAsync(Assignment a, string userId)
    {
        if (!a.AllowCardSelfReview || a.AllowFlashcardsAfterDue || !CardCompletion.HasCards(a)) return false;
        return await CanSelfReviewContentAsync(a, userId);
    }

    private async Task<bool> CanSelfReviewContentAsync(Assignment a, string userId)
    {
        if (!AssignmentService.CanReopenPractice(a, Now) || !await IsSelfReviewMemberAsync(a, userId)) return false;
        var p = await db.Participants.SingleOrDefaultAsync(p => p.AssignmentId == a.Id && p.UserId == userId);
        return !AssignmentService.IsOpen(a, Now, p) && await GetHiddenContentNoticeAsync(a) == null;
    }

    public async Task<HashSet<int>> SelfReviewAssignmentIdsAsync(IEnumerable<Assignment> assignments, string userId)
    {
        var result = new HashSet<int>();
        foreach (var a in assignments.Where(a => AssignmentService.CanReopenPractice(a, Now)))
            if (await CanSelfReviewAsync(a, userId) || await CanSelfReviewCardsAsync(a, userId)) result.Add(a.Id);
        return result;
    }

    public async Task<Attempt> StartSelfReviewAsync(int assignmentId, string userId)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        var a = await AttemptLock.LearnerAsync(db, assignmentId, userId);
        await AttemptLock.MemberForLearnerAsync(db, a.ClassroomId, userId);
        await AccountLockState.EnsureAllowedAsync(db, userId, Now);
        if (a.Mode != AssignmentMode.Practice) throw new BusinessException("Chỉ tự ôn lại được bài Ôn tập.");
        await EnsurePracticeAccessAsync(a, userId);
        // Công tắc chỉ chặn lượt mới; lượt tự ôn đã mở vẫn được tiếp tục.
        var continuing = await db.Attempts.AnyAsync(at => at.Participant.AssignmentId == assignmentId
            && at.Participant.UserId == userId && at.Kind == AttemptKind.SelfReview
            && (at.Status == AttemptStatus.InProgress || at.Status == AttemptStatus.Paused));
        if (!(continuing ? await CanSelfReviewContentAsync(a, userId) : await CanSelfReviewAsync(a, userId)))
            throw new BusinessException("Hiện bạn chưa thể tự ôn lại bài này.");
        if (a.Form == AssignmentForm.FlashcardOnly) throw new BusinessException("Bài này chỉ học thẻ ghi nhớ.");
        await db.Entry(a).Reference(x => x.Quiz).Query().Include(q => q!.Questions).ThenInclude(q => q.Options).LoadAsync();
        await db.Entry(a).Reference(x => x.Quiz).Query().Include(q => q!.Questions).ThenInclude(q => q.Blanks).ThenInclude(b => b.Answers).LoadAsync();
        var p = await JoinParticipantAsync(a, userId);
        p = await AttemptLock.ParticipantAsync(db, p.Id);
        foreach (var open in p.Attempts.Where(AttemptRules.IsOpen).ToList())
        {
            var current = await AttemptLock.LoadAsync(db, open.Id);
            if (current.Kind == AttemptKind.SelfReview)
            {
                AdoptDevice(current);
                await db.SaveChangesAsync();
                await tx.CommitAsync();
                return current;
            }
            // Không thay lượt có cộng giờ riêng còn đang làm bằng lượt không tính điểm.
            if (!AttemptRules.ExpireIfDue(current, p, Now))
                throw new BusinessException("Hãy hoàn tất lượt đang làm trước khi tự ôn lại.");
            await db.SaveChangesAsync();
        }
        var attempt = CreateAttemptCore(a, p, null, false, null, AttemptKind.SelfReview);
        db.Attempts.Add(attempt);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        await notifier.LecturersAsync(assignmentId);
        return attempt;
    }
}
