using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MindSprintAI_ASP.NET_MVC.Controllers;
using MindSprintAI_ASP.NET_MVC.Data.Entities;
using MindSprintAI_ASP.NET_MVC.Models;
using MindSprintAI_ASP.NET_MVC.Services;

namespace MindSprintAI_ASP.NET_MVC.Tests;

public class Tang5c2PureTests
{
    [Theory]
    [InlineData(AssignmentMode.Practice)]
    [InlineData(AssignmentMode.Test)]
    public void GioRiengDichTheoHanChung(AssignmentMode mode)
    {
        var end = new DateTime(2026, 11, 5, 10, 0, 0, DateTimeKind.Utc);
        var a = new Assignment { Mode = mode, DueAt = end, TestEndsAt = end };
        var p = new Participant { ExtraMinutes = 25 };
        Assert.Equal(end.AddMinutes(25), AttemptRules.DeadlineFor(a, p));
        a.DueAt = a.TestEndsAt = end.AddMinutes(15);
        Assert.Equal(end.AddMinutes(40), AttemptRules.DeadlineFor(a, p));
    }

    [Fact]
    public async Task TrangQrMaHoaUrlVaCoVanBanThayThe()
    {
        const string url = "https://quiz.test/Classrooms/Join?code=12345678&x=\"<script>";
        var html = await Tang4ViewTests.Render("Shared", "JoinQr", new JoinQrModel("12345678", url, "https://quiz.test/Classrooms/Join"));
        Assert.Contains("data-join-url=\"https://quiz.test/", html);
        Assert.Contains("data-code=\"12345678\"", html);
        Assert.Contains("12345678", html); Assert.Contains("aria-label=", html);
        Assert.Contains("&lt;script&gt;", html); Assert.DoesNotContain("<script>", html);
        Assert.Contains("/lib/qrcode-generator/qrcode.js", html);
    }

    [Fact]
    public async Task TrangJoinDienSanMaVaMaHoaRazor()
    {
        var html = await Tang4ViewTests.Render("Classrooms", "Join", new object(), new() { ["Code"] = "\"><script>alert(1)</script>" });
        Assert.Contains("value=\"&quot;&gt;&lt;script&gt;", html);
        Assert.DoesNotContain("<script>alert", html);
    }

    [Theory]
    [InlineData(AssignmentMode.Practice, true)]
    [InlineData(AssignmentMode.Test, false)]
    public async Task BangTrucTiepHienGioVaChiOnTapCoCongLuot(AssignmentMode mode, bool attempts)
    {
        var a = new Assignment { Id = 1, Mode = mode };
        var model = new AssignmentDetailsModel { Assignment = a, Code = "12345678", OpenAttempts = 0,
            Rows = [new("hv", "<script>Minh</script>", CompletionStatus.InProgress, CompletionLabels.None, 1, null, 1,
                ExtraMinutes: 20, ExtraAttempts: 1, CanAdjust: true)] };
        var html = await Tang4ViewTests.Render("Assignments", "_LiveTable", model);
        Assert.Contains("+20", html); Assert.Contains("name=\"minutes\"", html);
        Assert.Equal(attempts, html.Contains("name=\"extraAttempts\""));
        Assert.Contains("__RequestVerificationToken", html);
        Assert.DoesNotContain("<script>Minh", html);
        a.AnswersPublishedAt = DateTime.UtcNow;
        html = WebUtility.HtmlDecode(await Tang4ViewTests.Render("Assignments", "_LiveTable", model));
        Assert.DoesNotContain("name=\"minutes\"", html); Assert.Contains("Đáp án đã công bố", html);
    }

