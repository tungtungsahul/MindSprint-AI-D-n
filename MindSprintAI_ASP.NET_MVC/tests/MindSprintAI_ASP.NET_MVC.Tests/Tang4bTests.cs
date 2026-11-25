using MindSprintAI_ASP.NET_MVC.Models;
using System.Text.Json;
using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MindSprintAI_ASP.NET_MVC.Controllers;
using MindSprintAI_ASP.NET_MVC.Data;
using MindSprintAI_ASP.NET_MVC.Data.Entities;
using MindSprintAI_ASP.NET_MVC.Hubs;
using MindSprintAI_ASP.NET_MVC.Services;

namespace MindSprintAI_ASP.NET_MVC.Tests;

public class Tang4bPureTests
{
    private static readonly DateTime Now = new(2026, 11, 5, 8, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(null, AttemptStatus.InProgress)]
    [InlineData(29, AttemptStatus.InProgress)]
    [InlineData(30, AttemptStatus.InProgress)]
    [InlineData(31, AttemptStatus.Paused)]
    public void TrangThaiChiTheoKetNoi(int? seconds, AttemptStatus expected)
    {
        var a = new Attempt { DisconnectedAt = seconds == null ? null : Now.AddSeconds(-seconds.Value), LastSeenAt = Now.AddDays(-1) };
        Assert.Equal(expected, AttemptRules.DisplayStatus(a, Now));
    }

    [Fact]
    public void ThoatTamDungKetNoiLaiDangLamKhongDoiDongHo()
    {
        var a = new Attempt { Status = AttemptStatus.Paused, DeadlineAt = Now.AddHours(1) };
        Assert.Equal(AttemptStatus.Paused, AttemptRules.DisplayStatus(a, Now));
        a.Status = AttemptStatus.InProgress;
        a.DisconnectedAt = null;
        Assert.Equal(AttemptStatus.InProgress, AttemptRules.DisplayStatus(a, Now));
        Assert.Equal(Now.AddHours(1), a.DeadlineAt);
        a.Status = AttemptStatus.Ended;
        a.DisconnectedAt = Now.AddDays(-1);
        Assert.Equal(AttemptStatus.Ended, AttemptRules.DisplayStatus(a, Now));
    }

    [Fact]
    public void HaiTabCungThietBiMotTabMatVanConKetNoi()
    {
        var t = new LearnConnectionTracker();
        t.Add("tab-1", 1, "may-a");
        t.Add("tab-2", 1, "may-a");
        t.Add("may-khac", 1, "may-b");
        t.Remove("tab-1");
        Assert.Equal(["tab-2"], t.ForDevice(1, "may-a"));
        t.Remove("tab-2");
        Assert.Empty(t.ForDevice(1, "may-a"));
        Assert.Equal(["may-khac"], t.ForDevice(1, "may-b"));
        Assert.Null(t.Find("tab-1"));
    }

    [Theory]
    [InlineData(false), InlineData(true)]
    public async Task CookieNgauNhienHttpOnlyMotNamVaKhongDoiCookieDaCo(bool https)
    {
        var http = new DefaultHttpContext();
        http.Request.Scheme = https ? "https" : "http";
        var middleware = new LearnDeviceMiddleware(_ => Task.CompletedTask);
        await middleware.InvokeAsync(http);
        var id = LearnDevice.Read(http);
        Assert.Matches("^[A-F0-9]{64}$", id!);
        var cookie = http.Response.Headers.SetCookie.ToString();
        Assert.Contains("httponly", cookie);
        Assert.Contains("samesite=lax", cookie);
        Assert.Contains("max-age=31536000", cookie);
        Assert.Equal(https, cookie.Contains("secure"));
        var next = new DefaultHttpContext();
        next.Request.Headers.Cookie = $"qem_device={id}";
        await middleware.InvokeAsync(next);
        Assert.Equal(id, LearnDevice.Read(next));
        Assert.Equal(0, next.Response.Headers.SetCookie.Count);
    }

    [Fact]
    public void HubBatBuocDangNhapVaPayloadChiCoIdGioUrl()
    {
        Assert.NotEmpty(typeof(LearnHub).GetCustomAttributes(typeof(AuthorizeAttribute), true));
        Assert.Equal(["AttemptId", "AssignmentId", "Time", "Url"], typeof(LearnNotice).GetProperties().Select(p => p.Name));
        var json = JsonSerializer.Serialize(new LearnNotice(AttemptId: 1, Time: VnTime.Format(Now), Url: "/Learn/State/1"));
        Assert.DoesNotContain("IsCorrect", json);
        Assert.DoesNotContain("Answer", json);
    }

