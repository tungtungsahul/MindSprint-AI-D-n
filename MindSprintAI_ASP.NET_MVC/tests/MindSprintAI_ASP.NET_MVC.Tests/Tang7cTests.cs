using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using MindSprintAI_ASP.NET_MVC.Controllers;
using MindSprintAI_ASP.NET_MVC.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using MindSprintAI_ASP.NET_MVC.Data.Entities;
using MindSprintAI_ASP.NET_MVC.Services;

namespace MindSprintAI_ASP.NET_MVC.Tests;

public class Tang7cPureTests
{
    [Fact]
    public async Task FormMoiKhopTenThamSoVaCoChongGiaMao()
    {
        var room = new Classroom { Id = 9, Name = "Lớp <script>" };
        var invitations = new[] { InvitationKind.Assistant, InvitationKind.Transfer }.Select((k, n) => new Invitation
        { Id = n + 1, Classroom = room, Kind = k, Email = "recipient@test.vn", ExpiresAt = DateTime.UtcNow }).ToList();
        var html = await Tang4ViewTests.Render("Invitations", "Index", new InvitationPage(invitations, invitations));
        Assert.DoesNotContain("Lớp <script>", html);
        FormsMatch(html, typeof(InvitationsController));
        var account = await Tang4ViewTests.Render("Account", "Index", new ApplicationUser());
        FormsMatch(account, typeof(AccountController), "EmailReminders");
        Assert.Contains("checked", Assert.Single(Forms(account), f => f.Contains("data-action=\"EmailReminders\"")));
        var off = await Tang4ViewTests.Render("Account", "Index", new ApplicationUser { EmailRemindersEnabled = false });
        Assert.DoesNotContain("checked", Assert.Single(Forms(off), f => f.Contains("data-action=\"EmailReminders\"")));
        var alert = new Notification { Type = NotificationType.NewDevice, PayloadJson = System.Text.Json.JsonSerializer.Serialize(new NotificationPayload("vị trí ước tính không xác định", "/Account")) };
        var bell = await Tang4ViewTests.Render("Notifications", "Index", new NotificationPage([alert], 1, false));
        Assert.Contains("https://db-ip.com", bell); Assert.Contains("Vị trí ước tính theo DB-IP.com", WebUtility.HtmlDecode(bell));
    }

    private static IEnumerable<string> Forms(string html) => Regex.Matches(html, "<form\\b[^>]*>[\\s\\S]*?</form>").Select(m => m.Value);
    private static void FormsMatch(string html, Type controller, string? only = null)
    {
        var actions = controller.GetMethods().Where(m => m.IsDefined(typeof(HttpPostAttribute))).Select(m => m.Name).ToArray();
        var forms = Forms(html).Where(f => (only == null ? actions : [only]).Any(a => f.Contains($"data-action=\"{a}\""))).ToList();
        Assert.NotEmpty(forms);
        foreach (var form in forms)
        {
            var action = Regex.Match(form, "data-action=\"([^\"]+)\"").Groups[1].Value;
            var method = controller.GetMethods().Single(m => m.Name == action && m.IsDefined(typeof(HttpPostAttribute)));
            var names = method.GetParameters().Where(p => !p.IsDefined(typeof(FromServicesAttribute))).Select(p => p.Name!).Order();
            var fields = Regex.Matches(form, "name=\"([^\"]+)\"").Select(m => m.Groups[1].Value).Where(n => n != "__RequestVerificationToken").Distinct().Order();
            Assert.Equal(names, fields); Assert.Contains("__RequestVerificationToken", form); Assert.Contains("method=\"post\"", form);
        }
    }

