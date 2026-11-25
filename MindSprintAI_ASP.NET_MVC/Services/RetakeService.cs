using Microsoft.EntityFrameworkCore;
using MindSprintAI_ASP.NET_MVC.Data;
using MindSprintAI_ASP.NET_MVC.Data.Entities;

namespace MindSprintAI_ASP.NET_MVC.Services;

public class RetakeService(AppDbContext db, AssignmentService assignments, AttemptService attempts, TimeProvider clock)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    internal static bool ExpireUnused(Participant p, DateTime now)
    {
        var expired = p.RetakeRequests.Where(r => r.Status == RetakeRequestStatus.Approved && r.WindowEndsAt <= now).ToList();
        foreach (var r in expired) r.Status = RetakeRequestStatus.Expired;
        if (expired.Count > 0) p.CompletionLabels |= CompletionLabels.RetakeNotUsed;
        return expired.Count > 0;
    }

    private static bool HasCapacity(Participant p) =>
        p.Attempts.Count(a => a.Status != AttemptStatus.Cancelled) < 2
        && !p.Attempts.Any(a => a.Kind == AttemptKind.Retake && a.Status != AttemptStatus.Cancelled);

    public static bool CanRequest(Participant p) => (p.GuestId == null || p.Guest?.MergedIntoUserId != null)
        && p.User?.IsGuest != true && p.Assignment.Mode == AssignmentMode.Test
        && p.Attempts.Any(a => !AttemptRules.IsOpen(a)) && !p.Attempts.Any(AttemptRules.IsOpen)
        && HasCapacity(p) && !p.RetakeRequests.Any(r => r.Status is RetakeRequestStatus.Pending or RetakeRequestStatus.Approved);

    internal static string RequestError(Participant p)
    {
        if (p.Attempts.Any(AttemptRules.IsOpen)) return "Hãy làm xong lượt đang làm rồi mới xin làm lại.";
        if (!p.Attempts.Any(a => !AttemptRules.IsOpen(a))) return "Bạn chưa có lượt Kiểm tra đã kết thúc.";
        if (!HasCapacity(p)) return "Bạn đã dùng hết số lần được làm lại của bài này.";
        if (p.RetakeRequests.Any(r => r.Status is RetakeRequestStatus.Pending or RetakeRequestStatus.Approved))
            return "Bạn đã có yêu cầu làm lại đang chờ hoặc đã được duyệt.";
        return "Bài này không cho xin làm lại.";
    }

    public async Task<List<RetakeRequest>> ListForLecturerAsync(int assignmentId, string userId)
    {
        await assignments.GetForLecturerAsync(assignmentId, userId);
        var list = await db.RetakeRequests.Include(r => r.Participant).ThenInclude(p => p.User)
            .Where(r => r.Participant.AssignmentId == assignmentId).OrderByDescending(r => r.CreatedAt).ToListAsync();
        // Tài khoản 2 rà 13/11/2026: trước đây đánh dấu hết hạn rồi lưu mà không khóa → có thể ghi đè lần "Bắt đầu làm lại" đúng giây cuối
        // hoặc nhãn hoàn thành học viên vừa ghi. Nay chỉ người có quyền làm lại đã quá hạn mới được xử lý, bằng khóa học viên như các đường khác.
        var changed = false;
        foreach (var p in list.Select(r => r.Participant).Distinct().Where(p => p.RetakeRequests.Any(r => r.Status == RetakeRequestStatus.Approved && r.WindowEndsAt <= Now)).ToList())
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            await AttemptLock.LearnerAsync(db, assignmentId, p.UserId ?? "participant:" + p.Id);
            await AttemptLock.ParticipantAsync(db, p.Id);
            foreach (var entry in db.ChangeTracker.Entries<RetakeRequest>().Where(e => e.Entity.ParticipantId == p.Id).ToList()) await entry.ReloadAsync();
            changed |= ExpireUnused(p, Now);
            await db.SaveChangesAsync();
            await tx.CommitAsync();
        }
        if (changed) await assignments.NotifyLecturersAsync(assignmentId);
        return list;
    }

    public async Task<RetakeRequest> RequestAsync(int assignmentId, string? reason, string userId)
    {
        if (await db.Users.AnyAsync(u => u.Id == userId && u.IsGuest)) throw new ForbiddenException();
        if (string.IsNullOrWhiteSpace(reason)) throw new BusinessException("Nhập lý do xin làm lại.");
        var overview = await attempts.GetOverviewAsync(assignmentId, userId);
        if (overview.Assignment.Mode != AssignmentMode.Test) throw new BusinessException("Chỉ xin làm lại bài Kiểm tra.");
        var hiddenNotice = ContentVisibility.LearnerNotice(await assignments.GetHiddenContentReasonAsync(overview.Assignment));
        if (hiddenNotice != null) throw new BusinessException(hiddenNotice);
        await using var transaction = await db.Database.BeginTransactionAsync();
        var a = await AttemptLock.LearnerAsync(db, assignmentId, userId);
        var member = await AttemptLock.MemberForLearnerAsync(db, a.ClassroomId, userId);
        await AccountLockState.EnsureAllowedAsync(db, userId, Now);
        var p = await db.Participants.Include(p => p.Guest).Include(p => p.RetakeRequests)
            .SingleOrDefaultAsync(p => p.AssignmentId == assignmentId && p.UserId == userId)
            ?? throw new BusinessException("Bạn chưa có lượt Kiểm tra đã kết thúc.");
        p = await AttemptLock.ParticipantAsync(db, p.Id);
        if (!AssignmentAccess.CanStart(a, member, p)) throw new ForbiddenException();
        ExpireUnused(p, Now);
        if (!CanRequest(p)) throw new BusinessException(RequestError(p));
        var r = new RetakeRequest { Participant = p, Reason = reason.Trim(), CreatedAt = Now };
        db.RetakeRequests.Add(r);
        db.Audit?.Add(AuditAction.RetakeRequested, "Assignment", assignmentId, userId); // (NK3)
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        await assignments.NotifyLecturersAsync(assignmentId);
        return r;
    }

    public async Task<int> DecideAsync(int requestId, bool approve, DateTime? windowEndsAt, string userId)
    {
        var request = await db.RetakeRequests.Include(r => r.Participant).SingleOrDefaultAsync(r => r.Id == requestId)
            ?? throw new BusinessException("Không tìm thấy yêu cầu làm lại.");
        var a = await assignments.GetForLecturerAsync(request.Participant.AssignmentId, userId);
        await using var transaction = await db.Database.BeginTransactionAsync();
        await AttemptLock.AssignmentAsync(db, a.Id);
        await assignments.GetForLecturerAsync(a.Id, userId);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT [Id] FROM [Participants] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {request.ParticipantId}");
        await db.Entry(request).ReloadAsync();
        await db.Entry(request.Participant).Collection(p => p.Attempts).LoadAsync();
        if (request.Status != RetakeRequestStatus.Pending) throw new BusinessException("Yêu cầu này đã được xử lý.");
        if (approve)
        {
            if (!HasCapacity(request.Participant) || request.Participant.Attempts.Any(AttemptRules.IsOpen))
                throw new BusinessException("Học viên đã dùng hết lượt làm lại hoặc đang có lượt dở.");
            if (windowEndsAt == null || windowEndsAt <= Now) throw new BusinessException("Hạn làm lại phải là một thời điểm sau bây giờ.");
            request.WindowStartsAt = Now;
            request.WindowEndsAt = windowEndsAt;
            request.AnswersWerePublished = a.AnswersPublishedAt != null;
        }
        request.Status = approve ? RetakeRequestStatus.Approved : RetakeRequestStatus.Rejected;
        request.DecidedAt = Now;
        request.DecidedById = userId;
        if (request.Participant.UserId is { } learner)
            await assignments.Notifications.AddAsync(learner, NotificationType.Retake, new(approve
                ? "Yêu cầu làm lại của bạn đã được duyệt." : "Yêu cầu làm lại của bạn đã bị từ chối.", $"/Learn/Assignment/{a.Id}", AssignmentId: a.Id));
        db.Audit?.Add(approve ? AuditAction.RetakeApproved : AuditAction.RetakeRejected, "RetakeRequest", requestId, userId); // (NK3)
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        if (request.Participant.UserId is { } recipient) await assignments.Notifications.PushAsync(recipient);
        await assignments.NotifyLecturersAsync(a.Id);
        return a.Id;
    }

    public async Task<int> ConsentAssignmentAsync(int requestId, string userId) =>
        await db.RetakeRequests.Where(r => r.Id == requestId && r.Participant.UserId == userId)
            .Select(r => (int?)r.Participant.AssignmentId).SingleOrDefaultAsync() ?? throw new ForbiddenException();

    public async Task<Attempt> StartAsync(int requestId, string userId, bool consent = false, DeviceSignalResult? signals = null)
    {
        var request = await db.RetakeRequests.AsNoTracking().SingleOrDefaultAsync(r => r.Id == requestId)
            ?? throw new BusinessException("Không tìm thấy yêu cầu làm lại.");
        await using var transaction = await db.Database.BeginTransactionAsync();
        var assignmentId = await db.Participants.Where(p => p.Id == request.ParticipantId)
            .Select(p => p.AssignmentId).SingleAsync();
        var assignment = await AttemptLock.LearnerAsync(db, assignmentId, userId);
        var member = await AttemptLock.MemberForLearnerAsync(db, assignment.ClassroomId, userId);
        await AccountLockState.EnsureAllowedAsync(db, userId, Now);
        var p = await AttemptLock.ParticipantAsync(db, request.ParticipantId);
        await db.Entry(p).Reference(p => p.Assignment).Query().Include(a => a.Quiz!)
            .ThenInclude(q => q.Questions).ThenInclude(q => q.Options).LoadAsync();
        await db.Entry(p).Reference(p => p.Assignment).Query().Include(a => a.Quiz!)
            .ThenInclude(q => q.Questions).ThenInclude(q => q.Blanks).ThenInclude(b => b.Answers).LoadAsync();
        foreach (var entry in db.ChangeTracker.Entries<RetakeRequest>().Where(e => e.Entity.ParticipantId == p.Id).ToList())
            await entry.ReloadAsync();
        await db.Entry(p).Collection(p => p.RetakeRequests).Query().LoadAsync();
        if (p.UserId != userId || !AssignmentAccess.CanStart(p.Assignment, member, p)
            || await db.Users.AnyAsync(u => u.Id == userId && u.IsGuest)) throw new ForbiddenException();
        var hiddenNotice = ContentVisibility.LearnerNotice(await assignments.GetHiddenContentReasonAsync(assignment));
        if (hiddenNotice != null) throw new BusinessException(hiddenNotice);
        var r = p.RetakeRequests.Single(r => r.Id == requestId);
        if (r.Status == RetakeRequestStatus.Used)
        {
            var existing = p.Attempts.SingleOrDefault(a => a.RetakeRequestId == r.Id && AttemptRules.IsOpen(a));
            if (existing != null)
            {
                await transaction.CommitAsync();
                return existing;
            }
        }
        if (ExpireUnused(p, Now))
        {
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            await assignments.NotifyLecturersAsync(assignmentId);
            throw new BusinessException("Đã quá hạn làm lại.");
        }
        if (p.Assignment.Mode != AssignmentMode.Test || r.Status != RetakeRequestStatus.Approved
            || r.WindowStartsAt > Now || r.WindowEndsAt == null)
            throw new BusinessException("Bạn chưa được duyệt làm lại, hoặc chưa tới thời gian làm lại.");
        if (!HasCapacity(p) || p.Attempts.Any(AttemptRules.IsOpen)) throw new BusinessException("Bạn đã dùng hết lượt làm lại.");
        // (33A, B3a) khung làm lại độc lập với phiên đã đóng; thời lượng vẫn bị giới hạn bởi cuối khung.
        var attempt = attempts.CreateAttempt(p.Assignment, p, null, consent, signals);
        var deadline = attempt.StartedAt.AddMinutes(p.Assignment.DurationMinutes!.Value);
        attempt.DeadlineAt = deadline < r.WindowEndsAt ? deadline : r.WindowEndsAt;
        attempt.Kind = AttemptKind.Retake;
        attempt.RetakeRequest = r;
        r.Status = RetakeRequestStatus.Used;
        p.CompletionStatus = CompletionStatus.InProgress;
        p.CompletionLabels &= ~CompletionLabels.RetakeNotUsed;
        db.Attempts.Add(attempt);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        await assignments.NotifyLecturersAsync(assignmentId);
        return attempt;
    }

    public async Task CancelAsync(long attemptId, string? reason, string userId)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new BusinessException("Nhập lý do hủy lượt.");
        var header = await db.Attempts.Where(x => x.Id == attemptId)
            .Select(x => new { x.ParticipantId, x.Participant.AssignmentId }).SingleOrDefaultAsync()
            ?? throw new BusinessException("Không tìm thấy lượt làm.");
        await using var transaction = await db.Database.BeginTransactionAsync();
        var a = await AttemptLock.AssignmentAsync(db, header.AssignmentId);
        await assignments.GetForLecturerAsync(a.Id, userId);
        // (47) quyết định giữ lượt khách có thể chuyển sang dòng tài khoản; khóa người/lượt theo Id trước.
        var people = await db.Participants.Include(p => p.Guest).Where(p => p.AssignmentId == a.Id).OrderBy(p => p.Id).ToListAsync();
        foreach (var p in people) await AttemptLock.ParticipantAsync(db, p.Id);
        foreach (var at in people.SelectMany(p => p.Attempts).OrderBy(at => at.Id).ToList()) await AttemptLock.LoadAsync(db, at.Id);
        var attempt = await AttemptLock.LoadAsync(db, attemptId);
        if (a.Mode != AssignmentMode.Test && !attempt.MadeAsGuest)
            throw new BusinessException("Chỉ hủy được lượt của bài Kiểm tra, hoặc lượt làm khi chưa đăng nhập (khách).");
        if (attempt.Status == AttemptStatus.Cancelled) return;
        attempt.Status = AttemptStatus.Cancelled;
        attempt.CancelledAt = Now;
        attempt.CancelledById = userId;
        attempt.CancelReason = reason.Trim();
        attempt.EndedAt ??= Now;
        var remaining = attempt.Participant.Attempts.Where(x => x.Status != AttemptStatus.Cancelled && x.Kind != AttemptKind.SelfReview).ToList();
        // (B5b) khi hủy phải khôi phục cả nhãn khảo sát của lượt trước từ bản chụp đã lưu.
        foreach (var prior in remaining) await AttemptLock.LoadAsync(db, prior.Id);
        attempt.Participant.CompletionStatus = remaining.Any(AttemptRules.IsOpen) ? CompletionStatus.InProgress
            : remaining.Any() ? (AttemptRules.AwaitsScores(remaining.OrderByDescending(x => x.AttemptNumber).First()) ? CompletionStatus.SubmittedAwaitingScore : CompletionStatus.Completed)
            : CompletionStatus.NotStarted;
        AttemptRules.RefreshCompletionLabels(attempt.Participant);
        if (a.Mode == AssignmentMode.Test) GuestService.ResolveTestChoice(people, Now);
        db.Audit?.Add(AuditAction.RetakeCancelled, "Attempt", attemptId, userId); // (NK3)
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        await assignments.NotifyLecturersAsync(a.Id);
    }
}
