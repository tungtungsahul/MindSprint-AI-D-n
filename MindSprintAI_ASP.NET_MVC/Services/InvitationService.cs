using System.Net;
using System.Net.Mail;
using Microsoft.EntityFrameworkCore;
using MindSprintAI_ASP.NET_MVC.Data;
using MindSprintAI_ASP.NET_MVC.Data.Entities;

namespace MindSprintAI_ASP.NET_MVC.Services;

public class EmailLinks(IConfiguration config, IHttpContextAccessor accessor)
{
    public string To(string path)
    {
        var request = accessor.HttpContext?.Request;
        var origin = config["App:BaseUrl"]?.TrimEnd('/')
            ?? (request == null ? "https://localhost:7228" : request.Scheme + "://" + request.Host + request.PathBase);
        return origin + path;
    }
}

// Email/xác minh để trang nói rõ vì sao danh sách trống (lời mời chỉ hiện với đúng email đã xác minh).
public record InvitationPage(List<Invitation> Received, List<Invitation> Sent, string? Email = null, bool EmailConfirmed = true);

public class InvitationService(AppDbContext db, TimeProvider clock, NotificationService notifications,
    IEmailSender email, EmailLinks links)
{
    public const string SentMessage = "Đã gửi lời mời tới email này."; // (L4)
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    public static string Normalize(string value) => value.Trim().ToUpperInvariant();

    public async Task InviteAssistantAsync(int classroomId, string address, string inviterId)
    {
        Invitation row;
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            await ClassroomLock.RoomAsync(db, classroomId);
            await ClassroomLock.AssignmentsAsync(db, classroomId);
            await ClassroomLock.MembersAsync(db, classroomId);
            await EnsureMainAsync(classroomId, inviterId);
            var recipient = await FindRecipientAsync(Normalize(address ?? ""));
            if (recipient != null) await CheckAssistantAsync(new() { ClassroomId = classroomId }, recipient.Id);
            row = await CreateAsync(classroomId, address ?? "", inviterId, InvitationKind.Assistant, false);
            await NotifySentAsync(row);
            if (recipient != null)
            {
                var member = await db.ClassMembers.SingleOrDefaultAsync(m => m.ClassroomId == classroomId && m.UserId == recipient.Id);
                member ??= new() { ClassroomId = classroomId, UserId = recipient.Id };
                if (member.Id == 0) db.ClassMembers.Add(member);
                member.Role = ClassRole.AssistantLecturer; member.Status = ClassMemberStatus.Invited;
                member.AddedAt = Now; member.JoinedAt = null; member.LeftAt = null; member.InvitedById = inviterId;
                await db.SaveChangesAsync(); row.MemberId = member.Id;
                await NotifyRecipientAsync(row, recipient);
            }
            db.Audit?.Add(AuditAction.AssistantInvited, "Classroom", classroomId, inviterId); // (NK3)
            await db.SaveChangesAsync(); await tx.CommitAsync();
        }
        await AfterInviteAsync(row);
    }

    internal Task<ApplicationUser?> FindRecipientAsync(string normalized) => db.Users.SingleOrDefaultAsync(u => !u.IsGuest
        && u.Email != null && u.Email.ToUpper() == normalized);

    // Bên gọi đã khóa lớp và mở giao dịch; sổ lời mời cũng là bộ đếm email (L6).
    internal async Task<Invitation> CreateAsync(int classroomId, string address, string inviterId, InvitationKind kind, bool stays)
    {
        address = (address ?? "").Trim();
        if (address.Length > 256 || !MailAddress.TryCreate(address, out var parsed) || parsed.Address != address)
            throw new BusinessException("Email không hợp lệ.");
        await Tang7cLock.AcquireAsync(db, "invite:" + inviterId);
        var day = Now.AddHours(7).Date.AddHours(-7);
        if (await db.Invitations.CountAsync(i => i.InviterId == inviterId && i.CreatedAt >= day && i.CreatedAt < day.AddDays(1)) >= 20)
            throw new BusinessException("Bạn đã gửi đủ 20 email mời hôm nay (giờ Việt Nam). Hãy thử lại vào ngày mai.");
        if (kind == InvitationKind.Assistant && await db.Invitations.CountAsync(i => i.ClassroomId == classroomId
            && i.Kind == kind && i.Status == InvitationStatus.Pending && i.ExpiresAt > Now) >= 10)
            throw new BusinessException("Mỗi lớp có tối đa 10 lời mời giảng viên phụ đang chờ.");
        if (kind == InvitationKind.Transfer && (await db.ClassTransfers.AnyAsync(t => t.ClassroomId == classroomId && t.Status == ClassTransferStatus.Pending)
            || await db.Invitations.AnyAsync(i => i.ClassroomId == classroomId && i.Kind == kind && i.Status == InvitationStatus.Pending)))
            throw new BusinessException("Lớp đang có một yêu cầu chuyển chờ nhận.");
        var normalized = Normalize(address);
        var row = new Invitation { ClassroomId = classroomId, InviterId = inviterId, Email = address, NormalizedEmail = normalized,
            Kind = kind, FromUserStays = stays, CreatedAt = Now, ExpiresAt = Now.AddDays(7) };
        db.Invitations.Add(row);
        return row;
    }

    internal async Task AfterInviteAsync(Invitation row)
    {
        await notifications.PushAsync(new[] { row.InviterId, row.TransferFromUserId, row.RecipientNotifiedAt != null ? row.RecipientId : null }.OfType<string>().ToArray());
        var room = await db.Classrooms.Where(c => c.Id == row.ClassroomId).Select(c => c.Name).SingleAsync();
        var inviter = await db.Users.Where(u => u.Id == row.InviterId).Select(u => u.DisplayName).SingleAsync();
        // (L5) link chỉ dẫn tới trang đăng nhập/lời mời và đăng ký, không mã/token.
        await email.SendAsync(row.Email, $"Lời mời {Brand.Name}",
            $"<p>{WebUtility.HtmlEncode(inviter)} mời bạn {(row.Kind == InvitationKind.Assistant ? "làm giảng viên phụ" : "nhận chuyển lớp")} lớp {WebUtility.HtmlEncode(room)}.</p>"
            + $"<p>Hạn: {VnTime.Format(row.ExpiresAt)} (giờ Việt Nam).</p><p><a href=\"{WebUtility.HtmlEncode(links.To("/Invitations"))}\">Xem lời mời</a> · "
            + $"<a href=\"{WebUtility.HtmlEncode(links.To("/Account/Register"))}\">Đăng ký bằng email được mời và xác minh email</a></p>");
    }

    internal async Task NotifySentAsync(Invitation row)
    {
        var name = await db.Classrooms.Where(c => c.Id == row.ClassroomId).Select(c => c.Name).SingleAsync();
        foreach (var uid in new[] { row.InviterId, row.TransferFromUserId }.OfType<string>().Distinct())
            await notifications.AddAsync(uid, row.Kind == InvitationKind.Assistant ? NotificationType.Assistant : NotificationType.Transfer,
                new($"Lớp \"{name}\": {SentMessage}", "/Invitations", row.ClassroomId));
    }

    internal async Task NotifyRecipientAsync(Invitation row, ApplicationUser recipient)
    {
        row.RecipientId = recipient.Id;
        if (!recipient.EmailConfirmed || row.RecipientNotifiedAt != null) return;
        var name = await db.Classrooms.Where(c => c.Id == row.ClassroomId).Select(c => c.Name).SingleAsync();
        await notifications.AddAsync(recipient.Id, row.Kind == InvitationKind.Assistant ? NotificationType.Assistant : NotificationType.Transfer,
            new($"Lớp \"{name}\": bạn có lời mời {(row.Kind == InvitationKind.Assistant ? "giảng viên phụ" : "chuyển lớp")}. Hạn {VnTime.Format(row.ExpiresAt)} (giờ Việt Nam).", "/Invitations", row.ClassroomId));
        row.RecipientNotifiedAt = Now;
    }

    public async Task<InvitationPage> ListAsync(string userId)
    {
        await RevealAsync(userId);
        var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == userId);
        var normalized = Normalize(user.Email ?? "");
        var received = user.EmailConfirmed && !user.IsGuest ? await db.Invitations.AsNoTracking().Include(i => i.Classroom)
            .Where(i => i.NormalizedEmail == normalized && i.Status == InvitationStatus.Pending && i.ExpiresAt > Now && i.Classroom.DeletedAt == null)
            .OrderByDescending(i => i.Id).Take(100).ToListAsync() : [];
        var sent = await db.Invitations.AsNoTracking().Include(i => i.Classroom).Where(i => (i.InviterId == userId
            || i.Classroom.Members.Any(m => m.UserId == userId && m.Status == ClassMemberStatus.Active && m.Role == ClassRole.MainLecturer)) && i.Status == InvitationStatus.Pending
            && i.ExpiresAt > Now && i.Classroom.DeletedAt == null).OrderByDescending(i => i.Id).Take(100).ToListAsync();
        return new(received, sent, user.Email, user.EmailConfirmed);
    }

    public async Task RevealAsync(string userId)
    {
        var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == userId);
        if (!user.EmailConfirmed || user.IsGuest || user.Email == null) return;
        var normalized = Normalize(user.Email);
        foreach (var id in await db.Invitations.Where(i => i.NormalizedEmail == normalized && i.Status == InvitationStatus.Pending
            && i.ExpiresAt > Now && i.RecipientNotifiedAt == null && i.Classroom.DeletedAt == null).OrderBy(i => i.Id).Select(i => i.Id).Take(100).ToListAsync())
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            var header = await db.Invitations.AsNoTracking().SingleAsync(i => i.Id == id);
            await ClassroomLock.RoomAsync(db, header.ClassroomId);
            var row = await LoadAsync(id);
            if (row.Status != InvitationStatus.Pending || row.ExpiresAt <= Now || row.RecipientNotifiedAt != null) continue;
            await NotifyRecipientAsync(row, user);
            await db.SaveChangesAsync(); await tx.CommitAsync();
        }
        await notifications.PushAsync(userId);
    }

    internal async Task<Invitation> LoadAsync(long id)
    {
        var row = await db.Invitations.SingleOrDefaultAsync(i => i.Id == id) ?? throw new ForbiddenException();
        await db.Entry(row).ReloadAsync(); return row;
    }

    // Người có đường dẫn lời mời nhưng đăng nhập sai tài khoản: nói cách xử lý, không lộ email được mời.
    public const string WrongRecipient = "Lời mời này gửi tới một email khác với tài khoản bạn đang đăng nhập, hoặc email của bạn chưa được xác minh. Hãy đăng nhập đúng tài khoản có email được mời (đã xác minh) rồi mở lại lời mời.";

    internal async Task EnsureRecipientAsync(Invitation row, string userId)
    {
        if (!await db.Users.AnyAsync(u => u.Id == userId && !u.IsGuest && u.EmailConfirmed && u.Email != null && u.Email.ToUpper() == row.NormalizedEmail))
            throw new BusinessException(WrongRecipient);
    }

    internal async Task EnsureMainAsync(int classroomId, string userId)
    {
        if (!await db.ClassMembers.AnyAsync(m => m.ClassroomId == classroomId && m.UserId == userId && m.Role == ClassRole.MainLecturer
            && m.Status == ClassMemberStatus.Active && m.Classroom.DeletedAt == null)) throw new ForbiddenException();
    }

    private async Task CheckAssistantAsync(Invitation row, string userId)
    {
        var member = await db.ClassMembers.SingleOrDefaultAsync(m => m.ClassroomId == row.ClassroomId && m.UserId == userId);
        if (member?.Status is ClassMemberStatus.Active or ClassMemberStatus.PendingApproval
            || member?.Status == ClassMemberStatus.Invited && member.Id != row.MemberId)
            throw new BusinessException("Tài khoản này đã là thành viên hoặc đang chờ vào lớp.");
    }

    public async Task DecideAssistantAsync(long id, bool accept, string userId)
    {
        var header = await db.Invitations.AsNoTracking().SingleOrDefaultAsync(i => i.Id == id) ?? throw new ForbiddenException();
        await using var tx = await db.Database.BeginTransactionAsync();
        await ClassroomLock.RoomAsync(db, header.ClassroomId);
        await ClassroomLock.AssignmentsAsync(db, header.ClassroomId);
        await ClassroomLock.MembersAsync(db, header.ClassroomId);
        var row = await LoadAsync(id);
        await EnsureRecipientAsync(row, userId);
        if (row.Kind != InvitationKind.Assistant || row.Status != InvitationStatus.Pending) throw new BusinessException("Lời mời đã được xử lý.");
        row.RecipientId = userId;
        if (row.ExpiresAt <= Now)
        {
            await FinishAsync(row, InvitationStatus.Expired);
            await db.SaveChangesAsync(); await tx.CommitAsync(); await AfterFinishAsync(row);
            throw new BusinessException("Lời mời đã hết hạn sau 7 ngày.");
        }
        if (accept)
        {
            await EnsureMainAsync(row.ClassroomId, row.InviterId); // (L4) kiểm lại lúc nhận.
            await AccountLockState.EnsureAllowedAsync(db, userId, Now);
            await CheckAssistantAsync(row, userId);
            var member = await db.ClassMembers.SingleOrDefaultAsync(m => m.ClassroomId == row.ClassroomId && m.UserId == userId);
            member ??= new() { ClassroomId = row.ClassroomId, UserId = userId };
            if (member.Id == 0) db.ClassMembers.Add(member);
            member.Role = ClassRole.AssistantLecturer; member.Status = ClassMemberStatus.Active;
            member.AddedAt = row.CreatedAt; member.JoinedAt = Now; member.LeftAt = null; member.InvitedById = row.InviterId;
            await db.SaveChangesAsync(); row.MemberId = member.Id;
        }
        await FinishAsync(row, accept ? InvitationStatus.Accepted : InvitationStatus.Declined);
        db.Audit?.Add(accept ? AuditAction.AssistantAccepted : AuditAction.AssistantDeclined, "Invitation", id, userId); // (NK3)
        await db.SaveChangesAsync(); await tx.CommitAsync(); await AfterFinishAsync(row);
    }

    public async Task RevokeAsync(long id, string userId)
    {
        var header = await db.Invitations.AsNoTracking().SingleOrDefaultAsync(i => i.Id == id) ?? throw new ForbiddenException();
        await using var tx = await db.Database.BeginTransactionAsync();
        await ClassroomLock.RoomAsync(db, header.ClassroomId);
        await ClassroomLock.AssignmentsAsync(db, header.ClassroomId);
        await ClassroomLock.MembersAsync(db, header.ClassroomId);
        await EnsureMainAsync(header.ClassroomId, userId);
        var row = await LoadAsync(id);
        if (row.Status != InvitationStatus.Pending) throw new BusinessException("Lời mời đã được xử lý.");
        await FinishAsync(row, row.ExpiresAt <= Now ? InvitationStatus.Expired : InvitationStatus.Revoked);
        db.Audit?.Add(AuditAction.InvitationRevoked, "Invitation", id, userId); // (NK3)
        await db.SaveChangesAsync(); await tx.CommitAsync(); await AfterFinishAsync(row);
    }

    internal async Task FinishAsync(Invitation row, InvitationStatus status)
    {
        row.Status = status;
        if (row.MemberId is { } mid && status != InvitationStatus.Accepted)
        {
            var m = await db.ClassMembers.FindAsync(mid);
            if (m?.Status == ClassMemberStatus.Invited)
            {
                m.Status = status == InvitationStatus.Expired ? ClassMemberStatus.InvitationExpired
                    : status == InvitationStatus.Revoked ? ClassMemberStatus.InvitationRevoked : ClassMemberStatus.InvitationDeclined;
                m.LeftAt = Now;
            }
        }
        if (row.TransferId is { } tid)
        {
            var t = await db.ClassTransfers.FindAsync(tid);
            if (t?.Status == ClassTransferStatus.Pending) t.Status = status == InvitationStatus.Expired ? ClassTransferStatus.Expired
                : status == InvitationStatus.Revoked ? ClassTransferStatus.Cancelled : status == InvitationStatus.Declined ? ClassTransferStatus.Declined : ClassTransferStatus.Accepted;
        }
        var recipient = await FindRecipientAsync(row.NormalizedEmail);
        row.RecipientId = recipient?.EmailConfirmed == true ? recipient.Id : null;
        var name = await db.Classrooms.Where(c => c.Id == row.ClassroomId).Select(c => c.Name).SingleAsync();
        var text = $"Lớp \"{name}\": lời mời {StatusText(status)}.";
        foreach (var uid in new[] { row.InviterId, row.TransferFromUserId, row.RecipientId }.OfType<string>().Distinct())
            await notifications.AddAsync(uid, row.Kind == InvitationKind.Assistant ? NotificationType.Assistant : NotificationType.Transfer,
                new(text, "/Invitations", row.ClassroomId));
    }

    internal async Task AfterFinishAsync(Invitation row)
    {
        await notifications.PushAsync(new[] { row.InviterId, row.TransferFromUserId, row.RecipientId }.OfType<string>().ToArray());
        if (row.Status is not (InvitationStatus.Accepted or InvitationStatus.Declined or InvitationStatus.Expired)) return; // (L7) thu hồi không email.
        var inviter = await db.Users.AsNoTracking().SingleAsync(u => u.Id == row.InviterId);
        var name = await db.Classrooms.Where(c => c.Id == row.ClassroomId).Select(c => c.Name).SingleAsync();
        if (inviter.Email != null) await email.SendAsync(inviter.Email, $"Lời mời {StatusText(row.Status)} · {Brand.Name}",
            $"<p>Lời mời lớp {WebUtility.HtmlEncode(name)} {StatusText(row.Status)}.</p><p><a href=\"{WebUtility.HtmlEncode(links.To("/Invitations"))}\">Xem lời mời</a></p>");
    }

    public static string StatusText(InvitationStatus status) => status switch
    {
        InvitationStatus.Accepted => "đã được chấp nhận", InvitationStatus.Declined => "đã bị từ chối",
        InvitationStatus.Revoked => "đã thu hồi", InvitationStatus.Expired => "đã hết hạn", _ => "đang chờ"
    };

    internal async Task<List<Invitation>> CancelForClassAsync(int classroomId)
    {
        var rows = await db.Invitations.Where(i => i.ClassroomId == classroomId && i.Status == InvitationStatus.Pending).ToListAsync();
        foreach (var row in rows) await FinishAsync(row, row.ExpiresAt <= Now ? InvitationStatus.Expired : InvitationStatus.Revoked);
        return rows;
    }

    public async Task ExpireAsync()
    {
        foreach (var id in await db.Invitations.Where(i => i.Status == InvitationStatus.Pending && i.ExpiresAt <= Now)
            .OrderBy(i => i.Id).Select(i => i.Id).Take(200).ToListAsync())
        {
            var header = await db.Invitations.AsNoTracking().SingleAsync(i => i.Id == id);
            await using var tx = await db.Database.BeginTransactionAsync();
            // Lớp đã xóa vẫn phải kết thúc lời mời; chỉ khóa hàng, không yêu cầu lớp còn hoạt động.
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT [Id] FROM [Classrooms] WITH (UPDLOCK, HOLDLOCK) WHERE [Id]={header.ClassroomId}");
            var row = await LoadAsync(id);
            if (row.Status != InvitationStatus.Pending || row.ExpiresAt > Now) continue;
            await FinishAsync(row, InvitationStatus.Expired);
            await db.SaveChangesAsync(); await tx.CommitAsync(); await AfterFinishAsync(row);
        }
    }
}
