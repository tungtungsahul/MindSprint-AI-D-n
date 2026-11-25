using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Caching.Memory;
using MindSprintAI_ASP.NET_MVC.Controllers;
using MindSprintAI_ASP.NET_MVC.Data;
using MindSprintAI_ASP.NET_MVC.Data.Entities;
using MindSprintAI_ASP.NET_MVC.Models;
using MindSprintAI_ASP.NET_MVC.Services;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace MindSprintAI_ASP.NET_MVC.Tests;

public sealed class Tang8Sink : ILogEventSink
{
    public List<LogEvent> Events { get; } = [];
    public void Emit(LogEvent logEvent) { lock (Events) Events.Add(logEvent); }
    public string Json()
    {
        using var writer = new StringWriter(); var formatter = new CompactJsonFormatter();
        foreach (var e in Events) formatter.Format(e, writer);
        return writer.ToString();
    }
}

// (NK1, NK2, NK5, NK6) Không mở kết nối database: chỉ middleware, file tạm và Razor.
public class Tang8PureTests
{
    internal static string Root
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Program.cs"))) dir = dir.Parent;
            return dir!.FullName;
        }
    }

    [Theory]
    [InlineData("hv.minh@test.local", "hv.m***@test.local")]
    [InlineData("a@school.vn", "a***@school.vn")]
    [InlineData("", "***")]
    [InlineData(null, "***")]
    [InlineData("not-an-email", "***")]
    [InlineData("a@", "***")]
    public void NK2_MaskEmail(string? input, string expected) => Assert.Equal(expected, LogRedaction.MaskEmail(input));

    [Theory]
    [InlineData("/Account/ResetPassword?code=secret&userId=abc", "/Account/ResetPassword")]
    [InlineData("/Learn/Play/2", "/Learn/Play/2")]
    [InlineData("/a?password=hello#part", "/a")]
    [InlineData("/a#part", "/a")]
    [InlineData(null, "")]
    public void NK2_StripQuery(string? input, string expected) => Assert.Equal(expected, LogRedaction.StripQuery(input));

    [Theory]
    [InlineData("minh@example.test", "example.test")]
    [InlineData("malformed-secret", "unknown")]
    [InlineData(null, "unknown")]
    public void NK1_SmtpDomainOnly(string? email, string expected) => Assert.Equal(expected, LogRedaction.EmailDomain(email));

    [Fact]
    public void NK1_FileConfiguredAndActuallyWritesCompactJson()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mindsprint-log-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Logs:Directory"] = dir }).Build();
            using (var log = SystemLogging.Configure(new LoggerConfiguration(), config, Root).CreateLogger())
            {
                log.Information("file probe {Value}", 123);
                log.ForContext("SourceContext", "Microsoft.Something").Information("SUPPRESSED_LIBRARY_NOISE");
                log.ForContext("SourceContext", "Microsoft.Hosting.Lifetime").Information("LifetimeProbe");
            }
            var file = Assert.Single(Directory.GetFiles(dir, "mindsprint-*.log"));
            var lines = File.ReadAllLines(file);
            Assert.Equal(2, lines.Length);
            using var json = JsonDocument.Parse(lines[0]); Assert.Equal(123, json.RootElement.GetProperty("Value").GetInt32());
            Assert.DoesNotContain("SUPPRESSED_LIBRARY_NOISE", File.ReadAllText(file));
            Assert.Equal(50L * 1024 * 1024, SystemLogging.FileSizeLimit); Assert.Equal(30, SystemLogging.RetentionDays);
            var source = File.ReadAllText(Path.Combine(Root, "Services/SystemLogging.cs"));
            Assert.Contains("fileSizeLimitBytes: FileSizeLimit", source); Assert.Contains("rollOnFileSizeLimit: true", source);
            Assert.Contains("rollingInterval: RollingInterval.Day", source); Assert.Contains("retainedFileTimeLimit: TimeSpan.FromDays(RetentionDays)", source);
            Assert.Contains("buffered: false, shared: OperatingSystem.IsWindows()", source);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void NK2_GuardDropsDirectAndNestedSecrets()
    {
        var sink = new Tang8Sink();
        using var logger = new LoggerConfiguration().Enrich.With<RedactionEnricher>().WriteTo.Sink(sink).CreateLogger();
        logger.ForContext("Password", "pw-needle").ForContext("ApiKey", "api-needle")
            .ForContext("Cookie", "cookie-needle").ForContext("Authorization", "auth-needle")
            .Information("probe {@Details}", new { Label = "safe", Secret = "secret-needle", ResetCode = "reset-needle",
                Inner = new Dictionary<string, object> { ["Token"] = "token-needle", ["Other"] = "ok" } });
        var json = sink.Json();
        foreach (var secret in new[] { "pw-needle", "api-needle", "cookie-needle", "auth-needle", "secret-needle", "reset-needle", "token-needle" })
            Assert.DoesNotContain(secret, json);
        Assert.Contains("safe", json); Assert.Contains("ok", json);
    }

    [Theory]
    [InlineData(false, false), InlineData(false, true), InlineData(true, false), InlineData(true, true)]
    public async Task NK1_RequestPathActorElapsedStatusAndOneException(bool fail, bool useActivity)
    {
        var sink = new Tang8Sink();
        using var logger = new LoggerConfiguration().MinimumLevel.Information().Enrich.With<RedactionEnricher>()
            .Filter.ByExcluding(e => e.Properties.TryGetValue("SourceContext", out var s)
                && s.ToString().Contains("ExceptionHandlerMiddleware")).WriteTo.Sink(sink).CreateLogger();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.Logging.ClearProviders(); builder.Services.AddSerilog(logger);
        builder.Services.AddControllersWithViews();
        await using var host = builder.Build();
        IApplicationBuilder app = new ApplicationBuilder(host.Services);
        app.UseSerilogRequestLogging(o =>
        {
            o.Logger = logger;
            o.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";
            o.EnrichDiagnosticContext = SystemLogging.EnrichRequest;
            o.GetLevel = (_, _, _) => LogEventLevel.Information;
        });
        string? shown = null;
        app.UseExceptionHandler(new ExceptionHandlerOptions
        {
            SuppressDiagnosticsCallback = _ => true,
            ExceptionHandler = http =>
            {
                var controller = new HomeController(null!) { ControllerContext = new ControllerContext { HttpContext = http } };
                shown = Assert.IsType<ErrorViewModel>(Assert.IsType<ViewResult>(controller.Error()).Model).RequestId;
                http.Response.StatusCode = 500; return Task.CompletedTask;
            }
        });
        app.UseMiddleware<RequestExceptionCaptureMiddleware>();
        app.Run(http =>
        {
            // Simule Authentication: middleware truy cập bên ngoài vẫn phải thấy người dùng cuối cùng.
            http.User = new(new ClaimsIdentity([new(ClaimTypes.NameIdentifier, "actor-123")], "test"));
            if (fail) throw new InvalidOperationException("forced failure");
            http.Response.StatusCode = 201; return Task.CompletedTask;
        });
        using var scope = host.Services.CreateScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider, TraceIdentifier = "fallback-request-123" };
        context.Request.Path = "/Account/ResetPassword";
        context.Request.QueryString = new("?code=reset-needle&apiKey=api-needle");
        context.Request.Method = "POST";
        context.Request.Headers.Cookie = "secret-cookie=cookie-needle";
        context.Request.Headers.Authorization = "Bearer auth-needle";
        context.Request.Body = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("password=pw-needle&code=body-code-needle"));
        context.Connection.RemoteIpAddress = IPAddress.Loopback;
        context.Items["AuditGuestId"] = 77;
        using var activity = useActivity ? new Activity("Request").Start() : null;
        await app.Build()(context);
        var e = Assert.Single(sink.Events, e => e.MessageTemplate.Text.StartsWith("HTTP "));
        Assert.Equal("/Account/ResetPassword", ((ScalarValue)e.Properties["RequestPath"]).Value);
        Assert.Equal(fail ? 500 : 201, ((ScalarValue)e.Properties["StatusCode"]).Value);
        Assert.IsType<double>(((ScalarValue)e.Properties["Elapsed"]).Value);
        Assert.Equal("actor-123", ((ScalarValue)e.Properties["UserId"]).Value);
        Assert.Equal(77, ((ScalarValue)e.Properties["GuestId"]).Value);
        Assert.Equal(activity?.Id ?? context.TraceIdentifier, ((ScalarValue)e.Properties["RequestId"]).Value);
        if (fail)
        {
            Assert.Equal(shown, ((ScalarValue)e.Properties["RequestId"]).Value);
            Assert.Single(sink.Events, x => x.Exception != null); Assert.IsType<InvalidOperationException>(e.Exception);
        }
        else Assert.Null(e.Exception);
        var json = sink.Json();
        foreach (var secret in new[] { "reset-needle", "api-needle", "cookie-needle", "auth-needle", "pw-needle", "body-code-needle" })
            Assert.DoesNotContain(secret, json);
    }

    [Fact]
    public void NK2_NoSensitiveDataLoggingAndNoIpColumn()
    {
        var needle = "Enable" + "SensitiveDataLogging";
        foreach (var folder in new[] { "Services", "Data" })
            foreach (var file in Directory.EnumerateFiles(Path.Combine(Root, folder), "*.cs", SearchOption.AllDirectories))
                Assert.DoesNotContain(needle, File.ReadAllText(file));
        Assert.DoesNotContain(needle, File.ReadAllText(Path.Combine(Root, "Program.cs")));
        Assert.DoesNotContain(typeof(AuditEntry).GetProperties(), p => p.Name.Contains("Ip", StringComparison.OrdinalIgnoreCase));
        // MindSprint gộp migration Tang8_AuditLog vào KhoiTao: kiểm cấu hình bảng trên model thay vì trên migration lịch sử.
        using var db = new AppDbContext(AppDbContextFactory.BuildOptions(DatabaseFixture.ConnectionString));
        var entity = db.Model.FindEntityType(typeof(AuditEntry))!;
        Assert.Equal("AuditEntries", entity.GetTableName());
        Assert.Equal(4, entity.GetIndexes().Count());
        Assert.Equal(500, entity.FindProperty(nameof(AuditEntry.Note))!.GetMaxLength());
        Assert.Equal(100, entity.FindProperty(nameof(AuditEntry.RequestId))!.GetMaxLength());
    }

    [Fact]
    public void NK4_FileAgeRetentionAlsoHandlesSizeRolls()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mindsprint-retention-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var now = new DateTime(2026, 11, 8, 12, 0, 0, DateTimeKind.Utc);
            foreach (var f in new[] { "mindsprint-20261008.log", "mindsprint-20261008_001.log", "mindsprint-20261010.log", "other-20200101.log" })
                File.WriteAllText(Path.Combine(dir, f), "");
            Assert.Equal(2, LogRetentionWorker.PurgeFiles(dir, now));
            Assert.True(File.Exists(Path.Combine(dir, "mindsprint-20261010.log")));
            Assert.True(File.Exists(Path.Combine(dir, "other-20200101.log")));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task NK5_FormMatchesPlainLanguagePagingAndEscaping()
    {
        var filter = new AuditFilter { Email = "<img src=x>", RequestId = "lookup-1", Action = AuditAction.LoginFailed, Page = 2 };
        var page = new AuditPage(filter, [new(new() { At = DateTime.UtcNow, Action = AuditAction.LoginFailed, Result = AuditResult.Failed,
            Note = "<script>bad</script>", RequestId = "lookup-1" }, "Minh (hv.m***@test.vn)", "Tài khoản", null)], 101, 2);
        var html = await Tang4ViewTests.Render("Admin", "Audit", page);
        var form = Assert.Single(Regex.Matches(html, @"<form\b[^>]*>[\s\S]*?</form>").Select(m => m.Value));
        foreach (var p in typeof(AuditFilter).GetProperties().Where(p => p.Name != nameof(AuditFilter.Page)))
            Assert.Contains($"name=\"{p.Name}\"", form);
        Assert.Contains("method=\"get\"", form); Assert.DoesNotContain("<script>bad", html); Assert.DoesNotContain("<img src=x>", html);
        var method = typeof(AdminController).GetMethod("Audit")!;
        Assert.NotNull(method.GetCustomAttribute<HttpGetAttribute>());
        Assert.Equal(typeof(AuditFilter), method.GetParameters().Single(p => !p.IsDefined(typeof(FromServicesAttribute))).ParameterType);
        Assert.Contains(typeof(AdminController).GetCustomAttributes<AuthorizeAttribute>(), a => a.Policy == "SystemAdmin");
        var source = File.ReadAllText(Path.Combine(Root, "Views/Admin/Audit.cshtml"));
        Assert.All(PlainLanguageAudit.UserTexts("Audit.cshtml", source), text => Assert.False(PlainLanguageAudit.HasJargon(text), text));
        foreach (var a in Enum.GetValues<AuditAction>()) Assert.False(PlainLanguageAudit.HasJargon(a.Text()), a.Text());
        var next = filter.PageUrl("/Admin/Audit", 3);
        Assert.StartsWith("/Admin/Audit?", next); Assert.Contains("Action=4", next); Assert.Contains("Page=3", next); Assert.Contains("RequestId=lookup-1", next);
        Assert.Equal(3, page.Pages);
    }

    [Fact]
    public void NK6_TestScriptsStoreTrxOutputSummaryAndKeep20()
    {
        foreach (var name in new[] { "run-tests.ps1", "run-tests.sh" })
        {
            var source = File.ReadAllText(Path.Combine(Root, "tools", name));
            foreach (var needle in new[] { "dotnet test tests/MindSprintAI_ASP.NET_MVC.Tests", "trx", "console.log", "summary.txt", "20", "test_exit_code" })
                Assert.Contains(needle, name.EndsWith("ps1") ? source.Replace("testExitCode", "test_exit_code") : source);
        }
        // MindSprint: .gitignore ở gốc repo, trên thư mục project một cấp.
        Assert.Contains("TestResults/", File.ReadAllText(Path.Combine(Root, "..", ".gitignore")));
    }

    [Fact]
    public void NK1_G0ExcludesOnlySerilogMessageTemplate()
    {
        var logConfig = "app.UseSerilogRequestLogging(o => { o.MessageTemplate = \"HTTP {RequestPath}\"; });";
        Assert.DoesNotContain(PlainLanguageAudit.UserTexts("Program.cs", logConfig), PlainLanguageAudit.HasJargon);
        Assert.Contains(PlainLanguageAudit.UserTexts("Program.cs", "throw new BusinessException(\"HTTP sai\");"), PlainLanguageAudit.HasJargon);
        Assert.Contains(PlainLanguageAudit.UserTexts("Program.cs", "o.MessageTemplate = \"HTTP sai\";"), PlainLanguageAudit.HasJargon);
    }

    [Fact]
    public void NK3_DisableMarsPreservesDatabaseAndEnablesSavepoints()
    {
        var configured = AppDbContextFactory.AuditConnectionString("Server=localhost;Database=MindSprintAI_Tests;Integrated Security=true;MultipleActiveResultSets=true");
        var settings = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(configured);
        Assert.False(settings.MultipleActiveResultSets); Assert.Equal("MindSprintAI_Tests", settings.InitialCatalog);
        Assert.Equal("localhost", settings.DataSource); Assert.True(settings.IntegratedSecurity);
    }
}