    [Fact]
    public async Task TrangBaiGiaoHienLuotConLaiVaHanRieng()
    {
        var end = new DateTime(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc);
        var model = new LearnerAssignmentModel { Assignment = new() { MaxAttempts = 1 }, Attempts = [], CanStart = true,
            RemainingAttempts = 2, PersonalDeadline = end };
        var html = WebUtility.HtmlDecode(await Tang4ViewTests.Render("Learn", "Assignment", model));
        Assert.Contains("Số lượt còn lại: 2", html); Assert.Contains(VnTime.Format(end), html);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NutQrVaHoanTacChiHienKhiCoMoc(bool undo)
    {
        var until = undo ? DateTime.UtcNow.AddHours(48) : (DateTime?)null;
        var model = new AssignmentDetailsModel { Assignment = new() { Id = 1 }, Code = "12345678", OpenAttempts = 0,
            Rows = [], UndoCodeUntil = until };
        var assignment = WebUtility.HtmlDecode(await Tang4ViewTests.Render("Assignments", "Details", model));
        var classroom = WebUtility.HtmlDecode(await Tang4ViewTests.Render("Classrooms", "Details", new Classroom { Id = 1 },
            new() { ["Assignments"] = new List<Assignment>(), ["Code"] = "12345678", ["UndoCodeUntil"] = until }));
        foreach (var html in new[] { assignment, classroom })
        {
            Assert.Contains("Mã QR", html); Assert.Equal(undo, html.Contains("Dùng lại mã cũ"));
            Assert.Contains("__RequestVerificationToken", html);
        }
    }

    [Fact]
    public async Task UrlActionTaoLinkJoinTuyetDoiVaMaHoaQuery()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ApplicationName = typeof(AssignmentsController).Assembly.GetName().Name });
        builder.Services.AddControllersWithViews(); builder.Logging.SetMinimumLevel(LogLevel.Warning);
        await using var app = builder.Build(); app.MapDefaultControllerRoute();
        app.Urls.Add("http://127.0.0.1:0"); await app.StartAsync();
        using var scope = app.Services.CreateScope();
        var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        http.Request.Scheme = "https"; http.Request.Host = new HostString("quiz.test");
        http.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(), "test"));
        var helper = scope.ServiceProvider.GetRequiredService<IUrlHelperFactory>()
            .GetUrlHelper(new ActionContext(http, new RouteData(), new ActionDescriptor()));
        Assert.Equal("https://quiz.test/Classrooms/Join?code=12345678",
            helper.Action("Join", "Classrooms", new { code = "12345678" }, http.Request.Scheme));
        Assert.DoesNotContain("<script>", helper.Action("Join", "Classrooms", new { code = "\"><script>" }, http.Request.Scheme)!);
    }
}