    [Theory]
    [InlineData(ReminderKind.Due)]
    [InlineData(ReminderKind.ClassApproval)]
    [InlineData(ReminderKind.ParticipantApproval)]
    [InlineData(ReminderKind.Retake)]
    [InlineData(ReminderKind.Invitation)]
    [InlineData(ReminderKind.Transfer)]
    public void MoiTruyVanNhacViecDichSangSqlKhongCanKetNoiDb(ReminderKind kind)
    {
        using var db = new AppDbContext(AppDbContextFactory.BuildOptions(DatabaseFixture.ConnectionString));
        var service = new ReminderService(db, TimeProvider.System, null!, null!, null!, null!, null!, NullLogger<ReminderService>.Instance);
        var query = (IQueryable)typeof(ReminderService).GetMethod("Candidates", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(service, [kind])!;
        Assert.Contains("SELECT", query.ToQueryString());
        var emailUsers = (IQueryable)typeof(ReminderService).GetMethod("EmailUsers", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(service, null)!;
        Assert.Contains("EmailRemindersEnabled", emailUsers.ToQueryString());
        var pending = (IQueryable)typeof(ReminderService).GetMethod("RelevantReminders", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(service, [kind, "test-user"])!;
        Assert.Contains("SELECT", pending.ToQueryString());
    }

    [Fact]
    public void EmailNhacViecMacDinhBat()
    {
        // MindSprint gộp migration Tang7c vào KhoiTao: kiểm giá trị mặc định trên model.
        using var db = new AppDbContext(AppDbContextFactory.BuildOptions(DatabaseFixture.ConnectionString));
        Assert.Equal(true, db.Model.FindEntityType(typeof(ApplicationUser))!.FindProperty(nameof(ApplicationUser.EmailRemindersEnabled))!.GetDefaultValue());
    }

    [Theory]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) Firefox/144.0", "Firefox trên Windows")]
    [InlineData("Mozilla/5.0 (Linux; Android 14) Chrome/142.0 Mobile", "Chrome trên Android")]
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) Version/18.0 Safari/604.1", "Safari trên iOS")]
    [InlineData("", "Trình duyệt không xác định trên Hệ điều hành không xác định")]
    public void TenThietBiNganVaKhongLuuUserAgent(string ua, string expected) => Assert.Equal(expected, DeviceLabel.Parse(ua));

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    [InlineData("::ffff:192.168.1.9")]
    [InlineData("10.2.3.4")]
    [InlineData("172.31.0.1")]
    [InlineData("169.254.0.1")]
    [InlineData("fe80::1")]
    [InlineData("fc00::1")]
    public void IpNoiBoKhongTraViTri(string value) => Assert.False(GeoIpLocation.IsPublic(IPAddress.Parse(value)));

    [Fact]
    public void ThieuMmdbKhongNemLoiVaCoNguonUocTinh()
    {
        using var geo = new GeoIpLocation(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["GeoIp:CityDatabasePath"] = "khong-ton-tai.mmdb" }).Build(), new GeoEnvironment());
        Assert.Equal("không xác định", geo.Estimate(IPAddress.Parse("8.8.8.8")));
        Assert.Contains("Vị trí ước tính theo DB-IP.com", GeoIpLocation.Attribution);
        Assert.Contains("https://db-ip.com", GeoIpLocation.AttributionHtml);
    }

    [Fact]
    public void MaThietBiChiLuuSha256()
    {
        var raw = new string('a', 64);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))), DeviceLabel.Hash(raw));
        Assert.NotEqual(raw, DeviceLabel.Hash(raw));
    }
}

internal sealed class Tang7cEmail : IEmailSender
{
    public List<(string To, string Subject, string Body)> Sent { get; } = [];
    public bool Fail { get; set; }
    public Task SendAsync(string to, string subject, string htmlBody)
    {
        if (Fail) throw new InvalidOperationException("test");
        Sent.Add((to, subject, htmlBody)); return Task.CompletedTask;
    }
}

internal sealed class Tang7cServices
{
    public Tang7cEmail Email { get; } = new();
    public NotificationService Notices { get; }
    public InvitationService Invitations { get; }
    public ClassroomService Classrooms { get; }
    public ClassTransferService Transfers { get; }
    public ReminderService Reminders { get; }
    public Tang7cServices(Harness h)
    {
        Notices = new(h.Db, h.Clock, h.Notifier);
        var links = new EmailLinks(new ConfigurationBuilder().Build(), new HttpContextAccessor());
        Invitations = new(h.Db, h.Clock, Notices, Email, links);
        Classrooms = new(h.Db, h.Codes, h.Clock, h.Notifier, Invitations);
        Transfers = new(h.Db, Classrooms, new(h.Db, h.Clock), Notices, h.Clock, Invitations);
        Reminders = new(h.Db, h.Clock, Notices, Email, links, Invitations, Transfers, NullLogger<ReminderService>.Instance);
    }
}

[Collection("db")]
public class Tang7cTests
{
    private static async Task<ApplicationUser> User(Harness h, bool confirmed = true)
    {
        var user = (await h.Db.Users.FindAsync(await h.UserAsync("7c")))!;
        user.EmailConfirmed = confirmed; await h.Db.SaveChangesAsync(); return user;
    }
    private static Task<Invitation> Pending(Harness h, int room) => h.Db.Invitations.SingleAsync(i => i.ClassroomId == room && i.Status == InvitationStatus.Pending);

