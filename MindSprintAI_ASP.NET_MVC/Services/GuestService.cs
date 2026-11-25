using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using MindSprintAI_ASP.NET_MVC.Data;
using MindSprintAI_ASP.NET_MVC.Data.Entities;

namespace MindSprintAI_ASP.NET_MVC.Services;

public class GuestService(AppDbContext db, TimeProvider clock, ILearnNotifier notifier)
{
    public const string CookieName = "qem_guest";
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    public static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    public static string? HashToken(string? token) => token?.Length == 64 && token.All(Uri.IsHexDigit)
        ? Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))) : null;
    public static string ValidateName(string? name) => name?.Trim() is { Length: >= 1 and <= 50 } value
        ? value : throw new BusinessException("Nhập tên khách từ 1 đến 50 ký tự.");

    // Khóa bí mật khách trước các khóa nghiệp vụ; Join/Merge cùng thứ tự, không giữ khóa token ở đường làm bài.
    private Task TokenLockAsync(string hash) => db.Database.ExecuteSqlInterpolatedAsync(
        $"DECLARE @r int; EXEC @r = sp_getapplock @Resource = {"guest:" + hash}, @LockMode = 'Exclusive', @LockOwner = 'Transaction'; IF @r < 0 THROW 50000, N'Không lấy được khóa khách.', 1;");

    public async Task<Guest?> ResumeAsync(string? token)
    {
        var hash = HashToken(token);
        return hash == null ? null : await db.Guests.Include(g => g.User).SingleOrDefaultAsync(g => g.BrowserTokenHash == hash
            && g.MergedIntoUserId == null && g.User.LockoutEnd == null);
    }

    public async Task CheckEntryAsync(int assignmentId, string? token)
    {
        var a = await db.Assignments.SingleOrDefaultAsync(a => a.Id == assignmentId)
            ?? throw new BusinessException("Không tìm thấy bài.");
        AssignmentAccess.EnsureJoinable(a, Now);
        var hash = HashToken(token);
        if (a.StrangerPolicy == StrangerPolicy.Reject && !await db.Participants.AnyAsync(p => p.AssignmentId == assignmentId
            && p.Guest != null && p.Guest.BrowserTokenHash == hash && p.Guest.MergedIntoUserId == null))
            throw new BusinessException("Bài này chỉ dành cho thành viên lớp. Hãy vào lớp bằng mã vào lớp trước.");
    }

    // Pháp lý C3: khách mới, hoặc khách cũ vào bài chưa từng vào, phải tick ô đồng ý ("để giảng viên của bài này xem").
    public async Task<bool> NeedsConsentAsync(int assignmentId, string? token)
    {
        var guest = await ResumeAsync(token);
        return guest == null || !await db.Participants.AnyAsync(p => p.AssignmentId == assignmentId && p.GuestId == guest.Id);
    }

    public async Task<Guest> JoinAsync(int assignmentId, string? displayName, string token)
    {
        var hash = HashToken(token) ?? throw new BusinessException("Không nhận dạng được trình duyệt.");
        await using var tx = await db.Database.BeginTransactionAsync();
        await TokenLockAsync(hash);
        // Thử tải 13/11/2026: khách vào bài chỉ khóa chung dòng bài (không bắt cả lớp xếp hàng); xem AttemptLock.
        var a = await AttemptLock.SharedAsync(db, assignmentId);
        AssignmentAccess.EnsureJoinable(a, Now);
        var guest = await ResumeAsync(token);
        if (a.StrangerPolicy == StrangerPolicy.Reject && (guest == null
            || !await db.Participants.AnyAsync(p => p.AssignmentId == a.Id && p.GuestId == guest.Id)))
            throw new BusinessException("Bài này chỉ dành cho thành viên lớp. Hãy vào lớp bằng mã vào lớp trước.");
        if (guest == null)
        {
            // Có cookie của khách đã gộp thì không tạo lại tài khoản ẩn bằng bí mật cũ.
            if (await db.Guests.AnyAsync(g => g.BrowserTokenHash == hash)) throw new BusinessException("Kết quả khách đã được lưu vào tài khoản. Hãy đăng nhập.");
            var name = ValidateName(displayName);
            var username = "guest-" + Guid.NewGuid().ToString("N");
            var user = new ApplicationUser { UserName = username, NormalizedUserName = username.ToUpperInvariant(),
                DisplayName = name, IsGuest = true, SecurityStamp = Guid.NewGuid().ToString(), CreatedAt = Now,
                // Người gọi (trang Tham gia) đã bắt tick ô đồng ý C3 trước khi tạo khách mới.
                PolicyAcceptedAt = Now, PolicyVersion = LegalPolicy.CurrentVersion };
            guest = new Guest { User = user, DisplayName = name, BrowserTokenHash = hash, CreatedAt = Now };
            db.Guests.Add(guest);
            await db.SaveChangesAsync();
        }
        await AttemptLock.LearnerKeyAsync(db, a.Id, guest.UserId);
        var p = await db.Participants.SingleOrDefaultAsync(p => p.AssignmentId == a.Id && p.GuestId == guest.Id);
        if (p == null)
        {
            // Tên khách không trùng trong bài: phần chọn tên + thêm người làm xếp hàng với những người có tên có thể trùng.
            // Thử tải 14/11/2026 (Ôn tập, 650 khách vào trong lúc người khác đang làm): hàng chờ quá 25 giây → 188 người gặp lỗi 500.
            try { await AttemptLock.GuestNameAsync(db, a.Id, guest.User.DisplayName); }
            catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number == 50000)
            {
                throw new BusinessException("Đang có nhiều người vào bài cùng lúc. Bạn bấm Vào bài lại sau vài giây.");
            }
            var name = guest.User.DisplayName;
            // Chỉ đọc những tên có thể trùng ("Minh", "Minh (2)"...) thay cho mọi tên trong bài: giữ lượt xếp hàng ngắn khi lớp đông.
            // So sánh trong SQL Server không phân biệt hoa thường (collation mặc định), giống OrdinalIgnoreCase ở dưới.
            var prefix = name + " (";
            var names = await db.Participants.Where(p => p.AssignmentId == a.Id)
                .Select(p => p.GuestDisplayName ?? p.User!.DisplayName)
                .Where(n => n == name || n.StartsWith(prefix)).ToListAsync();
            // Thành viên chưa làm bài cũng giữ tên của họ, tránh khách bị nhầm với học viên.
            names.AddRange(await db.ClassMembers.Where(m => m.ClassroomId == a.ClassroomId && m.Status == ClassMemberStatus.Active)
                .Select(m => m.User.DisplayName).Where(n => n == name || n.StartsWith(prefix)).ToListAsync());
            var unique = name;
            for (var suffix = 2; names.Contains(unique, StringComparer.OrdinalIgnoreCase); suffix++) unique = $"{name} ({suffix})";
            guest.DisplayName = unique;
            // Pháp lý C3: mỗi bài mới khách đều tick lại ô đồng ý (trang Tham gia kiểm qua NeedsConsentAsync).
            guest.User.PolicyAcceptedAt = Now; guest.User.PolicyVersion = LegalPolicy.CurrentVersion;
            p = new Participant { Assignment = a, UserId = guest.UserId, GuestId = guest.Id, GuestDisplayName = unique,
                IsStranger = true, ApprovalStatus = a.StrangerPolicy == StrangerPolicy.RequireApproval ? ApprovalStatus.Pending : ApprovalStatus.NotRequired,
                CreatedAt = Now };
            db.Participants.Add(p);
            await db.SaveChangesAsync();
        }
        if (p.ApprovalStatus == ApprovalStatus.Rejected) throw new BusinessException("Giảng viên đã từ chối yêu cầu tham gia.");
        await tx.CommitAsync();
        await notifier.LecturersAsync(a.Id);
        return guest;
    }

    // (45, 47, 55) chỉ gọi sau đăng nhập đúng mật khẩu và email đã xác minh, còn token cùng trình duyệt.
    public async Task<bool> MergeAsync(string? token, string userId)
    {
        var hash = HashToken(token);
        if (hash == null) return false;
        await using var tx = await db.Database.BeginTransactionAsync();
        await TokenLockAsync(hash);
        var guest = await db.Guests.Include(g => g.User).SingleOrDefaultAsync(g => g.BrowserTokenHash == hash);
        if (guest != null) { await db.Entry(guest).ReloadAsync(); await db.Entry(guest.User).ReloadAsync(); }
        if (guest == null || guest.MergedIntoUserId != null) return false;
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId && !u.IsGuest && u.EmailConfirmed);
        if (user == null) throw new ForbiddenException();
        var assignmentIds = await db.Participants.Where(p => p.GuestId == guest.Id).OrderBy(p => p.AssignmentId)
            .Select(p => p.AssignmentId).ToListAsync();
        var classIds = await db.Assignments.Where(a => assignmentIds.Contains(a.Id) && a.ClassroomId != null)
            .Select(a => a.ClassroomId!.Value).Distinct().OrderBy(id => id).ToListAsync();
        foreach (var id in classIds) await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT [Id] FROM [Classrooms] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {id}");
        var assignments = new List<Assignment>();
        foreach (var id in assignmentIds) assignments.Add(await AttemptLock.AssignmentAsync(db, id));
        foreach (var a in assignments) await AttemptLock.MemberAsync(db, a.ClassroomId, userId);
        await AccountLockState.EnsureAllowedAsync(db, userId, Now);
        var people = await db.Participants.Where(p => assignmentIds.Contains(p.AssignmentId)
            && (p.GuestId == guest.Id || p.UserId == userId)).OrderBy(p => p.Id).ToListAsync();
        foreach (var p in people) await AttemptLock.ParticipantAsync(db, p.Id);
        foreach (var at in people.SelectMany(p => p.Attempts).OrderBy(at => at.Id).ToList()) await AttemptLock.LoadAsync(db, at.Id);
        foreach (var a in assignments)
        {
            var source = people.Single(p => p.AssignmentId == a.Id && p.GuestId == guest.Id);
            var target = people.SingleOrDefault(p => p.AssignmentId == a.Id && p.UserId == userId);
            foreach (var at in source.Attempts)
            {
                AttemptRules.ExpireIfDue(at, source, Now);
                at.MadeAsGuest = true;
            }
            source.CompletionLabels |= CompletionLabels.MadeAsGuest;
            if (a.Mode == AssignmentMode.Test && target != null) continue; // giữ cả hai để GV hủy lượt theo (47).
            if (target == null)
            {
                source.UserId = userId;
                var member = await db.ClassMembers.SingleOrDefaultAsync(m => m.ClassroomId == a.ClassroomId && m.UserId == userId);
                source.IsStranger = member?.Status != ClassMemberStatus.Active || !AssignmentService.IsAssignedTo(a, member);
                if (!source.IsStranger) source.ApprovalStatus = ApprovalStatus.NotRequired;
            }
            else
            {
                var moved = source.Attempts.OrderBy(at => at.AttemptNumber).ToList();
                var exceeds = a.MaxAttempts is { } max && target.Attempts.Concat(moved).Count(at => at.Status != AttemptStatus.Cancelled && at.Kind != AttemptKind.SelfReview) > (long)max + target.ExtraAttempts;
                // Ưu tiên lượt tính điểm của khách khi tài khoản chỉ đang tự ôn.
                foreach (var at in target.Attempts.Where(at => at.Kind == AttemptKind.SelfReview && AttemptRules.IsOpen(at)).ToList())
                    AttemptRules.End(at, target, AttemptEndReason.Submitted, Now);
                if (target.Attempts.Any(at => at.Kind != AttemptKind.SelfReview && AttemptRules.IsOpen(at)))
                    foreach (var at in moved.Where(AttemptRules.IsOpen).ToList()) AttemptRules.End(at, source, AttemptEndReason.Submitted, Now);
                // Giải phóng unique index lượt mở trước khi chuyển lượt khách, vẫn trong cùng giao dịch.
                await db.SaveChangesAsync();
                var next = target.Attempts.Select(at => at.AttemptNumber).DefaultIfEmpty().Max();
                foreach (var at in moved)
                {
                    at.Participant = target;
                    at.AttemptNumber = ++next;
                    at.ExcludeFromGradebook = exceeds;
                }
                target.CompletionLabels |= CompletionLabels.MadeAsGuest;
                target.MasteryReached |= source.MasteryReached;
                // Giữ dòng khách làm dấu vết, nhưng không còn sở hữu lượt nào.
                source.UserId = null;
                source.GuestStartedAt = null;
                source.CompletionStatus = CompletionStatus.NotStarted;
            }
            await db.SaveChangesAsync();
            // Chuyển tiến độ thẻ theo từng thẻ; không tạo bản ghi trùng unique index.
            var destination = target ?? source;
            foreach (var card in await db.FlashcardProgresses.Where(c => c.ParticipantId == source.Id).ToListAsync())
            {
                var existing = await db.FlashcardProgresses.SingleOrDefaultAsync(c => c.ParticipantId == destination.Id && c.FlashcardId == card.FlashcardId);
                if (existing != null && existing.Id != card.Id)
                {
                    // (D3a) gộp riêng từng chế độ, lần ôn SRS không ghi đè nhóm truyền thống.
                    async Task<DateTime> LastMode(FlashcardProgress progress, FlashcardMode mode) =>
                        mode == FlashcardMode.Traditional ? progress.TraditionalReviewedAt ?? progress.LastReviewedAt :
                        await db.FlashcardReviews.Where(r => r.ParticipantId == progress.ParticipantId && r.FlashcardId == progress.FlashcardId && r.Mode == mode)
                            .Where(r => r.WasDue).MaxAsync(r => (DateTime?)r.ReviewedAt) ?? DateTime.MinValue;
                    if (card.HasTraditionalRating != false && (existing.HasTraditionalRating == false
                        || await LastMode(card, FlashcardMode.Traditional) > await LastMode(existing, FlashcardMode.Traditional)))
                    { existing.LastRating = card.LastRating; existing.HasTraditionalRating = true; existing.TraditionalReviewedAt = card.TraditionalReviewedAt ?? card.LastReviewedAt; }
                    if (card.SrsBox != null && (existing.SrsBox == null
                        || await LastMode(card, FlashcardMode.SpacedRepetition) > await LastMode(existing, FlashcardMode.SpacedRepetition)))
                    { existing.SrsBox = card.SrsBox; existing.SrsDueDate = card.SrsDueDate; }
                    if (card.LastReviewedAt > existing.LastReviewedAt) existing.LastReviewedAt = card.LastReviewedAt;
                    if (card.FirstReviewedAt < existing.FirstReviewedAt) existing.FirstReviewedAt = card.FirstReviewedAt;
                    existing.ReviewCount += card.ReviewCount;
                    // Giữ bản gốc ở dòng khách làm dấu vết, không xóa dữ liệu học tập.
                }
                else { card.ParticipantId = destination.Id; card.UserId = userId; }
            }
            foreach (var review in await db.FlashcardReviews.Where(r => r.ParticipantId == source.Id).ToListAsync())
            { review.ParticipantId = destination.Id; review.UserId = userId; }
            await db.SaveChangesAsync();
            if (!CardCompletion.HasCards(a))
            {
                var live = destination.Attempts.Where(at => at.Status != AttemptStatus.Cancelled && at.Kind != AttemptKind.SelfReview).OrderBy(at => at.AttemptNumber).ToList();
                destination.CompletionStatus = live.Any(AttemptRules.IsOpen) ? CompletionStatus.InProgress
                    : live.Count == 0 ? CompletionStatus.NotStarted
                    : AttemptRules.AwaitsScores(live[^1]) ? CompletionStatus.SubmittedAwaitingScore : CompletionStatus.Completed;
                AttemptRules.RefreshCompletionLabels(destination);
            }
            await CardCompletion.RefreshAsync(db, destination, Now);
        }
        guest.MergedIntoUserId = userId;
        guest.User.LockoutEnabled = true;
        guest.User.LockoutEnd = DateTimeOffset.MaxValue;
        guest.User.SecurityStamp = Guid.NewGuid().ToString();
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        foreach (var id in assignmentIds) await notifier.LecturersAsync(id);
        return true;
    }

    // (47) chỉ sau khi GV hủy hết lượt tài khoản: lượt khách được chọn chuyển sang tài khoản đó.
    internal static void ResolveTestChoice(List<Participant> people, DateTime now)
    {
        foreach (var source in people.Where(p => p.Guest?.MergedIntoUserId != null && p.UserId == p.Guest.UserId))
        {
            var target = people.SingleOrDefault(p => p.UserId == source.Guest!.MergedIntoUserId);
            if (target == null || target.Attempts.Any(at => at.Status != AttemptStatus.Cancelled)) continue;
            var kept = source.Attempts.Where(at => at.Status != AttemptStatus.Cancelled).OrderBy(at => at.AttemptNumber).ToList();
            if (kept.Count == 0) continue;
            var next = target.Attempts.Select(at => at.AttemptNumber).DefaultIfEmpty().Max();
            foreach (var at in kept)
            {
                AttemptRules.ExpireIfDue(at, source, now);
                at.Participant = target; at.AttemptNumber = ++next; at.MadeAsGuest = true;
                source.Attempts.Remove(at);
                if (!target.Attempts.Contains(at)) target.Attempts.Add(at);
            }
            source.UserId = null; source.GuestStartedAt = null;
            target.CompletionLabels |= CompletionLabels.MadeAsGuest;
            target.CompletionStatus = kept.Any(AttemptRules.IsOpen) ? CompletionStatus.InProgress
                : AttemptRules.AwaitsScores(kept[^1]) ? CompletionStatus.SubmittedAwaitingScore : CompletionStatus.Completed;
            AttemptRules.RefreshCompletionLabels(target, kept[^1]);
        }
    }

    public async Task KeepTestGuestAsync(int assignmentId, int participantId, string? reason, string lecturerId)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 500) throw new BusinessException("Nhập lý do chọn lượt, tối đa 500 ký tự.");
        await using var tx = await db.Database.BeginTransactionAsync();
        var a = await AttemptLock.AssignmentAsync(db, assignmentId);
        if (!await db.ClassMembers.AnyAsync(m => m.ClassroomId == a.ClassroomId && m.UserId == lecturerId
            && m.Classroom.DeletedAt == null && m.Status == ClassMemberStatus.Active
            && (m.Role == ClassRole.MainLecturer || m.Role == ClassRole.AssistantLecturer))) throw new ForbiddenException();
        var people = await db.Participants.Include(p => p.Guest).Where(p => p.AssignmentId == assignmentId).OrderBy(p => p.Id).ToListAsync();
        foreach (var p in people) await AttemptLock.ParticipantAsync(db, p.Id);
        foreach (var at in people.SelectMany(p => p.Attempts).OrderBy(at => at.Id).ToList()) await AttemptLock.LoadAsync(db, at.Id);
        var source = people.SingleOrDefault(p => p.Id == participantId) ?? throw new ForbiddenException();
        if (a.Mode != AssignmentMode.Test || source.Guest?.MergedIntoUserId == null)
            throw new BusinessException("Không có lượt nào cần chọn: chỉ chọn được khi cùng một người có lượt làm lúc chưa đăng nhập (khách) và lượt làm bằng tài khoản.");
        var target = people.SingleOrDefault(p => p.UserId == source.Guest?.MergedIntoUserId && p.Id != source.Id);
        // Tài khoản chỉ có dòng phòng chờ thì không có lượt để bấm Hủy; GV cần một lựa chọn rõ ràng.
        if (target == null)
            throw new BusinessException("Chưa chọn giữ lượt được vì tài khoản của người này chưa vào bài. Hãy đợi họ đăng nhập và vào bài rồi thử lại.");
        if (source.UserId == null) return; // gửi lại sau khi đã chọn không chuyển lần thứ hai.
        if (target.Attempts.Count > 0) throw new BusinessException("Người này đã có lượt làm bằng tài khoản. Để chỉ giữ một lượt, hãy bấm Hủy lượt ở lượt bạn không muốn giữ.");
        if (!source.Attempts.Any(at => at.Status != AttemptStatus.Cancelled)) throw new BusinessException("Không còn lượt làm lúc chưa đăng nhập (khách) để giữ.");
        ResolveTestChoice(people, Now);
        db.AssignmentChanges.Add(new() { AssignmentId = assignmentId, ChangedById = lecturerId, ChangedAt = Now,
            Field = $"Participants[{participantId}].GuestChoice", NewValue = target.UserId, Reason = reason.Trim() });
        await db.SaveChangesAsync(); await tx.CommitAsync();
        await notifier.LecturersAsync(assignmentId);
    }
}
