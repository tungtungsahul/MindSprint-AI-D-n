using MindSprintAI_ASP.NET_MVC.Data.Entities;
using MindSprintAI_ASP.NET_MVC.Services;

namespace MindSprintAI_ASP.NET_MVC.Tests;

[Collection("db")]
public class RekalReportsTests
{
    [Fact]
    public async Task LocTrangThaiTimLopPhanTrangVaQuyen()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(1); var other = await h.ScenarioAsync(1);
        for (var i = 0; i < 22; i++) h.Db.Assignments.Add(new()
        {
            ClassroomId = s.Room.Id, CreatedById = s.Lecturer, QuizId = s.Quiz.Id,
            Status = i == 0 ? AssignmentStatus.Closed : AssignmentStatus.Open,
            OpensAt = i == 1 ? h.Clock.UtcNow.AddDays(1) : null, CreatedAt = h.Clock.UtcNow.AddMinutes(i)
        });
        await h.Db.SaveChangesAsync();
        var all = await h.Assignments.GetReportPageAsync(s.Lecturer);
        Assert.Equal(23, all.Total); Assert.Equal(20, all.Items.Count); Assert.Equal(2, all.Pages);
        var second = await h.Assignments.GetReportPageAsync(s.Lecturer, page: 2);
        Assert.Equal(3, second.Items.Count); Assert.Empty(all.Items.Select(i => i.Id).Intersect(second.Items.Select(i => i.Id)));
        Assert.Equal(1, (await h.Assignments.GetReportPageAsync(s.Lecturer, status: "closed")).Total);
        Assert.Equal(1, (await h.Assignments.GetReportPageAsync(s.Lecturer, status: "upcoming")).Total);
        Assert.Equal(21, (await h.Assignments.GetReportPageAsync(s.Lecturer, status: "open")).Total);
        Assert.Equal(23, (await h.Assignments.GetReportPageAsync(s.Lecturer, q: "Quiz 1")).Total);
        Assert.Empty((await h.Assignments.GetReportPageAsync(s.Lecturer, q: "Không có tên này")).Items);
        Assert.Empty((await h.Assignments.GetReportPageAsync(s.Lecturer, classId: other.Room.Id)).Items);
        Assert.Empty((await h.Assignments.GetReportPageAsync(s.Student)).Items);
        Assert.DoesNotContain(all.Items, x => x.Id == other.Assignment.Id);
        Assert.Equal(2, (await h.Assignments.GetReportPageAsync(s.Lecturer, page: int.MaxValue)).Page);
    }

    [Theory]
    [InlineData("future", AssignmentReportState.Upcoming)]
    [InlineData("overdue", AssignmentReportState.Closed)]
    [InlineData("test-wait", AssignmentReportState.Upcoming)]
    [InlineData("test-running", AssignmentReportState.Open)]
    [InlineData("test-end", AssignmentReportState.Closed)]
    public async Task TrangThaiSqlKhopQuyTacDongHo(string kind, AssignmentReportState expected)
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(1);
        var a = s.Assignment; var now = h.Clock.UtcNow;
        if (kind == "future") a.OpensAt = now.AddMinutes(1);
        if (kind == "overdue") a.DueAt = now;
        if (kind.StartsWith("test"))
        {
            a.Mode = AssignmentMode.Test; a.DueAt = null;
            a.TestStartedAt = kind == "test-wait" ? null : now.AddMinutes(-5);
            a.TestEndsAt = kind == "test-end" ? now : now.AddMinutes(10);
        }
        await h.Db.SaveChangesAsync();
        Assert.Equal(expected, AssignmentService.ReportState(a, now));
        Assert.Equal(expected, Assert.Single((await h.Assignments.GetReportPageAsync(s.Lecturer)).Items).State);
    }
}