// (NK3–NK5) Mọi ca thực thi SQL đều thuộc collection db.
[Collection("db")]
public class Tang8Tests
{
    private sealed class AuditScope : IDisposable
    {
        private readonly ServiceProvider provider;
        public AuditLog Log { get; }
        public DefaultHttpContext Http { get; } = new();
        public AuditScope(Harness h, string? userId = null, bool guest = false)
        {
            var services = new ServiceCollection();
            services.AddScoped(_ => DatabaseFixture.NewContext());
            provider = services.BuildServiceProvider();
            Http.TraceIdentifier = Guid.NewGuid().ToString("N"); Http.Request.Method = "POST";
            if (userId != null) Http.User = new(new ClaimsIdentity(guest
                ? [new(ClaimTypes.NameIdentifier, userId), new(GuestAccess.Claim, "true")]
                : [new(ClaimTypes.NameIdentifier, userId)], "test"));
            Log = new(h.Db, new HttpContextAccessor { HttpContext = Http }, provider.GetRequiredService<IServiceScopeFactory>(), h.Clock,
                NullLogger<AuditLog>.Instance);
            Log.Attach();
        }
        public void Dispose() => provider.Dispose();
    }

    [Fact]
    public async Task NK3_RepresentativeServicesOneEntryPerActionTarget()
    {
        using var h = new Harness(); var user = await h.UserAsync("GV");
        using var audit = new AuditScope(h, user);
        var set = await h.Library.CreateStudySetAsync("Riêng tư", user);
        var quiz = await h.Library.CreateQuizAsync(set.Id, "Quiz", user);
        await h.Library.SaveChoiceQuestionAsync(quiz.Id, null, null, Harness.Single(), user);
        await h.Library.MarkReadyAsync(quiz.Id, user);
        var room = await h.Classrooms.CreateAsync("Lớp", ClassJoinPolicy.Immediate, user);
        var assignment = await h.Assignments.CreatePracticeAsync(room.Id, quiz.Id, new(null, h.Clock.UtcNow.AddDays(2), 3), user);
        var student = await h.UserAsync("HV");
        await h.Classrooms.JoinByCodeAsync((await h.Codes.GetActiveAsync(JoinCodeTarget.Classroom, room.Id))!.Code, student);
        var a = await h.Attempts.StartAsync(assignment.Id, student);
        await h.Attempts.FinishAsync(a.Id, student);
        var cards = new FlashcardService(h.Db, h.Library, h.Clock, h.Notifier);
        await cards.SaveAsync(set.Id, null, "Nội dung riêng không được ghi", "Đáp án riêng", user);
        var admin = (await h.Db.Users.FindAsync(user))!; admin.IsSystemAdmin = true; await h.Db.SaveChangesAsync();
        var target = await h.UserAsync("Khác");
        await new AdminService(h.Db, h.Clock).ChangeRoleAsync(target, true, "Hợp lệ", user);
        var rows = await h.Db.AuditEntries.Where(e => e.RequestId == audit.Http.TraceIdentifier).ToListAsync();
        foreach (var action in new[] { AuditAction.StudySetCreated, AuditAction.QuizCreated, AuditAction.QuestionCreated,
            AuditAction.ClassroomCreated, AuditAction.AssignmentCreated, AuditAction.AttemptStarted, AuditAction.AttemptSubmitted,
            AuditAction.FlashcardCreated, AuditAction.AdminGranted })
        {
            var row = Assert.Single(rows, e => e.Action == action);
            Assert.Equal(AuditResult.Success, row.Result); Assert.Equal(AuditActorKind.User, row.ActorKind);
            Assert.NotEqual("0", row.TargetId); Assert.Equal("", row.Note);
        }
        Assert.Equal(user, rows.Single(e => e.Action == AuditAction.StudySetCreated).ActorUserId);
        Assert.Equal(student, rows.Single(e => e.Action == AuditAction.AttemptStarted).ActorUserId);
        Assert.Equal(set.Id.ToString(), rows.Single(e => e.Action == AuditAction.StudySetCreated).TargetId);
        Assert.All(rows.GroupBy(e => (e.Action, e.TargetType, e.TargetId)), g => Assert.Single(g));
        var json = JsonSerializer.Serialize(rows); Assert.DoesNotContain("Nội dung riêng", json); Assert.DoesNotContain("Đáp án riêng", json);
    }

