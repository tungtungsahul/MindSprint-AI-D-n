using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MindSprintAI_ASP.NET_MVC.Controllers;
using MindSprintAI_ASP.NET_MVC.Data;
using MindSprintAI_ASP.NET_MVC.Data.Entities;
using MindSprintAI_ASP.NET_MVC.Models;
using MindSprintAI_ASP.NET_MVC.Services;

namespace MindSprintAI_ASP.NET_MVC.Tests;

public class Tang5bPureTests
{
    [Fact]
    public void AnNoiDungKetThucTestChamNhapVaTinhLaiNhan()
    {
        var a = Tang4RuleTests.Build(0); a.Participant.Assignment.Mode = AssignmentMode.Test;
        var q = Tang4RuleTests.Add(a, QuestionType.MultipleChoice);
        q.SnapshotJson = (QuestionSnapshot.FromJson(q.SnapshotJson) with { Options = [new(1, 0, "A", true), new(2, 1, "B", true)] }).ToJson();
        q.DraftJson = "{\"Selected\":[1,2]}";
        Assert.Equal(4, (int)CloseReason.HiddenByAdmin);
        Assert.Equal(7, (int)AttemptEndReason.HiddenByAdmin);
        Assert.Equal(512, (int)CompletionLabels.HiddenByAdmin);
        AttemptRules.End(a, a.Participant, AttemptEndReason.HiddenByAdmin, Tang4cPureTests.Now);
        Assert.Equal(1, a.FirstScore); Assert.Single(q.Submissions);
        Assert.Contains("Kết thúc do quản trị viên ẩn nội dung", MindSprintAI_ASP.NET_MVC.Models.DisplayText.Of(a.Participant.CompletionLabels));
        Assert.Equal("Kết thúc do quản trị viên ẩn nội dung", MindSprintAI_ASP.NET_MVC.Models.DisplayText.Of(a.EndReason));
        AttemptRules.End(a, a.Participant, AttemptEndReason.Submitted, Tang4cPureTests.Now);
        Assert.Equal(AttemptEndReason.HiddenByAdmin, a.EndReason); Assert.Single(q.Submissions);
        a.Status = AttemptStatus.InProgress;
        AttemptRules.End(a, a.Participant, AttemptEndReason.Submitted, Tang4cPureTests.Now);
        Assert.False(a.Participant.CompletionLabels.HasFlag(CompletionLabels.HiddenByAdmin));
    }

    [Fact]
    public async Task ReviewBoAnKhongTraNoiDungNhungGiuDiemVaMaHoaLyDo()
    {
        var a = Tang4RuleTests.Build(1); a.Status = AttemptStatus.Ended; a.FirstScore = 1; a.MaxScore = 1;
        a.Participant.Assignment.Quiz = new() { StudySet = new() { HiddenByAdminAt = Tang4cPureTests.Now } };
        var questions = AttemptService.ProjectQuestions(a, false);
        Assert.Empty(questions);
        var html = WebUtility.HtmlDecode(await Tang4ViewTests.Render("Learn", "Review", a, new()
        { ["Questions"] = questions, ["ShowScores"] = true, ["HiddenContentNotice"] = "Nội dung bài đã bị quản trị viên ẩn: <script>vi phạm</script>" }));
        Assert.Contains("Nội dung bài đã bị quản trị viên ẩn:", html); Assert.Contains("10,00", html);
        Assert.DoesNotContain("BÍ_MẬT", html); Assert.DoesNotContain("Câu 1.", html);
        var encoded = await Tang4ViewTests.Render("Learn", "Review", a, new()
        { ["Questions"] = questions, ["ShowScores"] = true, ["HiddenContentNotice"] = "Nội dung bài đã bị quản trị viên ẩn: <script>" });
        Assert.DoesNotContain("<script>", encoded); Assert.Contains("&lt;script&gt;", encoded);
    }

    [Fact]
    public async Task TrangBaiGiaoVaGiangVienHienLyDoAnCoMaHoa()
    {
        var a = new Assignment { Status = AssignmentStatus.Closed, CloseReason = CloseReason.HiddenByAdmin };
        var model = new LearnerAssignmentModel { Assignment = a, Attempts = [], CanStart = false,
            HiddenContentNotice = "Nội dung bài đã bị quản trị viên ẩn: <script>vi phạm</script>" };
        var html = await Tang4ViewTests.Render("Learn", "Assignment", model);
        Assert.Contains("Nội dung bài đã bị quản trị viên ẩn:", WebUtility.HtmlDecode(html));
        Assert.DoesNotContain("<script>", html); Assert.Contains("&lt;script&gt;", html);
        html = await Tang4ViewTests.Render("Assignments", "Details", new AssignmentDetailsModel
        { Assignment = a, Code = "12345678", OpenAttempts = 0, Rows = [], HiddenContentReason = "<script>vi phạm</script>" });
        Assert.Contains("Đã đóng vì quản trị viên ẩn nội dung:", WebUtility.HtmlDecode(html));
        Assert.DoesNotContain("<script>", html); Assert.Contains("&lt;script&gt;", html);
    }