    [Fact]
    public void MatKetNoiChoPhepTrong()
    {
        // MindSprint gộp migration Tang4b vào KhoiTao: kiểm cột trên model.
        using var db = DatabaseFixture.NewContext();
        Assert.True(db.Model.FindEntityType(typeof(Attempt))!.FindProperty(nameof(Attempt.DisconnectedAt))!.IsNullable);
    }

    [Fact]
    public async Task ChiMayCuNhanMovedVaBiGoKhoiNhom()
    {
        var hub = new TestLearnHub();
        hub.Joined.UnionWith([("cu-1", "attempt-1"), ("cu-2", "attempt-1"), ("moi", "attempt-1")]);
        var notifier = new SignalRLearnNotifier(hub);
        await notifier.MovedAsync(1, ["cu-1", "cu-2"]);
        Assert.Equal(("moved", "clients:cu-1,cu-2"), Assert.Single(hub.Sent));
        Assert.Equal([("moi", "attempt-1")], hub.Joined);
        await notifier.MovedAsync(1, []);
        Assert.Single(hub.Sent);
    }

    [Theory]
    [InlineData(false), InlineData(true)]
    public async Task XacNhanXoaRoiMaHoaTenVaLaPostHaiBuoc(bool leave)
    {
        const string name = "Minh </script><script>alert('x')</script>";
        const string title = "Bài <img src=x onerror=alert(1)>";
        var html = await Render("Classrooms", "ConfirmMembership", new MembershipConfirmation(1, 2, leave, name, [title]));
        Assert.DoesNotContain("<script>", html);
        Assert.DoesNotContain("<img src=x", html);
        Assert.Contains(title, WebUtility.HtmlDecode(html));
        Assert.Contains(leave ? "Bạn đang làm bài" : name, WebUtility.HtmlDecode(html));
        Assert.Contains("name=\"confirmed\" value=\"true\"", html);
        Assert.Contains("method=\"post\"", html);
        Assert.Contains("__RequestVerificationToken", html);
        Assert.Contains("role=\"alertdialog\"", html);
        Assert.Contains("autofocus", html);
    }

    [Fact]
    public async Task ChuyenMayKhongLoCauTraLoiVaDongBaiCoHaiLuaChon()
    {
        var a = new Attempt { Id = 1, Participant = new Participant { AssignmentId = 2 },
            Questions = [new AttemptQuestion { SnapshotJson = "DAP_AN_BI_MAT" }] };
        var html = await Render("Learn", "Takeover", a);
        Assert.Contains("Bài đang mở trên thiết bị khác. Chuyển sang thiết bị này?", WebUtility.HtmlDecode(html));
        Assert.DoesNotContain("DAP_AN_BI_MAT", html);
        Assert.Contains("__RequestVerificationToken", html);
        var close = await Render("Assignments", "CloseChoice", new Assignment { Id = 1, Quiz = new Quiz { Title = "<script>" } },
            new() { ["OpenAttempts"] = 2 });
        Assert.DoesNotContain("<script>", close);
        Assert.Contains("name=\"delayed\" value=\"true\"", close);
        Assert.Contains("name=\"delayed\" value=\"false\"", close);
        // Bài chỉ có thẻ ghi nhớ không có tên quiz: không để câu thành "đang làm bài ."
        var cards = WebUtility.HtmlDecode(await Render("Assignments", "CloseChoice", new Assignment { Id = 1 }, new() { ["OpenAttempts"] = 1 }));
        Assert.Contains("đang làm bài <b>Thẻ ghi nhớ</b>", cards);
        Assert.DoesNotContain("cộng thêm giờ", cards);
    }

    private static async Task<string> Render(string folder, string name, object model, Dictionary<string, object?>? values = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        { ApplicationName = typeof(LearnController).Assembly.GetName().Name, EnvironmentName = "Development" });
        builder.Services.AddControllersWithViews().AddApplicationPart(typeof(LearnController).Assembly);
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        await using var app = builder.Build();
        app.MapDefaultControllerRoute();
        using var scope = app.Services.CreateScope();
        var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        http.Request.Scheme = "http"; http.Request.Host = new HostString("localhost");
        http.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(), "Render test"));
        var route = new RouteData(); route.Values["controller"] = folder; route.Values["action"] = name;
        var action = new ActionContext(http, route, new ActionDescriptor());
        var view = scope.ServiceProvider.GetRequiredService<IRazorViewEngine>().GetView(null, $"/Views/{folder}/{name}.cshtml", false);
        Assert.True(view.Success);
        var data = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary()) { Model = model };
        foreach (var (key, value) in values ?? []) data[key] = value;
        var temp = scope.ServiceProvider.GetRequiredService<ITempDataDictionaryFactory>().GetTempData(http);
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        await view.View.RenderAsync(new ViewContext(action, view.View, data, temp, writer, new HtmlHelperOptions()));
        return writer.ToString();
    }
}