    [Fact]
    public async Task LoiMoiGvPhuKhongCoQuyenChoDenKhiChapNhan()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(); var f = new Tang7cServices(h); var u = await User(h);
        await f.Classrooms.AddAssistantAsync(s.Room.Id, u.Email!, s.Lecturer);
        var i = await Pending(h, s.Room.Id);
        var member = await h.Db.ClassMembers.SingleAsync(m => m.Id == i.MemberId);
        Assert.Equal(ClassMemberStatus.Invited, member.Status);
        Assert.False(await h.Classrooms.IsLecturerAsync(s.Room.Id, u.Id));
        await Assert.ThrowsAsync<ForbiddenException>(() => h.Gradebook.GetReportAsync(s.Assignment.Id, u.Id));
        await Assert.ThrowsAsync<ForbiddenException>(() => h.Classrooms.GetForLecturerAsync(s.Room.Id, u.Id));
        Assert.Empty(await h.Classrooms.ListMyClassesAsync(u.Id));
        await f.Invitations.DecideAssistantAsync(i.Id, true, u.Id);
        Assert.Equal(ClassMemberStatus.Active, member.Status); Assert.True(await h.Classrooms.IsLecturerAsync(s.Room.Id, u.Id));
        await h.Gradebook.GetReportAsync(s.Assignment.Id, u.Id);
        Assert.Equal(2, f.Email.Sent.Count); // L7: email mời + email báo chấp nhận cho người mời.
    }

    [Theory]
    [InlineData("decline", ClassMemberStatus.InvitationDeclined, 2)]
    [InlineData("revoke", ClassMemberStatus.InvitationRevoked, 1)]
    [InlineData("expire", ClassMemberStatus.InvitationExpired, 2)]
    public async Task TuChoiThuHoiHetHanKhongCapQuyenVaBaoDungKenh(string action, ClassMemberStatus expected, int emailCount)
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(); var f = new Tang7cServices(h); var u = await User(h);
        await f.Classrooms.AddAssistantAsync(s.Room.Id, u.Email!, s.Lecturer); var i = await Pending(h, s.Room.Id);
        if (action == "revoke") await f.Invitations.RevokeAsync(i.Id, s.Lecturer);
        else if (action == "decline") await f.Invitations.DecideAssistantAsync(i.Id, false, u.Id);
        else
        {
            h.Clock.UtcNow = i.ExpiresAt;
            await Assert.ThrowsAsync<BusinessException>(() => f.Invitations.DecideAssistantAsync(i.Id, true, u.Id));
        }
        Assert.Equal(expected, (await h.Db.ClassMembers.SingleAsync(m => m.Id == i.MemberId)).Status);
        Assert.False(await h.Classrooms.IsLecturerAsync(s.Room.Id, u.Id));
        Assert.Equal(emailCount, f.Email.Sent.Count);
        Assert.True(await h.Db.Notifications.CountAsync(n => n.UserId == u.Id && n.Type == NotificationType.Assistant) >= 2);
        Assert.True(await h.Db.Notifications.AnyAsync(n => n.UserId == s.Lecturer && n.Type == NotificationType.Assistant));
        if (emailCount == 2) Assert.Equal((await h.Db.Users.FindAsync(s.Lecturer))!.Email, f.Email.Sent[^1].To);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChuyenLopHetHan7NgayBaoHaiBen(bool worker)
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(); var f = new Tang7cServices(h); var u = await User(h);
        var t = await f.Transfers.OfferAsync(s.Room.Id, u.Email!, true, s.Lecturer);
        h.Clock.UtcNow = t.CreatedAt.AddDays(7);
        if (worker) await f.Invitations.ExpireAsync();
        else await Assert.ThrowsAsync<BusinessException>(() => f.Transfers.DecideAsync(t.Id, true, u.Id));
        Assert.Equal(ClassTransferStatus.Expired, (await h.Db.ClassTransfers.SingleAsync(x => x.Id == t.Id)).Status);
        foreach (var id in new[] { s.Lecturer, u.Id })
            Assert.Contains(await h.Db.Notifications.Where(n => n.UserId == id).ToListAsync(), n => NotificationService.Payload(n).Text.Contains("hết hạn"));
        // DB test dùng chung: worker làm hết hạn cả chuyển lớp của test khác, nên chỉ đếm thư của hai bên này.
        var lecturerEmail = (await h.Db.Users.FindAsync(s.Lecturer))!.Email;
        Assert.Equal(2, f.Email.Sent.Count(m => m.To == u.Email || m.To == lecturerEmail));
    }

    [Theory]
    [InlineData(InvitationKind.Assistant)]
    [InlineData(InvitationKind.Transfer)]
    public async Task EmailChuaDangKyChiHienSauXacMinhVaKhongNhanBangEmailKhac(InvitationKind kind)
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(); var f = new Tang7cServices(h);
        var address = $"unknown-{Guid.NewGuid():N}@test.vn";
        if (kind == InvitationKind.Assistant) await f.Classrooms.AddAssistantAsync(s.Room.Id, address, s.Lecturer);
        else await f.Transfers.OfferAsync(s.Room.Id, address, true, s.Lecturer);
        var i = await Pending(h, s.Room.Id); var originalExpiry = i.ExpiresAt;
        Assert.Equal(InvitationService.Normalize(address), i.NormalizedEmail);
        var u = await User(h, false); u.Email = address; await h.Db.SaveChangesAsync();
        Assert.Empty((await f.Invitations.ListAsync(u.Id)).Received);
        Assert.False(await h.Db.Notifications.AnyAsync(n => n.UserId == u.Id));
        var other = await User(h);
        // Sai tài khoản hoặc email chưa xác minh: vẫn bị chặn, nhưng câu lỗi nói cách xử lý (không chỉ "không có quyền").
        var wrong = await Assert.ThrowsAsync<BusinessException>(() => kind == InvitationKind.Assistant
            ? f.Invitations.DecideAssistantAsync(i.Id, true, other.Id) : f.Transfers.DecideInvitationAsync(i.Id, true, other.Id));
        Assert.Equal(InvitationService.WrongRecipient, wrong.Message); Assert.DoesNotContain(address, wrong.Message);
        await Assert.ThrowsAsync<BusinessException>(() => kind == InvitationKind.Assistant
            ? f.Invitations.DecideAssistantAsync(i.Id, true, u.Id) : f.Transfers.DecideInvitationAsync(i.Id, true, u.Id));
        u.EmailConfirmed = true; await h.Db.SaveChangesAsync();
        Assert.Single((await f.Invitations.ListAsync(u.Id)).Received); Assert.Equal(originalExpiry, i.ExpiresAt);
        Assert.True(await h.Db.Notifications.AnyAsync(n => n.UserId == u.Id));
        if (kind == InvitationKind.Assistant) await f.Invitations.DecideAssistantAsync(i.Id, true, u.Id);
        else await f.Transfers.DecideInvitationAsync(i.Id, true, u.Id);
        Assert.True(await h.Classrooms.IsLecturerAsync(s.Room.Id, u.Id));
    }

    [Fact]
    public async Task DieuKienThanhVienDuocKiemLaiKhiEmailLaCoTaiKhoan()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(); var f = new Tang7cServices(h);
        var address = $"future-{Guid.NewGuid():N}@test.vn";
        await f.Classrooms.AddAssistantAsync(s.Room.Id, address, s.Lecturer); var i = await Pending(h, s.Room.Id);
        var u = await User(h); u.Email = address; await h.Db.SaveChangesAsync();
        await h.Classrooms.JoinByCodeAsync(s.ClassCode, u.Id);
        await Assert.ThrowsAsync<BusinessException>(() => f.Invitations.DecideAssistantAsync(i.Id, true, u.Id));
        Assert.False(await h.Classrooms.IsLecturerAsync(s.Room.Id, u.Id));
        await Assert.ThrowsAsync<BusinessException>(() => f.Classrooms.AddAssistantAsync(s.Room.Id, u.Email!, s.Lecturer));
        await Assert.ThrowsAsync<BusinessException>(() => f.Transfers.OfferAsync(s.Room.Id, u.Email!, true, s.Lecturer));
    }

    [Fact]
    public async Task EmailMoiKhongCoTokenVaControllerBaoCungCau()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(); var f = new Tang7cServices(h); var u = await User(h);
        foreach (var address in new[] { u.Email!, $"unknown-{Guid.NewGuid():N}@test.vn" })
        {
            var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, s.Lecturer)], "test")) };
            var controller = new ClassroomsController(f.Classrooms, h.Assignments, h.Codes, null!, null!, null!, null!)
            { ControllerContext = new ControllerContext { HttpContext = http }, TempData = new TempDataDictionary(http, new FakeTempData()) };
            await controller.AddAssistant(s.Room.Id, address);
            Assert.Equal(InvitationService.SentMessage, controller.TempData["Success"]);
            var body = f.Email.Sent[^1].Body;
            Assert.Contains("/Invitations", body); Assert.Contains("/Account/Register", body);
            Assert.DoesNotContain("token", body, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain("code=", body);
            Assert.DoesNotContain("?", body);
        }
        Assert.Equal("Đã gửi lời mời tới email này.", InvitationService.SentMessage);
    }

    [Fact]
    public async Task GioiHan20EmailMoiTheoNgayVietNam()
    {
        using var h = new Harness(); var lecturer = await h.UserAsync("7c inviter"); var f = new Tang7cServices(h);
        for (var n = 0; n < 20; n++)
        {
            var room = await h.Classrooms.CreateAsync("Quota", ClassJoinPolicy.Immediate, lecturer);
            await f.Classrooms.AddAssistantAsync(room.Id, $"future-{Guid.NewGuid():N}@test.vn", lecturer);
        }
        var last = await h.Classrooms.CreateAsync("21", ClassJoinPolicy.Immediate, lecturer);
        var error = await Assert.ThrowsAsync<BusinessException>(() => f.Classrooms.AddAssistantAsync(last.Id, $"{Guid.NewGuid():N}@test.vn", lecturer));
        Assert.Contains("20", error.Message); Assert.Equal(20, f.Email.Sent.Count);
        h.Clock.UtcNow = h.Clock.UtcNow.Date.AddHours(17); // 00:00 ngày Việt Nam kế tiếp.
        await f.Classrooms.AddAssistantAsync(last.Id, $"{Guid.NewGuid():N}@test.vn", lecturer);
        Assert.Equal(21, f.Email.Sent.Count);
    }

    [Fact]
    public async Task GioiHan10LoiMoiGvPhuDangCho()
    {
        using var h = new Harness(); var id = await h.UserAsync("Quota10"); var f = new Tang7cServices(h);
        var room = await h.Classrooms.CreateAsync("Quota10", ClassJoinPolicy.Immediate, id);
        for (var n = 0; n < 10; n++) await f.Classrooms.AddAssistantAsync(room.Id, $"{Guid.NewGuid():N}@test.vn", id);
        var error = await Assert.ThrowsAsync<BusinessException>(() => f.Classrooms.AddAssistantAsync(room.Id, $"{Guid.NewGuid():N}@test.vn", id));
        Assert.Contains("10", error.Message); Assert.Equal(10, f.Email.Sent.Count);
    }

    [Theory]
    [InlineData(CompletionStatus.NotStarted, true)]
    [InlineData(CompletionStatus.InProgress, true)]
    [InlineData(CompletionStatus.Paused, true)]
    [InlineData(CompletionStatus.Completed, false)]
    [InlineData(CompletionStatus.SubmittedAwaitingScore, false)]
    [InlineData(CompletionStatus.Incomplete, false)]
    public async Task NhacHocVien24GioChiMotLanKhiChuaXong(CompletionStatus status, bool expected)
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(); var f = new Tang7cServices(h);
        var user = (await h.Db.Users.FindAsync(s.Student))!; user.EmailConfirmed = true;
        s.Assignment.DueAt = h.Clock.UtcNow.AddHours(24);
        h.Db.Participants.Add(new() { AssignmentId = s.Assignment.Id, UserId = user.Id, CompletionStatus = status });
        await h.Db.SaveChangesAsync();
        await f.Reminders.RunAsync(); await f.Reminders.RunAsync();
        Assert.Equal(expected ? 1 : 0, await h.Db.Reminders.CountAsync(r => r.UserId == user.Id && r.Kind == ReminderKind.Due));
        Assert.Equal(expected ? 1 : 0, f.Email.Sent.Count(e => e.To == user.Email));
        Assert.Equal(expected ? 1 : 0, await h.Db.Notifications.CountAsync(n => n.UserId == user.Id && n.Type == NotificationType.Reminder));
    }

    [Theory]
    [InlineData("closed")]
    [InlineData("no-due")]
    [InlineData("guest")]
    [InlineData("early")]
    [InlineData("past")]
    public async Task KhongNhacBaiDongKhachKhongHanChuaDenLucHoacQuaHan(string state)
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(); var f = new Tang7cServices(h);
        var u = (await h.Db.Users.FindAsync(s.Student))!; u.EmailConfirmed = true;
        s.Assignment.DueAt = h.Clock.UtcNow.AddHours(1);
        if (state == "closed") s.Assignment.Status = AssignmentStatus.Closed;
        if (state == "no-due") s.Assignment.DueAt = null;
        if (state == "guest") u.IsGuest = true;
        if (state == "early") s.Assignment.DueAt = h.Clock.UtcNow.AddHours(25);
        if (state == "past") s.Assignment.DueAt = h.Clock.UtcNow;
        await h.Db.SaveChangesAsync(); await f.Reminders.RunAsync();
        Assert.False(await h.Db.Reminders.AnyAsync(r => r.UserId == u.Id));
        Assert.DoesNotContain(f.Email.Sent, e => e.To == u.Email);
    }

    [Fact]
    public async Task NhacGvDuMoiLoaiYeuCauVaChiNguoiCoQuyenDuyet()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(); var f = new Tang7cServices(h);
        var assistant = await User(h); var invited = await User(h); var pendingUser = await User(h);
        await h.InviteAndAcceptAssistantAsync(s.Room.Id, assistant.Email!, s.Lecturer);
        await f.Classrooms.AddAssistantAsync(s.Room.Id, invited.Email!, s.Lecturer);
        var i = await Pending(h, s.Room.Id);
        var older = h.Clock.UtcNow.AddHours(-25); i.CreatedAt = older;
        var request = new ClassMember { ClassroomId = s.Room.Id, UserId = pendingUser.Id, Role = ClassRole.Student,
            Status = ClassMemberStatus.PendingApproval, CreatedAt = older };
        h.Db.ClassMembers.Add(request);
        var participant = new Participant { AssignmentId = s.Assignment.Id, UserId = pendingUser.Id,
            ApprovalStatus = ApprovalStatus.Pending, CreatedAt = older };
        h.Db.Participants.Add(participant); await h.Db.SaveChangesAsync();
        var retake = new RetakeRequest { ParticipantId = participant.Id, Status = RetakeRequestStatus.Pending, CreatedAt = older, Reason = "Xin làm lại" };
        h.Db.RetakeRequests.Add(retake);
        var target = await User(h);
        var transfer = await f.Transfers.OfferAsync(s.Room.Id, target.Email!, true, s.Lecturer);
        var transferInvite = await h.Db.Invitations.SingleAsync(x => x.TransferId == transfer.Id); transferInvite.CreatedAt = older;
        await h.Db.SaveChangesAsync(); await f.Reminders.RunAsync(); await f.Reminders.RunAsync();
        foreach (var id in new[] { s.Lecturer, assistant.Id })
        {
            var kinds = await h.Db.Reminders.Where(r => r.UserId == id).Select(r => r.Kind).ToListAsync();
            Assert.Contains(ReminderKind.ClassApproval, kinds); Assert.Contains(ReminderKind.ParticipantApproval, kinds); Assert.Contains(ReminderKind.Retake, kinds);
        }
        Assert.Single(await h.Db.Reminders.Where(r => r.UserId == invited.Id && r.Kind == ReminderKind.Invitation).ToListAsync());
        Assert.Single(await h.Db.Reminders.Where(r => r.UserId == target.Id && r.Kind == ReminderKind.Invitation).ToListAsync());
        Assert.False(await h.Db.Reminders.AnyAsync(r => r.UserId == s.Student && r.Kind != ReminderKind.Due));
        Assert.False(await h.Db.Reminders.AnyAsync(r => r.UserId == invited.Id && r.Kind == ReminderKind.ClassApproval));
    }

    [Fact]
    public async Task EmailToiDaMotMoiNgayVaMucMoiChoNgaySauNeuConLienQuan()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(); var f = new Tang7cServices(h); var u = await User(h);
        // Một lời mời quá 24h được gửi trong digest hôm nay.
        await f.Classrooms.AddAssistantAsync(s.Room.Id, u.Email!, s.Lecturer); var first = await Pending(h, s.Room.Id);
        first.CreatedAt = h.Clock.UtcNow.AddHours(-25); await h.Db.SaveChangesAsync();
        f.Email.Sent.Clear(); await f.Reminders.RunAsync();
        Assert.Single(f.Email.Sent, e => e.To == u.Email);
        var room2 = await h.Classrooms.CreateAsync("Còn chờ ngày sau", ClassJoinPolicy.Immediate, s.Lecturer);
        await f.Classrooms.AddAssistantAsync(room2.Id, u.Email!, s.Lecturer); var next = await Pending(h, room2.Id); next.CreatedAt = first.CreatedAt;
        var room3 = await h.Classrooms.CreateAsync("Sẽ thu hồi", ClassJoinPolicy.Immediate, s.Lecturer);
        await f.Classrooms.AddAssistantAsync(room3.Id, u.Email!, s.Lecturer); var gone = await Pending(h, room3.Id); gone.CreatedAt = first.CreatedAt;
        await h.Db.SaveChangesAsync(); f.Email.Sent.RemoveAll(e => e.Subject.Contains("Lời mời"));
        await f.Reminders.RunAsync(); Assert.Single(f.Email.Sent, e => e.To == u.Email);
        await f.Invitations.RevokeAsync(gone.Id, s.Lecturer);
        h.Clock.UtcNow = h.Clock.UtcNow.Date.AddHours(17); await f.Reminders.RunAsync();
        var digests = f.Email.Sent.Where(e => e.To == u.Email).ToList(); Assert.Equal(2, digests.Count);
        Assert.Contains("Còn chờ ngày sau", WebUtility.HtmlDecode(digests[1].Body));
        Assert.DoesNotContain("Sẽ thu hồi", WebUtility.HtmlDecode(digests[1].Body));
        Assert.Contains("/Account", digests[1].Body);
        Assert.Equal(3, await h.Db.Reminders.CountAsync(r => r.UserId == u.Id));
    }

    [Theory]
    [InlineData("off")]
    [InlineData("locked")]
    [InlineData("identity-locked")]
    [InlineData("unconfirmed")]
    public async Task TatEmailKhoaChuaXacMinhVanCoChuongNhungKhongDigest(string state)
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(); var f = new Tang7cServices(h);
        var user = (await h.Db.Users.FindAsync(s.Student))!; user.EmailConfirmed = state != "unconfirmed";
        s.Assignment.DueAt = h.Clock.UtcNow.AddHours(2);
        if (state == "off") user.EmailRemindersEnabled = false;
        if (state == "identity-locked") user.LockoutEnd = h.Clock.GetUtcNow().AddHours(1);
        if (state == "locked") h.Db.AccountLocks.Add(new() { UserId = user.Id, StartsAt = h.Clock.UtcNow, Kind = AccountLockKind.Protective });
        await h.Db.SaveChangesAsync(); await f.Reminders.RunAsync();
        Assert.True(await h.Db.Reminders.AnyAsync(r => r.UserId == user.Id));
        Assert.True(await h.Db.Notifications.AnyAsync(n => n.UserId == user.Id && n.Type == NotificationType.Reminder));
        Assert.DoesNotContain(f.Email.Sent, e => e.To == user.Email);
        if (state == "off")
        {
            await f.Reminders.SetEmailEnabledAsync(user.Id, true);
            await f.Reminders.RunAsync(); Assert.Single(f.Email.Sent, e => e.To == user.Email);
            await f.Reminders.SetEmailEnabledAsync(user.Id, false);
            await f.Classrooms.AddAssistantAsync(s.Room.Id, (await User(h)).Email!, s.Lecturer);
            Assert.Contains(f.Email.Sent, e => e.Subject.Contains("Lời mời")); // setting không tắt email mời.
        }
    }

    [Fact]
    public async Task ThietBiDauImLangMoiCoEmailChuongVaKhongLuuMaGoc()
    {
        using var h = new Harness(); var f = new Tang7cServices(h); var u = await User(h); u.EmailRemindersEnabled = false; await h.Db.SaveChangesAsync();
        using var geo = new GeoIpLocation(new ConfigurationBuilder().Build(), new GeoEnvironment());
        var service = new NewDeviceService(h.Db, h.Clock, f.Email, f.Notices, geo, NullLogger<NewDeviceService>.Instance, SecurityRound1Fixtures.Links);
        var http = new DefaultHttpContext(); http.Request.Host = new("localhost"); http.Request.Scheme = "https";
        http.Connection.RemoteIpAddress = IPAddress.Parse("::ffff:192.168.1.7");
        http.Request.Headers.UserAgent = "Mozilla/5.0 (Windows NT 10.0) Firefox/144.0";
        http.Items[LearnDevice.CookieName] = new string('a', 64); await service.SeenAsync(u, http);
        Assert.Empty(f.Email.Sent); Assert.False(await h.Db.Notifications.AnyAsync(n => n.UserId == u.Id));
        http.Items[LearnDevice.CookieName] = new string('b', 64); await service.SeenAsync(u, http); await service.SeenAsync(u, http);
        var message = Assert.Single(f.Email.Sent); var bell = Assert.Single(await h.Db.Notifications.Where(n => n.UserId == u.Id).ToListAsync());
        foreach (var text in new[] { WebUtility.HtmlDecode(message.Body), NotificationService.Payload(bell).Text })
        {
            Assert.Contains("Firefox trên Windows", text); Assert.Contains("192.168.1.7", text); Assert.Contains("không xác định", text);
            Assert.Contains("ước tính", text);
            Assert.Contains(VnTime.Format(h.Clock.UtcNow), text);
        }
        Assert.Contains("Vị trí ước tính theo DB-IP.com", NotificationService.Payload(bell).Text);
        // Email: không lặp tiêu đề trong thân thư; vị trí không xác định thì không ghi nguồn DB-IP.
        Assert.StartsWith("<p>Firefox trên Windows", WebUtility.HtmlDecode(message.Body));
        Assert.DoesNotContain("db-ip.com", message.Body); Assert.Contains("#protective-lock", message.Body);
        var devices = await h.Db.KnownDevices.Where(d => d.UserId == u.Id).ToListAsync(); Assert.Equal(2, devices.Count);
        Assert.DoesNotContain(devices, d => d.DeviceHash == new string('a', 64) || d.DeviceHash == new string('b', 64));
        Assert.All(devices, d => Assert.Equal(64, d.DeviceHash.Length));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("app")]
    [InlineData("email")]
    [InlineData("passkey")]
    public async Task CompleteLaDiemChungVaEmailLoiKhongChanDangNhap(string? method)
    {
        using var h = new Harness(); using var identity = new TwoFactorIdentity(h);
        var user = await identity.PrepareAsync(h);
        identity.Http.Items[LearnDevice.CookieName] = new string('a', 64);
        Assert.True((await identity.Login.CompleteAsync(user, false, verifiedMethod: method)).Succeeded);
        await identity.NextAsync(); identity.Http.Items[LearnDevice.CookieName] = new string('b', 64);
        Assert.True((await identity.Login.CompleteAsync(user, false, verifiedMethod: method)).Succeeded);
        Assert.Single(identity.Email.Subjects, m => m.Contains("Đăng nhập mới"));
        Assert.Single(await h.Db.Notifications.Where(n => n.UserId == user.Id && n.Type == NotificationType.NewDevice).ToListAsync());
        using var geo = new GeoIpLocation(new ConfigurationBuilder().Build(), new GeoEnvironment());
        var f = new Tang7cServices(h); f.Email.Fail = true;
        var failing = new NewDeviceService(h.Db, h.Clock, f.Email, f.Notices, geo, NullLogger<NewDeviceService>.Instance, SecurityRound1Fixtures.Links);
        // NextAsync hủy scope cũ: phải lấy UserManager/SignInManager của scope mới.
        await identity.NextAsync(); identity.Http.Items[LearnDevice.CookieName] = new string('c', 64);
        using var cache = new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions());
        var login = new LoginService(identity.Users, identity.SignIn, cache, Tang5bPureTests.Turnstile(),
            new(h.Db, identity.Users, f.Email, h.Clock, h.Notifier), h.Clock, failing);
        Assert.True((await login.CompleteAsync(user, false, verifiedMethod: method)).Succeeded);
        Assert.Equal(3, await h.Db.KnownDevices.CountAsync(d => d.UserId == user.Id));
        Assert.Equal(2, await h.Db.Notifications.CountAsync(n => n.UserId == user.Id && n.Type == NotificationType.NewDevice));
    }

    [Theory]
    [InlineData("password")]
    [InlineData("2fa")]
    [InlineData("Google")]
    [InlineData("passkey")]
    public async Task CacLuongDangNhapThatDeuGhiThietBiSauThanhCong(string path)
    {
        using var h = new Harness(); var fake = new TestPasskeyHandler();
        using var i = new TwoFactorIdentity(h, fake, Tang6cPureTests.Config());
        var user = await i.PrepareAsync(h);
        var info = Tang6cTests.Info(email: user.Email);
        if (path == "Google") Assert.True((await i.Users.AddLoginAsync(user, new("Google", info.ProviderKey, "Google"))).Succeeded);
        string? credential = null;
        if (path == "passkey")
        {
            fake.Users = () => i.Users;
            fake.Key = TestPasskeyHandler.NewKey();
            Assert.True((await i.Users.AddOrUpdatePasskeyAsync(user, fake.Key)).Succeeded);
            credential = WebEncoders.Base64UrlEncode(fake.Key.CredentialId);
        }
        if (path == "2fa") await i.TwoFactor.ChangeAsync(user.Id, TwoFactorChange.EnableEmail, null, null, "https://localhost/Account");
        for (var n = 0; n < 2; n++)
        {
            if (n > 0) await i.NextAsync();
            i.Http.Items[LearnDevice.CookieName] = new string(n == 0 ? 'a' : 'b', 64);
            if (path == "Google") Assert.True((await i.External.SignInAsync(info)).Succeeded);
            else if (path == "passkey")
            {
                await i.Passkeys.RequestOptionsAsync(false); await i.NextAsync();
                i.Http.Items[LearnDevice.CookieName] = new string(n == 0 ? 'a' : 'b', 64);
                Assert.True((await i.Passkeys.LoginAsync(System.Text.Json.JsonSerializer.Serialize(credential), false, false)).Outcome.Succeeded);
            }
            else
            {
                var outcome = await i.Login.LoginAsync(user.Email!, "Valid123!", false, null, null);
                if (path == "2fa")
                {
                    Assert.True(outcome.RequiresTwoFactor);
                    Assert.Equal(n, await h.Db.KnownDevices.CountAsync(d => d.UserId == user.Id)); // chưa hoàn tất chưa ghi.
                    await i.NextAsync(); i.Http.Items[LearnDevice.CookieName] = new string(n == 0 ? 'a' : 'b', 64);
                    var pending = (await i.TwoFactor.PendingAsync())!;
                    await i.TwoFactor.SendEmailAsync(pending.User.Id);
                    var result = await i.Controller().LoginTwoFactor("email", i.EmailCode(), i.TwoFactor);
                    Assert.IsType<LocalRedirectResult>(result);
                    h.Clock.UtcNow = h.Clock.UtcNow.AddMinutes(2); // lần gửi mã sau đủ 60 giây.
                }
                else Assert.True(outcome.Succeeded);
            }
        }
        Assert.Equal(2, await h.Db.KnownDevices.CountAsync(d => d.UserId == user.Id));
        Assert.Single(await h.Db.Notifications.Where(n => n.UserId == user.Id && n.Type == NotificationType.NewDevice).ToListAsync());
        Assert.Single(i.Email.Subjects, m => m.Contains("Đăng nhập mới"));
    }

    [Fact]
    public async Task DeNghiChuyenCuTruocMigrationVanHetHanVaEmailNguoiMoi()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(); var f = new Tang7cServices(h); var u = await User(h);
        var t = new ClassTransfer { ClassroomId = s.Room.Id, FromUserId = s.Lecturer, ToUserId = u.Id, CreatedAt = h.Clock.UtcNow.AddDays(-7) };
        h.Db.ClassTransfers.Add(t); await h.Db.SaveChangesAsync(); await f.Transfers.ExpireLegacyAsync();
        Assert.Equal(ClassTransferStatus.Expired, t.Status);
        Assert.Single(f.Email.Sent, e => e.To == (h.Db.Users.Find(s.Lecturer))!.Email);
        foreach (var id in new[] { s.Lecturer, u.Id }) Assert.True(await h.Db.Notifications.AnyAsync(n => n.UserId == id && n.Type == NotificationType.Transfer));
    }

    [Fact]
    public async Task LoiMoiChuyenEmailLaKiemLaiHocVienVaHanGoc()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(); var f = new Tang7cServices(h);
        var address = $"future-{Guid.NewGuid():N}@test.vn";
        await f.Transfers.OfferAsync(s.Room.Id, address, true, s.Lecturer); var i = await Pending(h, s.Room.Id);
        var u = await User(h); u.Email = address; await h.Db.SaveChangesAsync();
        await h.Classrooms.JoinByCodeAsync(s.ClassCode, u.Id);
        await Assert.ThrowsAsync<BusinessException>(() => f.Transfers.DecideInvitationAsync(i.Id, true, u.Id));
        Assert.False(await h.Classrooms.IsLecturerAsync(s.Room.Id, u.Id));
        Assert.False(await h.Db.ClassTransfers.AnyAsync(t => t.ClassroomId == s.Room.Id));
        h.Clock.UtcNow = i.ExpiresAt;
        await Assert.ThrowsAsync<BusinessException>(() => f.Transfers.DecideInvitationAsync(i.Id, true, u.Id));
        Assert.Equal(InvitationStatus.Expired, i.Status);
    }

    [Fact]
    public async Task DigestLoiKhongThuLaiCungNgayVaKhongMatMucConLienQuan()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(); var f = new Tang7cServices(h); var u = await User(h);
        await f.Classrooms.AddAssistantAsync(s.Room.Id, u.Email!, s.Lecturer);
        var invite = await Pending(h, s.Room.Id); invite.CreatedAt = h.Clock.UtcNow.AddHours(-25); await h.Db.SaveChangesAsync();
        f.Email.Sent.Clear(); f.Email.Fail = true;
        await f.Reminders.RunAsync(); f.Email.Fail = false; await f.Reminders.RunAsync();
        Assert.DoesNotContain(f.Email.Sent, e => e.To == u.Email);
        Assert.Null((await h.Db.Reminders.SingleAsync(r => r.UserId == u.Id)).EmailedAt);
        h.Clock.UtcNow = h.Clock.UtcNow.Date.AddHours(17); await f.Reminders.RunAsync();
        Assert.Single(f.Email.Sent, e => e.To == u.Email);
        var reminder = await h.Db.Reminders.SingleAsync(r => r.UserId == u.Id); await h.Db.Entry(reminder).ReloadAsync();
        Assert.NotNull(reminder.EmailedAt);
    }
}

internal sealed class GeoEnvironment : IHostEnvironment
{
    public string EnvironmentName { get; set; } = "Test";
    public string ApplicationName { get; set; } = "Test";
    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