    [Fact]
    public async Task NK3_AuditRollsBackWithBusinessTransaction()
    {
        using var h = new Harness(); var user = await h.UserAsync("Rollback"); using var audit = new AuditScope(h, user);
        int id;
        await using (var tx = await h.Db.Database.BeginTransactionAsync())
        {
            var set = await h.Library.CreateStudySetAsync("Rollback", user); id = set.Id;
            Assert.True(await h.Db.AuditEntries.AnyAsync(e => e.TargetId == id.ToString() && e.RequestId == audit.Http.TraceIdentifier));
            await tx.RollbackAsync();
        }
        using var verify = DatabaseFixture.NewContext();
        Assert.False(await verify.StudySets.AnyAsync(s => s.Id == id));
        Assert.False(await verify.AuditEntries.AnyAsync(e => e.RequestId == audit.Http.TraceIdentifier));
    }

    [Fact]
    public async Task NK3_IdentityLifecycleAndCredentialsNeverEnterAudit()
    {
        using var h = new Harness(); using var identity = new TwoFactorIdentity(h); using var audit = new AuditScope(h);
        var user = new ApplicationUser { UserName = Guid.NewGuid() + "@example.test", DisplayName = "Học viên" };
        user.Email = user.UserName;
        Assert.True((await identity.Users.CreateAsync(user, "Valid123!")).Succeeded);
        var code = await identity.Users.GenerateEmailConfirmationTokenAsync(user);
        Assert.True((await identity.Users.ConfirmEmailAsync(user, code)).Succeeded);
        Assert.True((await identity.Users.ChangePasswordAsync(user, "Valid123!", "NewPassword123!")).Succeeded);
        var rows = await h.Db.AuditEntries.Where(e => e.RequestId == audit.Http.TraceIdentifier).ToListAsync();
        foreach (var action in new[] { AuditAction.Registered, AuditAction.EmailConfirmed, AuditAction.PasswordChanged })
        {
            var entry = Assert.Single(rows, r => r.Action == action);
            Assert.Equal(user.Id, entry.ActorUserId); Assert.Equal(user.Id, entry.TargetId); Assert.Equal(AuditResult.Success, entry.Result);
        }
        var serialized = JsonSerializer.Serialize(rows);
        foreach (var secret in new[] { user.Email!, "Valid123!", "NewPassword123!", code, user.PasswordHash! }) Assert.DoesNotContain(secret, serialized);
    }