public sealed class RecordingLearnNotifier : ILearnNotifier
{
    public List<(string Event, LearnNotice Notice)> Events { get; } = [];
    public List<LecturerNotice> LecturerEvents { get; } = [];
    public Func<LecturerNotice, Task>? OnLecturers { get; set; }
    public async Task LecturersAsync(int assignmentId, int? recheckAfterSeconds = null)
    {
        var notice = new LecturerNotice(assignmentId, recheckAfterSeconds);
        if (OnLecturers != null) await OnLecturers(notice);
        lock (LecturerEvents) LecturerEvents.Add(notice); // dịch vụ thật gửi song song (Task.WhenAll): List thường mất phần tử.
    }
    public List<string> MovedConnections { get; } = [];
    public Func<string, LearnNotice, Task>? OnNotice { get; set; }
    public async Task AttemptAsync(long attemptId, string eventName, LearnNotice notice)
    {
        if (OnNotice != null) await OnNotice(eventName, notice);
        lock (Events) Events.Add((eventName, notice)); // thông báo đóng bài gửi song song; khóa để không mất sự kiện.
    }
    public Task TestStartedAsync(int assignmentId) => AttemptAsync(0, "test-started", new LearnNotice(AssignmentId: assignmentId));
    public Task MovedAsync(long attemptId, IReadOnlyList<string> oldConnections)
    {
        lock (MovedConnections) MovedConnections.AddRange(oldConnections);
        return AttemptAsync(attemptId, "moved", new LearnNotice(AttemptId: attemptId));
    }
}

[Collection("db")]
public class Tang4bTests
{
    private static AttemptService Attempts(Harness h) => Tang4DbTests.Attempts(h);
    private static LearnConnectionService Connections(Harness h, TestLearnHub hub) => new(h.Db, Attempts(h), h.Clock, h.Connections, hub, h.Classrooms, h.Notifier);

    private static Task Write(Harness h, Attempt a, AttemptQuestion q, string user, string action) => action switch
    {
        "Answer" => Attempts(h).AnswerAsync(a.Id, q.Id, Guid.NewGuid(), [], user, blanks: ["Đúng"]),
        "Draft" => Attempts(h).SaveDraftAsync(a.Id, q.Id, [], user, blanks: ["Đúng"]),
        "Flag" => Attempts(h).ToggleFlagAsync(a.Id, q.Id, user),
        _ => Attempts(h).FinishAsync(a.Id, user)
    };

    [Theory]
    [InlineData("Answer"), InlineData("Draft"), InlineData("Flag"), InlineData("Finish")]
    public async Task ThietBiKhacBiChanChuyenMayThiChiMayMoiDuocGhi(string action)
    {
        using var h = new Harness();
        var s = await Tang4DbTests.Mixed(h, choices: 1, input: true, surveys: false, test: true);
        var a = await Attempts(h).StartAsync(s.Assignment.Id, s.Student);
        var q = a.Questions.Single(q => Tang4DbTests.Snap(q).Type == QuestionType.TextInput);
        Assert.Equal("test-browser", a.ActiveDeviceId);
        h.Connections.Add("cu-tab-1", a.Id, "test-browser");
        h.Connections.Add("cu-tab-2", a.Id, "test-browser");
        h.Connections.Add("moi-tab", a.Id, "may-moi");
        h.Device.Id = "may-moi";
        await Assert.ThrowsAsync<BusinessException>(() => Write(h, a, q, s.Student, action));
        Assert.True((await Attempts(h).OpenPlayAsync(a.Id, s.Student)).NeedsTakeover);
        var fake = new RecordingLearnNotifier
        {
            OnNotice = async (_, _) =>
            {
                using var fresh = DatabaseFixture.NewContext();
                fresh.Database.SetCommandTimeout(3);
                Assert.Equal("may-moi", (await fresh.Attempts.SingleAsync(x => x.Id == a.Id)).ActiveDeviceId);
            }
        };
        h.Notifier = fake;
        await Attempts(h).TakeoverAsync(a.Id, s.Student);
        Assert.Equal(["cu-tab-1", "cu-tab-2"], fake.MovedConnections);
        h.Device.Id = "test-browser";
        await Assert.ThrowsAsync<BusinessException>(() => Write(h, a, q, s.Student, action));
        h.Device.Id = "may-moi";
        await Write(h, a, q, s.Student, action);
        Assert.Equal("may-moi", (await h.Db.Attempts.AsNoTracking().SingleAsync(x => x.Id == a.Id)).ActiveDeviceId);
    }

