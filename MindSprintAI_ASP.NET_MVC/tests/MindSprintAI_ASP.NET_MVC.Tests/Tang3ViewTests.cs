using System.Globalization;
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
using MindSprintAI_ASP.NET_MVC.Services;

namespace MindSprintAI_ASP.NET_MVC.Tests;

public class Tang3ViewTests
{
    private static async Task<string> Render(string name, Attempt attempt, bool retake, string folder = "Learn", object? model = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(LearnController).Assembly.GetName().Name, EnvironmentName = "Development"
        });
        builder.Services.AddControllersWithViews().AddApplicationPart(typeof(LearnController).Assembly);
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        await using var app = builder.Build();
        app.MapDefaultControllerRoute();
        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync(); // khởi tạo route thật để kiểm tra cả URL của form Kiểm tra lại.
        using var scope = app.Services.CreateScope();
        var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        http.Request.Scheme = "http";
        http.Request.Host = new HostString("localhost");
        http.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(), "Render test"));
        var route = new RouteData();
        route.Values["controller"] = folder; route.Values["action"] = name;
        var action = new ActionContext(http, route, new ActionDescriptor());
        var view = scope.ServiceProvider.GetRequiredService<IRazorViewEngine>().GetView(null, $"/Views/{folder}/{name}.cshtml", false);
        Assert.True(view.Success);
        var data = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary()) { Model = model ?? attempt };
        data["Questions"] = AttemptService.ProjectQuestions(attempt, retake);
        data["ShowScores"] = false;
        var temp = scope.ServiceProvider.GetRequiredService<ITempDataDictionaryFactory>().GetTempData(http);
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        await view.View.RenderAsync(new ViewContext(action, view.View, data, temp, writer, new HtmlHelperOptions()));
        // Mốc thời gian ISO có 7 chữ số lẻ của giây (vd. "08:00:58.9593279Z") thỉnh thoảng chứa đúng đáp án số "9327"
        // → test hỏng ngẫu nhiên (gặp 15/11/2026). Thay mốc thời gian bằng chữ cố định rồi mới tìm đáp án trong HTML.
        return System.Text.RegularExpressions.Regex.Replace(writer.ToString(), @"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?Z?", "THOI_DIEM");
    }

    [Theory]
    [InlineData(QuestionType.TextInput, false), InlineData(QuestionType.NumberInput, false)]
    [InlineData(QuestionType.TextInput, true), InlineData(QuestionType.NumberInput, true)]
    public async Task HtmlKhongChuaDapAnKhiDangLamVaXemLaiTruocCongBo(QuestionType type, bool answered)
    {
        var attempt = Tang3AttemptTests.Build(type);
        attempt.Questions[0].AnswerCount = answered ? 1 : 0;
        foreach (var page in new[] { "Play", "Review" })
        {
            attempt.Status = page == "Play" ? AttemptStatus.InProgress : AttemptStatus.Ended;
            var html = await Render(page, attempt, false);
            Assert.DoesNotContain("DAP_AN_CHU_BI_MAT", html);
            Assert.DoesNotContain("9327", html);
            Assert.DoesNotContain("0.00123", html);
            Assert.DoesNotContain("data-is-correct", html);
            Assert.DoesNotContain("SnapshotJson", html);
            Assert.DoesNotContain("Giải thích:", html);
        }
    }

    [Theory]
    [InlineData(QuestionType.TextInput), InlineData(QuestionType.NumberInput)]
    public async Task HtmlDaCongBoNhungDuocDuyetLamLaiVanAnDapAn(QuestionType type)
    {
        var attempt = Tang3AttemptTests.Build(type);
        attempt.Status = AttemptStatus.Ended;
        attempt.Participant.Assignment.AnswersPublishedAt = DateTime.UtcNow;
        var html = await Render("Review", attempt, true);
        Assert.DoesNotContain("DAP_AN_CHU_BI_MAT", html);
        Assert.DoesNotContain("9327", html);
    }

    [Fact]
    public async Task XemTruocSuaDuCotVaMaHoaNoiDung()
    {
        var preview = ExcelImportService.ToPreview(1, "mau.xlsx", ExcelTemplate.Read(new MemoryStream(ExcelTemplate.Create()), "mau.xlsx"));
        preview.Rows[0].Values[0] = "<script>alert('x')</script>";
        var html = await Render("Preview", Tang3AttemptTests.Build(), false, "Import", preview);
        Assert.Contains("Rows[0].Values[18]", html);
        Assert.Contains("Rows[4].Values[1]", html);
        Assert.Contains("Rows[0].Include", html);
        Assert.Contains("/Import/Recheck", html);
        Assert.DoesNotContain("<script>alert", html);
        if (Environment.GetEnvironmentVariable("TANG3_VIEW_OUTPUT") is { } path)
            await File.WriteAllTextAsync(path, html); // tùy chọn xuất HTML thật cho kiểm tra bố cục ngoài SQL.
    }
}
