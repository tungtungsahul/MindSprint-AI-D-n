using System.IO.Compression;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MindSprintAI_ASP.NET_MVC.Controllers;
using MindSprintAI_ASP.NET_MVC.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using MindSprintAI_ASP.NET_MVC.Data.Entities;
using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MindSprintAI_ASP.NET_MVC.Data;
using MindSprintAI_ASP.NET_MVC.Models;

namespace MindSprintAI_ASP.NET_MVC.Tests;

internal static class SecurityRound1Fixtures
{
    public static EmailLinks Links => new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["App:BaseUrl"] = "https://localhost:7228" }).Build(), new HttpContextAccessor());
    public static AccountEmailService Emails(Harness h, UserManager<ApplicationUser> users, IEmailSender email, EmailLinks? links = null) =>
        new(h.Db, users, email, h.Clock, links ?? Links);
}

public class SecurityRound1PureTests
{
    private static string Root => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
    internal static IConfiguration Config(string? origin = "https://rekal.example", string? site = "real-site", string? secret = "real-secret") =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["App:BaseUrl"] = origin, ["Turnstile:SiteKey"] = site, ["Turnstile:SecretKey"] = secret, ["AllowedHosts"] = "*" }).Build();

    [Fact]
    public void S1_LinkEmailKhongTinHostVaGiuCachChayDev()
    {
        var http = new DefaultHttpContext(); http.Request.Host = new("evil.example"); http.Request.Scheme = "http";
        var accessor = new HttpContextAccessor { HttpContext = http };
        Assert.Equal("https://rekal.example/Account/ForgotPassword", new EmailLinks(Config(), accessor).To("/Account/ForgotPassword"));
        Assert.Equal("http://evil.example/Account", new EmailLinks(new ConfigurationBuilder().Build(), accessor).To("/Account"));
        // Các đường email còn lại cùng dùng helper, kể cả thông báo bảo mật 2FA/OAuth.
        foreach (var path in new[] { "Controllers/AccountController.cs", "Controllers/AccountTwoFactor.cs", "Services/NewDeviceService.cs" })
        {
            var source = File.ReadAllText(Path.Combine(Root, path));
            Assert.DoesNotContain("Request.Scheme", source);
            Assert.DoesNotContain("Request.Host", source);
        }
    }

    [Theory]
    [InlineData(null), InlineData(""), InlineData("http://rekal.example"), InlineData("https://"), InlineData("https://rekal.example/#bad")]
    public void S1_ChanChayThatKhiBaseUrlKhongHopLe(string? origin)
    {
        var log = new ConfigLog();
        Assert.Contains("App:BaseUrl", Assert.Throws<InvalidOperationException>(() => DeploymentSecurity.Validate(Config(origin), false, log)).Message);
        Assert.Contains(LogLevel.Critical, log.Levels);
        DeploymentSecurity.Validate(Config(origin), true, log);
    }

    [Fact]
    public void S1_CanhBaoAllowedHostsVaChapNhanCauHinhThat()
    {
        var log = new ConfigLog(); DeploymentSecurity.Validate(Config(), false, log);
        Assert.Contains(LogLevel.Warning, log.Levels);
        var config = Config(); config["AllowedHosts"] = "rekal.example";
        log.Levels.Clear(); DeploymentSecurity.Validate(config, false, log);
        Assert.Empty(log.Levels);
    }

    [Theory]
    [InlineData("127.0.0.1", true), InlineData("::1", true), InlineData("192.0.2.1", false)]
    public async Task S2_ChiTinProxyDaKhaiBao(string proxy, bool trusted)
    {
        var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        using (services)
        {
            var options = new ForwardedHeadersOptions(); DeploymentSecurity.ConfigureProxy(options, Config());
            Assert.Equal(1, options.ForwardLimit); Assert.Empty(options.KnownIPNetworks);
            var pipeline = new ApplicationBuilder(services);
            pipeline.UseForwardedHeaders(options);
            pipeline.Run(http =>
            {
                Assert.Equal(trusted, http.Request.IsHttps);
                Assert.Equal(trusted ? "203.0.113.9" : proxy, http.Connection.RemoteIpAddress!.ToString());
                return Task.CompletedTask;
            });
            var request = new DefaultHttpContext { RequestServices = services };
            request.Connection.RemoteIpAddress = IPAddress.Parse(proxy); request.Request.Scheme = "http";
            request.Request.Headers["X-Forwarded-For"] = "198.51.100.8, 203.0.113.9";
            request.Request.Headers["X-Forwarded-Proto"] = "https";
            await pipeline.Build()(request);
        }
    }

    [Fact]
    public void S2_DanhSachProxyThayTheMacDinhVaThuTuCookie()
    {
        var config = Config(); config["ReverseProxy:KnownProxies:0"] = "192.0.2.10";
        var options = new ForwardedHeadersOptions(); DeploymentSecurity.ConfigureProxy(options, config);
        Assert.Equal(IPAddress.Parse("192.0.2.10"), Assert.Single(options.KnownProxies));
        var source = File.ReadAllText(Path.Combine(Root, "Program.cs"));
        var forwarded = source.IndexOf("app.UseForwardedHeaders();", StringComparison.Ordinal);
        Assert.True(forwarded >= 0);
        foreach (var next in new[] { "app.UseHsts();", "app.UseHttpsRedirection();", "app.UseAuthentication();", "app.UseRateLimiter();" })
            Assert.True(forwarded < source.IndexOf(next, StringComparison.Ordinal));
        Assert.Equal(2, source.Split("o.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;").Length - 1);
    }

    [Theory]
    [InlineData(null, "real"), InlineData("real", null), InlineData("1x00000000000000000000AA", "real")]
    [InlineData("real", "1x0000000000000000000000000000000AA")]
    public void S6_ThieuHoacKhoaThuTurnstilePhaiDung(string? site, string? secret)
    {
        var log = new ConfigLog();
        Assert.Contains("Turnstile", Assert.Throws<InvalidOperationException>(() => DeploymentSecurity.Validate(Config(site: site, secret: secret), false, log)).Message);
        Assert.Contains(LogLevel.Critical, log.Levels);
        DeploymentSecurity.Validate(Config(site: site, secret: secret), true, log);
    }

    [Fact]
    public void S4_GuiLaiXacMinhCoGioiHanTanSuat()
    {
        var method = typeof(AccountController).GetMethod(nameof(AccountController.ResendConfirmation), [typeof(string)])!;
        Assert.Equal(AccountRateLimits.EntryPolicy, Assert.Single(method.GetCustomAttributes(typeof(EnableRateLimitingAttribute), true)
            .Cast<EnableRateLimitingAttribute>()).PolicyName);
    }

    [Fact]
    public void S9_TuChoiTongGiaiNenHon5MbTruocKhiDocWorkbook()
    {
        using var file = new MemoryStream();
        using (var zip = new ZipArchive(file, ZipArchiveMode.Create, true))
        {
            using var content = zip.CreateEntry("xl/worksheets/sheet1.xml").Open();
            content.Write(new byte[5 * 1024 * 1024 + 1]);
        }
        file.Position = 0;
        Assert.Equal("File Excel quá nặng so với giới hạn cho phép. Hãy dùng file mẫu và bớt dữ liệu.",
            Assert.Throws<BusinessException>(() => ExcelTemplate.Read(file, "test.xlsx")).Message);
    }

    [Fact]
    public void S9_G0ChiChoPhepDungCauNguoiDungDaDuyet()
    {
        // Câu báo S9 không được dùng chữ "máy chủ" (G0), không có ngoại lệ riêng.
        Assert.False(PlainLanguageAudit.HasJargon("MindSprint AI đang bận đọc nhiều file Excel cùng lúc. Hãy thử lại sau ít phút."));
        Assert.True(PlainLanguageAudit.HasJargon("Máy chủ đang đọc nhiều file Excel. Hãy thử lại sau ít phút."));
        Assert.Contains(PlainLanguageAudit.UserTexts("DeploymentSecurity.cs", "throw new BusinessException(\"IP không đúng\");"), PlainLanguageAudit.HasJargon);
        Assert.Empty(PlainLanguageAudit.UserTexts("DeploymentSecurity.cs", "throw new InvalidOperationException(\"IP proxy không đúng\");"));
    }

    private sealed class ConfigLog : ILogger
    {
        public List<LogLevel> Levels { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Levels.Add(logLevel);
    }
}