    [Fact]
    public async Task LuotCuNhanMayDauTienMoVaStartKhongTuChuyenMay()
    {
        using var h = new Harness();
        var s = await h.ScenarioAsync(questions: 1);
        var a = await h.Attempts.StartAsync(s.Assignment.Id, s.Student);
        a.ActiveDeviceId = null;
        await h.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<BusinessException>(() => Connections(h, new TestLearnHub())
            .JoinAttemptAsync("chua-mo", a.Id, s.Student, h.Device.Id));
        h.Device.Id = "dau-tien";
        Assert.False((await h.Attempts.OpenPlayAsync(a.Id, s.Student)).NeedsTakeover);
        Assert.Equal("dau-tien", a.ActiveDeviceId);
        h.Device.Id = "thu-hai";
        var same = await h.Attempts.StartAsync(s.Assignment.Id, s.Student);
        Assert.Equal(a.Id, same.Id);
        Assert.Equal("dau-tien", same.ActiveDeviceId);
        Assert.True((await h.Attempts.OpenPlayAsync(a.Id, s.Student)).NeedsTakeover);
    }

    [Fact]
    public async Task JoinAttemptChanNguoiKhacSaiMayVaLuotDaKetThuc()
    {
        using var h = new Harness();
        var s = await h.ScenarioAsync(questions: 1);
        var a = await h.Attempts.StartAsync(s.Assignment.Id, s.Student);
        var hub = new TestLearnHub();
        var service = Connections(h, hub);
        var other = await h.UserAsync("Khác");
        await Assert.ThrowsAsync<ForbiddenException>(() => service.JoinAttemptAsync("khac", a.Id, other, h.Device.Id));
        await Assert.ThrowsAsync<BusinessException>(() => service.JoinAttemptAsync("sai-may", a.Id, s.Student, "khac-may"));
        Assert.Empty(hub.Joined);
        Assert.Null(h.Connections.Find("sai-may"));
        await service.JoinAttemptAsync("dung", a.Id, s.Student, h.Device.Id);
        Assert.Contains(("dung", $"attempt-{a.Id}"), hub.Joined);
        await h.Attempts.FinishAsync(a.Id, s.Student);
        await Assert.ThrowsAsync<BusinessException>(() => service.JoinAttemptAsync("het", a.Id, s.Student, h.Device.Id));
    }

    [Fact]
    public async Task JoinWaitingChiNguoiThamGiaDungBaiKiemTra()
    {
        using var h = new Harness();
        var s = await h.ScenarioAsync(questions: 1);
        var test = await h.Assignments.CreateTestAsync(s.Room.Id, s.Quiz.Id, new TestSettings(null, 45), s.Lecturer);
        await h.Attempts.GetAssignmentPageAsync(test.Id, s.Student);
        var other = await h.UserAsync("Chưa tham gia");
        await h.Classrooms.JoinByCodeAsync(s.ClassCode, other);
        var second = await h.Assignments.CreateTestAsync(s.Room.Id, s.Quiz.Id, new TestSettings(null, 45), s.Lecturer);
        var hub = new TestLearnHub();
        var service = Connections(h, hub);
        await Assert.ThrowsAsync<ForbiddenException>(() => service.JoinWaitingAsync("khac", test.Id, other));
        await Assert.ThrowsAsync<ForbiddenException>(() => service.JoinWaitingAsync("khac-bai", second.Id, s.Student));
        await Assert.ThrowsAsync<ForbiddenException>(() => service.JoinWaitingAsync("on-tap", s.Assignment.Id, s.Student));
        await Assert.ThrowsAsync<ForbiddenException>(() => service.JoinWaitingAsync("gv", test.Id, s.Lecturer));
        Assert.Empty(hub.Joined);
        await service.JoinWaitingAsync("dung", test.Id, s.Student);
        Assert.Contains(("dung", $"assignment-{test.Id}-waiting"), hub.Joined);
    }