    [Fact]
    public async Task GioiHan100MoiPhutTheoIpVaTrang429TiengViet()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddAccountRateLimits();
        await using var app = builder.Build();
        var pipeline = new ApplicationBuilder(app.Services);
        pipeline.UseRateLimiter();
        pipeline.Run(_ => Task.CompletedTask);
        var request = pipeline.Build();
        for (var i = 0; i < 101; i++)
        {
            var http = new DefaultHttpContext { RequestServices = app.Services };
            http.Connection.RemoteIpAddress = IPAddress.Loopback;
            http.Request.Method = "POST"; http.Request.Path = "/Account/Login";
            http.Response.Body = new MemoryStream();
            http.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(new EnableRateLimitingAttribute("account-post")), "Login"));
            await request(http);
            Assert.Equal(i < 100 ? 200 : 429, http.Response.StatusCode);
            if (i == 100)
            {
                http.Response.Body.Position = 0;
                Assert.Contains("Bạn thao tác quá nhanh", await new StreamReader(http.Response.Body).ReadToEndAsync());
                Assert.Equal("60", http.Response.Headers.RetryAfter);
            }
        }
        var another = new DefaultHttpContext { RequestServices = app.Services };
        another.Connection.RemoteIpAddress = IPAddress.Parse("127.0.0.2");
        another.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(new EnableRateLimitingAttribute("account-post")), "Join"));
        await request(another); Assert.Equal(200, another.Response.StatusCode);
    }

    [Theory]
    [InlineData(false, true, 2), InlineData(false, false, 1)]
    public void KhongTuGoHoacGoAdminCuoi(bool grant, bool self, int count) =>
        Assert.Throws<BusinessException>(() => AdminService.ValidateRevoke(grant, self, count));

    [Theory]
    [InlineData(null), InlineData(""), InlineData("   ")]
    public void KhoaViPhamCanLyDo(string? reason) => Assert.Throws<BusinessException>(() =>
        AccountLockService.Validate(AccountLockKind.Violation, reason, null, Tang4cPureTests.Now));

    [Fact]
    public void KhoaViPhamKhongNhanHanDaQua() => Assert.Throws<BusinessException>(() =>
        AccountLockService.Validate(AccountLockKind.Violation, "Vi phạm", Tang4cPureTests.Now, Tang4cPureTests.Now));

    [Fact]
    public void KetThucDoKhoaChamNhapTestVaNhamDung()
    {
        var a = Tang4RuleTests.Build(0); a.Participant.Assignment.Mode = AssignmentMode.Test;
        var q = Tang4RuleTests.Add(a, QuestionType.MultipleChoice);
        q.SnapshotJson = (QuestionSnapshot.FromJson(q.SnapshotJson) with { Options = [new(1, 0, "A", true), new(2, 1, "B", true)] }).ToJson();
        q.DraftJson = "{\"Selected\":[1,2]}";
        AttemptRules.End(a, a.Participant, AttemptEndReason.AccountLocked, Tang4cPureTests.Now);
        Assert.Equal(AttemptEndReason.AccountLocked, a.EndReason);
        Assert.True(a.Participant.CompletionLabels.HasFlag(CompletionLabels.StoppedByLock));
        Assert.Equal(1, a.FirstScore); Assert.Single(q.Submissions);
        a.Status = AttemptStatus.InProgress;
        AttemptRules.End(a, a.Participant, AttemptEndReason.Submitted, Tang4cPureTests.Now);
        Assert.False(a.Participant.CompletionLabels.HasFlag(CompletionLabels.StoppedByLock));
    }

    [Theory]
    [InlineData(false), InlineData(true)]
    public async Task WidgetChiTaiKhiCan(bool required)
    {
        var html = await Tang4ViewTests.Render("Account", "Login", new MindSprintAI_ASP.NET_MVC.Models.LoginModel(), new()
        { ["ChallengeRequired"] = required, ["TurnstileSiteKey"] = "site-test" });
        Assert.Equal(required, html.Contains("https://challenges.cloudflare.com/turnstile/v0/api.js"));
        Assert.DoesNotContain("SecretKey", html);
    }

    [Fact]
    public async Task BaoCaoHienDungNoiDungVaMaHoaHtml()
    {
        var html = await Tang4ViewTests.Render("Admin", "Report", new ReportDetails(new ContentReport { Id = 3, TargetType = ReportTarget.User },
            new("<script>alert('x')</script>", [], [])));
        Assert.DoesNotContain("<script>alert", html); Assert.Contains("&lt;script&gt;", html);
        Assert.DoesNotContain("FirstScore", html); Assert.DoesNotContain("StudySets/Quiz", html);
    }

    [Fact]
    public void ModelKhongThieuSoVoiSnapshot()
    {
        using var db = DatabaseFixture.NewContext();
        var model = db.GetService<Microsoft.EntityFrameworkCore.Metadata.IDesignTimeModel>().Model;
        var snapshot = db.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrationsAssembly>().ModelSnapshot!.Model;
        var initialized = db.GetService<Microsoft.EntityFrameworkCore.Infrastructure.IModelRuntimeInitializer>().Initialize(snapshot, true);
        Assert.False(db.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrationsModelDiffer>().HasDifferences(initialized.GetRelationalModel(), model.GetRelationalModel()));
        // MindSprint gộp migration Tang5b vào KhoiTao: phần "chỉ thêm" do Tang2RuleTests.Migration_SauKhoiTao_ChiThemKhongXoaHayDoiTen kiểm.
    }

    [Theory]
    [InlineData(typeof(AccountController), "Login")]
    [InlineData(typeof(AccountController), "ForgotPassword")]
    [InlineData(typeof(ClassroomsController), "Join")]
    public void PostNhapLieuCoRateLimit(Type controller, string action)
    {
        var method = Assert.Single(controller.GetMethods(), m => m.Name == action && m.IsDefined(typeof(HttpPostAttribute), true));
        Assert.NotNull(method.GetCustomAttributes(typeof(EnableRateLimitingAttribute), true).SingleOrDefault());
        Assert.Contains(typeof(AdminController).GetCustomAttributes(typeof(AuthorizeAttribute), true), a => ((AuthorizeAttribute)a).Policy == "SystemAdmin");
    }

    internal sealed class CloudflareFake(bool success = true, bool networkError = false) : HttpMessageHandler
    {
        public string? Posted { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("https://challenges.cloudflare.com/turnstile/v0/siteverify", request.RequestUri!.ToString());
            Assert.Equal(HttpMethod.Post, request.Method);
            Posted = await request.Content!.ReadAsStringAsync(cancellationToken);
            if (networkError) throw new HttpRequestException("Mạng lỗi");
            return new(HttpStatusCode.OK) { Content = new StringContent(success ? "{\"success\":true}" : "{\"success\":false}") };
        }
    }

    internal static TurnstileService Turnstile(bool success = true, bool networkError = false) => new(
        new HttpClient(new CloudflareFake(success, networkError)), new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["Turnstile:SiteKey"] = "site-test", ["Turnstile:SecretKey"] = "secret-test" }).Build());

    [Theory]
    [InlineData(true, false, true), InlineData(false, false, false), InlineData(true, true, false)]
    public async Task TurnstileChiTinKetQuaMayChu(bool success, bool networkError, bool expected)
    {
        var result = await Turnstile(success, networkError).VerifyAsync("token-gia", "127.0.0.1");
        Assert.Equal(expected, result.Verified);
        if (!expected) Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    [Fact]
    public async Task GuiDuBaTruongVaKhongChapNhanTokenRong()
    {
        var handler = new CloudflareFake();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["Turnstile:SiteKey"] = "site", ["Turnstile:SecretKey"] = "secret" }).Build();
        var service = new TurnstileService(new HttpClient(handler), config);
        Assert.False((await service.VerifyAsync(null, "127.0.0.1")).Verified);
        Assert.Null(handler.Posted);
        Assert.True((await service.VerifyAsync("abc", "127.0.0.1")).Verified);
        Assert.Contains("secret=secret", handler.Posted); Assert.Contains("response=abc", handler.Posted);
        Assert.Contains("remoteip=127.0.0.1", handler.Posted);
    }

    [Fact]
    public async Task ChuaCauHinhThiChan()
    {
        var service = new TurnstileService(new HttpClient(new CloudflareFake()), new ConfigurationBuilder().Build());
        Assert.Equal("Chưa bật xác minh người thật. Hãy báo quản trị viên.", (await service.VerifyAsync("abc", null)).Error); // (G0)
    }
}

