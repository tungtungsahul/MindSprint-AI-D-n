using System.Net;
using MindSprintAI_ASP.NET_MVC.Data.Entities;
using MindSprintAI_ASP.NET_MVC.Services;

namespace MindSprintAI_ASP.NET_MVC.Tests;

public class RekalRenderedViewTests
{
    private static void SavePreview(string name, string html)
    {
        var directory = Path.Combine(Tang8PureTests.Root, "App_Data", "TestResults", "mindsprint-preview");
        Directory.CreateDirectory(directory);
        var assets = new Uri(Path.Combine(Tang8PureTests.Root, "wwwroot") + Path.DirectorySeparatorChar).AbsoluteUri;
        var styles = string.Join("", new[] { "lib/bootstrap/dist/css/bootstrap.min.css", "css/mindsprint-fonts.css", "css/mindsprint.css", "css/mindsprint-decor.css", "css/mindsprint-pages-a.css", "css/mindsprint-pages-b.css", "css/mindsprint-pages-c.css" }.Select(p => $"<link rel=\"stylesheet\" href=\"{assets}{p}\">"));
        var scripts = string.Join("", new[] { "lib/bootstrap/dist/js/bootstrap.bundle.min.js", "js/assignment-insights.js", "js/gradebook-pager.js", "js/library-tools.js" }.Select(p => $"<script src=\"{assets}{p}\"></script>"));
        File.WriteAllText(Path.Combine(directory, name + ".html"), $"<!doctype html><html lang=\"vi\"><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>MindSprint AI — mẫu kiểm giao diện</title>{styles}<body class=\"mindsprint-still\"><div class=\"container\"><p class=\"alert alert-info\">Dữ liệu mẫu để kiểm giao diện. Chưa kiểm chứng kết nối và quyền.</p><main id=\"main-content\">{html}</main></div>{scripts}</body></html>");
    }

    [Fact]
    public async Task BangDiemNhieuHocVienVaMaTranRenderDuDePhanTrang()
    {
        var a = Tang4RuleTests.Build(1).Participant.Assignment;
        var rows = Enumerable.Range(1, 53).Select(i => new GradebookRow(i.ToString(), i == 53 ? "Đặng Ánh" : "Học viên " + i,
            CompletionStatus.Completed, CompletionLabels.None, 1, new(1, 1), i,
            Screen: i % 11 == 0 ? new(2, 30, false, true, "10:00", 5) : null)).ToList();
        rows.Add(new("54", "Người ngoài lớp", CompletionStatus.Completed, CompletionLabels.None, 1, new(1, 1), 54, IsStranger: true));
        var matrix = new GradebookMatrix([new(1, 1, "Câu hỏi mẫu", 75)], rows.Where(r => !r.IsStranger)
            .Select(r => new MatrixRow(r, [new[] { new MatrixMark(1, MatrixState.Correct) }])).ToList(), null);
        var model = new MindSprintAI_ASP.NET_MVC.Models.AssignmentDetailsModel { Assignment = a, Code = "12345678", OpenAttempts = 0, Rows = rows, Matrix = matrix };
        var html = WebUtility.HtmlDecode(await Tang4ViewTests.Render("Assignments", "Details", model));
        Assert.Contains("data-participants=\"53\"", html);
        Assert.Contains("data-matrix-name=\"Đặng Ánh\"", html);
        Assert.Contains("id=\"gradebook-tools\"", html);
        // gradebook-pager.js thoát sớm nếu thiếu phần tử này, làm bảng điểm không phân trang.
        Assert.Contains("id=\"gradebook-pages\"", html);
        var icons = await Tang4ViewTests.Render("Shared", "_Icons", new object());
        SavePreview("assignment", icons + html);
    }

    [Fact]
    public async Task BaoCaoDungNutTheoTrangThaiVaKhongCoTuKhoHieu()
    {
        var items = new[] { AssignmentReportState.Open, AssignmentReportState.Upcoming, AssignmentReportState.Closed }
            .Select((state, i) => new AssignmentReportItem(i + 1, "Bài " + (i + 1), AssignmentMode.Practice, state, DateTime.UtcNow, 5, "12345678", 1, "Lớp A")).ToList();
        var model = new AssignmentReportPage(items, 1, 3, "all", null, null,
            items.ToDictionary(i => i.State, _ => 1), [new(1, "Lớp A")]);
        var html = WebUtility.HtmlDecode(await Tang4ViewTests.Render("Reports", "Index", model));
        Assert.Contains(">Đóng bài giao</button>", html);
        Assert.Contains(">Xem báo cáo</a>", html);
        Assert.Contains(">Sửa cài đặt</a>", html);
        // This renderer has no running route table; check routing attributes at the source boundary.
        Assert.Contains("asp-fragment=\"cai-dat\"", File.ReadAllText(Path.Combine(Tang8PureTests.Root, "Views/Reports/Index.cshtml")));
        Assert.Contains("method=\"post\"", html);
        Assert.Contains("__RequestVerificationToken", html);
        Assert.False(PlainLanguageAudit.HasJargon(System.Text.RegularExpressions.Regex.Replace(html, "<[^>]*>", " ")));
        SavePreview("reports", await Tang4ViewTests.Render("Shared", "_Icons", new object()) + html);
    }

    [Fact]
    public async Task ChiTietBoHocDapAnHaiCotCoNhanVaEncodeNoiDung()
    {
        var set = new StudySet { Id = 1, Title = "Toán", Quizzes = [new Quiz { Id = 2, Title = "Ôn tập", Questions = [new Question { Id = 3, Prompt = "<img src=x onerror=alert(1)>", Type = QuestionType.SingleChoice, Options = [new() { Text = "4", IsCorrect = true }, new() { Text = "5" }] }] }] };
        var html = await Tang4ViewTests.Render("StudySets", "Details", set);
        Assert.Contains("data-answer-toggle", html);
        Assert.Contains("id=\"show-answers\"", html);
        Assert.Contains("data-correct-answer=\"true\"", html);
        Assert.Contains("c-answer-grid", html);
        Assert.DoesNotContain("<img src=x", html);
        Assert.Contains("&lt;img", html);
        SavePreview("study-set", await Tang4ViewTests.Render("Shared", "_Icons", new object()) + html);
    }

    [Fact]
    public async Task CanhBaoDungLaiBieuMauDuyetVaTrangThaiRong()
    {
        var row = new GradebookRow("1", "An", CompletionStatus.InProgress, CompletionLabels.None, 1, null, 1,
            DeviceChange: new(1, 2, "Máy bị tắt", null), Screen: new(2, 30, false, true, "10:00", 5));
        var model = new MindSprintAI_ASP.NET_MVC.Models.AssignmentDetailsModel { Assignment = new(), Code = "12345678", OpenAttempts = 1, Rows = [row] };
        var html = WebUtility.HtmlDecode(await Tang4ViewTests.Render("Assignments", "_Warnings", model));
        Assert.Contains("asp-action=\"DecideDeviceChange\"", File.ReadAllText(Path.Combine(Tang8PureTests.Root, "Views/Assignments/_DeviceChange.cshtml")));
        Assert.Contains("name=\"requestId\"", html);
        Assert.Contains("Rời màn hình 2 lần", html);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(html, "data-warning-count=\"1\"").Cast<System.Text.RegularExpressions.Match>());
        model = new() { Assignment = new(), Code = "12345678", OpenAttempts = 0, Rows = [] };
        Assert.Contains("Chưa có cảnh báo nào trong bài này.", WebUtility.HtmlDecode(await Tang4ViewTests.Render("Assignments", "_Warnings", model)));
    }
}