[Collection("db")]
public class Tang5c2Tests
{
    private static RetakeService Retakes(Harness h) => new(h.Db, h.Assignments, h.Attempts, h.Clock);
    private static AssignmentsController Assignments(Harness h) => new(h.Assignments, h.Classrooms, h.Library,
        h.Gradebook, h.Codes, Retakes(h), Tang4DbTests.Surveys(h), h.Regrades,
        new(h.Db, h.Clock), new(h.Db, h.Classrooms, h.Clock));
    private static T AsUser<T>(T controller, string user, IServiceProvider? services = null) where T : Controller
    {
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user)], "test")) };
        http.Request.Scheme = "https"; http.Request.Host = new HostString("quiz.test");
        if (services != null) http.RequestServices = services;
        http.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(), "test"));
        controller.ControllerContext = new ControllerContext { HttpContext = http,
            RouteData = new RouteData(), ActionDescriptor = new Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor() };
        if (services != null) controller.Url = services.GetRequiredService<IUrlHelperFactory>().GetUrlHelper(controller.ControllerContext);
        return controller;
    }

    private static void Is403(Controller controller, Exception exception)
    {
        var action = new ActionContext(controller.HttpContext, new RouteData(), new ActionDescriptor());
        var context = new ExceptionContext(action, []) { Exception = exception };
        new BusinessExceptionFilter(null!).OnException(context);
        Assert.Equal(403, Assert.IsType<ViewResult>(context.Result).StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CongGioCapNhatLuotVaGiuOffsetSauDong15Phut(bool test)
    {
        using var h = new Harness(); var s = await h.ScenarioAsync();
        var a = test ? await h.Assignments.CreateTestAsync(s.Room.Id, s.Quiz.Id, new(null, 60), s.Lecturer) : s.Assignment;
        if (test) await h.Assignments.StartTestAsync(a.Id, s.Lecturer);
        var at = await h.Attempts.StartAsync(a.Id, s.Student);
        await h.Assignments.ChangeExtraMinutesAsync(a.Id, s.Student, 20, "Mất điện", s.Lecturer);
        Assert.Equal((test ? a.TestEndsAt : a.DueAt)!.Value.AddMinutes(20), at.DeadlineAt);
        if (!test)
        {
            await h.Assignments.ChangeDueAsync(a.Id, h.Clock.UtcNow.AddHours(2), "Đổi hạn", s.Lecturer);
            Assert.Equal(a.DueAt!.Value.AddMinutes(20), at.DeadlineAt);
        }
        await h.Assignments.CloseAfter15MinutesAsync(a.Id, s.Lecturer);
        Assert.Equal(h.Clock.UtcNow.AddMinutes(35), at.DeadlineAt);
        Assert.Equal(1, await h.Assignments.CountExtraTimeOpenAsync(a.Id, s.Lecturer));
    }

    [Fact]
    public async Task PopupThongBaoVaBangGvChiSauCommit()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync();
        var at = await h.Attempts.StartAsync(s.Assignment.Id, s.Student);
        var recorder = new AdjustmentRecorder(h, s.Assignment.Id, s.Student, at.Id); h.Notifier = recorder;
        await h.Assignments.ChangeExtraMinutesAsync(s.Assignment.Id, s.Student, 25, "Mất điện", s.Lecturer);
        Assert.Equal(["deadline-changed", "notification", "lecturers-changed"], recorder.Events);
        var notice = await h.Db.Notifications.SingleAsync(n => n.UserId == s.Student);
        Assert.Equal($"Giảng viên đã cộng thêm 25 phút cho bạn ở bài {s.Quiz.Title}: Mất điện", NotificationService.Payload(notice).Text);
        var change = await h.Db.AssignmentChanges.SingleAsync(c => c.AssignmentId == s.Assignment.Id && c.Field.EndsWith(".ExtraMinutes"));
        Assert.Equal("0", change.OldValue); Assert.Equal("25", change.NewValue); Assert.Equal("Mất điện", change.Reason);
        Assert.Equal(s.Lecturer, change.ChangedById);
    }

    [Theory]
    [InlineData(AssignmentMode.Practice)]
    [InlineData(AssignmentMode.Test)]
    public async Task DaCongBoDapAnThiKhongCongGio(AssignmentMode mode)
    {
        using var h = new Harness(); var s = await h.ScenarioAsync();
        s.Assignment.Mode = mode; s.Assignment.AnswersPublishedAt = h.Clock.UtcNow; await h.Db.SaveChangesAsync();
        var error = await Assert.ThrowsAsync<BusinessException>(() => h.Assignments.ChangeExtraMinutesAsync(s.Assignment.Id, s.Student, 20, "Lý do", s.Lecturer));
        Assert.Contains("Đáp án đã công bố", error.Message);
        Assert.False(await h.Db.Notifications.AnyAsync(n => n.UserId == s.Student));
    }

    [Theory]
    [InlineData(-1, "Lý do")]
    [InlineData(601, "Lý do")]
    [InlineData(1, "")]
    [InlineData(1, "   ")]
    [InlineData(1, null)]
    public async Task CongGioKiemBienVaLyDo(int minutes, string? reason)
    {
        using var h = new Harness(); var s = await h.ScenarioAsync();
        await Assert.ThrowsAsync<BusinessException>(() => h.Assignments.ChangeExtraMinutesAsync(s.Assignment.Id, s.Student, minutes, reason, s.Lecturer));
        Assert.False(await h.Db.Participants.AnyAsync(p => p.AssignmentId == s.Assignment.Id));
    }

    [Theory]
    [InlineData(10, false)]
    [InlineData(15, true)]
    [InlineData(30, true)]
    public async Task BoGioRiengKhongLamHetHanTucThi(int commonMinutes, bool allowed)
    {
        using var h = new Harness(); var s = await h.ScenarioAsync();
        await h.Assignments.ChangeDueAsync(s.Assignment.Id, h.Clock.UtcNow.AddMinutes(30), "Hạn", s.Lecturer);
        var at = await h.Attempts.StartAsync(s.Assignment.Id, s.Student);
        await h.Assignments.ChangeExtraMinutesAsync(s.Assignment.Id, s.Student, 30, "Bù giờ", s.Lecturer);
        h.Clock.UtcNow = h.Clock.UtcNow.AddMinutes(30 - commonMinutes);
        if (allowed)
        {
            await h.Assignments.ChangeExtraMinutesAsync(s.Assignment.Id, s.Student, 0, "Hết cần bù", s.Lecturer);
            Assert.Equal(s.Assignment.DueAt, at.DeadlineAt); Assert.Equal(0, at.Participant.ExtraMinutes);
        }
        else
        {
            var error = await Assert.ThrowsAsync<BusinessException>(() => h.Assignments.ChangeExtraMinutesAsync(s.Assignment.Id, s.Student, 0, "Hết cần bù", s.Lecturer));
            Assert.Contains("15 phút", error.Message);
            Assert.Equal(s.Assignment.DueAt!.Value.AddMinutes(30), at.DeadlineAt);
        }
    }

    [Fact]
    public async Task GvPhuDuocCongGioHocVien403VaXacNhanDongDemNguoiCoGio()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(); var assistant = await h.UserAsync("Cô Phụ");
        var u = (await h.Db.Users.FindAsync(assistant))!; u.EmailConfirmed = true; await h.Db.SaveChangesAsync();
        await h.InviteAndAcceptAssistantAsync(s.Room.Id, u.Email!, s.Lecturer);
        var at = await h.Attempts.StartAsync(s.Assignment.Id, s.Student);
        await h.Assignments.ChangeExtraMinutesAsync(s.Assignment.Id, s.Student, 600, "Mất điện", assistant);
        var controller = AsUser(Assignments(h), s.Student);
        var error = await Assert.ThrowsAsync<ForbiddenException>(() => controller.ChangeExtraMinutes(s.Assignment.Id, s.Student, 1, "Tự cộng"));
        Is403(controller, error);
        controller = AsUser(Assignments(h), assistant);
        var result = Assert.IsType<ViewResult>(await controller.Close(s.Assignment.Id, null));
        Assert.Equal("CloseChoice", result.ViewName); Assert.Equal(1, result.ViewData["ExtraTimeOpenAttempts"]);
        var html = WebUtility.HtmlDecode(await Tang4ViewTests.Render("Assignments", "CloseChoice", result.Model!,
            new() { ["OpenAttempts"] = 1, ["ExtraTimeOpenAttempts"] = result.ViewData["ExtraTimeOpenAttempts"] }));
        Assert.Contains("Trong đó 1 học viên được cộng thêm giờ riêng", html);
        // Hộp đóng bài nói rõ chuyện xảy ra với bài đang làm ở từng lựa chọn.
        Assert.Contains("câu chưa làm tính 0 điểm", html); Assert.Contains("còn 15 phút để làm nốt", html);
        await h.Assignments.CloseNowAsync(s.Assignment.Id, assistant);
        Assert.Equal(AttemptStatus.Ended, at.Status);
        Assert.Equal(0, await h.Assignments.CountExtraTimeOpenAsync(s.Assignment.Id, assistant));
    }

    [Fact]
    public async Task KhungLamLaiKhongBiGioCongVaDong15PhutThayDoi()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync();
        var a = await h.Assignments.CreateTestAsync(s.Room.Id, s.Quiz.Id, new(null, 60), s.Lecturer);
        await h.Assignments.StartTestAsync(a.Id, s.Lecturer);
        var first = await h.Attempts.StartAsync(a.Id, s.Student); await h.Attempts.FinishAsync(first.Id, s.Student);
        var r = await Retakes(h).RequestAsync(a.Id, "Làm lại", s.Student);
        await Retakes(h).DecideAsync(r.Id, true, h.Clock.UtcNow.AddHours(4), s.Lecturer);
        var retake = await Retakes(h).StartAsync(r.Id, s.Student); var deadline = retake.DeadlineAt;
        await h.Assignments.ChangeExtraMinutesAsync(a.Id, s.Student, 25, "Bù giờ thường", s.Lecturer);
        Assert.Equal(deadline, retake.DeadlineAt);
        await h.Assignments.CloseAfter15MinutesAsync(a.Id, s.Lecturer);
        Assert.Equal(deadline, retake.DeadlineAt);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CongLuotChoPhepBatDauThemVaHienConLai(bool individual)
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(settings: new(null, null, 1));
        var first = await h.Attempts.StartAsync(s.Assignment.Id, s.Student); await h.Attempts.FinishAsync(first.Id, s.Student);
        await Assert.ThrowsAsync<BusinessException>(() => h.Attempts.StartAsync(s.Assignment.Id, s.Student));
        if (individual) await h.Assignments.ChangeExtraAttemptsAsync(s.Assignment.Id, s.Student, 1, "Luyện thêm", s.Lecturer);
        else await h.Assignments.ChangeMaxAttemptsAsync(s.Assignment.Id, 2, "Luyện thêm", s.Lecturer);
        var page = await h.Attempts.GetAssignmentPageAsync(s.Assignment.Id, s.Student);
        Assert.Equal(1, page.RemainingAttempts); Assert.True(page.CanStart);
        var notice = await h.Db.Notifications.SingleAsync(n => n.UserId == s.Student);
        Assert.Equal($"Bạn được thêm 1 lượt ở bài {s.Quiz.Title}", NotificationService.Payload(notice).Text);
        var second = await h.Attempts.StartAsync(s.Assignment.Id, s.Student); Assert.NotEqual(first.Id, second.Id);
        await h.Attempts.FinishAsync(second.Id, s.Student);
        // (D15) câu lỗi nói con số tối thiểu thay vì "không quá giới hạn cho phép".
        if (individual)
            Assert.Equal("Học viên này đã dùng 2 lượt, nên lượt cộng riêng phải từ 1 trở lên.",
                (await Assert.ThrowsAsync<BusinessException>(() => h.Assignments.ChangeExtraAttemptsAsync(s.Assignment.Id, s.Student, 0, "Giảm", s.Lecturer))).Message);
        else
            Assert.Equal("Đã có học viên dùng hết 2 lượt, nên số lượt tối đa phải từ 2 trở lên.",
                (await Assert.ThrowsAsync<BusinessException>(() => h.Assignments.ChangeMaxAttemptsAsync(s.Assignment.Id, 1, "Giảm", s.Lecturer))).Message);
        Assert.True(await h.Db.AssignmentChanges.AnyAsync(c => c.AssignmentId == s.Assignment.Id && c.Reason == "Luyện thêm"));
    }

    [Fact]
    public async Task CongLuotTuChoiKiemTraVaBatBuocLyDoVaQuyen()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync();
        var a = await h.Assignments.CreateTestAsync(s.Room.Id, s.Quiz.Id, new(null, 60), s.Lecturer);
        await Assert.ThrowsAsync<BusinessException>(() => h.Assignments.ChangeMaxAttemptsAsync(a.Id, 3, "Cộng", s.Lecturer));
        await Assert.ThrowsAsync<BusinessException>(() => h.Assignments.ChangeExtraAttemptsAsync(a.Id, s.Student, 1, "Cộng", s.Lecturer));
        await Assert.ThrowsAsync<BusinessException>(() => h.Assignments.ChangeMaxAttemptsAsync(s.Assignment.Id, 4, "", s.Lecturer));
        await Assert.ThrowsAsync<BusinessException>(() => h.Assignments.ChangeExtraAttemptsAsync(s.Assignment.Id, s.Student, 1, "", s.Lecturer));
        await Assert.ThrowsAsync<ForbiddenException>(() => h.Assignments.ChangeMaxAttemptsAsync(s.Assignment.Id, 4, "Lén", s.Student));
        await Assert.ThrowsAsync<ForbiddenException>(() => h.Assignments.ChangeExtraAttemptsAsync(s.Assignment.Id, s.Student, 1, "Lén", s.Student));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChuaBatDauVanDuocCongGioVaVaoTrongPhanGioRieng(bool test)
    {
        using var h = new Harness(); var s = await h.ScenarioAsync();
        var a = test ? await h.Assignments.CreateTestAsync(s.Room.Id, s.Quiz.Id, new(null, 30), s.Lecturer) : s.Assignment;
        if (test) await h.Assignments.StartTestAsync(a.Id, s.Lecturer);
        else await h.Assignments.ChangeDueAsync(a.Id, h.Clock.UtcNow.AddMinutes(30), "Hạn", s.Lecturer);
        await h.Assignments.ChangeExtraMinutesAsync(a.Id, s.Student, 20, "Đến muộn", s.Lecturer);
        h.Clock.UtcNow = h.Clock.UtcNow.AddMinutes(35);
        Assert.True((await h.Attempts.GetOverviewAsync(a.Id, s.Student)).CanStart);
        if (test) Assert.True((await h.Attempts.GetTestStatusAsync(a.Id, s.Student)).CanStart);
        var at = await h.Attempts.StartAsync(a.Id, s.Student);
        Assert.Equal(h.Clock.UtcNow.AddMinutes(15), at.DeadlineAt);
    }

    [Fact]
    public async Task HocVienChuaLamCungNhanThongBaoTangLuotVaGvPhuCoQuyen()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(); var assistant = await h.UserAsync("Phụ");
        h.Db.ClassMembers.Add(new() { ClassroomId = s.Room.Id, UserId = assistant, Role = ClassRole.AssistantLecturer,
            Status = ClassMemberStatus.Active, JoinedAt = h.Clock.UtcNow }); await h.Db.SaveChangesAsync();
        await h.Assignments.ChangeMaxAttemptsAsync(s.Assignment.Id, 4, "Thêm cho lớp", assistant);
        var notice = await h.Db.Notifications.SingleAsync(n => n.UserId == s.Student);
        Assert.Contains("thêm 1 lượt", NotificationService.Payload(notice).Text);
        await h.Assignments.ChangeExtraAttemptsAsync(s.Assignment.Id, s.Student, 2, "Thêm riêng", assistant);
        var p = await h.Db.Participants.SingleAsync(p => p.AssignmentId == s.Assignment.Id && p.UserId == s.Student);
        Assert.Equal(2, p.ExtraAttempts); Assert.Equal("Thêm riêng", p.ExtraAttemptsReason);
        Assert.Equal(6, (await h.Attempts.GetAssignmentPageAsync(s.Assignment.Id, s.Student)).RemainingAttempts);
    }

    private static Task<JoinCode> Change(Harness h, Scenario s, bool assignment, bool undo = false, string? user = null) => assignment
        ? h.Assignments.ChangeCodeAsync(s.Assignment.Id, user ?? s.Lecturer, undo)
        : h.Classrooms.ChangeCodeAsync(s.Room.Id, user ?? s.Lecturer, undo);

    [Theory]
    [InlineData(false, 48, true)]
    [InlineData(true, 48, true)]
    [InlineData(false, 49, false)]
    [InlineData(true, 49, false)]
    public async Task HoanTacTrong48GioVaTuChoiSau48Gio(bool assignment, int hours, bool allowed)
    {
        using var h = new Harness(); var s = await h.ScenarioAsync();
        var target = assignment ? JoinCodeTarget.Assignment : JoinCodeTarget.Classroom;
        var id = assignment ? s.Assignment.Id : s.Room.Id;
        var old = (await h.Codes.GetActiveAsync(target, id))!.Code;
        var fresh = await Change(h, s, assignment);
        h.Clock.UtcNow = h.Clock.UtcNow.AddHours(hours);
        if (allowed)
        {
            Assert.NotNull(await h.Codes.UndoUntilAsync(target, id));
            Assert.Equal(old, (await Change(h, s, assignment, true)).Code);
            Assert.Equal(old, (await h.Codes.ResolveAsync(old)).Code);
            await Assert.ThrowsAsync<BusinessException>(() => h.Codes.ResolveAsync(fresh.Code));
            Assert.Null(await h.Codes.UndoUntilAsync(target, id));
        }
        else
        {
            Assert.Null(await h.Codes.UndoUntilAsync(target, id));
            await Assert.ThrowsAsync<BusinessException>(() => Change(h, s, assignment, true));
            Assert.Equal(fresh.Code, (await h.Codes.ResolveAsync(fresh.Code)).Code);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HoanTacMotBuocNguoiDaVaoGiuNguyenVaChiGvDuocDoi(bool assignment)
    {
        using var h = new Harness(); var s = await h.ScenarioAsync();
        var previous = await Change(h, s, assignment); var newer = await Change(h, s, assignment);
        var learner = assignment ? s.Student : await h.UserAsync("Mới vào");
        await h.Classrooms.JoinByCodeAsync(newer.Code, learner);
        Attempt? at = assignment ? await h.Attempts.StartAsync(s.Assignment.Id, learner) : null;
        await Assert.ThrowsAsync<ForbiddenException>(() => Change(h, s, assignment, true, learner));
        Assert.Equal(previous.Code, (await Change(h, s, assignment, true)).Code);
        await Assert.ThrowsAsync<BusinessException>(() => Change(h, s, assignment, true));
        Assert.NotNull(await h.Classrooms.GetActiveMembershipAsync(s.Room.Id, learner));
        if (at != null) Assert.Equal(at.Id, (await h.Attempts.StartAsync(s.Assignment.Id, learner)).Id);
        await Assert.ThrowsAsync<BusinessException>(() => h.Codes.ResolveAsync(newer.Code));
        Assert.Equal(previous.Code, (await h.Codes.ResolveAsync(previous.Code)).Code);
        await Change(h, s, assignment);
        Assert.Equal(previous.Code, (await Change(h, s, assignment, true)).Code);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task QrChiGvVaUrlTuyetDoiChayQuaTrangJoin(bool assignment)
    {
        using var h = new Harness(); var s = await h.ScenarioAsync();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ApplicationName = typeof(AssignmentsController).Assembly.GetName().Name });
        builder.Services.AddControllersWithViews(); builder.Logging.SetMinimumLevel(LogLevel.Warning);
        await using var app = builder.Build(); app.MapDefaultControllerRoute(); using var scope = app.Services.CreateScope();
        app.Urls.Add("http://127.0.0.1:0"); await app.StartAsync();
        var ac = AsUser(Assignments(h), s.Lecturer, scope.ServiceProvider);
        var cc = AsUser(new ClassroomsController(h.Classrooms, h.Assignments, h.Codes, null!, null!, null!, null!), s.Lecturer, scope.ServiceProvider);
        var result = Assert.IsType<ViewResult>(assignment ? await ac.Qr(s.Assignment.Id) : await cc.Qr(s.Room.Id));
        var model = Assert.IsType<JoinQrModel>(result.Model);
        Assert.Equal($"https://quiz.test/Classrooms/Join?code={model.Code}", model.JoinUrl);
        Assert.Equal("https://quiz.test/Classrooms/Join", model.JoinPageUrl);
        var html = await Tang4ViewTests.Render("Shared", "JoinQr", model);
        Assert.Contains($"data-join-url=\"{model.JoinUrl}\"", html); Assert.Contains($"data-code=\"{model.Code}\"", html);
        ac = AsUser(Assignments(h), s.Student); cc = AsUser(cc, s.Student);
        var error = assignment ? await Assert.ThrowsAsync<ForbiddenException>(() => ac.Qr(s.Assignment.Id))
            : await Assert.ThrowsAsync<ForbiddenException>(() => cc.Qr(s.Room.Id));
        Is403(assignment ? ac : cc, error);
    }

    [Fact]
    public async Task GvPhuDuocDoiVaHoanTacCaHaiLoaiMa()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(); var assistant = await h.UserAsync("Phụ");
        h.Db.ClassMembers.Add(new() { ClassroomId = s.Room.Id, UserId = assistant, Role = ClassRole.AssistantLecturer,
            Status = ClassMemberStatus.Active, JoinedAt = h.Clock.UtcNow }); await h.Db.SaveChangesAsync();
        foreach (var assignment in new[] { false, true })
        {
            var target = assignment ? JoinCodeTarget.Assignment : JoinCodeTarget.Classroom;
            var old = (await h.Codes.GetActiveAsync(target, assignment ? s.Assignment.Id : s.Room.Id))!.Code;
            await Change(h, s, assignment, user: assistant);
            Assert.Equal(old, (await Change(h, s, assignment, true, assistant)).Code);
        }
    }

    [Fact]
    public async Task LinkDienSanVanQuaBoDemTurnstileVaChoDuyetLop()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(); var learner = await h.UserAsync("Chờ duyệt");
        s.Room.JoinPolicy = ClassJoinPolicy.RequireApproval; await h.Db.SaveChangesAsync();
        var turnstile = Tang5bPureTests.Turnstile();
        var guard = new JoinCodeGuardService(h.Db, h.Codes, h.Clock, turnstile);
        for (var i = 0; i < 10; i++)
            await Assert.ThrowsAsync<BusinessException>(() => guard.ResolveAsync("bad", learner, null, null));
        var controller = AsUser(new ClassroomsController(h.Classrooms, h.Assignments, h.Codes, guard, turnstile, null!, null!), learner);
        controller.Request.QueryString = new QueryString("?code=" + s.ClassCode);
        Assert.IsType<ViewResult>(await controller.Join());
        Assert.Equal(s.ClassCode, controller.ViewData["Code"]); Assert.Equal(true, controller.ViewData["ChallengeRequired"]);
        controller.Request.Form = new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>());
        Assert.IsType<ViewResult>(await controller.Join(s.ClassCode));
        Assert.False(controller.ModelState.IsValid);
        Assert.Null(await h.Classrooms.GetActiveMembershipAsync(s.Room.Id, learner));
        await guard.ResolveAsync(s.ClassCode, learner, "verified-by-fake", null);
        Assert.Equal(JoinOutcome.PendingApproval, (await h.Classrooms.JoinByCodeAsync(s.ClassCode, learner)).Outcome);
        var member = await h.Db.ClassMembers.SingleAsync(m => m.ClassroomId == s.Room.Id && m.UserId == learner);
        Assert.Equal(ClassMemberStatus.PendingApproval, member.Status);
    }

    [Fact]
    public async Task ChuaXoaLopKhiLuotVanConGioCongRieng()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(settings: new(null, null, 1));
        await h.Assignments.ChangeDueAsync(s.Assignment.Id, h.Clock.UtcNow.AddMinutes(30), "Hạn", s.Lecturer);
        await h.Attempts.StartAsync(s.Assignment.Id, s.Student);
        await h.Assignments.ChangeExtraMinutesAsync(s.Assignment.Id, s.Student, 20, "Mất điện", s.Lecturer);
        h.Clock.UtcNow = h.Clock.UtcNow.AddMinutes(35);
        await Assert.ThrowsAsync<BusinessException>(() => h.Classrooms.DeleteAsync(s.Room.Id, s.Lecturer));
        Assert.Null((await h.Db.Classrooms.AsNoTracking().SingleAsync(c => c.Id == s.Room.Id)).DeletedAt);
        await h.Assignments.CloseNowAsync(s.Assignment.Id, s.Lecturer);
        await h.Classrooms.DeleteAsync(s.Room.Id, s.Lecturer);
        Assert.NotNull((await h.Db.Classrooms.AsNoTracking().SingleAsync(c => c.Id == s.Room.Id)).DeletedAt);
    }
}