[CollectionDefinition("ExcelReadSlots", DisableParallelization = true)]
public class ExcelReadSlotsCollection;

[Collection("ExcelReadSlots")]
public class SecurityRound1ExcelTests
{
    [Fact]
    public async Task S9_ChiHaiLuotDocVaTraLuotKeCaFileLoi()
    {
        using var entered = new CountdownEvent(2); using var release = new ManualResetEventSlim();
        using var first = new PausedStream(entered, release); using var second = new PausedStream(entered, release);
        Task Run(Stream stream) => Task.Factory.StartNew(() => Assert.Throws<BusinessException>(() => ExcelTemplate.Read(stream, "bad.xlsx")),
            CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        var tasks = new[] { Run(first), Run(second) };
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
            using var third = new MemoryStream();
            Assert.Equal("MindSprint AI đang bận đọc nhiều file Excel cùng lúc. Hãy thử lại sau ít phút.",
                Assert.Throws<BusinessException>(() => ExcelTemplate.Read(third, "third.xlsx")).Message);
        }
        finally { release.Set(); await Task.WhenAll(tasks); }
        Assert.Equal(7, ExcelTemplate.Read(new MemoryStream(ExcelTemplate.Create()), "ok.xlsx").Count);
    }

    private sealed class PausedStream(CountdownEvent entered, ManualResetEventSlim release) : MemoryStream
    {
        public override int Read(Span<byte> buffer)
        {
            entered.Signal();
            if (!release.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException();
            return 0;
        }
    }
}

[Collection("db")]
public class SecurityRound1DbTests
{
    private static PasswordResetService Reset(Harness h, SafetyIdentity i) =>
        new(i.Users, i.Email, h.Clock, i.Locks, SecurityRound1Fixtures.Emails(h, i.Users, i.Email), h.Db);