// Identity thật + DB riêng; chỉ Cloudflare/email/clock được thay bằng fake.
internal sealed class SafetyIdentity : IDisposable
{
    private readonly ServiceProvider provider;
    private readonly IServiceScope scope;
    public UserManager<ApplicationUser> Users { get; }
    public SignInManager<ApplicationUser> SignIn { get; }
    public AccountLockService Locks { get; }
    public SafetyEmail Email { get; } = new();
    public DefaultHttpContext Http { get; }
    public SafetyIdentity(Harness h)
    {
        var services = new ServiceCollection();
        services.AddLogging(o => o.SetMinimumLevel(LogLevel.Warning));
        services.AddDataProtection().UseEphemeralDataProtectionProvider();
        services.AddSingleton(h.Db); services.AddSingleton<TimeProvider>(h.Clock);
        services.AddSingleton<IEmailSender>(Email); services.AddSingleton(h.Notifier);
        services.AddHttpContextAccessor();
        services.AddIdentity<ApplicationUser, IdentityRole>(o =>
        {
            o.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
            o.SignIn.RequireConfirmedEmail = true; o.Lockout.MaxFailedAccessAttempts = 10;
            o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        }).AddEntityFrameworkStores<AppDbContext>().AddDefaultTokenProviders();
        services.AddAuthentication().AddCookie(TwoFactorService.PasskeyPendingScheme);
        services.ConfigureApplicationCookie(o => o.Events.OnRedirectToAccessDenied = c => { c.Response.StatusCode = 403; return Task.CompletedTask; });
        services.AddScoped<AccountLockService>();
        services.AddScoped<IAuthorizationHandler, SystemAdminHandler>();
        services.AddAuthorization(o => o.AddPolicy("SystemAdmin", p => p.RequireAuthenticatedUser().AddRequirements(new SystemAdminRequirement())));
        provider = services.BuildServiceProvider(); scope = provider.CreateScope();
        Http = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        Http.Request.Scheme = "https"; Http.Request.Host = new("localhost");
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = Http;
        Users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        SignIn = scope.ServiceProvider.GetRequiredService<SignInManager<ApplicationUser>>();
        Locks = scope.ServiceProvider.GetRequiredService<AccountLockService>();
    }
    public async Task<ApplicationUser> PrepareAsync(string id, bool admin = false)
    {
        var user = (await Users.FindByIdAsync(id))!;
        user.UserName = user.Email; user.EmailConfirmed = true; user.LockoutEnabled = true; user.IsSystemAdmin = admin;
        Assert.True((await Users.UpdateAsync(user)).Succeeded);
        Assert.True((await Users.AddPasswordAsync(user, "Valid123!")).Succeeded);
        return user;
    }
    public void Dispose() { scope.Dispose(); provider.Dispose(); }
}

internal sealed class SafetyEmail : IEmailSender
{
    public List<string> Messages { get; } = [];
    public List<string> Subjects { get; } = [];
    public Task SendAsync(string to, string subject, string htmlBody) { Messages.Add(htmlBody); Subjects.Add(subject); return Task.CompletedTask; }
}

[Collection("db")]
public class Tang5bTests
{
    [Fact]
    public async Task NguoiThuongNhan403TaiAdmin()
    {
        using var h = new Harness(); using var identity = new SafetyIdentity(h);
        var user = await identity.PrepareAsync(await h.UserAsync("Minh"));
        var principal = await identity.SignIn.CreateUserPrincipalAsync(user);
        // (51C) cả controller gắn policy SystemAdmin; policy từ chối người thường, nhận Admin.
        Assert.Contains(typeof(AdminController).GetCustomAttributes(true).OfType<AuthorizeAttribute>(), a => a.Policy == "SystemAdmin");
        var auth = identity.Http.RequestServices.GetRequiredService<IAuthorizationService>();
        Assert.False((await auth.AuthorizeAsync(principal, "SystemAdmin")).Succeeded);
        user.IsSystemAdmin = true; await h.Db.SaveChangesAsync();
        Assert.True((await auth.AuthorizeAsync(principal, "SystemAdmin")).Succeeded);
    }

    [Fact]
    public async Task KhoiDongChiBatAdminChoEmailDaTonTai()
    {
        using var h = new Harness(); using var identity = new SafetyIdentity(h);
        var user = await identity.PrepareAsync(await h.UserAsync("Admin"));
        var count = await h.Db.Users.CountAsync();
        await AdminService.BootstrapAsync(h.Db, [user.Email!.ToUpperInvariant(), "missing@test.local"]);
        Assert.True(user.IsSystemAdmin); Assert.Equal(count, await h.Db.Users.CountAsync());
    }