    [Fact]
    public async Task NK3_InvitationsTransfersApiKeyAuditAtServiceBoundary()
    {
        using var h = new Harness(); var scenario = await h.ScenarioAsync();
        var recipient = await h.UserAsync("GV nhận"); var user = (await h.Db.Users.FindAsync(recipient))!; user.EmailConfirmed = true;
        await h.Db.SaveChangesAsync();
        using var audit = new AuditScope(h, scenario.Lecturer);
        await h.Classrooms.AddAssistantAsync(scenario.Room.Id, user.Email!, scenario.Lecturer);
        var invitation = await h.Db.Invitations.SingleAsync(i => i.ClassroomId == scenario.Room.Id && i.Kind == InvitationKind.Assistant);
        await h.Invitations.DecideAssistantAsync(invitation.Id, true, recipient);
        var transfers = new ClassTransferService(h.Db, h.Classrooms, new(h.Db, h.Clock), new(h.Db, h.Clock, h.Notifier), h.Clock, h.Invitations);
        var transfer = await transfers.OfferAsync(scenario.Room.Id, user.Email!, true, scenario.Lecturer);
        await transfers.DecideAsync(transfer.Id, false, recipient);
        var protection = new EphemeralDataProtectionProvider();
        var keys = new UserApiKeyService(h.Db, protection, h.Clock);
        var key = "AIza" + new string('a', 35); await keys.SaveAsync(key, scenario.Lecturer, true); await keys.DeleteAsync(scenario.Lecturer);
        var rows = await h.Db.AuditEntries.Where(e => e.RequestId == audit.Http.TraceIdentifier).ToListAsync();
        foreach (var action in new[] { AuditAction.AssistantInvited, AuditAction.AssistantAccepted, AuditAction.TransferOffered,
            AuditAction.TransferDeclined, AuditAction.GeminiSaved, AuditAction.GeminiDeleted })
            Assert.Single(rows, e => e.Action == action && e.Result == AuditResult.Success);
        Assert.Equal(recipient, rows.Single(e => e.Action == AuditAction.AssistantAccepted).ActorUserId);
        Assert.DoesNotContain(key, JsonSerializer.Serialize(rows)); Assert.DoesNotContain(user.Email!, JsonSerializer.Serialize(rows));
    }