    private static AccountController Controller(Harness h, SafetyIdentity i, string host = "evil.example")
    {
        var env = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" }).Environment;
        var links = new EmailLinks(SecurityRound1PureTests.Config(), new HttpContextAccessor { HttpContext = i.Http });
        var controller = new AccountController(i.Users, i.SignIn, Reset(h, i), env, null!, Tang5bPureTests.Turnstile(), i.Locks, null!,
            links, SecurityRound1Fixtures.Emails(h, i.Users, i.Email, links))
        { ControllerContext = new() { HttpContext = i.Http } };
        i.Http.Request.Host = new(host); i.Http.Request.Scheme = "http";
        controller.Url = new FakeUrl(controller.ControllerContext);
        controller.TempData = new TempDataDictionary(i.Http, new FakeTempData());
        SecurityRound4PureTests.VerifiedForm(i.Http);
        return controller;
    }

    [Theory]
    [InlineData("10.91.42.1", "10.91.42.99", "10.91.43.1")]
    [InlineData("2001:db8:91::1", "2001:db8:91:abcd::9", "2001:db8:92::1")]
    public async Task S3_KhachDoiThietBiVanDemTheoDaiMang(string first, string same, string other)
    {
        using var h = new Harness(); var guard = new JoinCodeGuardService(h.Db, h.Codes, h.Clock, Tang5bPureTests.Turnstile());
        for (var n = 0; n < JoinCodeGuardService.NetworkLimit; n++)
        {
            h.Device.Id = Guid.NewGuid().ToString("N");
            await Assert.ThrowsAsync<BusinessException>(() => guard.ResolveAsync("sai", null, null, first));
        }
        using var secondDb = DatabaseFixture.NewContext();
        var restarted = new JoinCodeGuardService(secondDb, new(secondDb, h.Clock), h.Clock, Tang5bPureTests.Turnstile());
        Assert.True(await restarted.RequiresAsync(null, same)); Assert.False(await restarted.RequiresAsync(null, other));
        Assert.False(await restarted.RequiresAsync(await h.UserAsync("Đã đăng nhập"), first));
        var prefix = DeviceSignalService.Prefix(IPAddress.Parse(first));
        Assert.DoesNotContain(await h.Db.JoinCodeFailures.Select(x => x.KeyHash).ToListAsync(), x => x.Contains(prefix!));
    }

