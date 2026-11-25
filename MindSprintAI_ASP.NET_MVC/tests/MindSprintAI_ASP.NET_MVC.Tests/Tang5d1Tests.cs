using System.Reflection;
using System.Security.Claims;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MindSprintAI_ASP.NET_MVC.Controllers;
using MindSprintAI_ASP.NET_MVC.Data;
using MindSprintAI_ASP.NET_MVC.Data.Entities;
using MindSprintAI_ASP.NET_MVC.Hubs;
using MindSprintAI_ASP.NET_MVC.Models;
using MindSprintAI_ASP.NET_MVC.Services;

namespace MindSprintAI_ASP.NET_MVC.Tests;

public class Tang5d1PureTests
{
    [Fact]
    public void FormDoiChinhSachNguoiLaKhopTenThamSo()
    {
        // Hồi quy: ô "StrangerPolicy" của partial phải khớp tên tham số action, không thì luôn nhận giá trị 0.
        var view = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../../Views/Shared/_StrangerPolicy.cshtml"));
        Assert.Contains("name=\"StrangerPolicy\"", view);
        var param = typeof(MindSprintAI_ASP.NET_MVC.Controllers.AssignmentsController).GetMethod("ChangeStrangerPolicy")!.GetParameters();
        Assert.Contains(param, p => string.Equals(p.Name, "StrangerPolicy", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void LuotKhachVuotTranKhongDuocChonDiem()
    {
        var normal = new Attempt { Status = AttemptStatus.Ended, FirstScore = 1, MaxScore = 2 };
        var guest = new Attempt { Status = AttemptStatus.Ended, FirstScore = 2, MaxScore = 2,
            MadeAsGuest = true, ExcludeFromGradebook = true };
        Assert.Equal(1m, GradebookService.Select([normal, guest], ScoreSelection.Highest)!.Raw);
        Assert.Null(GradebookService.Select([guest], ScoreSelection.Highest));
    }

    [Fact]
    public void KhachKhongXinLamLai()
    {
        var p = new Participant { GuestId = 1, Assignment = new() { Mode = AssignmentMode.Test },
            Attempts = [new() { Status = AttemptStatus.Ended }] };
        Assert.False(RetakeService.CanRequest(p));
    }

    [Theory]
    [InlineData(" Hoa ", "Hoa")]
    [InlineData("<script>Hoa</script>", "<script>Hoa</script>")]
    public void TenKhachCatKhoangTrang(string value, string expected) => Assert.Equal(expected, GuestService.ValidateName(value));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void TenKhachBatBuoc(string? value) => Assert.Throws<BusinessException>(() => GuestService.ValidateName(value));

    [Fact]
    public void TokenChiLuuBam() => Assert.Equal(64, GuestService.HashToken(new string('A', 64))!.Length);

    [Fact]
    public void TokenNgauNhienVaKhongNhanTokenTuChe()
    {
        var tokens = Enumerable.Range(0, 100).Select(_ => GuestService.NewToken()).ToArray();
        Assert.Equal(100, tokens.Distinct().Count());
        Assert.All(tokens, token => { Assert.Matches("^[A-F0-9]{64}$", token); Assert.NotEqual(token, GuestService.HashToken(token)); });
        Assert.Null(GuestService.HashToken("123")); Assert.Null(GuestService.HashToken(new string('z', 64)));
        Assert.Throws<BusinessException>(() => GuestService.ValidateName(new string('a', 51)));
    }

    [Fact]
    public async Task MoiActionKhongDuocChoPhepDeuChanKhachKeCaAllowAnonymous()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ApplicationName = typeof(AppController).Assembly.GetName().Name });
        builder.Services.AddControllersWithViews().AddApplicationPart(typeof(AppController).Assembly);
        await using var app = builder.Build();
        var actions = app.Services.GetRequiredService<IActionDescriptorCollectionProvider>().ActionDescriptors.Items.OfType<ControllerActionDescriptor>().ToList();
        var allowed = new HashSet<string>
        {
            "Learn.Assignment", "Learn.TestStatus", "Learn.Start", "Learn.Play", "Learn.Review", "Learn.State", "Learn.Answer",
            "Learn.Draft", "Learn.Flag", "Learn.OpenQuestion", "Learn.Finish", "Learn.Exit", "Learn.Takeover",
            // (7e/F6, F7b, H2) khách đồng ý, xin/hủy chuyển máy và gửi theo dõi của chính lượt mình.
            "Learn.Consent", "Learn.CancelDeviceChange", "Learn.DeviceChangeState", "Learn.ScreenEvent", "DeviceSignals.Light",
            "Classrooms.Join", "Account.Register", "Account.Login", "Account.Logout", "Account.ConfirmEmail", "Account.RegisterPassword", "Account.ResendConfirmation",
            // Bước 2FA tiếp tục đăng nhập tài khoản thật để gộp khách; quản lý 2FA vẫn bị chặn.
            "Account.LoginTwoFactor", "Account.SendTwoFactorEmail",
            // OAuth tiếp tục đăng nhập/gộp khách; nối/gỡ provider vẫn chỉ cho tài khoản thật.
            "Account.ExternalLogin", "Account.ExternalLoginCallback",
            // Passkey đăng nhập tài khoản thật/gộp khách; tạo và quản lý passkey vẫn chặn khách.
            "Account.PasskeyRequestOptions", "Account.PasskeyLogin", "Account.TwoFactorPasskeyOptions", "Account.TwoFactorPasskeyLogin",
            // (7d/D1a, D3b) khách được đổi chế độ và xem lịch sử của chính phiên trong bài giao.
            "Flashcards.Study", "Flashcards.Rate", "Flashcards.ChangeMode", "Flashcards.History",
            // Pháp lý: khách đọc được Chính sách/Điều khoản/Trợ giúp và đồng ý lại khi chính sách đổi phiên bản.
            "Home.Privacy", "Home.Terms", "Home.Help", "Account.AcceptPolicy",
            // Bài làm theo đề (16/11/2026): khách làm bài như bài quiz; service tự kiểm chủ lượt / người có lượt / GV của lớp.
            "Papers.Exam", "Papers.File", "Papers.Save", "Papers.Upload", "Papers.DeleteFile"
        };
        Assert.True(actions.Count > 80);
        foreach (var action in actions)
        {
            var http = new DefaultHttpContext { User = GuestPrincipal() };
            var context = new AuthorizationFilterContext(new ActionContext(http, new RouteData(), action), []);
            new NotGuestFilter().OnAuthorization(context);
            var permitted = allowed.Contains(action.ControllerName + "." + action.ActionName);
            Assert.Equal(permitted, action.MethodInfo.IsDefined(typeof(AllowGuestAttribute)));
            if (permitted) Assert.Null(context.Result);
            else Assert.Equal(403, Assert.IsType<StatusCodeResult>(context.Result).StatusCode);
        }
    }