    [Theory]
    [InlineData(false, 120), InlineData(true, 120)]
    [InlineData(false, 10), InlineData(true, 10)]
    [InlineData(false, 15), InlineData(true, 15)]
    [InlineData(false, -1), InlineData(true, -1)]
    public async Task DongTreChiRutDungCotGiuGioCongVaThongBaoSauCommit(bool test, int oldMinutes)
    {
        using var h = new Harness();
        var s = await Tang4DbTests.Mixed(h, choices: 1, input: false, surveys: false, threshold: null, test: test);
        DateTime? old = oldMinutes < 0 ? null : h.Clock.UtcNow.AddMinutes(oldMinutes);
        var untouched = h.Clock.UtcNow.AddDays(3);
        s.Assignment.DueAt = test ? untouched : old;
        s.Assignment.TestEndsAt = test ? old : untouched;
        await h.Db.SaveChangesAsync();
        var a = await Attempts(h).StartAsync(s.Assignment.Id, s.Student);
        a.Participant.ExtraMinutes = 7;
        a.DeadlineAt = AttemptRules.DeadlineFor(s.Assignment, a.Participant);
        await h.Db.SaveChangesAsync();
        var expected = (old == null || oldMinutes > 15 ? h.Clock.UtcNow.AddMinutes(15) : old.Value).AddMinutes(7);
        var fake = new RecordingLearnNotifier
        {
            OnNotice = async (name, notice) =>
            {
                using var fresh = DatabaseFixture.NewContext();
                fresh.Database.SetCommandTimeout(3);
                var saved = await fresh.Attempts.Include(x => x.Participant).ThenInclude(p => p.Assignment).SingleAsync(x => x.Id == a.Id);
                Assert.Equal("deadline-changed", name);
                Assert.Equal(expected, saved.DeadlineAt);
                Assert.Equal(CloseReason.LecturerClosedDelayed, saved.Participant.Assignment.CloseReason);
                Assert.Equal(VnTime.Format(expected), notice.Time);
            }
        };
        h.Notifier = fake;
        Assert.Equal(1, await h.Assignments.RequestCloseAsync(s.Assignment.Id, null, s.Lecturer));
        Assert.Empty(fake.Events);
        await h.Assignments.CloseAfter15MinutesAsync(s.Assignment.Id, s.Lecturer);
        Assert.Equal(test ? untouched : expected.AddMinutes(-7), s.Assignment.DueAt);
        Assert.Equal(test ? expected.AddMinutes(-7) : untouched, s.Assignment.TestEndsAt);
        Assert.Equal(expected, a.DeadlineAt);
        Assert.Equal(7, a.Participant.ExtraMinutes);
        Assert.Equal(AssignmentStatus.Open, s.Assignment.Status);
        Assert.Equal("deadline-changed", Assert.Single(fake.Events).Event);
    }

    [Theory]
    [InlineData(false), InlineData(true)]
    public async Task KhongCoLuotMoThiDongNgay(bool test)
    {
        using var h = new Harness();
        var s = await Tang4DbTests.Mixed(h, choices: 1, input: false, surveys: false, threshold: null, test: test);
        Assert.Equal(0, await h.Assignments.CountOpenAttemptsAsync(s.Assignment.Id));
        await h.Assignments.CloseAfter15MinutesAsync(s.Assignment.Id, s.Lecturer);
        Assert.Equal(AssignmentStatus.Closed, s.Assignment.Status);
        Assert.Equal(CloseReason.LecturerClosedNow, s.Assignment.CloseReason);
        Assert.Equal(h.Clock.UtcNow, s.Assignment.ClosedAt);
    }