    [Fact]
    public async Task NK3_ExcelImportAndCloneWriteOnlyObjectIdentifiers()
    {
        using var h = new Harness(); var user = await h.UserAsync("GV"); using var audit = new AuditScope(h, user);
        var set = await h.Library.CreateStudySetAsync("Bộ học", user); var quiz = await h.Library.CreateQuizAsync(set.Id, "Quiz", user);
        var imports = new ExcelImportService(h.Db, h.Library, h.Clock);
        await imports.SaveAsync(new ImportPreview { QuizId = quiz.Id, FileName = "private-file.xlsx", Rows =
            [new() { Sheet = "Flashcard", Row = 2, Values = ["private-front", "private-back"] }] }, user);
        var copy = await new StudySetCloneService(h.Db, h.Clock).CloneAsync(set.Id, new([quiz.Id], true, true), user);
        var rows = await h.Db.AuditEntries.Where(e => e.RequestId == audit.Http.TraceIdentifier).ToListAsync();
        Assert.Equal(quiz.Id.ToString(), Assert.Single(rows, e => e.Action == AuditAction.ExcelImported).TargetId);
        Assert.Equal(copy.Id.ToString(), Assert.Single(rows, e => e.Action == AuditAction.StudySetCloned).TargetId);
        foreach (var text in new[] { "private-front", "private-back", "private-file.xlsx" }) Assert.DoesNotContain(text, JsonSerializer.Serialize(rows));
    }