    [Theory]
    [InlineData("JoinAttempt", true), InlineData("JoinWaiting", true), InlineData("JoinLecturer", false)]
    public async Task HubChiMoHaiPhuongThucChoKhach(string method, bool permitted)
    {
        var info = typeof(LearnHub).GetMethod(method)!;
        using var services = new ServiceCollection().BuildServiceProvider();
        var context = new HubInvocationContext(new GuestHubCaller(), services, new LearnHub(null!), info, []);
        var filter = new NotGuestHubFilter();
        if (!permitted) await Assert.ThrowsAsync<HubException>(() => filter.InvokeMethodAsync(context, _ => ValueTask.FromResult<object?>(true)).AsTask());
        else Assert.Equal(true, await filter.InvokeMethodAsync(context, _ => ValueTask.FromResult<object?>(true)));
    }

    [Fact]
    public void KhoaNgoaiKhachVaModelKhopSnapshot()
    {
        // MindSprint gộp migration Tang5d1 vào KhoiTao: kiểm khóa ngoại trên model thay vì trên migration lịch sử.
        using var db = DatabaseFixture.NewContext();
        var migrations = db.GetService<IMigrationsAssembly>();
        Assert.Contains(db.Model.FindEntityType(typeof(Participant))!.GetForeignKeys(), fk => fk.PrincipalEntityType.ClrType == typeof(Guest));
        var snapshot = db.GetService<IModelRuntimeInitializer>().Initialize(migrations.ModelSnapshot!.Model, true);
        Assert.False(db.GetService<IMigrationsModelDiffer>().HasDifferences(snapshot.GetRelationalModel(),
            db.GetService<Microsoft.EntityFrameworkCore.Metadata.IDesignTimeModel>().Model.GetRelationalModel()));
    }

    [Fact]
    public async Task BangBaoCaoTachNguoiNgoaiLopVaMaHoaTenKhach()
    {
        var model = new AssignmentDetailsModel { Assignment = new() { Id = 1 }, Code = "12345678", OpenAttempts = 0,
            Rows = [new("hv", "Minh", CompletionStatus.Completed, CompletionLabels.None, 1, null, 1),
                new("khach", "<script>Hoa</script>", CompletionStatus.Completed, CompletionLabels.None, 1, null, 2, IsStranger: true, IsGuest: true)] };
        var html = await Tang4ViewTests.Render("Assignments", "_LiveTable", model);
        Assert.Contains("Người ngoài lớp", WebUtility.HtmlDecode(html)); Assert.Contains("Khách", WebUtility.HtmlDecode(html));
        Assert.Contains("data-participants=\"1\"", html); Assert.Contains("data-completed=\"1\"", html);
        Assert.DoesNotContain("<script>Hoa", html); Assert.Contains("&lt;script&gt;Hoa", html);
    }

    [Fact]
    public async Task FormDuyetKhachCoChongGiaMaoVaMaHoaTen()
    {
        var p = new Participant { Id = 2, AssignmentId = 1, GuestId = 1, GuestDisplayName = "<script>Hoa</script>",
            IsStranger = true, ApprovalStatus = ApprovalStatus.Pending };
        var html = await Tang4ViewTests.Render("Assignments", "_LiveTable", new AssignmentDetailsModel
        { Assignment = new() { Id = 1 }, Code = "12345678", OpenAttempts = 0, Rows = [], Waiting = [p] });
        Assert.Contains("name=\"participantId\"", html); Assert.Contains("name=\"approve\"", html);
        Assert.Contains("__RequestVerificationToken", html);
        Assert.DoesNotContain("<script>Hoa", html); Assert.Contains("&lt;script&gt;Hoa", html);
    }

    [Fact]
    public async Task TrangTaoVaChiTietDeuCoChinhSachNguoiNgoaiLop()
    {
        var model = new AssignmentCreateModel { Classroom = new() { Id = 1 }, Form = new(), ReadyQuizzes = [new() { Id = 1 }], StudySets = [new() { Id = 1 }] };
        var create = await Tang4ViewTests.Render("Assignments", "Create", model);
        Assert.Contains("name=\"StrangerPolicy\"", create);
        var details = await Tang4ViewTests.Render("Assignments", "Details", new AssignmentDetailsModel
        { Assignment = new() { Id = 1 }, Code = "12345678", Rows = [], OpenAttempts = 0 });
        Assert.Contains("name=\"StrangerPolicy\"", details); Assert.Contains("__RequestVerificationToken", details);
        Assert.Equal(StrangerPolicy.Reject, model.Form.StrangerPolicy);
    }

    internal static ClaimsPrincipal GuestPrincipal(string id = "guest") => new(new ClaimsIdentity(
        [new(ClaimTypes.NameIdentifier, id), new(GuestAccess.Claim, "true")], "test"));

    private sealed class GuestHubCaller : HubCallerContext
    {
        public override string ConnectionId => "guest-tab";
        public override string? UserIdentifier => "guest";
        public override ClaimsPrincipal User => GuestPrincipal();
        public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
        public override IFeatureCollection Features { get; } = new FeatureCollection();
        public override CancellationToken ConnectionAborted => CancellationToken.None;
        public override void Abort() { }
    }
}

[Collection("db")]
public class Tang5d1Tests
{
    private static GuestService Guests(Harness h) => new(h.Db, h.Clock, h.Notifier);
    private static RetakeService Retakes(Harness h) => new(h.Db, h.Assignments, h.Attempts, h.Clock);
    private static async Task<Scenario> Scenario(Harness h, bool test = false, int? max = 3)
    {
        var s = await h.ScenarioAsync(1, new PracticeSettings(null, h.Clock.UtcNow.AddDays(2), max));
        return test ? s with { Assignment = await h.Assignments.CreateTestAsync(s.Room.Id, s.Quiz.Id, new(null, 30), s.Lecturer) } : s;
    }
    private static async Task<string> Code(Harness h, Scenario s) => (await h.Codes.GetActiveAsync(JoinCodeTarget.Assignment, s.Assignment.Id))!.Code;
    private static async Task<ApplicationUser> Confirm(Harness h, string id)
    {
        var user = (await h.Db.Users.FindAsync(id))!; user.EmailConfirmed = true;
        await h.Db.SaveChangesAsync(); return user;
    }
    private static async Task<Attempt> Do(Harness h, Scenario s, string userId, bool correct = false)
    {
        var a = await h.Attempts.StartAsync(s.Assignment.Id, userId);
        if (correct)
        {
            var q = a.Questions.Single(); var right = QuestionSnapshot.FromJson(q.SnapshotJson).Options.Single(o => o.IsCorrect).Id;
            await h.Attempts.AnswerAsync(a.Id, q.Id, Guid.NewGuid(), [right], userId, questionVersion: q.QuestionVersion);
        }
        await h.Attempts.FinishAsync(a.Id, userId);
        return a;
    }