    [Theory]
    [InlineData(false), InlineData(true)]
    public async Task XoaVaTuRoiCanXacNhanKetThucGiuDiemChamNhapVaNhan(bool leave)
    {
        using var h = new Harness();
        var s = await Tang4DbTests.Mixed(h, choices: 1, input: true, surveys: false, test: true);
        var a = await Attempts(h).StartAsync(s.Assignment.Id, s.Student);
        var confirmed = a.Questions.Single(q => Tang4DbTests.Snap(q).Type == QuestionType.SingleChoice);
        await Attempts(h).AnswerAsync(a.Id, confirmed.Id, Guid.NewGuid(), [AssignmentTests.CorrectOption(confirmed)], s.Student);
        var draft = a.Questions.Single(q => Tang4DbTests.Snap(q).Type == QuestionType.TextInput);
        await Attempts(h).SaveDraftAsync(a.Id, draft.Id, [], s.Student, blanks: ["Đúng"]);
        var member = await h.Db.ClassMembers.SingleAsync(m => m.ClassroomId == s.Room.Id && m.UserId == s.Student);
        var fake = new RecordingLearnNotifier();
        h.Notifier = fake;
        var confirmation = leave ? await h.Classrooms.LeaveAsync(s.Room.Id, s.Student)
            : await h.Classrooms.RemoveMemberAsync(member.Id, s.Lecturer);
        Assert.NotNull(confirmation);
        Assert.Equal(leave, confirmation.Leaving);
        Assert.Contains(s.Quiz.Title, confirmation.AssignmentTitles);
        using (var fresh = DatabaseFixture.NewContext())
        {
            Assert.Equal(ClassMemberStatus.Active, (await fresh.ClassMembers.SingleAsync(m => m.Id == member.Id)).Status);
            Assert.Equal(AttemptStatus.InProgress, (await fresh.Attempts.SingleAsync(x => x.Id == a.Id)).Status);
            Assert.Equal(1, await fresh.AnswerSubmissions.CountAsync(x => x.AttemptQuestion.AttemptId == a.Id));
        }
        Assert.Empty(fake.Events);
        fake.OnNotice = async (name, _) =>
        {
            using var fresh = DatabaseFixture.NewContext();
            fresh.Database.SetCommandTimeout(3);
            var saved = await fresh.Attempts.SingleAsync(x => x.Id == a.Id);
            Assert.Equal("removed", name);
            Assert.Equal(AttemptEndReason.RemovedFromClass, saved.EndReason);
            Assert.Equal(3, saved.FirstScore);
            Assert.Equal(leave ? ClassMemberStatus.Left : ClassMemberStatus.Removed,
                (await fresh.ClassMembers.SingleAsync(x => x.Id == member.Id)).Status);
        };
        var result = leave ? await h.Classrooms.LeaveAsync(s.Room.Id, s.Student, true)
            : await h.Classrooms.RemoveMemberAsync(member.Id, s.Lecturer, true);
        Assert.Null(result);
        Assert.Equal(AttemptStatus.Ended, a.Status);
        Assert.Equal(AttemptEndReason.RemovedFromClass, a.EndReason);
        Assert.Equal(1, confirmed.FirstScore);
        Assert.Equal(2, draft.FirstScore);
        Assert.Equal(3, a.FirstScore);
        Assert.True(a.Participant.CompletionLabels.HasFlag(CompletionLabels.RemovedFromClass));
        // (B4b, 67) trang GV ghi đúng lý do: tự rời ≠ bị xóa; không kèm thêm "Đã rời lớp" cho người bị xóa.
        var (_, rows) = await h.Gradebook.GetReportAsync(s.Assignment.Id, s.Lecturer);
        Assert.Equal([leave ? "Bị dừng do rời lớp" : "Bị dừng do bị xóa khỏi lớp"],
            DisplayText.Of(rows.Single(r => r.UserId == s.Student).Labels).ToList());
        Assert.Single(a.Participant.Attempts, x => x.Status != AttemptStatus.Cancelled);
        Assert.Single(fake.Events);
        if (leave) await h.Classrooms.LeaveAsync(s.Room.Id, s.Student, true);
        else await h.Classrooms.RemoveMemberAsync(member.Id, s.Lecturer, true);
        Assert.Single(fake.Events);
    }

    [Fact]
    public async Task HaiKetNoiNgatCuoiMoiGhiMatKetNoiThoatVaNoiLaiKhongDoiHan()
    {
        using var h = new Harness();
        var s = await h.ScenarioAsync(questions: 1);
        var a = await h.Attempts.StartAsync(s.Assignment.Id, s.Student);
        var deadline = a.DeadlineAt;
        var service = Connections(h, new TestLearnHub());
        await service.JoinAttemptAsync("mot", a.Id, s.Student, h.Device.Id);
        await service.JoinAttemptAsync("hai", a.Id, s.Student, h.Device.Id);
        Assert.Null(a.DisconnectedAt);
        await service.DisconnectAsync("mot");
        Assert.Null(a.DisconnectedAt);
        await service.DisconnectAsync("hai");
        Assert.Equal(h.Clock.UtcNow, a.DisconnectedAt);
        Assert.Equal(AttemptStatus.InProgress, AttemptRules.DisplayStatus(a, h.Clock.UtcNow.AddSeconds(29)));
        Assert.Equal(AttemptStatus.Paused, AttemptRules.DisplayStatus(a, h.Clock.UtcNow.AddSeconds(31)));
        await service.JoinAttemptAsync("lai", a.Id, s.Student, h.Device.Id);
        await h.Attempts.PauseAsync(a.Id, s.Student);
        Assert.Equal(AttemptStatus.Paused, AttemptRules.DisplayStatus(a, h.Clock.UtcNow));
        await service.DisconnectAsync("lai");
        await service.JoinAttemptAsync("tiep", a.Id, s.Student, h.Device.Id);
        Assert.Equal(AttemptStatus.InProgress, AttemptRules.DisplayStatus(a, h.Clock.UtcNow));
        Assert.Null(a.DisconnectedAt);
        Assert.Equal(deadline, a.DeadlineAt);
        Assert.Null(a.EndReason);
    }