    [Fact]
    public async Task NK3_SeparateAuditFailureDoesNotBreakAction()
    {
        using var h = new Harness(); var user = await h.UserAsync("Failure");
        using var broken = new ServiceCollection().AddScoped<AppDbContext>(_ => throw new InvalidOperationException("unavailable"))
            .BuildServiceProvider();
        var log = new AuditLog(h.Db, new HttpContextAccessor(), broken.GetRequiredService<IServiceScopeFactory>(), h.Clock, NullLogger<AuditLog>.Instance);
        log.Attach();
        await log.WriteSeparateAsync(AuditAction.LoginFailed, "User", user, user, AuditResult.Failed);
        var set = await h.Library.CreateStudySetAsync("Still saved", user);
        Assert.True(await h.Db.StudySets.AnyAsync(s => s.Id == set.Id));
    }

    [Fact]
    public async Task NK3_RestoreHookCoversClassroomAndQuestionWithoutNewBusinessRoutes()
    {
        using var h = new Harness(); var scenario = await h.ScenarioAsync();
        var question = scenario.Quiz.Questions.First();
        scenario.Room.DeletedAt = h.Clock.UtcNow; question.DeletedAt = h.Clock.UtcNow; await h.Db.SaveChangesAsync();
        using var audit = new AuditScope(h, scenario.Lecturer);
        scenario.Room.DeletedAt = null; question.DeletedAt = null; await h.Db.SaveChangesAsync();
        var rows = await h.Db.AuditEntries.Where(e => e.RequestId == audit.Http.TraceIdentifier).ToListAsync();
        Assert.Equal(scenario.Room.Id.ToString(), Assert.Single(rows, e => e.Action == AuditAction.ClassroomRestored).TargetId);
        Assert.Equal(question.Id.ToString(), Assert.Single(rows, e => e.Action == AuditAction.QuestionRestored).TargetId);
    }