    [Theory]
    [InlineData(false, false, StrangerPolicy.Reject), InlineData(false, false, StrangerPolicy.Allow), InlineData(false, false, StrangerPolicy.RequireApproval)]
    [InlineData(false, true, StrangerPolicy.Reject), InlineData(false, true, StrangerPolicy.Allow), InlineData(false, true, StrangerPolicy.RequireApproval)]
    [InlineData(true, false, StrangerPolicy.Reject), InlineData(true, false, StrangerPolicy.Allow), InlineData(true, false, StrangerPolicy.RequireApproval)]
    [InlineData(true, true, StrangerPolicy.Reject), InlineData(true, true, StrangerPolicy.Allow), InlineData(true, true, StrangerPolicy.RequireApproval)]
    public async Task BaChinhSachChoKhachVaTaiKhoanNgoaiLop(bool test, bool guest, StrangerPolicy policy)
    {
        using var h = new Harness(); var s = await Scenario(h, test);
        await h.Assignments.ChangeStrangerPolicyAsync(s.Assignment.Id, policy, s.Lecturer);
        var userId = await h.UserAsync("Hoa"); var token = GuestService.NewToken(); var code = await Code(h, s);
        async Task Join()
        {
            if (guest) userId = (await Guests(h).JoinAsync(s.Assignment.Id, " Hoa ", token)).UserId;
            else await h.Classrooms.JoinByCodeAsync(code, userId);
        }
        if (policy == StrangerPolicy.Reject)
        {
            Assert.Contains("Bài này chỉ dành cho thành viên lớp", (await Assert.ThrowsAsync<BusinessException>(Join)).Message);
            Assert.False(await h.Db.Participants.AnyAsync(p => p.AssignmentId == s.Assignment.Id)); return;
        }
        await Join();
        Assert.False(await h.Db.ClassMembers.AnyAsync(m => m.ClassroomId == s.Room.Id && m.UserId == userId));
        var p = await h.Db.Participants.SingleAsync(p => p.AssignmentId == s.Assignment.Id && p.UserId == userId);
        Assert.True(p.IsStranger); Assert.Equal(guest, p.GuestId != null);
        var page = await h.Attempts.GetAssignmentPageAsync(s.Assignment.Id, userId);
        if (policy == StrangerPolicy.RequireApproval)
        {
            Assert.Equal(ApprovalStatus.Pending, p.ApprovalStatus); Assert.Equal("Chờ giảng viên duyệt", page.CannotStartReason);
            Assert.True(page.IsWaiting); Assert.False((await h.Attempts.GetTestStatusAsync(s.Assignment.Id, userId)).CanStart);
            await Assert.ThrowsAsync<BusinessException>(() => h.Attempts.StartAsync(s.Assignment.Id, userId));
            await h.Assignments.DecideStrangerAsync(s.Assignment.Id, p.Id, true, s.Lecturer);
        }
        if (test)
        {
            Assert.False((await h.Attempts.GetTestStatusAsync(s.Assignment.Id, userId)).CanStart);
            await Assert.ThrowsAsync<BusinessException>(() => h.Attempts.StartAsync(s.Assignment.Id, userId));
            await h.Assignments.StartTestAsync(s.Assignment.Id, s.Lecturer);
        }
        Assert.NotNull(await h.Attempts.StartAsync(s.Assignment.Id, userId));
    }

    [Fact]
    public async Task TenTrungCungBaiThemSoVaTokenCuTiepTucTokenKhacKhongDuoc()
    {
        using var h = new Harness(); var s = await Scenario(h);
        await h.Assignments.ChangeStrangerPolicyAsync(s.Assignment.Id, StrangerPolicy.Allow, s.Lecturer);
        var token = GuestService.NewToken(); var first = await Guests(h).JoinAsync(s.Assignment.Id, " Hoa ", token);
        var resumed = await Guests(h).JoinAsync(s.Assignment.Id, "Tên khác", token);
        Assert.Equal(first.Id, resumed.Id); Assert.Null(await Guests(h).ResumeAsync(GuestService.NewToken()));
        var other = await Guests(h).JoinAsync(s.Assignment.Id, "Hoa", GuestService.NewToken());
        Assert.NotEqual(first.Id, other.Id);
        Assert.Equal("Hoa (2)", other.DisplayName);
        Assert.Equal("Hoa (2)", (await h.Db.Participants.SingleAsync(p => p.GuestId == other.Id)).GuestDisplayName);
        // 14/11/2026: chỉ đọc tên có thể trùng (SQL) — vẫn không phân biệt hoa thường; tên gần giống không bị thêm số.
        Assert.Equal("hoa (3)", (await Guests(h).JoinAsync(s.Assignment.Id, "hoa", GuestService.NewToken())).DisplayName);
        Assert.Equal("Hoa_", (await Guests(h).JoinAsync(s.Assignment.Id, "Hoa_", GuestService.NewToken())).DisplayName);
        Assert.Equal(GuestService.HashToken(token), first.BrowserTokenHash); Assert.NotEqual(token, first.BrowserTokenHash);
        Assert.Null(first.User.Email); Assert.Null(first.User.PasswordHash); Assert.True(first.User.IsGuest);
        var a = await h.Attempts.StartAsync(s.Assignment.Id, first.UserId);
        Assert.Equal(a.Id, (await h.Attempts.StartAsync(s.Assignment.Id, resumed.UserId)).Id);
        await Assert.ThrowsAsync<ForbiddenException>(() => h.Attempts.GetForLearnerAsync(a.Id, other.UserId));
    }