    [Fact]
    public async Task KhoiDongChiDatLaiLuotMoDangNull()
    {
        using var h = new Harness();
        var s = await h.ScenarioAsync(questions: 1);
        var ended = await h.Attempts.StartAsync(s.Assignment.Id, s.Student);
        await h.Attempts.FinishAsync(ended.Id, s.Student);
        var open = await h.Attempts.StartAsync(s.Assignment.Id, s.Student);
        var second = await h.ScenarioAsync(questions: 1);
        var cancelled = await h.Attempts.StartAsync(second.Assignment.Id, second.Student);
        await h.Attempts.FinishAsync(cancelled.Id, second.Student);
        cancelled.Status = AttemptStatus.Cancelled;
        cancelled.DisconnectedAt = null;
        await h.Db.SaveChangesAsync();
        var paused = await h.Attempts.StartAsync(second.Assignment.Id, second.Student);
        paused.Status = AttemptStatus.Paused;
        paused.DisconnectedAt = null;
        ended.DisconnectedAt = open.DisconnectedAt = null;
        await h.Db.SaveChangesAsync();
        await LearnConnectionService.ResetDisconnectedAsync(h.Db, h.Clock.UtcNow);
        var saved = await h.Db.Attempts.AsNoTracking().Where(x => x.ParticipantId == open.ParticipantId).ToListAsync();
        Assert.Null(saved.Single(x => x.Id == ended.Id).DisconnectedAt);
        Assert.Equal(h.Clock.UtcNow, saved.Single(x => x.Id == open.Id).DisconnectedAt);
        Assert.Null((await h.Db.Attempts.AsNoTracking().SingleAsync(x => x.Id == cancelled.Id)).DisconnectedAt);
        Assert.Equal(h.Clock.UtcNow, (await h.Db.Attempts.AsNoTracking().SingleAsync(x => x.Id == paused.Id)).DisconnectedAt);
        var before = h.Clock.UtcNow;
        await LearnConnectionService.ResetDisconnectedAsync(h.Db, before.AddMinutes(1));
        Assert.Equal(before, (await h.Db.Attempts.AsNoTracking().SingleAsync(x => x.Id == open.Id)).DisconnectedAt);
    }

    [Fact]
    public async Task DoiHanVaDongNgayDayThongBaoSauCommitVaGiuChanTraLoi()
    {
        using var h = new Harness();
        var s = await h.ScenarioAsync(questions: 1);
        var a = await h.Attempts.StartAsync(s.Assignment.Id, s.Student);
        var due = h.Clock.UtcNow.AddMinutes(40);
        var fake = new RecordingLearnNotifier
        {
            OnNotice = async (name, _) =>
            {
                using var fresh = DatabaseFixture.NewContext();
                fresh.Database.SetCommandTimeout(3);
                var saved = await fresh.Attempts.SingleAsync(x => x.Id == a.Id);
                if (name == "deadline-changed") Assert.Equal(due, saved.DeadlineAt);
                else Assert.Equal(AttemptEndReason.ClosedByLecturer, saved.EndReason);
            }
        };
        h.Notifier = fake;
        await h.Assignments.ChangeDueAsync(s.Assignment.Id, due, null, s.Lecturer);
        Assert.Equal(due, (await h.Attempts.GetStateAsync(a.Id, s.Student)).DeadlineAt);
        await h.Assignments.CloseNowAsync(s.Assignment.Id, s.Lecturer);
        Assert.Equal(["deadline-changed", "closed"], fake.Events.Select(x => x.Event));
        await h.Assignments.CloseNowAsync(s.Assignment.Id, s.Lecturer);
        Assert.Equal(2, fake.Events.Count);
        await Assert.ThrowsAsync<BusinessException>(() => h.Attempts.AnswerAsync(a.Id, a.Questions[0].Id,
            Guid.NewGuid(), [AssignmentTests.CorrectOption(a.Questions[0])], s.Student));
    }

    [Fact]
    public async Task BatDauKiemTraChiDaySauCommit()
    {
        using var h = new Harness();
        var s = await h.ScenarioAsync(questions: 1);
        var test = await h.Assignments.CreateTestAsync(s.Room.Id, s.Quiz.Id, new TestSettings(null, 45), s.Lecturer);
        var fake = new RecordingLearnNotifier
        {
            OnNotice = async (name, notice) =>
            {
                using var fresh = DatabaseFixture.NewContext();
                fresh.Database.SetCommandTimeout(3);
                Assert.Equal("test-started", name);
                Assert.Equal(test.Id, notice.AssignmentId);
                Assert.NotNull((await fresh.Assignments.SingleAsync(x => x.Id == test.Id)).TestStartedAt);
            }
        };
        h.Notifier = fake;
        await h.Assignments.StartTestAsync(test.Id, s.Lecturer);
        Assert.Single(fake.Events);
    }