    [Fact]
    public async Task BacBoVaKhoaViPhamTuBaoCaoLuuNguoiXuLy()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(); using var identity = new SafetyIdentity(h);
        var admin = await identity.PrepareAsync(await h.UserAsync("Admin"), true);
        var reports = new ContentReportService(h.Db, h.Clock, h.Assignments);
        var context = new ReportContext(ReportTarget.User, s.Student, s.Assignment.Id);
        var id = await reports.SubmitAsync(context, ContentReportService.Reasons[0], null, s.Lecturer);
        await reports.HandleAsync(id, false, null, admin.Id);
        var row = (await h.Db.ContentReports.FindAsync(id))!;
        Assert.Equal(ReportStatus.Dismissed, row.Status); Assert.Equal(admin.Id, row.HandledById); Assert.Equal(h.Clock.UtcNow, row.HandledAt);
        var next = await reports.SubmitAsync(context, ContentReportService.Reasons[2], null, s.Lecturer);
        await reports.LockReportedUserAsync(next, "Quấy rối", null, admin.Id, identity.Locks);
        Assert.Equal(ReportStatus.Handled, (await h.Db.ContentReports.FindAsync(next))!.Status);
        Assert.Equal(AccountLockKind.Violation, (await identity.Locks.ActiveAsync(s.Student))!.Kind);
        await identity.Locks.UnlockAsync(s.Student, admin.Id);
        Assert.Null(await identity.Locks.ActiveAsync(s.Student));
        await Assert.ThrowsAsync<BusinessException>(() => reports.LockReportedUserAsync(next, "Gửi lặp", null, admin.Id, identity.Locks));
    }

    [Fact]
    public async Task LanThuSauCanTokenKeCaDungMatKhauVaThanhCongDatLai()
    {
        using var h = new Harness(); using var identity = new SafetyIdentity(h);
        var user = await identity.PrepareAsync(await h.UserAsync("Minh"));
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var login = new LoginService(identity.Users, identity.SignIn, cache, Tang5bPureTests.Turnstile(), identity.Locks, h.Clock);
        for (var i = 0; i < 5; i++) Assert.False((await login.LoginAsync(user.Email!, "wrong", false, null, null)).Succeeded);
        Assert.True((await login.LoginAsync(user.Email!.ToUpperInvariant(), "Valid123!", false, null, null)).ChallengeRequired);
        Assert.Equal(5, user.AccessFailedCount);
        Assert.True((await login.LoginAsync(user.Email!, "Valid123!", false, "fake-valid", null)).Succeeded);
        Assert.Equal(0, user.AccessFailedCount);
        Assert.False(string.IsNullOrEmpty(identity.Http.Response.Headers.SetCookie));
    }

    [Fact]
    public async Task EmailLaCoCungNguongThongBaoVaKhoaMuoiLan()
    {
        using var h = new Harness(); using var identity = new SafetyIdentity(h);
        var user = await identity.PrepareAsync(await h.UserAsync("Minh"));
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var login = new LoginService(identity.Users, identity.SignIn, cache, Tang5bPureTests.Turnstile(), identity.Locks, h.Clock);
        var unknown = Guid.NewGuid() + "@test.vn";
        for (var i = 0; i < 10; i++)
        {
            var token = i >= 5 ? "fake-valid" : null;
            var existing = await login.LoginAsync(user.Email!, "wrong", false, token, null);
            var absent = await login.LoginAsync(unknown, "wrong", false, token, null);
            Assert.Equal(existing, absent);
        }
        Assert.True(await identity.Users.IsLockedOutAsync(user));
        Assert.True((await login.LoginAsync(unknown.ToUpperInvariant(), "anything", false, null, null)).ChallengeRequired);
        Assert.False((await login.LoginAsync(user.Email!, "Valid123!", false, "fake-valid", null)).Succeeded);
        var reset = new PasswordResetService(identity.Users, identity.Email, h.Clock, identity.Locks, SecurityRound1Fixtures.Emails(h, identity.Users, identity.Email), h.Db);
        string resetToken = "", resetNonce = "";
        Assert.NotNull(await reset.RequestAsync(user.Email!, (_, t, n) => { resetToken = t; resetNonce = n; return "https://localhost/reset"; }));
        await reset.ResetAsync(user.Id, resetToken, resetNonce, "NewValid123!");
        Assert.False(await identity.Users.IsLockedOutAsync(user));
        Assert.True((await login.LoginAsync(user.Email!, "NewValid123!", false, null, null)).Succeeded);
    }

    [Theory]
    [InlineData(false), InlineData(true)]
    public async Task SaiMaLan11CanTokenLuuDbVaReset24Gio(bool anonymous)
    {
        using var h = new Harness(); var user = anonymous ? null : await h.UserAsync("Minh");
        var remoteIp = "10.51.71.1"; var browser = anonymous ? "may-5b" : null; // khách: 10 lần sai tính theo trình duyệt (chốt 13/11/2026)
        var guard = new JoinCodeGuardService(h.Db, h.Codes, h.Clock, Tang5bPureTests.Turnstile());
        for (var i = 0; i < 10; i++) await Assert.ThrowsAsync<BusinessException>(() => guard.ResolveAsync("wrong", user, null, remoteIp, browser));
        Assert.True(await guard.RequiresAsync(user, remoteIp, browser));
        var room = await h.Classrooms.CreateAsync("Lớp", ClassJoinPolicy.Immediate, user ?? await h.UserAsync("Lan"));
        var code = (await h.Codes.GetActiveAsync(JoinCodeTarget.Classroom, room.Id))!.Code;
        await Assert.ThrowsAsync<BusinessException>(() => guard.ResolveAsync(code, user, null, remoteIp, browser));
        using (var db2 = DatabaseFixture.NewContext())
        {
            var afterRestart = new JoinCodeGuardService(db2, new(db2, h.Clock), h.Clock, Tang5bPureTests.Turnstile());
            Assert.True(await afterRestart.RequiresAsync(user, remoteIp, browser));
            await afterRestart.ResolveAsync(code, user, "fake-valid", remoteIp, browser);
            Assert.True(await afterRestart.RequiresAsync(user, remoteIp, browser)); // 5b mới: mã đúng không đặt lại bộ đếm
        }
        h.Db.ChangeTracker.Clear();
        for (var i = 0; i < 10; i++) await Assert.ThrowsAsync<BusinessException>(() => guard.ResolveAsync("wrong", user, anonymous ? "fake-valid" : null, remoteIp, browser));
        h.Clock.UtcNow = h.Clock.UtcNow.AddHours(24);
        Assert.False(await guard.RequiresAsync(user, remoteIp, browser));
        await Assert.ThrowsAsync<BusinessException>(() => guard.ResolveAsync("wrong", user, null, remoteIp, browser));
        Assert.Equal(1, (await h.Db.JoinCodeFailures.OrderByDescending(x => x.Id).FirstAsync()).FailCount);
    }

    [Fact]
    public async Task MaDungCuaBaiKetThucKhongLaLanSai()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync();
        var guard = new JoinCodeGuardService(h.Db, h.Codes, h.Clock, Tang5bPureTests.Turnstile());
        await Assert.ThrowsAsync<BusinessException>(() => guard.ResolveAsync("wrong", s.Student, null, null));
        await h.Assignments.CloseNowAsync(s.Assignment.Id, s.Lecturer);
        var code = (await h.Codes.GetActiveAsync(JoinCodeTarget.Assignment, s.Assignment.Id))!.Code;
        Assert.NotNull(await guard.ResolveAsync(code, s.Student, null, null));
        Assert.False(await guard.RequiresAsync(s.Student, null));
        Assert.Equal(1, (await h.Db.JoinCodeFailures.OrderByDescending(x => x.Id).FirstAsync()).FailCount);
    }

    [Fact]
    public async Task KhoaBaoVeQuaEmailMotLanVaResetMoKhoa()
    {
        using var h = new Harness(); using var identity = new SafetyIdentity(h);
        var user = await identity.PrepareAsync(await h.UserAsync("Minh"));
        string token = "", nonce = "";
        await identity.Locks.RequestProtectiveAsync(user.Id, (_, t, n) => { token = t; nonce = n; return "https://localhost/confirm"; });
        Assert.Null(await identity.Locks.ActiveAsync(user.Id)); Assert.Single(identity.Email.Messages);
        var stamp = user.SecurityStamp;
        await identity.Locks.ConfirmProtectiveAsync(user.Id, token, nonce);
        Assert.NotEqual(stamp, user.SecurityStamp);
        Assert.Equal(AccountLockKind.Protective, (await identity.Locks.ActiveAsync(user.Id))!.Kind);
        await Assert.ThrowsAsync<BusinessException>(() => identity.Locks.ConfirmProtectiveAsync(user.Id, token, nonce));
        var reset = new PasswordResetService(identity.Users, identity.Email, h.Clock, identity.Locks, SecurityRound1Fixtures.Emails(h, identity.Users, identity.Email), h.Db);
        await reset.RequestAsync(user.Email!, (_, t, n) => { token = t; nonce = n; return "https://localhost/reset"; });
        await reset.ResetAsync(user.Id, token, nonce, "NewValid123!");
        Assert.Null(await identity.Locks.ActiveAsync(user.Id));
        Assert.NotNull((await h.Db.AccountLocks.SingleAsync(x => x.UserId == user.Id)).UnlockedAt);
        await Assert.ThrowsAsync<BusinessException>(() => reset.ResetAsync(user.Id, token, nonce, "Other123!"));
    }

    [Fact]
    public async Task LinkKhoaMoiHuyLinkCuVaHetHanSau30Phut()
    {
        using var h = new Harness(); using var identity = new SafetyIdentity(h);
        var user = await identity.PrepareAsync(await h.UserAsync("Minh"));
        string token = "", nonce = "";
        await identity.Locks.RequestProtectiveAsync(user.Id, (_, t, n) => { token = t; nonce = n; return "https://localhost/confirm"; });
        await identity.Locks.RequestProtectiveAsync(user.Id, (_, _, _) => "https://localhost/new");
        await Assert.ThrowsAsync<BusinessException>(() => identity.Locks.ConfirmProtectiveAsync(user.Id, token, nonce));
        await identity.Locks.RequestProtectiveAsync(user.Id, (_, t, n) => { token = t; nonce = n; return "https://localhost/confirm"; });
        h.Clock.UtcNow = h.Clock.UtcNow.AddMinutes(30);
        await Assert.ThrowsAsync<BusinessException>(() => identity.Locks.ConfirmProtectiveAsync(user.Id, token, nonce));
        Assert.Null(await identity.Locks.ActiveAsync(user.Id));
    }

    [Fact]
    public async Task KhoaViPhamKhongMoBangResetVaTuHetHan()
    {
        using var h = new Harness(); using var identity = new SafetyIdentity(h);
        var admin = await identity.PrepareAsync(await h.UserAsync("Admin"), true);
        var user = await identity.PrepareAsync(await h.UserAsync("Minh"));
        await Assert.ThrowsAsync<BusinessException>(() => identity.Locks.LockAsync(user.Id, AccountLockKind.Violation, "", null, admin.Id));
        await identity.Locks.LockAsync(user.Id, AccountLockKind.Violation, "Quấy rối", h.Clock.UtcNow.AddHours(1), admin.Id);
        string token = "", nonce = "";
        var reset = new PasswordResetService(identity.Users, identity.Email, h.Clock, identity.Locks, SecurityRound1Fixtures.Emails(h, identity.Users, identity.Email), h.Db);
        await reset.RequestAsync(user.Email!, (_, t, n) => { token = t; nonce = n; return "https://localhost/reset"; });
        await reset.ResetAsync(user.Id, token, nonce, "NewValid123!");
        Assert.Equal(AccountLockKind.Violation, (await identity.Locks.ActiveAsync(user.Id))!.Kind);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var login = new LoginService(identity.Users, identity.SignIn, cache, Tang5bPureTests.Turnstile(), identity.Locks, h.Clock);
        var denied = await login.LoginAsync(user.Email!, "NewValid123!", false, null, null);
        Assert.False(denied.Succeeded); Assert.Contains("Quấy rối", denied.Error); Assert.Contains(VnTime.Format(h.Clock.UtcNow.AddHours(1)), denied.Error);
        h.Clock.UtcNow = h.Clock.UtcNow.AddHours(1);
        Assert.Null(await identity.Locks.ActiveAsync(user.Id));
        Assert.True((await login.LoginAsync(user.Email!, "NewValid123!", false, null, null)).Succeeded);
    }

    [Fact]
    public async Task KhoaDungLuotChamCauDaChotVaPopupSauCommit()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync();
        var a = await h.Attempts.StartAsync(s.Assignment.Id, s.Student);
        var q = a.Questions[0]; var right = QuestionSnapshot.FromJson(q.SnapshotJson).Options.Single(o => o.IsCorrect).Id;
        await h.Attempts.AnswerAsync(a.Id, q.Id, Guid.NewGuid(), [right], s.Student, questionVersion: q.QuestionVersion);
        var notifier = new RecordingLearnNotifier
        {
            OnNotice = async (name, _) =>
            {
                Assert.Equal("account-locked", name);
                Assert.Null(h.Db.Database.CurrentTransaction);
                using var check = DatabaseFixture.NewContext();
                Assert.Equal(AttemptEndReason.AccountLocked, (await check.Attempts.FindAsync(a.Id))!.EndReason);
            }
        };
        h.Notifier = notifier;
        using var identity = new SafetyIdentity(h);
        var user = await identity.PrepareAsync(s.Student);
        var admin = await identity.PrepareAsync(await h.UserAsync("Admin"), true);
        await identity.Locks.LockAsync(user.Id, AccountLockKind.Protective, null, null, admin.Id);
        Assert.Equal(AttemptStatus.Ended, a.Status); Assert.Equal(1, a.FirstScore);
        Assert.True(a.Participant.CompletionLabels.HasFlag(CompletionLabels.StoppedByLock));
        Assert.Single(notifier.Events); Assert.Single(notifier.LecturerEvents);
        await Assert.ThrowsAsync<ForbiddenException>(() => h.Attempts.StartAsync(s.Assignment.Id, s.Student));
    }

    [Fact]
    public async Task PhienCuBiChanNgayCaSauKhiAdminMoKhoa()
    {
        using var h = new Harness(); using var identity = new SafetyIdentity(h);
        var user = await identity.PrepareAsync(await h.UserAsync("Minh"));
        var admin = await identity.PrepareAsync(await h.UserAsync("Admin"), true);
        identity.Http.User = await identity.SignIn.CreateUserPrincipalAsync(user);
        await identity.Locks.LockAsync(user.Id, AccountLockKind.Protective, null, null, admin.Id);
        await identity.Locks.UnlockAsync(user.Id, admin.Id);
        var called = false;
        await new AccountLockMiddleware(_ => { called = true; return Task.CompletedTask; }).InvokeAsync(identity.Http, h.Db, h.Clock, identity.SignIn);
        Assert.False(called); Assert.Equal("/Account/Login", identity.Http.Response.Headers.Location);
    }

    [Fact]
    public async Task AdminCapGoCoLichSuKhongTuGo()
    {
        using var h = new Harness(); var a = await h.UserAsync("Admin"); var target = await h.UserAsync("Minh");
        (await h.Db.Users.FindAsync(a))!.IsSystemAdmin = true; await h.Db.SaveChangesAsync();
        var service = new AdminService(h.Db, h.Clock);
        await Assert.ThrowsAsync<BusinessException>(() => service.ChangeRoleAsync(a, false, "Tự gỡ", a));
        await Assert.ThrowsAsync<BusinessException>(() => service.ChangeRoleAsync(target, true, "", a));
        await service.ChangeRoleAsync(target, true, "Phân công", a);
        Assert.True((await h.Db.Users.FindAsync(target))!.IsSystemAdmin);
        await service.ChangeRoleAsync(target, false, "Kết thúc", a);
        Assert.False((await h.Db.Users.FindAsync(target))!.IsSystemAdmin);
        Assert.Equal(new[] { true, false }, await h.Db.AdminRoleChanges.Where(r => r.AdminId == a).OrderBy(r => r.Id).Select(r => r.Granted).ToArrayAsync());
    }

    [Fact]
    public async Task AdminKhongDuocVaoLopHayBaiGiaoCuaNguoiKhac()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(); var admin = await h.UserAsync("Admin");
        (await h.Db.Users.FindAsync(admin))!.IsSystemAdmin = true; await h.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<ForbiddenException>(() => h.Classrooms.GetForLecturerAsync(s.Room.Id, admin));
        await Assert.ThrowsAsync<ForbiddenException>(() => h.Assignments.GetForLecturerAsync(s.Assignment.Id, admin));
        var controller = new LearnController(h.Assignments, h.Attempts, h.Classrooms, new(h.Db, h.Assignments, h.Attempts, h.Clock), new(h.Db, h.Assignments, h.Clock));
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new(new ClaimsIdentity([new(ClaimTypes.NameIdentifier, admin)], "test")) } };
        await Assert.ThrowsAsync<ForbiddenException>(() => controller.Assignment(s.Assignment.Id));
    }

    [Fact]
    public async Task BaoCaoGopXemGhiLogVaAnDongBaiDangChay()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(); var admin = await h.UserAsync("Admin");
        (await h.Db.Users.FindAsync(admin))!.IsSystemAdmin = true; await h.Db.SaveChangesAsync();
        var a = await h.Attempts.StartAsync(s.Assignment.Id, s.Student);
        var service = new ContentReportService(h.Db, h.Clock, h.Assignments);
        var context = new ReportContext(ReportTarget.Quiz, s.Quiz.Id.ToString(), s.Assignment.Id);
        var id = await service.SubmitAsync(context, ContentReportService.Reasons[0], "Ghi chú", s.Student);
        Assert.Equal(id, await service.SubmitAsync(context, ContentReportService.Reasons[1], "Bổ sung", s.Student));
        Assert.Equal(ReportStatus.Pending, (await h.Db.ContentReports.FindAsync(id))!.Status);
        var details = await service.ViewAsync(id, admin);
        Assert.Equal(3, details.Content.Questions.Count);
        await service.ViewAsync(id, admin);
        Assert.Equal(2, await h.Db.AdminAccessLogs.CountAsync(l => l.ContentReportId == id && l.AdminId == admin));
        await Assert.ThrowsAsync<ForbiddenException>(() => service.ViewAsync(id, s.Student));
        var q = a.Questions[0]; var right = QuestionSnapshot.FromJson(q.SnapshotJson).Options.Single(o => o.IsCorrect).Id;
        await h.Attempts.AnswerAsync(a.Id, q.Id, Guid.NewGuid(), [right], s.Student, questionVersion: q.QuestionVersion);
        await service.HandleAsync(id, true, "Nội dung vi phạm", admin);
        await Assert.ThrowsAsync<BusinessException>(() => h.Assignments.CreatePracticeAsync(s.Room.Id, s.Quiz.Id,
            new PracticeSettings(null, h.Clock.UtcNow.AddDays(1), 3), s.Lecturer));
        Assert.Equal(AttemptEndReason.HiddenByAdmin, a.EndReason);
        Assert.Equal(AssignmentStatus.Closed, s.Assignment.Status);
        await Assert.ThrowsAsync<BusinessException>(() => h.Attempts.AnswerAsync(a.Id, q.Id, Guid.NewGuid(), [right], s.Student, questionVersion: q.QuestionVersion));
        Assert.Equal(1, a.FirstScore);
        Assert.Equal(ReportStatus.Handled, (await h.Db.ContentReports.FindAsync(id))!.Status);
    }

    [Fact]
    public async Task AnDongCaOnTapKiemTraGiuDiemVaChiThongBaoSauCommit()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync();
        var other = await h.ScenarioAsync();
        var unaffected = await h.Attempts.StartAsync(other.Assignment.Id, other.Student);
        var practice = await h.Attempts.StartAsync(s.Assignment.Id, s.Student);
        await Tang4DbTests.Answer(h, practice, practice.Questions[0], s.Student);
        await h.Attempts.PauseAsync(practice.Id, s.Student);
        // Nháp Kiểm tra: câu nhiều lựa chọn được chấm theo đúng End dùng khi đóng ngay.
        var original = s.Quiz.Questions.OrderBy(q => q.OrderIndex).ElementAt(1);
        original.Type = QuestionType.MultipleChoice;
        original.Options.OrderBy(o => o.OrderIndex).First().IsCorrect = true;
        original.Explanation = "GIẢI_THÍCH_BỊ_ẨN";
        await h.Db.SaveChangesAsync();
        var test = await h.Assignments.CreateTestAsync(s.Room.Id, s.Quiz.Id,
            new TestSettings(null, 60, ShuffleQuestions: false, ShuffleOptions: false), s.Lecturer);
        await h.Assignments.StartTestAsync(test.Id, s.Lecturer);
        var testAttempt = await h.Attempts.StartAsync(test.Id, s.Student);
        await Tang4DbTests.Answer(h, testAttempt, testAttempt.Questions[0], s.Student);
        var draft = testAttempt.Questions[1];
        var selected = QuestionSnapshot.FromJson(draft.SnapshotJson).Options.Where(o => o.IsCorrect).Select(o => o.Id).ToArray();
        await h.Attempts.SaveDraftAsync(testAttempt.Id, draft.Id, selected, s.Student, questionVersion: draft.QuestionVersion);
        var admin = await h.UserAsync("Admin");
        (await h.Db.Users.FindAsync(admin))!.IsSystemAdmin = true; await h.Db.SaveChangesAsync();
        var fake = new RecordingLearnNotifier(); h.Notifier = fake;
        var reports = new ContentReportService(h.Db, h.Clock, h.Assignments);
        var context = new ReportContext(ReportTarget.Quiz, s.Quiz.Id.ToString(), s.Assignment.Id);
        var reportId = await reports.SubmitAsync(context, ContentReportService.Reasons[0], null, s.Student);
        async Task VerifyCommitted()
        {
            Assert.Null(h.Db.Database.CurrentTransaction);
            using var fresh = DatabaseFixture.NewContext(); fresh.Database.SetCommandTimeout(3);
            Assert.Equal(ReportStatus.Handled, (await fresh.ContentReports.FindAsync(reportId))!.Status);
            Assert.NotNull((await fresh.StudySets.FindAsync(s.Quiz.StudySetId))!.HiddenByAdminAt);
            Assert.Equal(2, await fresh.Assignments.CountAsync(a => a.StudySetId == s.Quiz.StudySetId
                && a.Status == AssignmentStatus.Closed && a.CloseReason == CloseReason.HiddenByAdmin));
            Assert.Equal(2, await fresh.Attempts.CountAsync(a => a.Participant.Assignment.StudySetId == s.Quiz.StudySetId
                && a.Status == AttemptStatus.Ended && a.EndReason == AttemptEndReason.HiddenByAdmin));
        }
        fake.OnNotice = async (name, notice) =>
        {
            Assert.Equal("content-hidden", name);
            Assert.Equal($"/Learn/Review/{notice.AttemptId}", notice.Url);
            await VerifyCommitted();
        };
        fake.OnLecturers = async _ => await VerifyCommitted();
        await reports.HandleAsync(reportId, true, "Nội dung vi phạm", admin);
        Assert.Equal(2, fake.Events.Count);
        Assert.Equal(new[] { practice.Id, testAttempt.Id }.Order(), fake.Events.Select(e => e.Notice.AttemptId!.Value).Order());
        Assert.Equal(new[] { s.Assignment.Id, test.Id }.Order(), fake.LecturerEvents.Select(e => e.AssignmentId).Order());
        foreach (var a in new[] { s.Assignment, test })
        {
            Assert.Equal(AssignmentStatus.Closed, a.Status); Assert.Equal(h.Clock.UtcNow, a.ClosedAt);
            Assert.Equal(CloseReason.HiddenByAdmin, a.CloseReason);
        }
        foreach (var a in new[] { practice, testAttempt })
        {
            Assert.Equal(AttemptStatus.Ended, a.Status); Assert.Equal(h.Clock.UtcNow, a.EndedAt);
            Assert.Equal(AttemptEndReason.HiddenByAdmin, a.EndReason);
            Assert.True(a.Participant.CompletionLabels.HasFlag(CompletionLabels.HiddenByAdmin));
            Assert.False(a.Participant.CompletionLabels.HasFlag(CompletionLabels.ClosedByLecturer));
            Assert.Equal(1, a.Questions[0].FirstScore); Assert.Single(a.Questions[0].Submissions);
        }
        Assert.Equal(1, practice.FirstScore); Assert.Equal(2, testAttempt.FirstScore);
        Assert.Equal(1, draft.FirstScore); Assert.Single(draft.Submissions);
        Assert.Equal(AssignmentStatus.Open, other.Assignment.Status); Assert.Null(other.Assignment.CloseReason);
        Assert.True(AttemptRules.IsOpen(unaffected)); Assert.Null(unaffected.EndReason);
        // Context mới, không dựa vào navigation StudySet tình cờ đã được nạp trong fixture.
        using (var fresh = DatabaseFixture.NewContext())
        {
            var attempts = new AttemptService(fresh, h.Clock, [new ChoiceGrader()], h.Device, new NullLearnNotifier(), h.Connections);
            foreach (var id in new[] { practice.Id, testAttempt.Id })
            {
                var a = await attempts.GetForLearnerAsync(id, s.Student);
                Assert.Empty(await attempts.GetPlayQuestionsAsync(a, s.Student));
                Assert.True(a.FirstScore > 0);
                Assert.Equal("Nội dung bài đã bị quản trị viên ẩn: Nội dung vi phạm", await attempts.GetHiddenContentNoticeAsync(a.Participant.Assignment));
            }
        }
        var (_, rows) = await h.Gradebook.GetReportAsync(s.Assignment.Id, s.Lecturer);
        Assert.Equal(1, Assert.Single(rows, r => r.UserId == s.Student).Score!.Raw);
        var (_, items) = await h.Gradebook.GetAttemptForLecturerAsync(testAttempt.Id, s.Lecturer);
        Assert.Equal(3, items.Count); Assert.Contains(items, q => q.Snapshot.Explanation == "GIẢI_THÍCH_BỊ_ẨN");
        Assert.Equal(3, (await reports.ViewAsync(reportId, admin)).Content.Questions.Count);
        Assert.Single(await h.Db.AdminAccessLogs.Where(l => l.ContentReportId == reportId).ToListAsync());
        var popups = fake.Events.Count; var lecturers = fake.LecturerEvents.Count;
        await Assert.ThrowsAsync<BusinessException>(() => reports.HandleAsync(reportId, true, "Gửi lặp", admin));
        var next = await reports.SubmitAsync(context, ContentReportService.Reasons[1], null, s.Student);
        await reports.HandleAsync(next, true, "Báo cáo thứ hai", admin);
        Assert.Equal(popups, fake.Events.Count); Assert.Equal(lecturers, fake.LecturerEvents.Count);
        Assert.Equal("Nội dung vi phạm", s.Quiz.StudySet.HiddenReason);
    }

    [Fact]
    public async Task AnChanHocTheVaXemNoiDungNhungGiuTrangDiemChoHocVienVaGiangVien()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync();
        var cards = new FlashcardService(h.Db, h.Library, h.Clock, h.Notifier);
        await cards.SaveAsync(s.Quiz.StudySetId, null, "MẶT_TRƯỚC_BÍ_MẬT", "MẶT_SAU_BÍ_MẬT", s.Lecturer);
        var cardAssignment = await h.Assignments.CreatePracticeAsync(s.Room.Id, 0,
            new PracticeSettings(null, null, null, Form: AssignmentForm.FlashcardOnly, StudySetId: s.Quiz.StudySetId), s.Lecturer);
        var session = await cards.GetSessionAsync(s.Quiz.StudySetId, cardAssignment.Id, s.Student);
        var attempt = await h.Attempts.StartAsync(s.Assignment.Id, s.Student);
        await Tang4DbTests.Answer(h, attempt, attempt.Questions[0], s.Student);
        var admin = await h.UserAsync("Admin");
        (await h.Db.Users.FindAsync(admin))!.IsSystemAdmin = true; await h.Db.SaveChangesAsync();
        var reports = new ContentReportService(h.Db, h.Clock, h.Assignments);
        var reportId = await reports.SubmitAsync(new(ReportTarget.StudySet, s.Quiz.StudySetId.ToString(), s.Assignment.Id),
            ContentReportService.Reasons[0], null, s.Student);
        await reports.HandleAsync(reportId, true, "Lý do ẩn <script>", admin);
        var notice = "Nội dung bài đã bị quản trị viên ẩn: Lý do ẩn <script>";
        var learner = new LearnController(h.Assignments, h.Attempts, h.Classrooms,
            new(h.Db, h.Assignments, h.Attempts, h.Clock), new(h.Db, h.Assignments, h.Clock))
        { ControllerContext = new() { HttpContext = new DefaultHttpContext { User = new(new ClaimsIdentity([new(ClaimTypes.NameIdentifier, s.Student)], "test")) } } };
        var review = Assert.IsType<ViewResult>(await learner.Review(attempt.Id));
        Assert.Empty(Assert.IsType<List<PlayQuestion>>(review.ViewData["Questions"]));
        Assert.Equal(notice, review.ViewData["HiddenContentNotice"]); Assert.Equal(true, review.ViewData["ShowScores"]);
        Assert.Equal(1, Assert.IsType<Attempt>(review.Model).FirstScore);
        var play = Assert.IsType<RedirectToActionResult>(await learner.Play(attempt.Id));
        Assert.Equal(nameof(LearnController.Review), play.ActionName);
        foreach (var id in new[] { s.Assignment.Id, cardAssignment.Id })
        {
            var page = Assert.IsType<ViewResult>(await learner.Assignment(id));
            var model = Assert.IsType<LearnerAssignmentModel>(page.Model);
            Assert.Equal(notice, model.HiddenContentNotice); Assert.False(model.CanStudyCards); Assert.False(model.CanStart);
            var html = await Tang4ViewTests.Render("Learn", "Assignment", model);
            Assert.Contains(notice, WebUtility.HtmlDecode(html)); Assert.DoesNotContain("<script>", html);
        }
        var flashcards = new FlashcardsController(cards)
        { ControllerContext = learner.ControllerContext };
        Assert.Equal(notice, (await Assert.ThrowsAsync<BusinessException>(() => flashcards.Study(s.Quiz.StudySetId, cardAssignment.Id))).Message);
        Assert.Equal(notice, (await Assert.ThrowsAsync<BusinessException>(() => flashcards.Study(s.Quiz.StudySetId, cardAssignment.Id, true))).Message);
        Assert.Equal(notice, (await Assert.ThrowsAsync<BusinessException>(() => flashcards.Rate(s.Quiz.StudySetId, cardAssignment.Id,
            session.Cards[0].Id, FlashcardRating.Remembered))).Message);
        var (owned, ownCards) = await cards.GetEditorAsync(s.Quiz.StudySetId, s.Lecturer);
        Assert.NotNull(owned.HiddenByAdminAt); Assert.Single(ownCards);
        Assert.Single((await cards.GetSessionAsync(s.Quiz.StudySetId, null, s.Lecturer)).Cards);
        Assert.Equal(CloseReason.HiddenByAdmin, cardAssignment.CloseReason);
        Assert.NotNull((await h.Assignments.GetForLecturerAsync(cardAssignment.Id, s.Lecturer)).ClosedAt);
        Assert.Equal("Lý do ẩn <script>", await h.Assignments.GetHiddenContentReasonAsync(cardAssignment));
        s.Assignment.SurveyResultsPublishedAt = h.Clock.UtcNow; await h.Db.SaveChangesAsync();
        Assert.Equal(notice, (await Assert.ThrowsAsync<BusinessException>(() => learner.SurveyResults(s.Assignment.Id))).Message);
        await Assert.ThrowsAsync<BusinessException>(() => h.Assignments.CreateTestAsync(s.Room.Id, s.Quiz.Id, new(null, 15), s.Lecturer));
        await Assert.ThrowsAsync<BusinessException>(() => h.Assignments.CreatePracticeAsync(s.Room.Id, 0,
            new PracticeSettings(null, null, null, Form: AssignmentForm.FlashcardOnly, StudySetId: s.Quiz.StudySetId), s.Lecturer));
    }

    [Fact]
    public async Task AnKhongChoMoLaiBangKhungLamLaiDaDuyet()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync();
        var test = await h.Assignments.CreateTestAsync(s.Room.Id, s.Quiz.Id, new(null, 30), s.Lecturer);
        await h.Assignments.StartTestAsync(test.Id, s.Lecturer);
        var a = await h.Attempts.StartAsync(test.Id, s.Student);
        await h.Attempts.FinishAsync(a.Id, s.Student);
        var retakes = new RetakeService(h.Db, h.Assignments, h.Attempts, h.Clock);
        var request = await retakes.RequestAsync(test.Id, "Làm lại", s.Student);
        await retakes.DecideAsync(request.Id, true, h.Clock.UtcNow.AddHours(2), s.Lecturer);
        var admin = await h.UserAsync("Admin");
        (await h.Db.Users.FindAsync(admin))!.IsSystemAdmin = true; await h.Db.SaveChangesAsync();
        var reports = new ContentReportService(h.Db, h.Clock, h.Assignments);
        var id = await reports.SubmitAsync(new(ReportTarget.Quiz, s.Quiz.Id.ToString(), test.Id), ContentReportService.Reasons[0], null, s.Student);
        await reports.HandleAsync(id, true, "Vi phạm", admin);
        var error = await Assert.ThrowsAsync<BusinessException>(() => retakes.StartAsync(request.Id, s.Student));
        Assert.Equal("Nội dung bài đã bị quản trị viên ẩn: Vi phạm", error.Message);
        Assert.Equal(error.Message, (await Assert.ThrowsAsync<BusinessException>(() => retakes.RequestAsync(test.Id, "Xin thêm", s.Student))).Message);
        Assert.Single(await h.Db.Attempts.Where(x => x.Participant.AssignmentId == test.Id).ToListAsync());
    }

    [Fact]
    public async Task BaoCaoCauHoiDungPhamViVaTenHienThi()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(); var a = await h.Attempts.StartAsync(s.Assignment.Id, s.Student);
        await h.Attempts.FinishAsync(a.Id, s.Student);
        var reports = new ContentReportService(h.Db, h.Clock, h.Assignments);
        var id = await reports.SubmitAsync(new(ReportTarget.Question, a.Questions[0].QuestionId.ToString(), s.Assignment.Id, a.Id), ContentReportService.Reasons[0], null, s.Student);
        var row = (await h.Db.ContentReports.FindAsync(id))!;
        var snapshot = System.Text.Json.JsonSerializer.Deserialize<ReportedContent>(row.ContentJson)!;
        Assert.Single(snapshot.Questions);
        var stranger = await h.UserAsync("Ngoài lớp");
        await Assert.ThrowsAsync<ForbiddenException>(() => reports.SubmitAsync(new(ReportTarget.Quiz, s.Quiz.Id.ToString(), s.Assignment.Id), ContentReportService.Reasons[0], null, stranger));
        await Assert.ThrowsAsync<ForbiddenException>(() => reports.SubmitAsync(new(ReportTarget.Question, a.Questions[1].QuestionId.ToString(), s.Assignment.Id, a.Id + 100000), ContentReportService.Reasons[0], null, s.Student));
        var nameId = await reports.SubmitAsync(new(ReportTarget.User, s.Student, s.Assignment.Id), ContentReportService.Reasons[2], null, s.Lecturer);
        Assert.Null((await h.Db.ContentReports.FindAsync(nameId))!.StudySetId);
    }
}