    [Theory]
    [InlineData(false), InlineData(true)]
    public async Task TuChoiDuyetKhongChoBatDauVaBaoSauCommit(bool guest)
    {
        using var h = new Harness(); var s = await Scenario(h); var hub = new TestLearnHub(); h.Notifier = new SignalRLearnNotifier(hub);
        await h.Assignments.ChangeStrangerPolicyAsync(s.Assignment.Id, StrangerPolicy.RequireApproval, s.Lecturer);
        var userId = guest ? (await Guests(h).JoinAsync(s.Assignment.Id, "Hoa", GuestService.NewToken())).UserId : await h.UserAsync("Hoa");
        if (!guest) await h.Classrooms.JoinByCodeAsync(await Code(h, s), userId);
        var p = await h.Db.Participants.SingleAsync(p => p.AssignmentId == s.Assignment.Id && p.UserId == userId);
        await h.Assignments.DecideStrangerAsync(s.Assignment.Id, p.Id, false, s.Lecturer);
        using var check = DatabaseFixture.NewContext(); Assert.Equal(ApprovalStatus.Rejected, (await check.Participants.FindAsync(p.Id))!.ApprovalStatus);
        Assert.Null(h.Db.Database.CurrentTransaction);
        Assert.Contains(hub.Sent, e => e.Event == "stranger-changed"); Assert.Contains(hub.Sent, e => e.Event == "lecturers-changed");
        await Assert.ThrowsAsync<ForbiddenException>(() => h.Attempts.StartAsync(s.Assignment.Id, userId));
        Assert.False((await h.Attempts.GetTestStatusAsync(s.Assignment.Id, userId)).CanStart);
    }

    [Fact]
    public async Task BaoCaoKhachAnTruocBatDauVaNguoiNgoaiKhongVaoSoDemLop()
    {
        using var h = new Harness(); var s = await Scenario(h);
        await h.Assignments.ChangeStrangerPolicyAsync(s.Assignment.Id, StrangerPolicy.Allow, s.Lecturer);
        var guest = await Guests(h).JoinAsync(s.Assignment.Id, "Hoa", GuestService.NewToken());
        var outsider = await h.UserAsync("An"); await h.Classrooms.JoinByCodeAsync(await Code(h, s), outsider);
        var (_, before) = await h.Gradebook.GetReportAsync(s.Assignment.Id, s.Lecturer);
        Assert.DoesNotContain(before, r => r.UserId == guest.UserId); Assert.Contains(before, r => r.UserId == outsider && r.IsStranger);
        await Do(h, s, guest.UserId);
        var (_, after) = await h.Gradebook.GetReportAsync(s.Assignment.Id, s.Lecturer);
        Assert.Single(after, r => !r.IsStranger); Assert.Equal(2, after.Count(r => r.IsStranger));
        Assert.True(after.Single(r => r.UserId == guest.UserId).IsGuest);
    }

    [Fact]
    public async Task TranLuotTheoKhachVaKhachKhongXinLamLai()
    {
        using var h = new Harness(); var s = await Scenario(h, max: 1);
        await h.Assignments.ChangeStrangerPolicyAsync(s.Assignment.Id, StrangerPolicy.Allow, s.Lecturer);
        var token = GuestService.NewToken(); var guest = await Guests(h).JoinAsync(s.Assignment.Id, "Hoa", token);
        await Do(h, s, guest.UserId); var resumed = (await Guests(h).ResumeAsync(token))!;
        Assert.Contains("hết số lượt", (await Assert.ThrowsAsync<BusinessException>(() => h.Attempts.StartAsync(s.Assignment.Id, resumed.UserId))).Message);
        var other = await Guests(h).JoinAsync(s.Assignment.Id, "Hoa", GuestService.NewToken());
        Assert.NotNull(await h.Attempts.StartAsync(s.Assignment.Id, other.UserId));
        await Assert.ThrowsAsync<ForbiddenException>(() => Retakes(h).RequestAsync(s.Assignment.Id, "Xin làm lại", guest.UserId));
    }

    [Fact]
    public async Task HubKhongVaoLuotCuaNguoiKhacVaKhachChoDuyetChiVaoPhongCuaMinh()
    {
        using var h = new Harness(); var s = await Scenario(h); var hub = new TestLearnHub();
        await h.Assignments.ChangeStrangerPolicyAsync(s.Assignment.Id, StrangerPolicy.RequireApproval, s.Lecturer);
        var guest = await Guests(h).JoinAsync(s.Assignment.Id, "Hoa", GuestService.NewToken());
        var a = await h.Attempts.StartAsync(s.Assignment.Id, s.Student);
        var connections = new LearnConnectionService(h.Db, h.Attempts, h.Clock, h.Connections, hub, h.Classrooms, h.Notifier);
        await Assert.ThrowsAsync<ForbiddenException>(() => connections.JoinAttemptAsync("tab", a.Id, guest.UserId, h.Device.Id));
        await connections.JoinWaitingAsync("tab", s.Assignment.Id, guest.UserId);
        Assert.Contains(("tab", SignalRLearnNotifier.WaitingGroup(s.Assignment.Id)), hub.Joined);
        var other = await Scenario(h);
        await Assert.ThrowsAsync<ForbiddenException>(() => connections.JoinWaitingAsync("tab", other.Assignment.Id, guest.UserId));
    }

    [Theory]
    [InlineData(false), InlineData(true)]
    public async Task GopVaoTaiKhoanChuaCoNguoiLamGiuKetQuaVaKhoaKhach(bool test)
    {
        using var h = new Harness(); var s = await Scenario(h, test); await Confirm(h, s.Student);
        await h.Assignments.ChangeStrangerPolicyAsync(s.Assignment.Id, StrangerPolicy.Allow, s.Lecturer);
        var token = GuestService.NewToken(); var guest = await Guests(h).JoinAsync(s.Assignment.Id, "Hoa", token);
        if (test) await h.Assignments.StartTestAsync(s.Assignment.Id, s.Lecturer);
        var a = await Do(h, s, guest.UserId, true); var stamp = guest.User.SecurityStamp;
        Assert.True(await Guests(h).MergeAsync(token, s.Student));
        Assert.Equal(s.Student, a.Participant.UserId); Assert.False(a.Participant.IsStranger); Assert.True(a.MadeAsGuest); Assert.False(a.ExcludeFromGradebook);
        Assert.Equal(s.Student, guest.MergedIntoUserId); Assert.True(guest.User.LockoutEnabled);
        Assert.Equal(DateTimeOffset.MaxValue, guest.User.LockoutEnd); Assert.NotEqual(stamp, guest.User.SecurityStamp);
        Assert.Null(await Guests(h).ResumeAsync(token)); Assert.False(await Guests(h).MergeAsync(token, s.Student));
        Assert.Equal(a.Id, (await h.Attempts.GetForLearnerAsync(a.Id, s.Student)).Id);
        await Assert.ThrowsAsync<ForbiddenException>(() => h.Attempts.GetForLearnerAsync(a.Id, guest.UserId));
        Assert.Single((await h.Gradebook.GetReportAsync(s.Assignment.Id, s.Lecturer)).Rows, r => !r.IsStranger);
    }

