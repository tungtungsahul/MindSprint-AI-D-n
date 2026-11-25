using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using MindSprintAI_ASP.NET_MVC.Controllers;
using MindSprintAI_ASP.NET_MVC.Data.Entities;
using MindSprintAI_ASP.NET_MVC.Models;
using MindSprintAI_ASP.NET_MVC.Services;

namespace MindSprintAI_ASP.NET_MVC.Tests;

public class Tang4dPureTests
{
    [Theory]
    [InlineData(null), InlineData(31)]
    public async Task PayloadChiCoMaBaiVaMocKiemTraLai(int? seconds)
    {
        var hub = new TestLearnHub();
        await new SignalRLearnNotifier(hub).LecturersAsync(12, seconds);
        Assert.Equal(("lecturers-changed", "group:assignment-12-lecturers"), Assert.Single(hub.Sent));
        var payload = Assert.Single(Assert.Single(hub.Payloads));
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.Equal(["assignmentId", "recheckAfterSeconds"], doc.RootElement.EnumerateObject().Select(p => p.Name));
        Assert.Equal(12, doc.RootElement.GetProperty("assignmentId").GetInt32());
        Assert.Equal(seconds, doc.RootElement.GetProperty("recheckAfterSeconds").Deserialize<int?>());
    }

    [Fact]
    public void BoGioRiengMacDinhTat()
    {
        // MindSprint gộp migration Tang4d vào KhoiTao: chỉ còn kiểm mặc định.
        Assert.False(new Assignment().IgnoreQuestionTimers);
        Assert.False(new PracticeSettings(null, null, null).IgnoreQuestionTimers);
        Assert.False(new TestSettings(null, 45).IgnoreQuestionTimers);
    }

    [Theory]
    [InlineData(false), InlineData(true)]
    public async Task PartialChiCoBangVaPhongChoMaHoaTen(bool waiting)
    {
        var a = new Assignment { Id = 12, Mode = waiting ? AssignmentMode.Test : AssignmentMode.Practice };
        var model = new AssignmentDetailsModel
        {
            Assignment = a, Code = "KHONG_DUOC_HIEN", OpenAttempts = 0,
            Rows = [new("user", "<script>alert(1)</script>", CompletionStatus.InProgress, CompletionLabels.None,
                1, null, 1, Progress: "1/3 câu đã trả lời")],
            Waiting = [new() { User = new ApplicationUser { DisplayName = "<img src=x onerror=alert(1)>" } }]
        };
        var html = await Tang4ViewTests.Render("Assignments", "_LiveTable", model);
        Assert.Contains("Tiến độ", WebUtility.HtmlDecode(html));
        Assert.Contains("1/3 câu đã trả lời", WebUtility.HtmlDecode(html));
        Assert.DoesNotContain("<script>", html); Assert.DoesNotContain("<img src=x", html);
        Assert.DoesNotContain("KHONG_DUOC_HIEN", html); Assert.DoesNotContain("aria-live", html);
        Assert.DoesNotContain("Tải lại trang", WebUtility.HtmlDecode(html));
        Assert.Equal(waiting, WebUtility.HtmlDecode(html).Contains("Phòng chờ"));
        if (waiting) Assert.Contains("__RequestVerificationToken", html);
    }

    [Theory]
    [InlineData(false), InlineData(true)]
    public async Task DetailsDungPartialVaKhoaSettingKemLyDo(bool editable)
    {
        var model = new AssignmentDetailsModel
        {
            Assignment = new Assignment { Id = 12, Mode = AssignmentMode.Test, IgnoreQuestionTimers = true },
            Code = "12345678", OpenAttempts = 0, Rows = [], CanEditMastery = editable
        };
        var html = await Tang4ViewTests.Render("Assignments", "Details", model);
        var decoded = WebUtility.HtmlDecode(html);
        Assert.Contains("Bỏ giờ riêng từng câu trong bài giao này", decoded);
        Assert.Equal(!editable, html.Contains("fieldset disabled"));
        Assert.Equal(!editable, decoded.Contains("cài đặt thời gian từng câu bị khóa"));
        Assert.Contains("id=\"assignment-live\"", html); Assert.Contains("aria-live=\"polite\"", html);
        Assert.DoesNotContain("Tải lại trang để cập nhật", decoded);
        Assert.Equal(1, decoded.Split("Bảng điểm").Length - 1);
    }