    [Fact]
    public async Task S3_MaCuVaMaKhongTonTaiBaoCungCau()
    {
        using var h = new Harness(); var room = await h.Classrooms.CreateAsync("Lớp", ClassJoinPolicy.Immediate, await h.UserAsync("GV"));
        var old = (await h.Codes.GetActiveAsync(JoinCodeTarget.Classroom, room.Id))!;
        old.IsActive = false; await h.Db.SaveChangesAsync();
        var missing = Enumerable.Range(0, 100000).Select(n => n.ToString("D8")).First(code => !h.Db.JoinCodes.Any(x => x.Code == code));
        var absent = await Assert.ThrowsAsync<BusinessException>(() => h.Codes.ResolveAsync(missing));
        Assert.Equal(absent.Message, (await Assert.ThrowsAsync<BusinessException>(() => h.Codes.ResolveAsync(old.Code))).Message);
    }

    [Fact]
    public async Task S4_MoiLoaiMotEmailMoiPhutVaKhongHuyLinkKhiBiChan()
    {
        using var h = new Harness(); using var i = new SafetyIdentity(h);
        var user = await i.PrepareAsync(await h.UserAsync("Chờ thư"));
        var emails = SecurityRound1Fixtures.Emails(h, i.Users, i.Email); var reset = Reset(h, i);
        string token = "", nonce = "";
        Assert.NotNull(await reset.RequestAsync(user.Email!, (_, t, n) => { token = t; nonce = n; return "https://rekal.example/reset"; }));
        user.EmailConfirmed = false; Assert.True((await i.Users.UpdateAsync(user)).Succeeded);
        Assert.Null(await emails.SendRegistrationAsync(user)); // Chung hạn 1 phút với thư đặt lại vừa gửi.
        Assert.Null(await emails.SendRegistrationAsync(user));
        user.EmailConfirmed = true; Assert.True((await i.Users.UpdateAsync(user)).Succeeded);
        Assert.Null(await reset.RequestAsync(user.Email!.ToUpperInvariant(), (_, _, _) => throw new Exception("Không được dựng link mới")));
        Assert.Single(i.Email.Messages);
        h.Clock.UtcNow = h.Clock.UtcNow.AddSeconds(59);
        Assert.False(await emails.TryReserveAsync(user));
        h.Clock.UtcNow = h.Clock.UtcNow.AddSeconds(1);
        Assert.True(await emails.TryReserveAsync(user));
        Assert.False(await reset.ResetAsync(user.Id, token, nonce, "NewPassword123!"));
        h.Clock.UtcNow = h.Clock.UtcNow.AddMinutes(1);
        Assert.NotNull(await reset.RequestAsync(user.Email!, (_, _, _) => "https://rekal.example/reset"));
    }

    [Fact]
    public async Task S4_HaiYeuCauDongThoiVaScopeMoiChiGuiMot()
    {
        using var h = new Harness(); using var i = new SafetyIdentity(h);
        var user = await i.PrepareAsync(await h.UserAsync("Đồng thời"));
        user.EmailConfirmed = false; Assert.True((await i.Users.UpdateAsync(user)).Succeeded);
        using var h2 = new Harness(); using var i2 = new SafetyIdentity(h2);
        var user2 = (await i2.Users.FindByIdAsync(user.Id))!;
        var result = await Task.WhenAll(SecurityRound1Fixtures.Emails(h, i.Users, i.Email).SendRegistrationAsync(user),
            SecurityRound1Fixtures.Emails(h2, i2.Users, i2.Email).SendRegistrationAsync(user2));
        Assert.Single(result, x => x != null); Assert.Equal(1, i.Email.Messages.Count + i2.Email.Messages.Count);
        using var h3 = new Harness(); using var i3 = new SafetyIdentity(h3);
        Assert.Null(await SecurityRound1Fixtures.Emails(h3, i3.Users, i3.Email).SendRegistrationAsync((await i3.Users.FindByIdAsync(user.Id))!));
    }