    [Theory]
    [InlineData(1, true), InlineData(3, false)]
    public async Task GopOnTapVuotTranGiuLuotNhungLoaiDiemKhach(int max, bool excluded)
    {
        using var h = new Harness(); var s = await Scenario(h, max: max); await Confirm(h, s.Student);
        var original = await Do(h, s, s.Student);
        await h.Assignments.ChangeStrangerPolicyAsync(s.Assignment.Id, StrangerPolicy.Allow, s.Lecturer);
        var token = GuestService.NewToken(); var guest = await Guests(h).JoinAsync(s.Assignment.Id, "Hoa", token);
        var moved = await Do(h, s, guest.UserId, true);
        Assert.True(await Guests(h).MergeAsync(token, s.Student));
        Assert.Equal(original.ParticipantId, moved.ParticipantId); Assert.Equal(2, moved.AttemptNumber);
        Assert.True(moved.MadeAsGuest); Assert.Equal(excluded, moved.ExcludeFromGradebook);
        Assert.True(moved.Participant.CompletionLabels.HasFlag(CompletionLabels.MadeAsGuest));
        var row = (await h.Gradebook.GetReportAsync(s.Assignment.Id, s.Lecturer)).Rows.Single(r => r.UserId == s.Student);
        Assert.Equal(excluded ? 0m : 1m, row.Score!.Raw);
        Assert.False(await Guests(h).MergeAsync(token, s.Student));
        Assert.Equal(2, await h.Db.Attempts.CountAsync(at => at.ParticipantId == original.ParticipantId));
    }

    [Fact]
    public async Task KiemTraTrungGiuCaHaiCanhBaoVaHuyMotLuot()
    {
        using var h = new Harness(); var s = await Scenario(h, true); await Confirm(h, s.Student);
        await h.Assignments.ChangeStrangerPolicyAsync(s.Assignment.Id, StrangerPolicy.Allow, s.Lecturer);
        await h.Assignments.StartTestAsync(s.Assignment.Id, s.Lecturer);
        var original = await Do(h, s, s.Student); var token = GuestService.NewToken();
        var guest = await Guests(h).JoinAsync(s.Assignment.Id, "Hoa", token); var kept = await Do(h, s, guest.UserId, true);
        await Guests(h).MergeAsync(token, s.Student);
        Assert.NotEqual(original.ParticipantId, kept.ParticipantId); Assert.Equal(guest.UserId, kept.Participant.UserId);
        Assert.NotNull(guest.User.LockoutEnd);
        var report = await h.Gradebook.GetReportAsync(s.Assignment.Id, s.Lecturer);
        Assert.Equal(2, report.Rows.Count(r => r.Warning == "có 2 lượt, 1 lượt làm khi là khách"));
        await Retakes(h).CancelAsync(original.Id, "Giữ lượt khách", s.Lecturer);
        Assert.Equal(original.ParticipantId, kept.ParticipantId);
        Assert.Equal(s.Student, kept.Participant.UserId);
        Assert.DoesNotContain((await h.Gradebook.GetReportAsync(s.Assignment.Id, s.Lecturer)).Rows, r => r.Warning != null);
        await Assert.ThrowsAsync<ForbiddenException>(() => h.Attempts.StartAsync(s.Assignment.Id, guest.UserId));
    }

    [Fact]
    public async Task ChuaXacMinhEmailVaTokenKhacKhongGopDuoc()
    {
        using var h = new Harness(); var s = await Scenario(h);
        await h.Assignments.ChangeStrangerPolicyAsync(s.Assignment.Id, StrangerPolicy.Allow, s.Lecturer);
        var token = GuestService.NewToken(); var guest = await Guests(h).JoinAsync(s.Assignment.Id, "Hoa", token);
        await Do(h, s, guest.UserId);
        Assert.False(await Guests(h).MergeAsync(GuestService.NewToken(), s.Student));
        await Assert.ThrowsAsync<ForbiddenException>(() => Guests(h).MergeAsync(token, s.Student));
        Assert.Null(guest.MergedIntoUserId);
    }

    [Fact]
    public async Task AdminKhongLietKeTinhKhachHayCapQuyenChoKhach()
    {
        using var h = new Harness(); var s = await Scenario(h); var admin = (await h.Db.Users.FindAsync(s.Lecturer))!;
        admin.IsSystemAdmin = true; await h.Db.SaveChangesAsync();
        await h.Assignments.ChangeStrangerPolicyAsync(s.Assignment.Id, StrangerPolicy.Allow, s.Lecturer);
        var service = new AdminService(h.Db, h.Clock); var before = await service.UsersAsync(admin.Id, null, 1);
        var guest = await Guests(h).JoinAsync(s.Assignment.Id, "Hoa", GuestService.NewToken()); await Do(h, s, guest.UserId);
        var after = await service.UsersAsync(admin.Id, null, 1);
        Assert.Equal(before.Total, after.Total); Assert.Equal(before.Stats, after.Stats);
        Assert.DoesNotContain(after.Rows, r => r.User.IsGuest);
        await Assert.ThrowsAsync<BusinessException>(() => service.UserAsync(guest.UserId, admin.Id));
        await Assert.ThrowsAsync<BusinessException>(() => service.ChangeRoleAsync(guest.UserId, true, "Thử", admin.Id));
    }

