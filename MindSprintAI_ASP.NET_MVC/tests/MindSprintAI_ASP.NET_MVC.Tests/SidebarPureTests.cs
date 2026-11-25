using System.Text.RegularExpressions;
using System.Net;
using MindSprintAI_ASP.NET_MVC.Data.Entities;
using MindSprintAI_ASP.NET_MVC.Models;
using MindSprintAI_ASP.NET_MVC.Services;

namespace MindSprintAI_ASP.NET_MVC.Tests;

public class SidebarPureTests
{
    [Theory]
    [InlineData("Cô Lan", "CL")]
    [InlineData("Đặng Ánh", "DA")]
    [InlineData("Minh", "M")]
    [InlineData("  cô   Lan  ", "CL")]
    [InlineData("", "K")]
    [InlineData(null, "K")]
    [InlineData("   ", "K")]
    [InlineData("Nguyễn Thị Minh Khánh", "NK")]
    [InlineData("A\u0301nh Đào", "AD")]
    public void ChuDauBoDauVaChiLayTuDauCuoi(string? name, string expected) =>
        Assert.Equal(expected, AvatarText.Initials(name));

    [Theory]
    [InlineData("Cô Lan", "var(--blue)")]
    [InlineData("Minh", "var(--blue)")]
    [InlineData("A", "var(--pink)")]
    [InlineData("C", "var(--warn)")]
    [InlineData("", "var(--acid)")]
    [InlineData(null, "var(--acid)")]
    public void MauCoDinhTheoTen(string? name, string expected)
    {
        Assert.Equal(expected, AvatarText.Tint(name));
        Assert.Equal(AvatarText.Tint(name), AvatarText.Tint(name));
    }

    [Fact]
    public void TenRatDaiVanChiHaiChuVaMauTrongBangMau()
    {
        var name = string.Join(' ', Enumerable.Repeat("Đặng", 2000));
        Assert.Equal("DD", AvatarText.Initials(name));
        Assert.Contains(AvatarText.Tint(name), new[] { "var(--acid)", "var(--pink)", "var(--blue)", "var(--warn)" });
    }

    [Fact]
    public void LayoutGiuMenuBieuMauVaLuuLuaChonCu()
    {
        var source = File.ReadAllText(Path.Combine(Tang8PureTests.Root, "Views/Shared/_Layout.cshtml"));
        foreach (var id in new[] { "account-menu", "admin-menu" })
            Assert.Contains($"id=\"{id}\" data-bs-toggle=\"dropdown\"", source);
        Assert.Contains("@me.DisplayName</span>", source);
        foreach (var action in new[] { "SwitchMode", "Logout" })
            Assert.Matches($"<form[^>]*method=\"post\"[^>]*asp-controller=\"Account\"[^>]*asp-action=\"{action}\"", source);
        Assert.DoesNotContain("asp-antiforgery=\"false\"", source);
        Assert.Contains("account-identity", source);
        Assert.Contains("@me.Email", source);
        Assert.Contains("if (rail === 'collapsed')", source); // MindSprint: mặc định mở rộng như giao diện nhóm.
        Assert.Single(Regex.Matches(source, "id=\"account-menu\"").Cast<Match>());
    }

    [Fact]
    public async Task BangPhuBoHocDungSoTrangVaGiuBoLoc()
    {
        var html = WebUtility.HtmlDecode(await Tang4ViewTests.Render("StudySets", "Index",
            new List<StudySet> { new() { Id = 1, Title = "Bộ <dài> có dấu" } },
            new() { ["TrashCount"] = 7 }));
        Assert.Contains("Tất cả bộ học (1)", html);
        Assert.Matches(@"Thùng rác\s*\(7\)", html);
        var nav = html[html.IndexOf("<nav", StringComparison.Ordinal)..html.IndexOf("</nav>", StringComparison.Ordinal)];
        Assert.Contains("id=\"library-search\"", nav);
        Assert.Contains("id=\"library-sort\"", nav);
        Assert.Contains("/StudySets/Trash", nav);
        Assert.Contains("__RequestVerificationToken", html);
    }

    [Fact]
    public async Task BangPhuBaoCaoDemCaNgoaiTrangHienTaiVaGiuBoLoc()
    {
        var model = new AssignmentReportPage([], 1, 25, "open", "Tìm có dấu", 3,
            new Dictionary<AssignmentReportState, int> { [AssignmentReportState.Open] = 21, [AssignmentReportState.Upcoming] = 3, [AssignmentReportState.Closed] = 1 },
            [new(3, "Lớp A")]);
        var html = WebUtility.HtmlDecode(await Tang4ViewTests.Render("Reports", "Index", model));
        foreach (var label in new[] { "Tất cả (25)", "Đang mở (21)", "Chưa mở (3)", "Đã đóng (1)" })
            Assert.Contains(label, html);
        Assert.Contains("c-report-status c-section-nav", html);
        Assert.Contains("name=\"status\" value=\"open\"", html);
        // Bộ dựng view không có bảng route đang chạy; kiểm ranh giới route từ nguồn.
        var source = File.ReadAllText(Path.Combine(Tang8PureTests.Root, "Views/Reports/Index.cshtml"));
        Assert.Contains("asp-route-classId=\"@Model.ClassId\"", source);
        Assert.Contains("asp-route-q=\"@Model.Query\"", source);
        Assert.Contains("aria-current=\"page\"", html);
        Assert.Contains("Chưa có bài giao phù hợp", html);
    }
}