    [Fact]
    public async Task S1_ThuDatLaiKhoaVaThietBiMoiKhongLayHostYeuCau()
    {
        using var h = new Harness(); using var i = new SafetyIdentity(h); var user = await i.PrepareAsync(await h.UserAsync("Link an toàn"));
        var controller = Controller(h, i);
        i.Http.User = new(new ClaimsIdentity([new(ClaimTypes.NameIdentifier, user.Id)], "test"));
        Assert.IsType<ViewResult>(await controller.ForgotPassword(user.Email!));
        Assert.IsType<ViewResult>(await controller.RequestLock());
        var links = new EmailLinks(SecurityRound1PureTests.Config(), new HttpContextAccessor { HttpContext = i.Http });
        using var geo = new GeoIpLocation(SecurityRound1PureTests.Config(), new GeoEnvironment());
        var devices = new NewDeviceService(h.Db, h.Clock, i.Email, new(h.Db, h.Clock, h.Notifier), geo, NullLogger<NewDeviceService>.Instance, links);
        i.Http.Request.Headers.Cookie = LearnDevice.CookieName + "=" + new string('a', 64);
        await devices.SeenAsync(user, i.Http);
        i.Http.Request.Headers.Cookie = LearnDevice.CookieName + "=" + new string('b', 64);
        await devices.SeenAsync(user, i.Http);
        Assert.Equal(3, i.Email.Messages.Count);
        Assert.All(i.Email.Messages, html => { Assert.DoesNotContain("evil.example", html); Assert.Contains("https://rekal.example/Account", html); });
    }

    [Theory]
    [InlineData(null), InlineData(false), InlineData(true)]
    public async Task S1S5_DangKyMoiVaTrungCungTrangVaDungThu(bool? confirmed)
    {
        using var h = new Harness(); using var i = new SafetyIdentity(h); var address = Guid.NewGuid() + "@test.vn";
        if (confirmed.HasValue)
        {
            var user = await i.PrepareAsync(await h.UserAsync("Có sẵn"));
            user.Email = address; user.UserName = address; user.EmailConfirmed = confirmed.Value;
            Assert.True((await i.Users.UpdateAsync(user)).Succeeded);
        }
        var controller = Controller(h, i);
        var result = Assert.IsType<ViewResult>(await controller.Register(new RegisterModel
            { Email = address, DisplayName = "Tên mới" }));
        Assert.Equal("CheckEmail", result.ViewName);
        Assert.Equal($"Hãy mở email {address} để làm tiếp.", result.Model);
        Assert.True(controller.ModelState.IsValid);
        var message = Assert.Single(i.Email.Messages); Assert.DoesNotContain("evil.example", message);
        var savedUser = (await i.Users.FindByEmailAsync(address))!;
        Assert.Equal(confirmed.HasValue ? "Có sẵn" : "Tên mới", savedUser.DisplayName);
        if (!confirmed.HasValue) { Assert.False(savedUser.EmailConfirmed); Assert.False(await i.Users.HasPasswordAsync(savedUser)); }
        if (confirmed == true)
        {
            Assert.Equal($"Bạn đã có tài khoản {Brand.Name}", Assert.Single(i.Email.Subjects));
            Assert.Equal($"<p>Có người vừa dùng email này để đăng ký {Brand.Name}, nhưng email này đã có tài khoản.</p><p>Nếu là bạn và bạn quên mật khẩu, hãy mở trang <a href=\"https://rekal.example/Account/ForgotPassword\">Quên mật khẩu</a>. Nếu không phải bạn, hãy bỏ qua thư này.</p>", message);
        }
        else Assert.Contains("https://rekal.example/Account/RegisterPassword?", message);
        controller = Controller(h, i);
        Assert.Equal("CheckEmail", Assert.IsType<ViewResult>(await controller.Register(new RegisterModel
            { Email = address, DisplayName = "Thử lại" })).ViewName);
        Assert.Single(i.Email.Messages);
        Assert.Equal(1, await h.Db.Users.CountAsync(u => u.Email == address));
        await h.Db.Entry(savedUser).ReloadAsync();
        Assert.Equal(confirmed.HasValue ? "Có sẵn" : "Tên mới", savedUser.DisplayName);
    }

    [Fact]
    public async Task S5_DangKyTrungKhongCanMatKhauVanGiuTrangChung()
    {
        using var h = new Harness(); using var i = new SafetyIdentity(h); var user = await i.PrepareAsync(await h.UserAsync("Có sẵn"));
        var controller = Controller(h, i);
        var result = Assert.IsType<ViewResult>(await controller.Register(new RegisterModel
            { Email = user.Email!, DisplayName = "Tên" }));
        Assert.Equal("CheckEmail", result.ViewName); Assert.True(controller.ModelState.IsValid); Assert.Single(i.Email.Messages);
    }