internal sealed class AdjustmentRecorder(Harness h, int assignmentId, string learner, long attemptId) : ILearnNotifier
{
    public List<string> Events { get; } = [];
    private async Task Committed(string name)
    {
        Assert.Null(h.Db.Database.CurrentTransaction);
        using var fresh = DatabaseFixture.NewContext(); fresh.Database.SetCommandTimeout(3);
        var at = await fresh.Attempts.Include(a => a.Participant).ThenInclude(p => p.Assignment).SingleAsync(a => a.Id == attemptId);
        Assert.Equal(25, at.Participant.ExtraMinutes);
        Assert.Equal(AttemptRules.DeadlineFor(at.Participant.Assignment, at.Participant), at.DeadlineAt);
        Assert.True(await fresh.Notifications.AnyAsync(n => n.UserId == learner));
        Events.Add(name);
    }
    public Task AttemptAsync(long id, string eventName, LearnNotice notice)
    {
        Assert.Equal(attemptId, id); Assert.Equal("deadline-changed", eventName);
        return Committed(eventName);
    }
    public Task NotificationAsync(string userId, NotificationNotice notice)
    {
        Assert.Equal(learner, userId); Assert.Equal(1, notice.Unread);
        return Committed("notification");
    }
    public Task LecturersAsync(int id, int? recheckAfterSeconds = null)
    {
        Assert.Equal(assignmentId, id); return Committed("lecturers-changed");
    }
    public Task TestStartedAsync(int id) => Task.CompletedTask;
    public Task MovedAsync(long id, IReadOnlyList<string> oldConnections) => Task.CompletedTask;
}