    [Fact]
    public async Task MayCuNgatKhongAnhHuongKetNoiMayDangGiuLuot()
    {
        using var h = new Harness();
        var s = await h.ScenarioAsync(questions: 1);
        var a = await h.Attempts.StartAsync(s.Assignment.Id, s.Student);
        var service = Connections(h, new TestLearnHub());
        await service.JoinAttemptAsync("cu", a.Id, s.Student, h.Device.Id);
        h.Device.Id = "moi";
        await h.Attempts.TakeoverAsync(a.Id, s.Student);
        await service.JoinAttemptAsync("moi", a.Id, s.Student, h.Device.Id);
        await service.DisconnectAsync("cu");
        Assert.Null(a.DisconnectedAt);
        Assert.Equal(AttemptStatus.InProgress, AttemptRules.DisplayStatus(a, h.Clock.UtcNow.AddMinutes(1)));
    }

    [Fact]
    public async Task HuyLuotBiXoaKhoiLopBoNhanRemoved()
    {
        using var h = new Harness();
        var s = await Tang4DbTests.Mixed(h, choices: 1, input: false, surveys: false, test: true);
        var first = await h.Attempts.StartAsync(s.Assignment.Id, s.Student);
        await h.Attempts.FinishAsync(first.Id, s.Student);
        await h.Assignments.CloseNowAsync(s.Assignment.Id, s.Lecturer);
        var retakes = new RetakeService(h.Db, h.Assignments, h.Attempts, h.Clock);
        var request = await retakes.RequestAsync(s.Assignment.Id, "Mất mạng", s.Student);
        await retakes.DecideAsync(request.Id, true, h.Clock.UtcNow.AddHours(1), s.Lecturer);
        var retake = await retakes.StartAsync(request.Id, s.Student);
        var member = await h.Db.ClassMembers.SingleAsync(m => m.ClassroomId == s.Room.Id && m.UserId == s.Student);
        await h.Classrooms.RemoveMemberAsync(member.Id, s.Lecturer, true);
        Assert.True(retake.Participant.CompletionLabels.HasFlag(CompletionLabels.RemovedFromClass));
        await h.Classrooms.JoinByCodeAsync(s.ClassCode, s.Student);
        await retakes.CancelAsync(retake.Id, "Xóa nhầm", s.Lecturer);
        Assert.False(retake.Participant.CompletionLabels.HasFlag(CompletionLabels.RemovedFromClass));
    }
}

public sealed class TestLearnHub : IHubContext<LearnHub>
{
    public HashSet<(string Connection, string Group)> Joined { get; } = [];
    public List<(string Event, string Target)> Sent { get; } = [];
    public List<object?[]> Payloads { get; } = [];
    public IHubClients Clients => new TestClients(Sent, Payloads);
    public IGroupManager Groups => new TestGroups(Joined);

    private sealed class TestGroups(HashSet<(string, string)> joined) : IGroupManager
    {
        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
        { joined.Add((connectionId, groupName)); return Task.CompletedTask; }
        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
        { joined.Remove((connectionId, groupName)); return Task.CompletedTask; }
    }

    private sealed class TestClients(List<(string Event, string Target)> sent, List<object?[]> payloads) : IHubClients
    {
        public IClientProxy All => new TestProxy(sent, payloads, "all");
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => new TestProxy(sent, payloads, "all-except");
        public IClientProxy Client(string connectionId) => new TestProxy(sent, payloads, "client:" + connectionId);
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => new TestProxy(sent, payloads, "clients:" + string.Join(',', connectionIds));
        public IClientProxy Group(string groupName) => new TestProxy(sent, payloads, "group:" + groupName);
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => new TestProxy(sent, payloads, "group-except:" + groupName);
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => new TestProxy(sent, payloads, "groups:" + string.Join(',', groupNames));
        public IClientProxy User(string userId) => new TestProxy(sent, payloads, "user:" + userId);
        public IClientProxy Users(IReadOnlyList<string> userIds) => new TestProxy(sent, payloads, "users:" + string.Join(',', userIds));
    }

    private sealed class TestProxy(List<(string Event, string Target)> sent, List<object?[]> payloads, string target) : IClientProxy
    {
        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
        { sent.Add((method, target)); payloads.Add(args); return Task.CompletedTask; }
    }
}
