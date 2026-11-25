using System.Data.Common;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using MindSprintAI_ASP.NET_MVC.Data;
using MindSprintAI_ASP.NET_MVC.Data.Entities;
using MindSprintAI_ASP.NET_MVC.Services;

namespace MindSprintAI_ASP.NET_MVC.Tests;

[Collection("db")]
public class LopSoLieuTests
{
    [Fact]
    public async Task ChiDemHocVienDangHocVaChoDuyet()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(1);
        foreach (var status in new[] { ClassMemberStatus.Active, ClassMemberStatus.PendingApproval, ClassMemberStatus.Left, ClassMemberStatus.Removed })
            h.Db.ClassMembers.Add(new() { ClassroomId = s.Room.Id, UserId = await h.UserAsync("An"), Role = ClassRole.Student, Status = status });
        await h.Db.SaveChangesAsync();
        var summary = (await h.Classrooms.GetLecturerSummariesAsync(s.Lecturer))[s.Room.Id];
        Assert.Equal(2, summary.Students);
        Assert.Equal(1, summary.PendingStudents);
        Assert.Equal(1, summary.NeedsReview);
    }

    [Theory]
    [InlineData("practice", true)]
    [InlineData("future", false)]
    [InlineData("overdue", false)]
    [InlineData("due-now", false)]
    [InlineData("opens-now", true)]
    [InlineData("running-test", true)]
    [InlineData("ended-test", false)]
    [InlineData("unstarted-test", false)]
    [InlineData("closed", false)]
    [InlineData("archived", false)]
    [InlineData("deleted-report", false)]
    public async Task BaiDangMoTheoGioVaTrangThai(string state, bool open)
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(1); var a = s.Assignment;
        var now = h.Clock.UtcNow;
        if (state == "future") a.OpensAt = now.AddMinutes(1);
        if (state == "opens-now") a.OpensAt = now;
        if (state == "overdue") a.DueAt = now.AddMinutes(-1);
        if (state == "due-now") a.DueAt = now;
        if (state.Contains("test"))
        {
            a.Mode = AssignmentMode.Test; a.DueAt = null;
            a.TestStartedAt = state == "unstarted-test" ? null : now.AddMinutes(-10);
            a.TestEndsAt = state == "ended-test" ? now : now.AddMinutes(10);
        }
        if (state == "closed") a.Status = AssignmentStatus.Closed;
        if (state == "archived") a.ArchivedAt = now;
        if (state == "deleted-report") a.ReportDeletedAt = now;
        await h.Db.SaveChangesAsync();
        Assert.Equal(open ? 1 : 0, (await h.Classrooms.GetLecturerSummariesAsync(s.Lecturer))[s.Room.Id].OpenAssignments);
    }

    [Theory]
    [InlineData("manual", 1)]
    [InlineData("publication", 1)]
    [InlineData("manual-and-publication", 1)]
    [InlineData("published", 0)]
    [InlineData("graded", 0)]
    [InlineData("cancelled", 0)]
    [InlineData("device", 1)]
    [InlineData("device-approved", 0)]
    [InlineData("device-expired", 0)]
    [InlineData("stranger", 1)]
    public async Task CacNguonCanXemVaKhongDemTrungBaiLam(string source, int expected)
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(1);
        var publication = source.Contains("publication") || source == "published";
        if (publication) s.Assignment.Mode = AssignmentMode.Test;
        if (source == "published") s.Assignment.ScoresPublishedAt = h.Clock.UtcNow;
        var p = new Participant { Assignment = s.Assignment, UserId = s.Student,
            CompletionStatus = publication ? CompletionStatus.SubmittedAwaitingScore : CompletionStatus.Completed,
            IsStranger = source == "stranger", ApprovalStatus = source == "stranger" ? ApprovalStatus.Pending : ApprovalStatus.NotRequired };
        var attempt = new Attempt { Participant = p, Status = source.StartsWith("device") ? AttemptStatus.InProgress : AttemptStatus.Ended,
            AttemptNumber = 1, DeadlineAt = source == "device-expired" ? h.Clock.UtcNow : h.Clock.UtcNow.AddHours(1) };
        if (source == "cancelled") attempt.Status = AttemptStatus.Cancelled;
        h.Db.Attempts.Add(attempt);
        if (source.Contains("manual") || source is "graded" or "cancelled")
        {
            // Hai câu chờ chấm vẫn chỉ là một người làm bài cần xem; dùng bản chụp, không dùng loại câu hiện tại.
            foreach (var type in new[] { QuestionType.TextInput, QuestionType.NumberInput })
                attempt.Questions.Add(new() { QuestionId = s.Quiz.Questions[0].Id,
                    SnapshotJson = new QuestionSnapshot(s.Quiz.Questions[0].Id, 0, type, "Điền đáp án", null, 1, []).ToJson(),
                    Submissions = [new() { ClientRequestId = Guid.NewGuid(), IsFinal = true, Score = source == "graded" ? 1 : null, SubmittedAt = h.Clock.UtcNow }] });
        }
        if (source.StartsWith("device")) attempt.DeviceChanges.Add(new() { RequestingDeviceId = "other",
            Status = source == "device-approved" ? DeviceChangeStatus.Approved : DeviceChangeStatus.Pending });
        await h.Db.SaveChangesAsync();
        var summary = (await h.Classrooms.GetLecturerSummariesAsync(s.Lecturer))[s.Room.Id];
        Assert.Equal(expected, summary.NeedsReview);
        // Trang nói rõ loại việc, và việc của từng bài giao khớp với tổng của lớp.
        Assert.Equal(expected, summary.Work!.Total);
        Assert.Equal(expected, (await h.Classrooms.GetAssignmentWorkAsync(s.Room.Id))[s.Assignment.Id].Work.Total);
        var text = string.Join(" · ", summary.Work.Describe());
        if (expected == 0) Assert.Empty(text);
        else Assert.Contains(source switch
        {
            "manual" => "1 bài làm cần chấm tay",
            "device" => "1 yêu cầu chuyển thiết bị",
            "stranger" => "1 người ngoài lớp chờ duyệt",
            _ => "1 học viên chờ công bố điểm",
        }, text);
    }

    [Fact]
    public async Task HocVienXinVaoLopGhiRoTrenTrangLop()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(1);
        h.Db.ClassMembers.Add(new() { ClassroomId = s.Room.Id, UserId = await h.UserAsync("An"), Role = ClassRole.Student, Status = ClassMemberStatus.PendingApproval });
        await h.Db.SaveChangesAsync();
        var summary = (await h.Classrooms.GetLecturerSummariesAsync(s.Lecturer))[s.Room.Id];
        Assert.Equal(["1 học viên xin vào lớp"], summary.Work!.Describe());
        // Việc xin vào lớp thuộc lớp, không gắn vào bài giao nào.
        Assert.Equal(0, (await h.Classrooms.GetAssignmentWorkAsync(s.Room.Id))[s.Assignment.Id].Work.Total);
    }

    [Theory]
    [InlineData(ClassRole.Student, ClassMemberStatus.Active, false)]
    [InlineData(ClassRole.AssistantLecturer, ClassMemberStatus.Invited, false)]
    [InlineData(ClassRole.AssistantLecturer, ClassMemberStatus.Left, false)]
    [InlineData(ClassRole.AssistantLecturer, ClassMemberStatus.Active, true)]
    public async Task ChiGiangVienDangHoatDongDuocXem(ClassRole role, ClassMemberStatus status, bool allowed)
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(1);
        var member = await h.Db.ClassMembers.SingleAsync(m => m.ClassroomId == s.Room.Id && m.UserId == s.Student);
        member.Role = role; member.Status = status; await h.Db.SaveChangesAsync();
        Assert.Equal(allowed, (await h.Classrooms.GetLecturerSummariesAsync(s.Student)).ContainsKey(s.Room.Id));
        Assert.Empty(await h.Classrooms.GetLecturerSummariesAsync(await h.UserAsync("Ngoài lớp")));
        s.Room.DeletedAt = h.Clock.UtcNow; await h.Db.SaveChangesAsync();
        Assert.Empty(await h.Classrooms.GetLecturerSummariesAsync(s.Lecturer));
    }

    [Fact]
    public async Task SoLuotSqlKhongTangTheoSoLop()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(1);
        var counter = new SqlCounter();
        var options = new DbContextOptionsBuilder<AppDbContext>(AppDbContextFactory.BuildOptions(DatabaseFixture.ConnectionString))
            .AddInterceptors(counter).Options;
        await using var db = new AppDbContext(options);
        var service = new ClassroomService(db, h.Codes, h.Clock, h.Notifier, h.Invitations);
        var first = await service.GetLecturerSummariesAsync(s.Lecturer);
        var oneClass = counter.Reads;
        for (var i = 0; i < 8; i++)
        {
            var room = await h.Classrooms.CreateAsync($"Lớp {i}", ClassJoinPolicy.Immediate, s.Lecturer);
            if (i < 7) await h.Assignments.CreatePracticeAsync(room.Id, s.Quiz.Id, new(null, null, null), s.Lecturer);
        }
        counter.Reads = 0;
        var many = await service.GetLecturerSummariesAsync(s.Lecturer);
        Assert.Single(first); Assert.Equal(9, many.Count);
        Assert.Equal(3, oneClass); Assert.Equal(oneClass, counter.Reads);
        Assert.Equal(7, many.Values.Count(v => v.Students == 0 && v.OpenAssignments == 1 && v.NeedsReview == 0));
        Assert.Single(many.Values, v => v.Students == 0 && v.OpenAssignments == 0 && v.NeedsReview == 0);
    }

    private sealed class SqlCounter : DbCommandInterceptor
    {
        public int Reads { get; set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { Reads++; return ValueTask.FromResult(result); }
    }

    [Fact]
    public async Task XemTatCaThanhVienChiGiangVienThayEmail()
    {
        // (D12) giảng viên cần email để phân biệt hai học viên trùng tên; học viên không được thấy email của nhau.
        using var h = new Harness(); var s = await h.ScenarioAsync();
        var lecturerView = await h.Classrooms.MemberNamesAsync(s.Room.Id, s.Lecturer);
        Assert.Contains(lecturerView.Members, m => m.UserId == s.Student && !string.IsNullOrEmpty(m.Email));
        var studentView = await h.Classrooms.MemberNamesAsync(s.Room.Id, s.Student);
        Assert.All(studentView.Members, m => Assert.Null(m.Email));
    }
}

