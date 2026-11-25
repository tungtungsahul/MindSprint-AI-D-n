using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MindSprintAI_ASP.NET_MVC.Controllers;
using MindSprintAI_ASP.NET_MVC.Data.Entities;
using MindSprintAI_ASP.NET_MVC.Models;
using MindSprintAI_ASP.NET_MVC.Services;

namespace MindSprintAI_ASP.NET_MVC.Tests;

public class Tang4ViewTests
{
    internal static async Task<string> Render(string folder, string name, object model, Dictionary<string, object?>? values = null,
        Action<IServiceCollection>? configureServices = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        { ApplicationName = typeof(LearnController).Assembly.GetName().Name, EnvironmentName = "Development" });
        builder.Services.AddControllersWithViews().AddApplicationPart(typeof(LearnController).Assembly);
        configureServices?.Invoke(builder.Services);
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

    [Theory]
    [InlineData(false), InlineData(true)]
    public async Task OnTapLamChuChiHienPhanHoiCauDaTraLoi(bool answered)
    {
        var a = Tang4RuleTests.Build(2);
        var first = a.Questions[0];
        if (answered)
        {
            first.AnswerCount = 1;
            first.Submissions.Add(new() { IsFinal = true, IsCorrect = false, PayloadJson = "{\"Selected\":[2]}" });
        }
        a.Questions[1].SnapshotJson = (QuestionSnapshot.FromJson(a.Questions[1].SnapshotJson) with { Explanation = "GIẢI_THÍCH_CHƯA_ĐƯỢC_XEM" }).ToJson();
        var html = WebUtility.HtmlDecode(await Render("Learn", "Play", a, new()
        { ["Questions"] = AttemptService.ProjectQuestions(a, false), ["Mastery"] = MasteryRules.Progress(a) }));
        Assert.DoesNotContain("SnapshotJson", html);
        Assert.DoesNotContain("GIẢI_THÍCH_CHƯA_ĐƯỢC_XEM", html);
        Assert.Contains("Kết thúc lượt", html); Assert.Contains("Câu sai", html);
        Assert.Equal(answered, html.Contains("data-is-correct="));
        Assert.Equal(answered, html.Contains("BÍ_MẬT"));
    }

    [Theory]
    [InlineData(false, false), InlineData(true, true)]
    public async Task KiemTraKhongLoDapAnTruocCongBoHoacKhiDuocDuyetLamLai(bool published, bool retake)
    {
        var a = Tang4RuleTests.Build(1); a.Status = AttemptStatus.Ended;
        a.Participant.Assignment.Mode = AssignmentMode.Test; a.Participant.Assignment.MasteryThreshold = null;
        a.Participant.Assignment.AnswersPublishedAt = published ? DateTime.UtcNow : null;
        a.Questions[0].AnswerCount = 1;
        a.Questions[0].Submissions.Add(new() { IsFinal = true, IsCorrect = true, PayloadJson = "{\"Selected\":[1]}" });
        foreach (var page in new[] { "Play", "Review" })
        {
            var html = WebUtility.HtmlDecode(await Render("Learn", page, a, new()
            { ["Questions"] = AttemptService.ProjectQuestions(a, retake), ["ShowScores"] = false }));
            Assert.DoesNotContain("BÍ_MẬT", html); Assert.DoesNotContain("data-is-correct", html);
            Assert.DoesNotContain("data-correct", html); Assert.DoesNotContain("SnapshotJson", html);
        }
    }

    [Theory]
    [InlineData("Assignment"), InlineData("Review"), InlineData("Details"), InlineData("Attempt")]
    public async Task ToanKhaoSatKhongHienKhongTrenKhong(string page)
    {
        var a = Tang4RuleTests.Build(0); a.Participant.Assignment.MasteryThreshold = null; a.Status = AttemptStatus.Ended;
        a.Participant.Assignment.Form = AssignmentForm.QuizOnly;
        var q = Tang4RuleTests.Add(a, QuestionType.SurveyText); q.AnswerCount = 1;
        q.Submissions.Add(new() { IsFinal = true, PayloadJson = "{\"Value\":\"Ý kiến\"}" });
        object model = page switch
        {
            "Assignment" => new LearnerAssignmentModel { Assignment = a.Participant.Assignment, Attempts = [a], CanStart = false,
                ShowScores = false, Ungraded = true, Score = new(0, 0) },
            "Details" => new AssignmentDetailsModel { Assignment = a.Participant.Assignment, Code = "12345678", OpenAttempts = 0,
                Rows = [new("1", "Minh", CompletionStatus.Completed, CompletionLabels.None, 1, new(0, 0), 1)] },
            _ => a
        };
        List<(AttemptQuestion Question, QuestionSnapshot Snapshot)> items = [(q, QuestionSnapshot.FromJson(q.SnapshotJson))];
        var html = WebUtility.HtmlDecode(await Render(page is "Details" or "Attempt" ? "Assignments" : "Learn", page, model, new()
        {
            ["Questions"] = AttemptService.ProjectQuestions(a, false), ["ShowScores"] = false,
            ["Items"] = items, ["InputReviews"] = GradebookService.InputReviews(items)
        }));
        Assert.Contains("Không chấm điểm", html); Assert.DoesNotContain("0/0", html);
        Assert.DoesNotContain("Chờ giảng viên công bố điểm", html);
        if (page != "Details") { Assert.DoesNotContain("0%", html); Assert.DoesNotContain("100%", html); }
        else Assert.Contains("<td>Không chấm điểm</td>", html); // mốc 80/90/100% trong form setting không phải điểm khảo sát.
        if (page is "Review" or "Attempt") Assert.DoesNotContain("bg-danger", html);
    }

    [Fact]
    public async Task KhaoSatAnSoLieuVaMaHoaYKienCongKhai()
    {
        var model = new LearnerSurveyResults(1,
            [new("Câu ít người", true, null, [], []), new("<img src=x onerror=alert(1)>", false, 5, [], ["<script>alert(2)</script>"])]);
        var html = await Render("Learn", "SurveyResults", model);
        Assert.Contains("Chưa đủ 5 người trả lời để công khai", WebUtility.HtmlDecode(html));
        Assert.DoesNotContain("<script>alert", html); Assert.DoesNotContain("<img src=x", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Fact]
    public async Task BaiGiaoKhoaSettingKhiDaCoLuot()
    {
        var a = Tang4RuleTests.Build(1).Participant.Assignment;
        var model = new AssignmentDetailsModel { Assignment = a, Code = "12345678", OpenAttempts = 0, Rows = [], CanEditMastery = false };
        var html = WebUtility.HtmlDecode(await Render("Assignments", "Details", model));
        Assert.Contains("fieldset disabled", html);
        Assert.Contains("Đã có người bắt đầu lượt", html);
        Assert.Contains("AnswersPerQuestion", html); Assert.Contains("ScoreRetries", html);
    }
}