    [Fact]
    public async Task CreateHienSettingChoCaHaiCheDo()
    {
        var html = await Tang4ViewTests.Render("Assignments", "Create", new AssignmentCreateModel
        {
            Classroom = new Classroom { Id = 1 }, ReadyQuizzes = [], StudySets = [new() { Id = 1 }],
            Form = new AssignmentInput()
        });
        var setting = html[html.IndexOf("id=\"IgnoreQuestionTimers\"", StringComparison.Ordinal)..];
        Assert.StartsWith("id=\"IgnoreQuestionTimers\" name=\"IgnoreQuestionTimers\" value=\"true\"", setting);
        Assert.DoesNotContain("checked", setting[..setting.IndexOf("/>", StringComparison.Ordinal)]);
        Assert.Contains("Bỏ giờ riêng từng câu trong bài giao này", WebUtility.HtmlDecode(setting));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CreateChonBaiTruocCaiDatThemThuGon(bool hasReadyQuiz)
    {
        var html = WebUtility.HtmlDecode(await Tang4ViewTests.Render("Assignments", "Create", new AssignmentCreateModel
        {
            Classroom = new Classroom { Id = 1 }, ReadyQuizzes = hasReadyQuiz ? [new() { Id = 1, Title = "Bài 1" }] : [],
            StudySets = [new() { Id = 1 }], Form = new AssignmentInput()
        }));
        int At(string s) => html.IndexOf(s, StringComparison.Ordinal);
        // GV chọn bài trước, đặt giờ sau; giám sát máy làm bài nằm trong mục "Cài đặt thêm" thu gọn.
        Assert.True(At("id=\"Mode\"") < At("id=\"QuizId\"") && At("id=\"QuizId\"") < At("id=\"DueAt\""));
        var more = html[At("<details class=\"col-12 b-more-settings\"")..At("</details>")];
        Assert.DoesNotContain(" open", more[..more.IndexOf('>')]);
        foreach (var field in new[] { "id=\"ScoreScale\"", "id=\"ShuffleQuestions\"", "id=\"MonitorScreen\"" })
            Assert.Contains(field, more);
        Assert.Equal(!hasReadyQuiz, html.Contains("Chưa có quiz nào sẵn sàng giao"));
    }
}

[Collection("db")]
public class Tang4dTests
{
    private static LearnConnectionService Connections(Harness h, TestLearnHub hub) =>
        new(h.Db, h.Attempts, h.Clock, h.Connections, hub, h.Classrooms, h.Notifier);
    private static RetakeService Retakes(Harness h) => new(h.Db, h.Assignments, h.Attempts, h.Clock);
    private static AssignmentsController Controller(Harness h, string user) => new(h.Assignments, h.Classrooms,
        h.Library, h.Gradebook, h.Codes, Retakes(h), Tang4DbTests.Surveys(h), h.Regrades,
        new(h.Db, h.Clock), new(h.Db, h.Classrooms, h.Clock))
    {
        ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext
        { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user)], "test")) } }
    };

    [Theory]
    [InlineData("main", true), InlineData("assistant", true), InlineData("student", false)]
    [InlineData("other-class", false), InlineData("removed-assistant", false)]
    public async Task JoinLecturerChiGiangVienDangHoatDongCuaLop(string role, bool allowed)
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(questions: 1);
        var user = role == "main" ? s.Lecturer : role == "student" ? s.Student : await h.UserAsync("GV phụ");
        if (role is "assistant" or "removed-assistant")
        {
            h.Db.ClassMembers.Add(new ClassMember { ClassroomId = s.Room.Id, UserId = user,
                Role = ClassRole.AssistantLecturer, Status = role == "assistant" ? ClassMemberStatus.Active : ClassMemberStatus.Removed });
            await h.Db.SaveChangesAsync();
        }
        if (role == "other-class") await h.Classrooms.CreateAsync("Lớp khác", ClassJoinPolicy.Immediate, user);
        var hub = new TestLearnHub(); var connections = Connections(h, hub);
        if (allowed)
        {
            await connections.JoinLecturerAsync("gv", s.Assignment.Id, user);
            Assert.Equal([("gv", $"assignment-{s.Assignment.Id}-lecturers")], hub.Joined);
        }
        else
        {
            await Assert.ThrowsAsync<ForbiddenException>(() => connections.JoinLecturerAsync("gv", s.Assignment.Id, user));
            Assert.Empty(hub.Joined);
        }
    }

    [Theory]
    [InlineData("start"), InlineData("answer"), InlineData("pause"), InlineData("finish")]
    [InlineData("close"), InlineData("remove"), InlineData("leave"), InlineData("disconnect")]
    [InlineData("reconnect"), InlineData("deadline"), InlineData("close-15")]
    public async Task BaoGiangVienSauCommit(string action)
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(questions: 2);
        Attempt? a = action == "start" ? null : await h.Attempts.StartAsync(s.Assignment.Id, s.Student);
        var connections = Connections(h, new TestLearnHub());
        if (action == "disconnect") await connections.JoinAttemptAsync("hv", a!.Id, s.Student, h.Device.Id);
        var fake = new RecordingLearnNotifier(); h.Notifier = fake;
        // (E) đọc bằng connection khác: gửi trước commit sẽ bị khóa hoặc không thấy thay đổi.
        fake.OnLecturers = async notice =>
        {
            using var fresh = DatabaseFixture.NewContext(); fresh.Database.SetCommandTimeout(3);
            var assignment = await fresh.Assignments.SingleAsync(x => x.Id == notice.AssignmentId);
            var attempt = await fresh.Attempts.Include(x => x.Questions).SingleOrDefaultAsync(x => x.Participant.AssignmentId == notice.AssignmentId);
            Assert.NotNull(attempt);
            if (action == "answer") Assert.Equal(1, attempt.Questions.Count(q => q.AnswerCount > 0));
            if (action == "pause") Assert.Equal(AttemptStatus.Paused, attempt.Status);
            if (action is "finish" or "close" or "remove" or "leave") Assert.Equal(AttemptStatus.Ended, attempt.Status);
            if (action == "disconnect") Assert.NotNull(attempt.DisconnectedAt);
            if (action == "reconnect") Assert.Null(attempt.DisconnectedAt);
            if (action == "close") Assert.Equal(AssignmentStatus.Closed, assignment.Status);
            if (action is "deadline" or "close-15") Assert.Equal(h.Clock.UtcNow.AddMinutes(action == "deadline" ? 20 : 15), assignment.DueAt);
            if (action is "remove" or "leave") Assert.Equal(action == "remove" ? ClassMemberStatus.Removed : ClassMemberStatus.Left,
                (await fresh.ClassMembers.SingleAsync(x => x.ClassroomId == s.Room.Id && x.UserId == s.Student)).Status);
        };
        switch (action)
        {
            case "start": await h.Attempts.StartAsync(s.Assignment.Id, s.Student); break;
            case "answer": await Tang4DbTests.Answer(h, a!, a!.Questions[0], s.Student); break;
            case "pause": await h.Attempts.PauseAsync(a!.Id, s.Student); break;
            case "finish": await h.Attempts.FinishAsync(a!.Id, s.Student); break;
            case "close": await h.Assignments.CloseNowAsync(s.Assignment.Id, s.Lecturer); break;
            case "remove":
                var member = await h.Db.ClassMembers.SingleAsync(x => x.ClassroomId == s.Room.Id && x.UserId == s.Student);
                await h.Classrooms.RemoveMemberAsync(member.Id, s.Lecturer, true); break;
            case "leave": await h.Classrooms.LeaveAsync(s.Room.Id, s.Student, true); break;
            case "disconnect": await Connections(h, new TestLearnHub()).DisconnectAsync("hv"); break;
            case "reconnect": await Connections(h, new TestLearnHub()).JoinAttemptAsync("hv", a!.Id, s.Student, h.Device.Id); break;
            case "deadline": await h.Assignments.ChangeDueAsync(s.Assignment.Id, h.Clock.UtcNow.AddMinutes(20), null, s.Lecturer); break;
            case "close-15": await h.Assignments.CloseAfter15MinutesAsync(s.Assignment.Id, s.Lecturer); break;
        }
        Assert.Equal(new LecturerNotice(s.Assignment.Id, action == "disconnect" ? 31 : null), Assert.Single(fake.LecturerEvents));
    }

    [Fact]
    public async Task SaiThietBiKhongPhatSuKien()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(questions: 1);
        var a = await h.Attempts.StartAsync(s.Assignment.Id, s.Student);
        var fake = new RecordingLearnNotifier(); h.Notifier = fake; h.Device.Id = "may-khac";
        await Assert.ThrowsAsync<BusinessException>(() => Tang4DbTests.Answer(h, a, a.Questions[0], s.Student));
        Assert.Empty(fake.LecturerEvents);
        Assert.Equal(0, (await h.Db.AttemptQuestions.AsNoTracking().SingleAsync(q => q.Id == a.Questions[0].Id)).AnswerCount);
    }

    [Fact]
    public async Task LiveHienTienDoVaTamDungSau31Giay()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(questions: 3);
        var a = await h.Attempts.StartAsync(s.Assignment.Id, s.Student);
        var connection = Connections(h, new TestLearnHub());
        await connection.JoinAttemptAsync("hv", a.Id, s.Student, h.Device.Id);
        await Tang4DbTests.Answer(h, a, a.Questions[0], s.Student);
        async Task Check(string status, string progress)
        {
            var result = Assert.IsType<PartialViewResult>(await Controller(h, s.Lecturer).Live(s.Assignment.Id));
            Assert.Equal("_LiveTable", result.ViewName);
            var html = WebUtility.HtmlDecode(await Tang4ViewTests.Render("Assignments", "_LiveTable", result.Model!));
            Assert.Contains(status, html); Assert.Contains(progress, html);
        }
        await Check("Đang làm", "1/3 câu đã trả lời");
        await connection.DisconnectAsync("hv");
        h.Clock.UtcNow = h.Clock.UtcNow.AddSeconds(30); await Check("Đang làm", "1/3 câu đã trả lời");
        h.Clock.UtcNow = h.Clock.UtcNow.AddSeconds(1); await Check("Tạm dừng", "1/3 câu đã trả lời");
        await connection.JoinAttemptAsync("hv-moi", a.Id, s.Student, h.Device.Id);
        await Check("Đang làm", "1/3 câu đã trả lời");
        await h.Attempts.FinishAsync(a.Id, s.Student);
        var (_, rows) = await h.Gradebook.GetReportAsync(s.Assignment.Id, s.Lecturer);
        Assert.Null(Assert.Single(rows).Progress);
    }

    [Fact]
    public async Task LiveTra403ChoNguoiKhongPhaiGiangVien()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync();
        var controller = Controller(h, s.Student);
        var exception = await Assert.ThrowsAsync<ForbiddenException>(() => controller.Live(s.Assignment.Id));
        var action = new ActionContext(controller.HttpContext, new Microsoft.AspNetCore.Routing.RouteData(), new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor());
        var context = new ExceptionContext(action, []) { Exception = exception };
        new BusinessExceptionFilter(null!).OnException(context);
        Assert.True(context.ExceptionHandled); Assert.Equal(403, Assert.IsType<ViewResult>(context.Result).StatusCode);
    }

    [Theory]
    [InlineData(false), InlineData(true)]
    public async Task BoGioSnapshotMoCauTraLoiKhongChayDongHo(bool test)
    {
        using var h = new Harness(); var s = await Tang4DbTests.Mixed(h, choices: 2, input: false, surveys: false, threshold: null, test: test);
        foreach (var q in s.Quiz.Questions) q.TimeLimitSeconds = 5;
        await h.Db.SaveChangesAsync();
        await h.Assignments.ChangeQuestionTimersAsync(s.Assignment.Id, true, s.Lecturer);
        var a = await h.Attempts.StartAsync(s.Assignment.Id, s.Student);
        Assert.All(a.Questions, q => Assert.Null(QuestionSnapshot.FromJson(q.SnapshotJson).TimeLimitSeconds));
        Assert.All(AttemptService.ProjectQuestions(a, false), q => Assert.False(q.BodyHidden));
        var opened = await h.Attempts.OpenQuestionAsync(a.Id, a.Questions[0].Id, s.Student);
        Assert.Null(opened.SecondsLeft); Assert.False(opened.Question.BodyHidden);
        h.Clock.UtcNow = h.Clock.UtcNow.AddSeconds(40);
        await Tang4DbTests.Answer(h, a, a.Questions[0], s.Student);
        await Tang4DbTests.Answer(h, a, a.Questions[1], s.Student); // câu không cần OpenQuestion.
        Assert.All(a.Questions, q => { Assert.Null(q.TimerStartedAt); Assert.Null(q.TimeLeftSeconds); Assert.Equal(1, q.AnswerCount); });
        // (E, lựa chọn B) Kiểm tra khóa sau lượt đầu; Ôn tập đổi được nhưng lượt đã mở giữ bản chụp cũ.
        if (test) await Assert.ThrowsAsync<BusinessException>(() => h.Assignments.ChangeQuestionTimersAsync(s.Assignment.Id, false, s.Lecturer));
        else await h.Assignments.ChangeQuestionTimersAsync(s.Assignment.Id, false, s.Lecturer);
        Assert.Equal(test, (await h.Db.Assignments.AsNoTracking().SingleAsync(x => x.Id == s.Assignment.Id)).IgnoreQuestionTimers);
        Assert.All(await h.Db.AttemptQuestions.AsNoTracking().Where(q => q.AttemptId == a.Id).ToListAsync(),
            q => Assert.Null(QuestionSnapshot.FromJson(q.SnapshotJson).TimeLimitSeconds));
    }

    [Fact]
    public async Task LuotCuVanGiuGioVaHuyLuotVanKhongDuocDoiSetting()
    {
        using var h = new Harness(); var s = await Tang4DbTests.Mixed(h, choices: 1, input: false, surveys: false, test: true);
        s.Quiz.Questions[0].TimeLimitSeconds = 10; await h.Db.SaveChangesAsync();
        var a = await h.Attempts.StartAsync(s.Assignment.Id, s.Student);
        await Assert.ThrowsAsync<BusinessException>(() => h.Assignments.ChangeQuestionTimersAsync(s.Assignment.Id, true, s.Lecturer));
        Assert.Equal(10, QuestionSnapshot.FromJson(a.Questions[0].SnapshotJson).TimeLimitSeconds);
        await Retakes(h).CancelAsync(a.Id, "Thử hủy", s.Lecturer);
        await Assert.ThrowsAsync<BusinessException>(() => h.Assignments.ChangeQuestionTimersAsync(s.Assignment.Id, true, s.Lecturer));
    }

    [Theory]
    [InlineData(false), InlineData(true)]
    public async Task GiaoBaiLuuSettingBoGioChoCaHaiCheDo(bool test)
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(questions: 1);
        var a = test
            ? await h.Assignments.CreateTestAsync(s.Room.Id, s.Quiz.Id, new TestSettings(null, 45, IgnoreQuestionTimers: true), s.Lecturer)
            : await h.Assignments.CreatePracticeAsync(s.Room.Id, s.Quiz.Id, new PracticeSettings(null, null, null, IgnoreQuestionTimers: true), s.Lecturer);
        Assert.True((await h.Db.Assignments.AsNoTracking().SingleAsync(x => x.Id == a.Id)).IgnoreQuestionTimers);
    }

    [Fact]
    public async Task HocVienKhongDoiDuocSettingGioRieng()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(questions: 1);
        await Assert.ThrowsAsync<ForbiddenException>(() => h.Assignments.ChangeQuestionTimersAsync(s.Assignment.Id, true, s.Student));
        Assert.False((await h.Db.Assignments.AsNoTracking().SingleAsync(x => x.Id == s.Assignment.Id)).IgnoreQuestionTimers);
    }

    [Fact]
    public async Task PhongChoTaoParticipantVaJoinDeuBaoSauCommit()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(questions: 1);
        var a = await h.Assignments.CreateTestAsync(s.Room.Id, s.Quiz.Id, new TestSettings(null, 45), s.Lecturer);
        var fake = new RecordingLearnNotifier(); h.Notifier = fake;
        fake.OnLecturers = async _ =>
        {
            using var fresh = DatabaseFixture.NewContext(); fresh.Database.SetCommandTimeout(3);
            Assert.True(await fresh.Participants.AnyAsync(p => p.AssignmentId == a.Id && p.UserId == s.Student));
        };
        await h.Attempts.GetAssignmentPageAsync(a.Id, s.Student);
        await Connections(h, new TestLearnHub()).JoinWaitingAsync("cho", a.Id, s.Student);
        Assert.Equal(2, fake.LecturerEvents.Count);
        Assert.Single(await h.Assignments.GetWaitingAsync(a.Id, s.Lecturer));
        await h.Classrooms.LeaveAsync(s.Room.Id, s.Student);
        Assert.Empty(await h.Assignments.GetWaitingAsync(a.Id, s.Lecturer));
    }

    [Theory]
    [InlineData(false), InlineData(true)]
    public async Task LazyHetHanVaTimeoutOnTapBaoTienDoSauCommit(bool wholeAttempt)
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(questions: 1);
        s.Quiz.Questions[0].TimeLimitSeconds = 5; await h.Db.SaveChangesAsync();
        var a = await h.Attempts.StartAsync(s.Assignment.Id, s.Student);
        await h.Attempts.OpenQuestionAsync(a.Id, a.Questions[0].Id, s.Student);
        var fake = new RecordingLearnNotifier(); h.Notifier = fake;
        fake.OnLecturers = async _ =>
        {
            using var fresh = DatabaseFixture.NewContext(); fresh.Database.SetCommandTimeout(3);
            var saved = await fresh.Attempts.Include(x => x.Questions).SingleAsync(x => x.Id == a.Id);
            if (wholeAttempt) Assert.Equal(AttemptStatus.Ended, saved.Status);
            else Assert.Equal(1, Assert.Single(saved.Questions).AnswerCount);
        };
        h.Clock.UtcNow = wholeAttempt ? a.DeadlineAt!.Value.AddSeconds(1) : h.Clock.UtcNow.AddSeconds(8);
        await h.Attempts.GetStateAsync(a.Id, s.Student);
        Assert.Single(fake.LecturerEvents);
        await h.Attempts.GetStateAsync(a.Id, s.Student);
        Assert.Single(fake.LecturerEvents); // không tạo vòng thông báo khi đọc lại bảng/trạng thái.
    }

    [Theory]
    [InlineData(false), InlineData(true)]
    public async Task DuyetTuChoiHuyVaDungLuotLamLaiDeuBao(bool approve)
    {
        using var h = new Harness(); var s = await Tang4DbTests.Mixed(h, choices: 1, input: false, surveys: false, test: true);
        s.Quiz.Questions[0].TimeLimitSeconds = 5;
        await h.Db.SaveChangesAsync();
        await h.Assignments.ChangeQuestionTimersAsync(s.Assignment.Id, true, s.Lecturer);
        var a = await h.Attempts.StartAsync(s.Assignment.Id, s.Student);
        await h.Attempts.FinishAsync(a.Id, s.Student);
        var r = await Retakes(h).RequestAsync(s.Assignment.Id, "Xin làm lại", s.Student);
        var fake = new RecordingLearnNotifier(); h.Notifier = fake;
        await Retakes(h).DecideAsync(r.Id, approve, h.Clock.UtcNow.AddHours(1), s.Lecturer);
        Assert.Single(fake.LecturerEvents);
        if (!approve) return;
        var retake = await Retakes(h).StartAsync(r.Id, s.Student);
        Assert.Equal(2, fake.LecturerEvents.Count);
        Assert.All(retake.Questions, q => Assert.Null(QuestionSnapshot.FromJson(q.SnapshotJson).TimeLimitSeconds));
        await Retakes(h).CancelAsync(retake.Id, "Thử hủy", s.Lecturer);
        Assert.Equal(3, fake.LecturerEvents.Count);
    }

    [Fact]
    public async Task GiangVienPhuMoDuocTrangChiTietVaKhaoSat()
    {
        // (48A) hồi quy: SurveyService từng chỉ cho người tạo bài giao, làm GV phụ bị 403 cả trang chi tiết.
        using var h = new Harness(); var s = await h.ScenarioAsync(questions: 1);
        var assistant = await h.UserAsync("GV phụ");
        h.Db.ClassMembers.Add(new ClassMember { ClassroomId = s.Room.Id, UserId = assistant,
            Role = ClassRole.AssistantLecturer, Status = ClassMemberStatus.Active });
        await h.Db.SaveChangesAsync();
        Assert.IsType<ViewResult>(await Controller(h, assistant).Details(s.Assignment.Id));
        Assert.IsType<PartialViewResult>(await Controller(h, assistant).Live(s.Assignment.Id));
        await Tang4DbTests.Surveys(h).GetForLecturerAsync(s.Assignment.Id, assistant);
        await Assert.ThrowsAsync<ForbiddenException>(() => Tang4DbTests.Surveys(h).GetForLecturerAsync(s.Assignment.Id, s.Student));
    }
}
