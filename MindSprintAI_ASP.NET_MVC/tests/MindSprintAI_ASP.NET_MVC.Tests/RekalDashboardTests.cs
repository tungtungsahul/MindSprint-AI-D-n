using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MindSprintAI_ASP.NET_MVC.Controllers;
using MindSprintAI_ASP.NET_MVC.Data.Entities;
using MindSprintAI_ASP.NET_MVC.Services;

namespace MindSprintAI_ASP.NET_MVC.Tests;

[Collection("db")]
public class RekalDashboardTests
{
    [Fact]
    public async Task SoDangLamKhongGomNguoiTamDungMatKetNoiHoacHetHan()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(1);
        foreach (var state in new[] { "working", "paused", "disconnected", "expired", "ended" })
        {
            var p = new Participant { Assignment = s.Assignment, UserId = await h.UserAsync(state) };
            h.Db.Attempts.Add(new() { Participant = p, AttemptNumber = 1,
                Status = state == "paused" ? AttemptStatus.Paused : state == "ended" ? AttemptStatus.Ended : AttemptStatus.InProgress,
                DisconnectedAt = state == "disconnected" ? h.Clock.UtcNow.AddSeconds(-31) : null,
                DeadlineAt = state == "expired" ? h.Clock.UtcNow : h.Clock.UtcNow.AddHours(1) });
        }
        await h.Db.SaveChangesAsync();
        var item = Assert.Single((await h.Assignments.GetLecturerDashboardAsync(s.Lecturer, "Lan")).Open);
        Assert.Equal(1, item.Working); Assert.Equal(1, item.Submitted);
    }

    [Fact]
    public async Task TrangChuGiangVienVaHocVienGiuDungDich()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(1);
        using var users = new UserManager<ApplicationUser>(new UserStore<ApplicationUser>(h.Db), Options.Create(new IdentityOptions()),
            new PasswordHasher<ApplicationUser>(), [], [], new UpperInvariantLookupNormalizer(), new IdentityErrorDescriber(), null!, NullLogger<UserManager<ApplicationUser>>.Instance);
        var user = await users.FindByIdAsync(s.Lecturer); user!.CurrentMode = UserMode.Lecturer; await h.Db.SaveChangesAsync();
        var controller = new HomeController(users) { ControllerContext = new() { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, s.Lecturer)], "test")) } } };
        // MindSprint 15/11 (người dùng chốt): một trang chủ kiểu nhóm cho cả hai chế độ, số liệu theo chế độ đang dùng.
        var home = new HomeService(h.Assignments, h.Attempts, h.Library, new KanbanService(h.Db), new StudyStatsService(h.Db, h.Clock), h.Clock);
        var lecturerView = Assert.IsType<HomeDashboard>(Assert.IsType<ViewResult>(await controller.Index(home)).Model);
        Assert.True(lecturerView.Lecturer);
        Assert.Single(lecturerView.Teaching!.Open);
        Assert.Empty(lecturerView.Todo);
        user.CurrentMode = UserMode.Learner; await h.Db.SaveChangesAsync();
        var learnerView = Assert.IsType<HomeDashboard>(Assert.IsType<ViewResult>(await controller.Index(home)).Model);
        Assert.False(learnerView.Lecturer);
        Assert.Null(learnerView.Teaching);
        // Người chưa đăng nhập: cùng trang, bản cho khách (không model).
        var anonymous = new HomeController(users) { ControllerContext = new() { HttpContext = new DefaultHttpContext() } };
        Assert.Null(Assert.IsType<ViewResult>(await anonymous.Index(home)).Model);
    }

    [Fact]
    public async Task TrangChuHocVienChiDemBaiConLamDuocVaViecChuaXong()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(1);
        var student = await h.Db.Users.FindAsync(s.Student);
        var kanban = new KanbanService(h.Db);
        await kanban.CreateAsync(s.Student, new("Đang làm", null, null, null, KanbanStatus.Doing));
        await kanban.CreateAsync(s.Student, new("Chưa làm", null, null));
        await kanban.CreateAsync(s.Student, new("Xong", null, null, null, KanbanStatus.Done));
        var view = await new HomeService(h.Assignments, h.Attempts, h.Library, kanban, new StudyStatsService(h.Db, h.Clock), h.Clock).GetAsync(student!);
        Assert.Equal(s.Assignment.Id, Assert.Single(view.Todo).Id);
        Assert.Equal(["Đang làm", "Chưa làm"], view.Tasks.Select(t => t.Title));  // việc đang làm lên trước, việc xong không hiện
        Assert.Equal((3, 1, 33), (view.TasksTotal, view.TasksDone, view.TaskPercent));
        // Bài đã quá hạn hoặc đã hoàn thành không mời làm nữa (cùng quy tắc "Cần làm" của Bài của tôi).
        var past = new Assignment { Id = 1, Mode = AssignmentMode.Practice, DueAt = h.Clock.UtcNow.AddMinutes(-1) };
        var done = new Assignment { Id = 2, Mode = AssignmentMode.Practice };
        var open = new Assignment { Id = 3, Mode = AssignmentMode.Practice };
        var todo = HomeService.LearnerTodo([past, done, open], [new Participant { AssignmentId = 2, CompletionStatus = CompletionStatus.Completed }], h.Clock.UtcNow);
        Assert.Equal(3, Assert.Single(todo).Id);
    }

    [Fact]
    public async Task ChiBaiCuaMinhDangMoVaToiDaNamBai()
    {
        using var h = new Harness(); var own = await h.ScenarioAsync(1); var other = await h.ScenarioAsync(1);
        for (var i = 0; i < 7; i++)
            await h.Assignments.CreatePracticeAsync(own.Room.Id, own.Quiz.Id, new(null, null, 3), own.Lecturer);
        var view = await h.Assignments.GetLecturerDashboardAsync(own.Lecturer, "Lan");
        Assert.Equal(5, view.Open.Count);
        Assert.DoesNotContain(view.Open, x => x.Id == other.Assignment.Id);
        var outsider = await h.UserAsync("Khác");
        Assert.Empty((await h.Assignments.GetLecturerDashboardAsync(outsider, "Khác")).Open);
        Assert.Empty((await h.Assignments.GetLecturerDashboardAsync(own.Student, "Minh")).Open);
    }
}

public class RekalDashboardPureTests
{
    [Theory]
    [InlineData(22, "Chào buổi sáng")]
    [InlineData(5, "Chào buổi chiều")]
    [InlineData(11, "Chào buổi tối")]
    [InlineData(21, "Chào buổi tối")]
    public void LoiChaoTheoGioVietNam(int utcHour, string expected) =>
        Assert.Equal(expected, AssignmentService.LecturerGreeting(new DateTime(2026, 11, 9, utcHour, 0, 0, DateTimeKind.Utc)));
}