    [Fact]
    public async Task S7_CapVaGoQuyenDoiStamp()
    {
        using var h = new Harness(); using var i = new SafetyIdentity(h);
        var admin = await i.PrepareAsync(await h.UserAsync("Admin"), true); var user = await i.PrepareAsync(await h.UserAsync("Đích"));
        var service = new AdminService(h.Db, h.Clock); var stamp = user.SecurityStamp;
        await service.ChangeRoleAsync(user.Id, true, "Cấp", admin.Id);
        Assert.NotEqual(stamp, user.SecurityStamp); Assert.True(user.IsSystemAdmin);
        stamp = user.SecurityStamp;
        await service.ChangeRoleAsync(user.Id, false, "Gỡ", admin.Id);
        Assert.NotEqual(stamp, user.SecurityStamp); Assert.False(user.IsSystemAdmin);
    }

    [Theory]
    [InlineData(false), InlineData(true)]
    public async Task S8_ResetGoLienKetVaBaoDungCau(bool external)
    {
        using var h = new Harness(); using var i = new SafetyIdentity(h); var user = await i.PrepareAsync(await h.UserAsync("Đặt lại"));
        using var services = new ServiceCollection().AddScoped(_ => DatabaseFixture.NewContext()).BuildServiceProvider();
        var audit = new AuditLog(h.Db, new HttpContextAccessor { HttpContext = i.Http }, services.GetRequiredService<IServiceScopeFactory>(),
            h.Clock, NullLogger<AuditLog>.Instance); audit.Attach();
        if (external)
        {
            Assert.True((await i.Users.AddLoginAsync(user, new("Google", Guid.NewGuid().ToString(), "Google"))).Succeeded);
            Assert.True((await i.Users.AddLoginAsync(user, new("Microsoft", Guid.NewGuid().ToString(), "Microsoft"))).Succeeded);
        }
        string token = "", nonce = ""; var reset = Reset(h, i);
        await reset.RequestAsync(user.Email!, (_, t, n) => { token = t; nonce = n; return "https://rekal.example/reset"; });
        var controller = Controller(h, i); var stamp = user.SecurityStamp;
        var result = Assert.IsType<ViewResult>(await controller.ResetPassword(new ResetPasswordModel
        { UserId = user.Id, Code = Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(System.Text.Encoding.UTF8.GetBytes(token)),
            Nonce = nonce, Password = "NewPassword123!", ConfirmPassword = "NewPassword123!" }));
        Assert.Equal("Message", result.ViewName);
        Assert.Equal(external
            ? "Đã đặt lại mật khẩu. Để bảo vệ tài khoản, các liên kết đăng nhập bằng Google/Microsoft đã được gỡ; bạn có thể nối lại trong Tài khoản."
            : "Đã đặt lại mật khẩu. Các thiết bị khác đã bị đăng xuất. Bạn có thể đăng nhập bằng mật khẩu mới.", result.Model);
        Assert.Empty(await i.Users.GetLoginsAsync(user)); Assert.NotEqual(stamp, user.SecurityStamp);
        Assert.True(await i.Users.CheckPasswordAsync(user, "NewPassword123!"));
        Assert.Equal(external ? 1 : 0, await h.Db.AuditEntries.CountAsync(x => x.Action == AuditAction.ExternalRemoved && x.TargetId == user.Id));
        await Assert.ThrowsAsync<BusinessException>(() => reset.ResetAsync(user.Id, token, nonce, "AnotherPassword123!"));
    }

    [Fact]
    public async Task S8_ResetKhongThanhCongGiuLienKetVaMatKhauCu()
    {
        using var h = new Harness(); using var i = new SafetyIdentity(h); var user = await i.PrepareAsync(await h.UserAsync("Giữ liên kết"));
        Assert.True((await i.Users.AddLoginAsync(user, new("Google", Guid.NewGuid().ToString(), "Google"))).Succeeded);
        var reset = Reset(h, i); string token = "", nonce = "";
        await reset.RequestAsync(user.Email!, (_, t, n) => { token = t; nonce = n; return "https://rekal.example/reset"; });
        await Assert.ThrowsAsync<BusinessException>(() => reset.ResetAsync(user.Id, token, "sai", "NewPassword123!"));
        await Assert.ThrowsAsync<BusinessException>(() => reset.ResetAsync(user.Id, token, nonce, "x"));
        Assert.Single(await i.Users.GetLoginsAsync(user)); Assert.True(await i.Users.CheckPasswordAsync(user, "Valid123!"));
    }
}
