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
using MindSprintAI_ASP.NET_MVC.Controllers;
using MindSprintAI_ASP.NET_MVC.Data.Entities;
using MindSprintAI_ASP.NET_MVC.Services;

namespace MindSprintAI_ASP.NET_MVC.Tests;

public class Tang2ViewTests
{
    private static async Task<string> RenderAsync(string name, object model, Dictionary<string, object?> values)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(LearnController).Assembly.GetName().Name,
            EnvironmentName = "Development"
        });
        builder.Services.AddControllersWithViews().AddApplicationPart(typeof(LearnController).Assembly);
        await using var app = builder.Build();
        app.MapDefaultControllerRoute();
        using var scope = app.Services.CreateScope();
        var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        http.Request.Scheme = "http";
        http.Request.Host = new HostString("localhost");
        http.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(), "Render test"));
        var route = new RouteData();
        route.Values["controller"] = "Learn";
        route.Values["action"] = name;
        var action = new ActionContext(http, route, new ActionDescriptor());
        var view = scope.ServiceProvider.GetRequiredService<IRazorViewEngine>().GetView(null, $"/Views/Learn/{name}.cshtml", false);
        Assert.True(view.Success, string.Join(", ", view.SearchedLocations ?? []));
        var data = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary()) { Model = model };
        foreach (var (key, value) in values) data[key] = value;
        var temp = scope.ServiceProvider.GetRequiredService<ITempDataDictionaryFactory>().GetTempData(http);
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        await view.View.RenderAsync(new ViewContext(action, view.View, data, temp, writer, new HtmlHelperOptions()));
        return writer.ToString();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task B5_HtmlDangLamKhongChuaDapAnHayGiaiThich(bool answered)
    {
        var snap = new QuestionSnapshot(1, 0, QuestionType.SingleChoice, "Câu hỏi", "GIAI_THICH_BI_MAT", 1,
            [new(11, 0, "A", true), new(12, 1, "B", false)]);
        var attempt = new Attempt
        {
            Id = 1, Participant = new Participant { Assignment = new Assignment { Mode = AssignmentMode.Test } },
            Questions = [new AttemptQuestion
            {
                SnapshotJson = snap.ToJson(), AnswerCount = answered ? 1 : 0,
                Submissions = answered ? [new AnswerSubmission { IsFinal = true, IsCorrect = true, PayloadJson = "{\"Selected\":[11]}" }] : []
            }]
        };
        var html = await RenderAsync("Play", attempt, new() { ["Questions"] = AttemptService.ProjectQuestions(attempt, false) });
        Assert.DoesNotContain("data-is-correct", html);
        Assert.DoesNotContain("data-correct", html);
        Assert.DoesNotContain("GIAI_THICH_BI_MAT", html);
        Assert.DoesNotContain("SnapshotJson", html);
    }

    [Fact]
    public async Task B6_HtmlXemLaiKhongChuaDiemTruocCongBo()
    {
        var attempt = new Attempt
        {
            Id = 1, Status = AttemptStatus.Ended, FirstScore = 9876.5432m, MaxScore = 10000,
            Participant = new Participant { Assignment = new Assignment { Mode = AssignmentMode.Test } }
        };
        var html = await RenderAsync("Review", attempt, new()
        {
            ["Questions"] = AttemptService.ProjectQuestions(attempt, false),
            ["ShowScores"] = AttemptService.CanShowScores(attempt.Participant)
        });
        Assert.Contains("Chờ giảng viên công bố điểm", System.Net.WebUtility.HtmlDecode(html));
        Assert.DoesNotContain("9876", html);
        Assert.DoesNotContain("9,88", html);
        Assert.DoesNotContain("9.88", html);
    }
}
