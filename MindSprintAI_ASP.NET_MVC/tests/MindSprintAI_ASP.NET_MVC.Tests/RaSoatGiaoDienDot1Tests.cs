using System.Net;
using MindSprintAI_ASP.NET_MVC.Data.Entities;
using MindSprintAI_ASP.NET_MVC.Models;
using MindSprintAI_ASP.NET_MVC.Services;
using Xunit;

namespace MindSprintAI_ASP.NET_MVC.Tests;

// Lỗi tìm ra khi rà độc lập giao diện 10/11/2026 (đợt 1).
public class RaSoatGiaoDienDot1Tests
{
    [Theory]
    [InlineData(0, "0 giây")]
    [InlineData(45, "45 giây")]
    [InlineData(120, "2 phút")]
    [InlineData(185, "3 phút 5 giây")]
    [InlineData(6069, "1 giờ 41 phút")]
    [InlineData(166348, "46 giờ 12 phút")]
    [InlineData(7200, "2 giờ")]
    public void ThoiLuongDeDoc(int seconds, string expected) => Assert.Equal(expected, DisplayText.Duration(seconds));

    [Theory]
    [InlineData(null, "không xác định")]
    [InlineData("::1", "mạng nội bộ")]
    [InlineData("127.0.0.1", "mạng nội bộ")]
    [InlineData("203.0.113.7", "203.0.113.7")]
    public void DiaChiMayNoiBoGhiBangLoiThuong(string? ip, string expected) => Assert.Equal(expected, NewDeviceService.NetworkText(ip));

    [Fact]
    public async Task BaiCuaToiKhongMoiLamBaiHetHanVaTachNhomChoDiem()
    {
        var now = DateTime.UtcNow;
        Assignment A(int id, string title, AssignmentMode mode, DateTime? due) => new()
        {
            Id = id, Mode = mode, Quiz = new() { Title = title }, Classroom = new() { Name = "Lớp A" },
            DueAt = mode == AssignmentMode.Practice ? due : null, TestEndsAt = mode == AssignmentMode.Test ? due : null
        };
        var expired = A(1, "Kiểm tra đã hết giờ", AssignmentMode.Test, now.AddDays(-1));
        var waiting = A(2, "Bài chờ điểm", AssignmentMode.Test, now.AddDays(-2));
        var open = A(3, "Ôn tập còn hạn", AssignmentMode.Practice, now.AddDays(2));
        var participations = new List<Participant>
        {
            new() { AssignmentId = 2, CompletionStatus = CompletionStatus.SubmittedAwaitingScore },
            new() { AssignmentId = 3, CompletionStatus = CompletionStatus.InProgress }
        };
        var html = WebUtility.HtmlDecode(await Tang4ViewTests.Render("Learn", "Index", new List<Assignment> { expired, waiting, open },
            new() { ["Participations"] = participations, ["Classes"] = new List<ClassMember>() }));
        var hero = html[html.IndexOf("class=\"learn-next\"", StringComparison.Ordinal)..];
        hero = hero[..hero.IndexOf("</a>", StringComparison.Ordinal)];
        Assert.Contains("Ôn tập còn hạn", hero);              // ô nổi bật chỉ mời bài còn làm được
        Assert.DoesNotContain("Kiểm tra đã hết giờ", hero);
        Assert.Contains("Chờ công bố điểm", html);
        Assert.Contains("Đã quá hạn", html);
        Assert.Contains("Còn <b>1</b> bài cần làm", html);
        Assert.Contains("1 bài chờ điểm", html);
    }

    [Fact]
    public async Task HetBaiCanLamThiHienOThongBaoThayVaoNutBatDau()
    {
        var a = new Assignment { Id = 9, Mode = AssignmentMode.Practice, Quiz = new() { Title = "Đã nộp" }, Classroom = new() { Name = "Lớp A" } };
        var html = WebUtility.HtmlDecode(await Tang4ViewTests.Render("Learn", "Index", new List<Assignment> { a },
            new()
            {
                ["Participations"] = new List<Participant> { new() { AssignmentId = 9, CompletionStatus = CompletionStatus.SubmittedAwaitingScore } },
                ["Classes"] = new List<ClassMember>()
            }));
        Assert.DoesNotContain("class=\"learn-next\"", html);
        Assert.Contains("Bạn đã làm hết bài được giao.", html);
    }

    [Fact]
    public async Task TrangLopOCheDoHocVienLietKeLopDaThamGia()
    {
        var model = new List<ClassMember>
        {
            new() { ClassroomId = 1, Role = ClassRole.Student, Status = ClassMemberStatus.Active, Classroom = new() { Id = 1, Name = "Lớp học thử" } },
            new() { ClassroomId = 2, Role = ClassRole.MainLecturer, Status = ClassMemberStatus.Active, Classroom = new() { Id = 2, Name = "Lớp đang dạy" } }
        };
        var html = WebUtility.HtmlDecode(await Tang4ViewTests.Render("Classrooms", "Index", model, new() { ["LearnerMode"] = true }));
        Assert.Contains("Lớp của tôi", html);
        Assert.Contains("Lớp học thử", html);
        Assert.DoesNotContain("Lớp đang dạy", html);
        Assert.DoesNotContain("Tạo lớp mới", html);
    }
}