    [Fact]
    public async Task NK3_FailedLoginAndCentralDeniedAreSeparateAndDeduplicated()
    {
        using var h = new Harness(); using var identity = new SafetyIdentity(h);
        using var audit = new AuditScope(h);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var turnstile = new TurnstileService(new HttpClient(), new ConfigurationBuilder().Build());
        var login = new LoginService(identity.Users, identity.SignIn, cache, turnstile, identity.Locks, h.Clock, audit: audit.Log);
        Assert.False((await login.LoginAsync("unknown@example.test", "password-needle", false, null, "127.0.0.1")).Succeeded);
        var row = Assert.Single(await h.Db.AuditEntries.Where(e => e.RequestId == audit.Http.TraceIdentifier).ToListAsync());
        Assert.Equal(AuditAction.LoginFailed, row.Action); Assert.Equal(AuditResult.Failed, row.Result);
        using var denied = new AuditScope(h, await h.UserAsync("NonAdmin"));
        var action = new ActionContext(denied.Http, new RouteData(), new ActionDescriptor());
        action.RouteData.Values["controller"] = "Admin"; action.RouteData.Values["action"] = "Role";
        var context = new ExceptionContext(action, []) { Exception = new ForbiddenException() };
        var filter = new BusinessExceptionFilter(null!, denied.Log, NullLogger<BusinessExceptionFilter>.Instance);
        await filter.OnExceptionAsync(context); await filter.OnExceptionAsync(context);
        Assert.Equal(403, Assert.IsType<ViewResult>(context.Result).StatusCode);
        var deniedRow = Assert.Single(await h.Db.AuditEntries.Where(e => e.RequestId == denied.Http.TraceIdentifier).ToListAsync());
        Assert.Equal(AuditResult.Denied, deniedRow.Result); Assert.Equal("Admin/Role", deniedRow.TargetId);
        Assert.DoesNotContain("password-needle", JsonSerializer.Serialize(row));
    }

    [Fact]
    public async Task NK3_SeparateFailureDoesNotCommitPendingBusinessChanges()
    {
        using var h = new Harness(); var user = await h.UserAsync("Pending"); using var audit = new AuditScope(h, user);
        h.Db.StudySets.Add(new() { Title = "Pending", OwnerId = user });
        await audit.Log.WriteSeparateAsync(AuditAction.ActionRejected, "ControllerAction", "StudySets/Create", result: AuditResult.Failed);
        using var verify = DatabaseFixture.NewContext();
        Assert.False(await verify.StudySets.AnyAsync(s => s.OwnerId == user));
        Assert.True(await verify.AuditEntries.AnyAsync(e => e.RequestId == audit.Http.TraceIdentifier));
    }