    [Fact]
    public async Task KhachKhongDangNhapMatKhauKeCaDuLieuBatThuongCoPassword()
    {
        using var h = new Harness(); var s = await Scenario(h);
        await h.Assignments.ChangeStrangerPolicyAsync(s.Assignment.Id, StrangerPolicy.Allow, s.Lecturer);
        var guest = await Guests(h).JoinAsync(s.Assignment.Id, "Hoa", GuestService.NewToken());
        using var identity = new SafetyIdentity(h);
        Assert.False(await identity.Users.CheckPasswordAsync(guest.User, "Valid123!"));
        guest.User.Email = Guid.NewGuid() + "@test.vn";
        await identity.PrepareAsync(guest.UserId);
        using var cache = new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions());
        var login = new LoginService(identity.Users, identity.SignIn, cache, Tang5bPureTests.Turnstile(), identity.Locks, h.Clock);
        Assert.False((await login.LoginAsync(guest.User.Email!, "Valid123!", true, null, null)).Succeeded);
    }

    [Fact]
    public async Task DangKyXacMinhDangNhapCungTrinhDuyetMoiLuuKetQua()
    {
        using var h = new Harness(); var s = await Scenario(h);
        await h.Assignments.ChangeStrangerPolicyAsync(s.Assignment.Id, StrangerPolicy.Allow, s.Lecturer);
        var token = GuestService.NewToken(); var guest = await Guests(h).JoinAsync(s.Assignment.Id, "Hoa", token);
        var attempt = await Do(h, s, guest.UserId, true);
        using var identity = new SafetyIdentity(h);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ApplicationName = typeof(AccountController).Assembly.GetName().Name, EnvironmentName = "Development" });
        builder.Services.AddControllersWithViews(); await using var app = builder.Build(); app.MapDefaultControllerRoute();
        using var cache = new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions());
        var turnstile = Tang5bPureTests.Turnstile();
        var login = new LoginService(identity.Users, identity.SignIn, cache, turnstile, identity.Locks, h.Clock);
        var controller = new AccountController(identity.Users, identity.SignIn, null!, builder.Environment, login, turnstile, identity.Locks, Guests(h), SecurityRound1Fixtures.Links, SecurityRound1Fixtures.Emails(h, identity.Users, identity.Email));
        var http = identity.Http; http.Request.Headers.Cookie = $"{GuestService.CookieName}={token}";
        SecurityRound4PureTests.VerifiedForm(http);
        var route = new RouteData(); route.Values["controller"] = "Account"; route.Values["action"] = "Register";
        controller.ControllerContext = new ControllerContext { HttpContext = http, RouteData = route };
        controller.Url = new FakeUrl(controller.ControllerContext); // không chạy routing thật trong test
        controller.TempData = new TempDataDictionary(http, new FakeTempData());
        var email = Guid.NewGuid() + "@test.vn";
        Assert.Equal("CheckEmail", Assert.IsType<ViewResult>(await controller.Register(new RegisterModel
        { Email = email, DisplayName = "Hoa tài khoản" })).ViewName);
        var user = (await identity.Users.FindByEmailAsync(email))!;
        Assert.False(user.EmailConfirmed); Assert.Null(guest.MergedIntoUserId);
        var model = new LoginModel { Email = email, Password = "Valid123!" };
        Assert.IsType<ViewResult>(await controller.Login(model)); Assert.Null(guest.MergedIntoUserId);
        // Link cũ chuyển tới đặt mật khẩu; chỉ POST lưu mới xác minh rồi đăng nhập lại.
        var code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(await identity.Users.GenerateEmailConfirmationTokenAsync(user)));
        Assert.Equal("RegisterPassword", Assert.IsType<RedirectToActionResult>(await controller.ConfirmEmail(user.Id, code)).ActionName);
        Assert.False(user.EmailConfirmed); controller.ModelState.Clear();
        await controller.RegisterPassword(new SetPasswordModel { UserId = user.Id, Code = code, Password = "Valid123!", ConfirmPassword = "Valid123!" });
        Assert.True(user.EmailConfirmed);
        Assert.Null(guest.MergedIntoUserId); controller.ModelState.Clear();
        Assert.IsType<LocalRedirectResult>(await controller.Login(model));
        Assert.Equal(user.Id, guest.MergedIntoUserId); Assert.Equal(user.Id, attempt.Participant.UserId);
        Assert.Contains("qem_guest=;", http.Response.Headers.SetCookie.ToString());
    }

    [Fact]
    public async Task CookieIdentityKhachCanDungBiMatVaBiVoHieuHoaSauGop()
    {
        using var h = new Harness(); var s = await Scenario(h); await Confirm(h, s.Student);
        await h.Assignments.ChangeStrangerPolicyAsync(s.Assignment.Id, StrangerPolicy.Allow, s.Lecturer);
        var token = GuestService.NewToken(); var guest = await Guests(h).JoinAsync(s.Assignment.Id, "Hoa", token);
        using var identity = new SafetyIdentity(h);
        var factory = new GuestClaimsFactory(identity.Users, identity.Http.RequestServices.GetRequiredService<RoleManager<IdentityRole>>(), Options.Create(new IdentityOptions()));
        var principal = await factory.CreateAsync(guest.User); Assert.True(GuestAccess.IsGuest(principal));
        var http = identity.Http; var seenGuest = false;
        var middleware = new GuestSessionMiddleware(c => { seenGuest = GuestAccess.IsGuest(c.User); return Task.CompletedTask; });
        http.User = principal; http.Request.Headers.Cookie = $"qem_guest={token}";
        await middleware.InvokeAsync(http, h.Db, h.Clock); Assert.True(seenGuest);
        http.User = principal; http.Request.Headers.Cookie = $"qem_guest={GuestService.NewToken()}";
        await middleware.InvokeAsync(http, h.Db, h.Clock); Assert.False(seenGuest);
        await Guests(h).MergeAsync(token, s.Student);
        http.User = principal; http.Request.Headers.Cookie = $"qem_guest={token}";
        await middleware.InvokeAsync(http, h.Db, h.Clock); Assert.False(seenGuest);
    }

    [Fact]
    public async Task JoinKhachDungBoDemTrinhDuyetVaCookieHttpOnly()
    {
        using var h = new Harness(); var s = await Scenario(h); using var identity = new SafetyIdentity(h);
        await h.Assignments.ChangeStrangerPolicyAsync(s.Assignment.Id, StrangerPolicy.Allow, s.Lecturer);
        var turnstile = Tang5bPureTests.Turnstile(); var guard = new JoinCodeGuardService(h.Db, h.Codes, h.Clock, turnstile);
        identity.Http.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("10.52.72.1");
        var browser = new string('A', 64); identity.Http.Request.Headers.Cookie = $"{LearnDevice.CookieName}={browser}"; // khách đếm theo trình duyệt (13/11/2026)
        for (var i = 0; i < 10; i++) await Assert.ThrowsAsync<BusinessException>(() => guard.ResolveAsync("sai", null, null, "10.52.72.1", browser));
        var controller = new ClassroomsController(h.Classrooms, h.Assignments, h.Codes, guard, turnstile, Guests(h), identity.SignIn);
        var http = identity.Http; http.User = Tang5d1PureTests.GuestPrincipal();
        controller.ControllerContext = new ControllerContext { HttpContext = http };
        controller.TempData = new TempDataDictionary(http, new FakeTempData());
        controller.Url = new FakeUrl(controller.ControllerContext);
        http.Request.Form = new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>());
        Assert.IsType<ViewResult>(await controller.Join(await Code(h, s), "Hoa"));
        Assert.Equal(true, controller.ViewData["ChallengeRequired"]);
        Assert.False(await h.Db.Participants.AnyAsync(p => p.AssignmentId == s.Assignment.Id && p.GuestId != null));
        // Pháp lý C3: khách mới chưa tick ô đồng ý thì chưa vào được.
        http.Request.Form = new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues> { ["cf-turnstile-response"] = "verified-by-fake" });
        Assert.IsType<ViewResult>(await controller.Join(await Code(h, s), "Hoa"));
        Assert.False(await h.Db.Participants.AnyAsync(p => p.AssignmentId == s.Assignment.Id && p.GuestId != null));
        Assert.IsType<RedirectToActionResult>(await controller.Join(await Code(h, s), "Hoa", acceptPolicy: true));
        var cookie = http.Response.Headers.SetCookie.Single(c => c!.StartsWith("qem_guest="))!;
        Assert.Contains("httponly", cookie); Assert.Contains("secure", cookie); Assert.Contains("samesite=lax", cookie);
        var token = cookie.Split(';')[0].Split('=')[1]; Assert.Matches("^[A-F0-9]{64}$", token);
        Assert.NotNull(await Guests(h).ResumeAsync(token));
    }

    [Fact]
    public async Task KhachChiHocTheQuaBaiGiaoVaGopTienDoKhongMatDanhGia()
    {
        using var h = new Harness(); var s = await Scenario(h); await Confirm(h, s.Student);
        var cards = new FlashcardService(h.Db, h.Library, h.Clock, h.Notifier);
        var set = await h.Db.StudySets.SingleAsync(x => x.Id == s.Quiz.StudySetId);
        await cards.SaveAsync(set.Id, null, "Mặt trước", "Mặt sau", s.Lecturer);
        var assignment = await h.Assignments.CreatePracticeAsync(s.Room.Id, s.Quiz.Id, new(null, null, 3,
            Form: AssignmentForm.Combined, StrangerPolicy: StrangerPolicy.Allow), s.Lecturer);
        s = s with { Assignment = assignment };
        var token = GuestService.NewToken(); var guest = await Guests(h).JoinAsync(assignment.Id, "Hoa", token);
        await Assert.ThrowsAsync<ForbiddenException>(() => cards.GetSessionAsync(set.Id, null, guest.UserId));
        var other = await Scenario(h);
        await Assert.ThrowsAsync<ForbiddenException>(() => cards.GetSessionAsync(set.Id, other.Assignment.Id, guest.UserId));
        var session = await cards.GetSessionAsync(set.Id, assignment.Id, guest.UserId);
        var card = await h.Db.Flashcards.SingleAsync(c => c.StudySetId == set.Id);
        await cards.RateAsync(set.Id, assignment.Id, card.Id, FlashcardRating.Remembered, guest.UserId);
        Assert.Contains((await h.Gradebook.GetReportAsync(assignment.Id, s.Lecturer)).Rows, r => r.UserId == guest.UserId && r.IsGuest);
        await cards.GetSessionAsync(set.Id, assignment.Id, s.Student);
        await cards.RateAsync(set.Id, assignment.Id, card.Id, FlashcardRating.NotRemembered, s.Student);
        h.Clock.UtcNow = h.Clock.UtcNow.AddMinutes(1);
        await cards.RateAsync(set.Id, assignment.Id, card.Id, FlashcardRating.Remembered, guest.UserId);
        await Guests(h).MergeAsync(token, s.Student);
        var p = await h.Db.Participants.SingleAsync(p => p.AssignmentId == assignment.Id && p.UserId == s.Student);
        var progress = await h.Db.FlashcardProgresses.SingleAsync(c => c.ParticipantId == p.Id && c.FlashcardId == card.Id);
        Assert.Equal(FlashcardRating.Remembered, progress.LastRating); Assert.Equal(3, progress.ReviewCount);
    }

    [Fact]
    public async Task HaiYeuCauGopDongThoiChiChuyenMotLan()
    {
        using var h = new Harness(); var s = await Scenario(h); await Confirm(h, s.Student);
        await h.Assignments.ChangeStrangerPolicyAsync(s.Assignment.Id, StrangerPolicy.Allow, s.Lecturer);
        var token = GuestService.NewToken(); var guest = await Guests(h).JoinAsync(s.Assignment.Id, "Hoa", token);
        var a = await Do(h, s, guest.UserId);
        using var left = DatabaseFixture.NewContext(); using var right = DatabaseFixture.NewContext();
        // Nạp trước để bắt lỗi DbContext giữ Guest.MergedIntoUserId cũ sau khi chờ khóa.
        await left.Guests.Include(g => g.User).SingleAsync(g => g.Id == guest.Id);
        await right.Guests.Include(g => g.User).SingleAsync(g => g.Id == guest.Id);
        var results = await Task.WhenAll(new GuestService(left, h.Clock, h.Notifier).MergeAsync(token, s.Student),
            new GuestService(right, h.Clock, h.Notifier).MergeAsync(token, s.Student));
        Assert.Single(results, result => result);
        using var check = DatabaseFixture.NewContext();
        Assert.Equal(s.Student, (await check.Attempts.Include(at => at.Participant).SingleAsync(at => at.Id == a.Id)).Participant.UserId);
        Assert.Single(await check.Attempts.Where(at => at.Participant.AssignmentId == s.Assignment.Id).ToListAsync());
    }

    [Theory]
    [InlineData(false), InlineData(true)]
    public async Task GopOnTapChiChotLuotKhachKhiCaHaiDangMo(bool accountOpen)
    {
        using var h = new Harness(); var s = await Scenario(h); await Confirm(h, s.Student);
        var original = await h.Attempts.StartAsync(s.Assignment.Id, s.Student);
        if (!accountOpen) await h.Attempts.FinishAsync(original.Id, s.Student);
        await h.Assignments.ChangeStrangerPolicyAsync(s.Assignment.Id, StrangerPolicy.Allow, s.Lecturer);
        var token = GuestService.NewToken(); var guest = await Guests(h).JoinAsync(s.Assignment.Id, "Hoa", token);
        var moved = await h.Attempts.StartAsync(s.Assignment.Id, guest.UserId); var deadline = moved.DeadlineAt;
        await Guests(h).MergeAsync(token, s.Student);
        Assert.Equal(accountOpen ? AttemptStatus.Ended : AttemptStatus.InProgress, moved.Status);
        Assert.Equal(deadline, moved.DeadlineAt);
        Assert.Single(await h.Db.Attempts.Where(at => at.ParticipantId == original.ParticipantId
            && (at.Status == AttemptStatus.InProgress || at.Status == AttemptStatus.Paused)).ToListAsync());
    }

    [Fact]
    public async Task SauGopLuotMoiCuaTaiKhoanKhongBiGhiLaKhach()
    {
        using var h = new Harness(); var s = await Scenario(h); await Confirm(h, s.Student);
        await h.Assignments.ChangeStrangerPolicyAsync(s.Assignment.Id, StrangerPolicy.Allow, s.Lecturer);
        var token = GuestService.NewToken(); var guest = await Guests(h).JoinAsync(s.Assignment.Id, "Hoa", token);
        await Do(h, s, guest.UserId); await Guests(h).MergeAsync(token, s.Student);
        using var fresh = DatabaseFixture.NewContext();
        var service = new AttemptService(fresh, h.Clock, [new ChoiceGrader()], h.Device, h.Notifier, h.Connections);
        Assert.False((await service.StartAsync(s.Assignment.Id, s.Student)).MadeAsGuest);
        Assert.DoesNotContain((await h.Gradebook.GetReportAsync(s.Assignment.Id, s.Lecturer)).Rows, r => r.Warning != null);
    }

    [Fact]
    public async Task TaiKhoanNgoaiLopDaDuocVaoVanXinLamLaiKiemTraDuoc()
    {
        using var h = new Harness(); var s = await Scenario(h, true);
        await h.Assignments.ChangeStrangerPolicyAsync(s.Assignment.Id, StrangerPolicy.Allow, s.Lecturer);
        var user = await h.UserAsync("Hoa"); await h.Classrooms.JoinByCodeAsync(await Code(h, s), user);
        await h.Assignments.StartTestAsync(s.Assignment.Id, s.Lecturer);
        await Do(h, s, user);
        Assert.True((await h.Attempts.GetAssignmentPageAsync(s.Assignment.Id, user)).CanRequestRetake);
        var request = await Retakes(h).RequestAsync(s.Assignment.Id, "Mất mạng", user);
        await Retakes(h).DecideAsync(request.Id, true, h.Clock.UtcNow.AddHours(1), s.Lecturer);
        Assert.Equal(AttemptKind.Retake, (await Retakes(h).StartAsync(request.Id, user)).Kind);
        Assert.False(await h.Db.ClassMembers.AnyAsync(m => m.ClassroomId == s.Room.Id && m.UserId == user));
    }

    [Fact]
    public async Task GiangVienHuyDiemCuKhachOnTapVaGiaiPhongLuot()
    {
        using var h = new Harness(); var s = await Scenario(h, max: 1);
        await h.Assignments.ChangeStrangerPolicyAsync(s.Assignment.Id, StrangerPolicy.Allow, s.Lecturer);
        var guest = await Guests(h).JoinAsync(s.Assignment.Id, "Hoa", GuestService.NewToken());
        var a = await Do(h, s, guest.UserId, true);
        await Retakes(h).CancelAsync(a.Id, "Hủy điểm cũ của khách", s.Lecturer);
        var row = (await h.Gradebook.GetReportAsync(s.Assignment.Id, s.Lecturer)).Rows.Single(r => r.UserId == guest.UserId);
        Assert.Null(row.Score); Assert.Equal(0, row.AttemptsUsed); Assert.Equal(a.Id, row.LatestAttemptId);
        Assert.NotNull(await h.Attempts.StartAsync(s.Assignment.Id, guest.UserId));
        var student = await Do(h, s, s.Student);
        await Assert.ThrowsAsync<BusinessException>(() => Retakes(h).CancelAsync(student.Id, "Không thuộc quy tắc khách", s.Lecturer));
    }

    [Fact]
    public async Task KiemTraTaiKhoanMoiOPhongChoVanCanGiangVienChonLuotKhach()
    {
        using var h = new Harness(); var s = await Scenario(h, true); await Confirm(h, s.Student);
        await h.Attempts.GetAssignmentPageAsync(s.Assignment.Id, s.Student);
        var target = await h.Db.Participants.SingleAsync(p => p.AssignmentId == s.Assignment.Id && p.UserId == s.Student);
        Assert.Empty(target.Attempts);
        await h.Assignments.ChangeStrangerPolicyAsync(s.Assignment.Id, StrangerPolicy.Allow, s.Lecturer);
        var token = GuestService.NewToken(); var guest = await Guests(h).JoinAsync(s.Assignment.Id, "Hoa", token);
        await h.Assignments.StartTestAsync(s.Assignment.Id, s.Lecturer);
        var a = await Do(h, s, guest.UserId, true); var sourceId = a.ParticipantId;
        await Guests(h).MergeAsync(token, s.Student);
        Assert.NotEqual(target.Id, a.ParticipantId);
        Assert.True((await h.Gradebook.GetReportAsync(s.Assignment.Id, s.Lecturer)).Rows.Single(r => r.UserId == guest.UserId).CanKeepGuest);
        await Assert.ThrowsAsync<ForbiddenException>(() => Guests(h).KeepTestGuestAsync(s.Assignment.Id, sourceId, "Giữ lượt", s.Student));
        await Guests(h).KeepTestGuestAsync(s.Assignment.Id, sourceId, "Tài khoản chưa có lượt làm", s.Lecturer);
        Assert.Equal(target.Id, a.ParticipantId); Assert.Equal(s.Student, a.Participant.UserId);
        await Guests(h).KeepTestGuestAsync(s.Assignment.Id, sourceId, "Gửi lại", s.Lecturer);
        Assert.Single(target.Attempts);
    }
}

// Đồ giả tối thiểu cho test controller: không cần routing/TempData thật.
internal sealed class FakeUrl(ActionContext context) : IUrlHelper
{
    public ActionContext ActionContext { get; } = context;
    public string? Action(UrlActionContext a) => $"/{a.Controller}/{a.Action}";
    public string? Content(string? contentPath) => contentPath;
    public bool IsLocalUrl(string? url) => url?.StartsWith('/') == true;
    public string? Link(string? routeName, object? values) => "/";
    public string? RouteUrl(UrlRouteContext routeContext) => "/";
}

internal sealed class FakeTempData : ITempDataProvider
{
    public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
    public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
}