public class LopSoLieuViewTests
{
    [Theory]
    [InlineData(2, 4)]
    [InlineData(0, 0)]
    public async Task TheLopHienBaSoVaChoDuyetKhiCo(int pending, int review)
    {
        var model = new List<ClassMember> { new() { ClassroomId = 42, Role = ClassRole.MainLecturer,
            Status = ClassMemberStatus.Active, Classroom = new() { Id = 42, Name = "Lớp A" } } };
        var html = WebUtility.HtmlDecode(await Tang4ViewTests.Render("Classrooms", "Index", model,
            new() { ["LecturerSummaries"] = new Dictionary<int, LecturerClassroomSummary> { [42] = new(42, 12, pending, 3, review) } }));
        Assert.Contains("<b>12</b> học viên", html);
        Assert.Contains("<b>3</b> bài đang mở", html);
        Assert.Contains(review > 0 ? $"<b class=\"class-stats-attention\">{review}</b> cần xem" : "<b>0</b> cần xem", html);
        Assert.Equal(pending > 0, html.Contains($"{pending} chờ duyệt"));
        Assert.Equal(review > 0, html.Contains("class=\"class-stats-attention\""));
        Assert.True(html.IndexOf("class=\"class-tags\"", StringComparison.Ordinal) < html.IndexOf("class=\"class-stats\"", StringComparison.Ordinal));
        Assert.True(html.IndexOf("class=\"class-stats\"", StringComparison.Ordinal) < html.IndexOf("class=\"class-foot\"", StringComparison.Ordinal));
        Assert.False(PlainLanguageAudit.HasJargon(html));
    }

}