    private sealed class FailAuditInterceptor : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<System.Data.Common.DbDataReader>> ReaderExecutingAsync(
            System.Data.Common.DbCommand command, CommandEventData eventData,
            InterceptionResult<System.Data.Common.DbDataReader> result, CancellationToken ct = default)
        {
            if (command.CommandText.Contains("INSERT INTO [AuditEntries]")) throw new InvalidOperationException("audit failure");
            return ValueTask.FromResult(result);
        }
    }

    [Fact]
    public async Task NK3_AuditInsertFailureKeepsBusinessSave()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>(AppDbContextFactory.BuildOptions(DatabaseFixture.ConnectionString))
            .AddInterceptors(new FailAuditInterceptor()).Options;
        await using var db = new AppDbContext(options);
        var user = new ApplicationUser { DisplayName = "Audit failure", UserName = Guid.NewGuid().ToString() }; db.Users.Add(user); await db.SaveChangesAsync();
        var set = new StudySet { OwnerId = user.Id, Title = "Business saved" }; db.StudySets.Add(set);
        db.AuditEntries.Add(new() { At = DateTime.UtcNow, Action = AuditAction.StudySetCreated, TargetType = "StudySet", TargetId = "1" });
        await db.SaveChangesAsync();
        using var verify = DatabaseFixture.NewContext(); Assert.True(await verify.StudySets.AnyAsync(s => s.Id == set.Id));
    }

    [Fact]
    public async Task NK4_Purge366DaysKeep364AndBoundary365()
    {
        using var h = new Harness(); var request = Guid.NewGuid().ToString();
        foreach (var days in new[] { 366, 365, 364 })
            h.Db.AuditEntries.Add(new() { At = h.Clock.UtcNow.AddDays(-days), Action = AuditAction.LoginSucceeded, RequestId = request });
        await h.Db.SaveChangesAsync();
        await LogRetentionWorker.PurgeAsync(h.Db, h.Clock.UtcNow);
        var rows = await h.Db.AuditEntries.AsNoTracking().Where(e => e.RequestId == request).ToListAsync();
        Assert.Equal(2, rows.Count); Assert.All(rows, e => Assert.True(e.At >= h.Clock.UtcNow.AddDays(-365)));
    }

    [Fact]
    public async Task NK5_AdminGuardFiltersVnDatesPagingAndViewAudit()
    {
        using var h = new Harness(); var admin = await h.UserAsync("Admin"); var actor = await h.UserAsync("Người làm");
        (await h.Db.Users.FindAsync(admin))!.IsSystemAdmin = true;
        var normalized = (await h.Db.Users.FindAsync(actor))!.NormalizedEmail = "ACTOR@EXAMPLE.TEST";
        (await h.Db.Users.FindAsync(actor))!.Email = "actor@example.test";
        for (var i = 0; i < 52; i++) h.Db.AuditEntries.Add(new() { At = new DateTime(2026, 11, 7, 17, 0, 0, DateTimeKind.Utc).AddMinutes(i),
            ActorKind = AuditActorKind.User, ActorUserId = actor, Action = AuditAction.StudySetCreated, Result = AuditResult.Success,
            TargetType = "StudySet", TargetId = "999999999", RequestId = "filter-" + admin });
        // Mỗi dòng nhiễu chỉ khác MỘT bộ lọc, để không vô tình bỏ qua một điều kiện.
        foreach (var field in new[] { "Email", "From", "To", "Action", "Result", "TargetType", "TargetId", "RequestId" })
        {
            var other = new AuditEntry { At = new DateTime(2026, 11, 7, 18, 0, 0, DateTimeKind.Utc), ActorUserId = actor,
                ActorKind = AuditActorKind.User, Action = AuditAction.StudySetCreated, Result = AuditResult.Success,
                TargetType = "StudySet", TargetId = "999999999", RequestId = "filter-" + admin };
            switch (field)
            {
                case "Email": other.ActorUserId = admin; break;
                case "From": other.At = new DateTime(2026, 11, 7, 16, 59, 59, DateTimeKind.Utc); break;
                case "To": other.At = new DateTime(2026, 11, 8, 17, 0, 0, DateTimeKind.Utc); break;
                case "Action": other.Action = AuditAction.StudySetEdited; break;
                case "Result": other.Result = AuditResult.Denied; break;
                case "TargetType": other.TargetType = "Quiz"; break;
                case "TargetId": other.TargetId = "999999998"; break;
                case "RequestId": other.RequestId = "unrelated"; break;
            }
            h.Db.AuditEntries.Add(other);
        }
        await h.Db.SaveChangesAsync();
        using var audit = new AuditScope(h, admin); var service = new AuditQueryService(h.Db, audit.Log, h.Clock);
        var forbidden = await Assert.ThrowsAsync<ForbiddenException>(() => service.QueryAsync(actor, new()));
        var rejected = new ExceptionContext(new ActionContext(audit.Http, new RouteData(), new ActionDescriptor()), []) { Exception = forbidden };
        new BusinessExceptionFilter(null!).OnException(rejected);
        Assert.Equal(403, Assert.IsType<ViewResult>(rejected.Result).StatusCode);
        var filter = new AuditFilter { Email = normalized, From = new(2026, 11, 8), To = new(2026, 11, 8),
            Action = AuditAction.StudySetCreated, Result = AuditResult.Success, TargetType = "StudySet", TargetId = "999999999", RequestId = "filter-" + admin };
        var page = await service.QueryAsync(admin, filter);
        Assert.Equal(52, page.Total); Assert.Equal(50, page.Rows.Count); Assert.Null(page.Rows[0].Url);
        Assert.Contains("acto***@example.test", page.Rows[0].Actor); Assert.DoesNotContain("actor@example.test", page.Rows[0].Actor);
        filter.Page = 2; var next = await service.QueryAsync(admin, filter); Assert.Equal(2, next.Rows.Count);
        Assert.True(page.Rows[^1].Entry.At > next.Rows[0].Entry.At);
        Assert.Single(await h.Db.AuditEntries.Where(e => e.RequestId == audit.Http.TraceIdentifier && e.Action == AuditAction.AdminViewedAudit).ToListAsync());
        var live = await h.Library.CreateStudySetAsync("Có đường dẫn", admin);
        var liveFilter = new AuditFilter { RequestId = audit.Http.TraceIdentifier, Action = AuditAction.StudySetCreated };
        var livePage = await service.QueryAsync(admin, liveFilter);
        Assert.Equal($"/Flashcards/Editor/{live.Id}", Assert.Single(livePage.Rows).Url);
        await h.Library.DeleteStudySetAsync(live.Id, admin);
        Assert.Null(Assert.Single((await service.QueryAsync(admin, liveFilter)).Rows).Url);
    }

    [Theory]
    [InlineData(false), InlineData(true)]
    public async Task NK3_ActorsGuestAndSystem(bool guest)
    {
        using var h = new Harness(); var id = guest ? await h.UserAsync("Khách") : null;
        using var audit = new AuditScope(h, id, guest);
        if (guest) audit.Http.Items["AuditGuestId"] = 321;
        audit.Log.Add(AuditAction.AttemptStarted, "Attempt", 5);
        audit.Log.Add(AuditAction.AttemptStarted, "Attempt", 5);
        await h.Db.SaveChangesAsync();
        var row = Assert.Single(await h.Db.AuditEntries.Where(e => e.RequestId == audit.Http.TraceIdentifier).ToListAsync());
        Assert.Null(row.ActorUserId); Assert.Equal(guest ? "321" : null, row.ActorGuestId);
        Assert.Equal(guest ? AuditActorKind.Guest : AuditActorKind.System, row.ActorKind);
    }
}
