using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;
using MindSprintAI_ASP.NET_MVC.Data;
using MindSprintAI_ASP.NET_MVC.Controllers;
using MindSprintAI_ASP.NET_MVC.Data.Entities;
using MindSprintAI_ASP.NET_MVC.Models;
using MindSprintAI_ASP.NET_MVC.Services;

namespace MindSprintAI_ASP.NET_MVC.Tests;

[Collection("db")] // dùng Harness (DB thật): không chạy song song với test DB.
public class Tang6aPureTests
{
    [Fact]
    public async Task CookieIdentityPendingNamPhutKhongCanKetNoiDb()
    {
        // Chỉ dựng Identity store; không gọi UserAsync hay bất kỳ truy vấn DB nào.
        using var h = new Harness(); using var identity = new SafetyIdentity(h);
        var user = new ApplicationUser { Id = "cookie-only", SecurityStamp = "stamp-current" };
        await TwoFactorService.BeginAsync(identity.SignIn, user, true, "/Account", h.Clock.GetUtcNow());
        var options = identity.Http.RequestServices.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>();
        var pendingOptions = options.Get(IdentityConstants.TwoFactorUserIdScheme);
        var pendingCookie = Assert.Single(identity.Http.Response.Headers.SetCookie, s => s!.StartsWith(pendingOptions.Cookie.Name + "="))!;
        var value = pendingCookie.Split(';')[0].Split('=', 2)[1];
        var ticket = pendingOptions.TicketDataFormat.Unprotect(value)!;
        Assert.Equal(user.Id, ticket.Principal.FindFirstValue(ClaimTypes.Name));
        Assert.Equal(TimeSpan.FromMinutes(5), ticket.Properties.ExpiresUtc - ticket.Properties.IssuedUtc);
        Assert.Equal("True", ticket.Properties.Items["remember"]); Assert.Equal("/Account", ticket.Properties.Items["returnUrl"]);
        Assert.DoesNotContain(identity.Http.Response.Headers.SetCookie, s => s!.StartsWith(".AspNetCore.Identity.Application="));
    }

    [Fact]
    public void UriVaKhoaNhapTayDungDinhDang()
    {
        Assert.Equal($"otpauth://totp/{Brand.Name}:a%2Bb%40test.vn?secret=ABCDEFGH&issuer={Brand.Name}&digits=6&period=30",
            TwoFactorService.AuthenticatorUri("a+b@test.vn", "ABCDEFGH"));
        Assert.Equal("ABCD EFGH", TwoFactorService.GroupKey("ABCDEFGH"));
        Assert.Equal(TimeSpan.FromMinutes(5), TwoFactorService.EmailLifetime);
        Assert.Equal(TwoFactorService.HashRecovery("ABC-123"), TwoFactorService.HashRecovery(" abc-123 "));
    }

    private static void FormMatches(string html, string action)
    {
        var method = typeof(AccountController).GetMethods().Single(m => m.Name == action && m.IsDefined(typeof(HttpPostAttribute)));
        var names = method.GetParameters().Where(p => !p.IsDefined(typeof(FromServicesAttribute)))
            .Select(p => p.Name!).ToArray();
        var forms = Regex.Matches(html, "<form\\b[^>]*>[\\s\\S]*?</form>").Select(m => m.Value);
        var form = Assert.Single(forms, f => f.Contains($"data-action=\"{action}\""));
        var fields = Regex.Matches(form, "name=\"([^\"]+)\"").Select(m => m.Groups[1].Value)
            .Where(n => n != "__RequestVerificationToken").Distinct().Order().ToArray();
        Assert.Equal(names.Order(), fields); Assert.Contains("__RequestVerificationToken", form);
        Assert.Equal(AccountPolicy.Expected(method.Name), method.GetCustomAttribute<EnableRateLimitingAttribute>()!.PolicyName);
        Assert.True(method.GetCustomAttribute<ResponseCacheAttribute>()!.NoStore);
    }

    [Fact]
    public async Task MoiFormKhopActionVaTrangTaiKhoanKhongLoBiMat()
    {
        var account = await Tang4ViewTests.Render("Account", "Index", new ApplicationUser(), new()
        { ["TwoFactor"] = new TwoFactorStatus(true, true, true, false, true, 3) });
        foreach (var action in new[] { "ResetAuthenticator", "SetEmailTwoFactor", "RegenerateRecoveryCodes", "DisableTwoFactor" }) FormMatches(account, action);
        Assert.Contains("3", account); Assert.Contains("mã dự phòng", WebUtility.HtmlDecode(account));
        Assert.DoesNotContain("otpauth", account); Assert.DoesNotContain("AuthenticatorKey", account);
        var setup = await Tang4ViewTests.Render("Account", "SetupAuthenticator", new AuthenticatorSetup("ABCD EFGH", "otpauth://totp/test"));
        FormMatches(setup, "SetupAuthenticator"); Assert.Contains("one-time-code", setup);
        var login = await Tang4ViewTests.Render("Account", "LoginTwoFactor", new TwoFactorLoginPage(true, true));
        FormMatches(login, "LoginTwoFactor"); FormMatches(login, "SendTwoFactorEmail");
        var admin = await Tang4ViewTests.Render("Account", "LoginTwoFactor", new TwoFactorLoginPage(true, false));
        Assert.DoesNotContain("Nhớ máy", WebUtility.HtmlDecode(admin)); Assert.DoesNotContain("SendTwoFactorEmail", admin);
        var adminAccount = await Tang4ViewTests.Render("Account", "Index", new ApplicationUser(), new()
        { ["TwoFactor"] = new TwoFactorStatus(true, true, false, true, true, 10) });
        Assert.DoesNotContain("data-action=\"DisableTwoFactor\"", adminAccount);
        Assert.DoesNotContain("data-action=\"ResetAuthenticator\"", adminAccount);
        Assert.DoesNotContain("data-action=\"SetEmailTwoFactor\"", adminAccount);
        var noPassword = await Tang4ViewTests.Render("Account", "Index", new ApplicationUser(), new()
        { ["TwoFactor"] = new TwoFactorStatus(true, false, true, false, false, 10) });
        foreach (var action in new[] { "SetEmailTwoFactor", "RegenerateRecoveryCodes", "DisableTwoFactor" }) FormMatches(noPassword, action);
        var emailOff = await Tang4ViewTests.Render("Account", "Index", new ApplicationUser(), new()
        { ["TwoFactor"] = new TwoFactorStatus(false, false, false, false, true, 0) });
        FormMatches(emailOff, "SetEmailTwoFactor");
        var codes = await Tang4ViewTests.Render("Account", "RecoveryCodes", new[] { "ABC-123" });
        Assert.Contains("ABC-123", codes); Assert.DoesNotContain("<form", codes);
    }
}

// Cookie handler thật, mỗi yêu cầu có scope mới; chỉ DB tests mới tạo fixture này.
internal sealed class TwoFactorIdentity : IDisposable
{
    private readonly ServiceProvider provider;
    private IServiceScope scope;
    // (T7) harness mô phỏng cookie do LearnDeviceMiddleware cấp trước luồng đăng nhập.
    public Dictionary<string, string> Cookies { get; } = new() { [LearnDevice.CookieName] = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)) };
    public DefaultHttpContext Http { get; private set; } = null!;
    public UserManager<ApplicationUser> Users => scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    public SignInManager<ApplicationUser> SignIn => scope.ServiceProvider.GetRequiredService<SignInManager<ApplicationUser>>();
    public TwoFactorService TwoFactor => scope.ServiceProvider.GetRequiredService<TwoFactorService>();
    public LoginService Login => scope.ServiceProvider.GetRequiredService<LoginService>();
    public GuestService Guests => scope.ServiceProvider.GetRequiredService<GuestService>();
    public PasskeyService Passkeys => scope.ServiceProvider.GetRequiredService<PasskeyService>();
    public ExternalLoginService External => scope.ServiceProvider.GetRequiredService<ExternalLoginService>();
    public SafetyEmail Email { get; } = new();
    public TwoFactorIdentity(Harness h, IPasskeyHandler<ApplicationUser>? passkeyHandler = null, IConfiguration? externalConfig = null)
    {
        var services = new ServiceCollection();
        services.AddLogging(o => o.SetMinimumLevel(LogLevel.Warning));
        services.AddDataProtection().UseEphemeralDataProtectionProvider();
        services.AddSingleton(h.Db); services.AddSingleton<TimeProvider>(h.Clock);
        services.AddSingleton<IConfiguration>(externalConfig ?? new ConfigurationBuilder().Build());
        services.AddScoped<ExternalLoginService>(); services.AddSingleton(SecurityRound1Fixtures.Links);
        services.AddSingleton<IEmailSender>(Email); services.AddSingleton(h.Notifier);
        services.AddSingleton<Microsoft.Extensions.Hosting.IHostEnvironment>(new GeoEnvironment());
        services.AddSingleton<GeoIpLocation>(); services.AddScoped<NotificationService>(); services.AddScoped<NewDeviceService>();
        services.AddHttpContextAccessor(); services.AddMemoryCache();
        services.AddSingleton(Tang5bPureTests.Turnstile());
        services.AddIdentity<ApplicationUser, IdentityRole>(o =>
        {
            o.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
            o.SignIn.RequireConfirmedEmail = true; o.Lockout.MaxFailedAccessAttempts = 10;
            o.Password.RequiredLength = 8;
            o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        }).AddEntityFrameworkStores<AppDbContext>().AddUserStore<ProtectedUserStore>().AddDefaultTokenProviders();
        services.AddAuthentication().AddQuizExternalProviders(externalConfig ?? new ConfigurationBuilder().Build());
        services.AddAuthentication().AddCookie(TwoFactorService.PasskeyPendingScheme, o =>
        { o.TimeProvider = h.Clock; o.ExpireTimeSpan = TimeSpan.FromMinutes(5); o.SlidingExpiration = false; });
        services.Configure<IdentityPasskeyOptions>(o => { o.UserVerificationRequirement = "required"; o.ResidentKeyRequirement = "required"; });
        if (passkeyHandler != null) services.AddSingleton(passkeyHandler);
        services.AddScoped<PasskeyService>();
        services.AddScoped<IUserClaimsPrincipalFactory<ApplicationUser>, GuestClaimsFactory>();
        services.AddDeviceSessions(); // (T7) dùng cùng validator/refresh với Program.cs.
        services.Configure<SecurityStampValidatorOptions>(o =>
        {
            o.ValidationInterval = TimeSpan.FromMinutes(1);
            o.TimeProvider = h.Clock;
        });
        services.Configure<CookieAuthenticationOptions>(IdentityConstants.TwoFactorUserIdScheme, o =>
        { o.TimeProvider = h.Clock; o.ExpireTimeSpan = TimeSpan.FromMinutes(5); o.SlidingExpiration = false; });
        services.Configure<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme, o => o.TimeProvider = h.Clock);
        services.AddScoped<AccountLockService>(); services.AddScoped<TwoFactorService>(); services.AddScoped<LoginService>(); services.AddScoped<GuestService>();
        provider = services.BuildServiceProvider(); scope = provider.CreateScope(); CreateHttp();
    }
    private void CreateHttp()
    {
        Http = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        Http.Request.Scheme = "https"; Http.Request.Host = new("localhost");
        Http.Request.Headers.Cookie = string.Join("; ", Cookies.Select(c => c.Key + "=" + c.Value));
        Http.Request.Form = new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>());
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = Http;
        SignIn.Context = Http;
    }
    public async Task NextAsync()
    {
        foreach (var header in Http.Response.Headers.SetCookie)
        {
            var pair = header!.Split(';')[0].Split('=', 2);
            if (pair[1] == "") Cookies.Remove(pair[0]); else Cookies[pair[0]] = pair[1];
        }
        scope.Dispose(); scope = provider.CreateScope(); CreateHttp();
        var ticket = await Http.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        if (ticket.Succeeded) Http.User = ticket.Principal!;
    }
    public AccountController Controller()
    {
        var controller = new AccountController(Users, SignIn, null!, null!, Login,
            Tang5bPureTests.Turnstile(), scope.ServiceProvider.GetRequiredService<AccountLockService>(), Guests, SecurityRound1Fixtures.Links, null!)
        { ControllerContext = new ControllerContext { HttpContext = Http } };
        controller.Url = new FakeUrl(controller.ControllerContext);
        controller.TempData = new TempDataDictionary(Http, new FakeTempData());
        return controller;
    }
    public async Task<ApplicationUser> PrepareAsync(Harness h, bool admin = false)
    {
        var user = (await Users.FindByIdAsync(await h.UserAsync("2FA")))!;
        user.UserName = user.Email; user.EmailConfirmed = true; user.IsSystemAdmin = admin; user.LockoutEnabled = true;
        Assert.True((await Users.UpdateAsync(user)).Succeeded);
        Assert.True((await Users.AddPasswordAsync(user, "Valid123!")).Succeeded);
        return user;
    }
    public async Task<string[]> EnableAppAsync(ApplicationUser user)
    {
        await TwoFactor.SetupAsync(user.Id);
        var key = (await Users.GetAuthenticatorKeyAsync(user))!;
        return await TwoFactor.ConfirmSetupAsync(user.Id, Totp(key), "https://localhost/Account");
    }
    // RFC 6238: tạo mã độc lập để kiểm provider Identity, không dùng mã giả trong test.
    // stepOffset: bước 30 giây lệch so với hiện tại (mã đã dùng ở bước này thì lần sau cần bước mới hơn, Identity nhận tới +2).
    public static string Totp(string base32, int stepOffset = 0)
    {
        var bits = string.Concat(base32.Select(c => Convert.ToString("ABCDEFGHIJKLMNOPQRSTUVWXYZ234567".IndexOf(c), 2).PadLeft(5, '0')));
        var key = Enumerable.Range(0, bits.Length / 8).Select(i => Convert.ToByte(bits.Substring(i * 8, 8), 2)).ToArray();
        var counter = BitConverter.GetBytes(DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30 + stepOffset);
        if (BitConverter.IsLittleEndian) Array.Reverse(counter);
        var hash = HMACSHA1.HashData(key, counter); var offset = hash[^1] & 15;
        var number = ((hash[offset] & 127) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (number % 1_000_000).ToString("D6");
    }
    public string EmailCode() => Regex.Match(Email.Messages.Last(m => m.Contains("Mã đăng nhập:")), "<b>([0-9]{6})</b>").Groups[1].Value;
    public void Dispose() { scope.Dispose(); provider.Dispose(); }
}

[Collection("db")]
public class Tang6aTests
{
    private const string AccountUrl = "https://localhost/Account";

    [Fact]
    public async Task SetupPhaiCoMaDungVaMuoiMaDuPhongBamDungMotLan()
    {
        using var h = new Harness(); using var i = new TwoFactorIdentity(h); var user = await i.PrepareAsync(h);
        await i.TwoFactor.SetupAsync(user.Id);
        await Assert.ThrowsAsync<BusinessException>(() => i.TwoFactor.ConfirmSetupAsync(user.Id, "wrong", AccountUrl));
        Assert.False(user.TwoFactorEnabled); Assert.Equal(1, user.AccessFailedCount);
        var codes = await i.EnableAppAsync(user);
        Assert.Equal(10, codes.Length); Assert.Equal(10, (await i.TwoFactor.StatusAsync(user)).RecoveryCodes);
        var stored = await i.Users.GetAuthenticationTokenAsync(user, "[AspNetUserStore]", "RecoveryCodes");
        foreach (var code in codes) Assert.DoesNotContain(code, stored!);
        Assert.True((await i.Users.RedeemTwoFactorRecoveryCodeAsync(user, codes[0])).Succeeded);
        Assert.False((await i.Users.RedeemTwoFactorRecoveryCodeAsync(user, codes[0])).Succeeded);
        Assert.Equal(9, await i.Users.CountRecoveryCodesAsync(user));
        var next = await i.TwoFactor.ChangeAsync(user.Id, TwoFactorChange.Regenerate, "Valid123!", null, AccountUrl);
        Assert.Equal(10, next.Length); Assert.False((await i.Users.RedeemTwoFactorRecoveryCodeAsync(user, codes[1])).Succeeded);
        Assert.Equal(10, (await i.TwoFactor.StatusAsync(user)).RecoveryCodes);
        var key = (await i.Users.GetAuthenticatorKeyAsync(user))!;
        foreach (var message in i.Email.Messages)
        {
            Assert.Contains("Nếu không phải bạn", message); Assert.Contains("Khóa bảo vệ", message);
            Assert.DoesNotContain(key, message); Assert.DoesNotContain("otpauth", message);
            foreach (var code in codes.Concat(next)) Assert.DoesNotContain(code, message);
        }
    }

    // Lỗi thật khi thử trình duyệt: mở lại trang thiết lập sau khi đã bật → BusinessExceptionFilter quay về Referer
    // (chính trang này) → vòng chuyển hướng vô tận.
    [Fact]
    public async Task MoLaiTrangThietLapKhiDaBatVeTaiKhoanKhongLap()
    {
        using var h = new Harness(); using var i = new TwoFactorIdentity(h); var user = await i.PrepareAsync(h, admin: true);
        await i.EnableAppAsync(user);
        await i.SignIn.SignInAsync(user, false); await i.NextAsync();
        var controller = i.Controller();
        var result = Assert.IsType<RedirectToActionResult>(await controller.SetupAuthenticator(true, i.TwoFactor));
        Assert.Equal("Index", result.ActionName); Assert.Contains("đã được bật", controller.TempData["Error"]!.ToString());
    }

    [Theory]
    [InlineData("app"), InlineData("recovery"), InlineData("email")]
    public async Task MatKhauChiTaoPendingVaDungMaMoiDangNhapGopKhach(string method)
    {
        using var h = new Harness(); var scenario = await h.ScenarioAsync();
        await h.Assignments.ChangeStrangerPolicyAsync(scenario.Assignment.Id, StrangerPolicy.Allow, scenario.Lecturer);
        using var i = new TwoFactorIdentity(h); var user = await i.PrepareAsync(h);
        var token = GuestService.NewToken(); var guest = await i.Guests.JoinAsync(scenario.Assignment.Id, "Khách", token);
        var attempt = await h.Attempts.StartAsync(scenario.Assignment.Id, guest.UserId);
        var codes = await i.EnableAppAsync(user);
        if (method == "email") await i.TwoFactor.ChangeAsync(user.Id, TwoFactorChange.EnableEmail, null, null, AccountUrl);
        i.Cookies[GuestService.CookieName] = token; await i.NextAsync();
        await i.SignIn.SignInAsync(guest.User, false); await i.NextAsync();
        Assert.True(GuestAccess.IsGuest(i.Http.User));
        var controller = i.Controller();
        Assert.Equal("LoginTwoFactor", Assert.IsType<RedirectToActionResult>(await controller.Login(new LoginModel
        { Email = user.Email!, Password = "Valid123!", ReturnUrl = "/Account", RememberMe = true })).ActionName);
        Assert.Null(guest.MergedIntoUserId);
        Assert.DoesNotContain(i.Http.Response.Headers.SetCookie, c => c!.StartsWith(".AspNetCore.Identity.Application="));
        await i.NextAsync();
        Assert.NotNull(await i.SignIn.GetTwoFactorAuthenticationUserAsync());
        string code;
        if (method == "app") code = TwoFactorIdentity.Totp((await i.Users.GetAuthenticatorKeyAsync(user))!, 1); // mã bước hiện tại đã dùng khi thiết lập
        else if (method == "email") { await i.TwoFactor.SendEmailAsync(user.Id); code = i.EmailCode(); }
        else code = codes[0];
        controller = i.Controller();
        Assert.Equal("/Account", Assert.IsType<LocalRedirectResult>(await controller.LoginTwoFactor(method, code, i.TwoFactor)).Url);
        Assert.Equal(user.Id, guest.MergedIntoUserId); Assert.Equal(user.Id, attempt.Participant.UserId);
        Assert.Contains("Đã lưu kết quả làm khi là khách", controller.TempData["Success"]!.ToString());
        await i.NextAsync();
        Assert.Equal(user.Id, i.Http.User.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.Null(await i.TwoFactor.PendingAsync()); Assert.Equal(0, user.AccessFailedCount);
    }

    [Theory]
    [InlineData("app"), InlineData("email"), InlineData("recovery")]
    public async Task SaiMaCongDonCungMatKhauVaKhoaMuoiLan(string method)
    {
        using var h = new Harness(); using var i = new TwoFactorIdentity(h); var user = await i.PrepareAsync(h);
        await i.EnableAppAsync(user);
        for (var n = 0; n < 4; n++) Assert.False((await i.Login.LoginAsync(user.Email!, "wrong", false, null, null)).Succeeded);
        Assert.True((await i.Login.LoginAsync(user.Email!, "Valid123!", false, null, null)).RequiresTwoFactor);
        Assert.Equal(4, user.AccessFailedCount); await i.NextAsync();
        for (var n = 0; n < 6; n++) Assert.False(await i.TwoFactor.VerifyLoginAsync((await i.TwoFactor.PendingAsync())!, method, "wrong"));
        Assert.True(await i.Users.IsLockedOutAsync(user));
        Assert.InRange(user.LockoutEnd!.Value - DateTimeOffset.UtcNow, TimeSpan.FromMinutes(14), TimeSpan.FromMinutes(16));
    }

    [Fact]
    public async Task PendingHetHanVaKhoaBaoVeLucHoanTatDeuTuChoi()
    {
        using var h = new Harness(); using var i = new TwoFactorIdentity(h); var user = await i.PrepareAsync(h);
        var codes = await i.EnableAppAsync(user);
        await i.Login.LoginAsync(user.Email!, "Valid123!", false, null, null); await i.NextAsync();
        var pending = (await i.TwoFactor.PendingAsync())!;
        h.Clock.UtcNow = h.Clock.UtcNow.AddMinutes(6);
        Assert.Null(await i.TwoFactor.PendingAsync());
        Assert.False(await i.TwoFactor.VerifyLoginAsync(pending, "recovery", codes[0]));
        Assert.IsType<RedirectToActionResult>(await i.Controller().LoginTwoFactor("recovery", codes[0], i.TwoFactor));
        await i.NextAsync();
        await i.Login.LoginAsync(user.Email!, "Valid123!", false, null, null); await i.NextAsync();
        pending = (await i.TwoFactor.PendingAsync())!;
        Assert.True(await i.TwoFactor.VerifyLoginAsync(pending, "recovery", codes[0]));
        h.Db.AccountLocks.Add(new() { UserId = user.Id, Kind = AccountLockKind.Protective, StartsAt = h.Clock.UtcNow });
        await h.Db.SaveChangesAsync();
        Assert.False((await i.Login.CompleteAsync(user, false, i.Guests, expectedSecurityStamp: pending.SecurityStamp)).Succeeded);
        Assert.DoesNotContain(i.Http.Response.Headers.SetCookie, c => c!.StartsWith(".AspNetCore.Identity.Application="));
    }

    [Fact]
    public async Task EmailPhaiOptInHetHanDungNamPhutVaGuiLaiMotPhut()
    {
        using var h = new Harness(); using var i = new TwoFactorIdentity(h); var user = await i.PrepareAsync(h);
        Assert.False(await i.TwoFactor.EmailAllowedAsync(user));
        await Assert.ThrowsAsync<ForbiddenException>(() => i.TwoFactor.SendEmailAsync(user.Id));
        var codes = await i.TwoFactor.ChangeAsync(user.Id, TwoFactorChange.EnableEmail, null, null, AccountUrl);
        Assert.Equal(10, codes.Length); Assert.True(await i.TwoFactor.EmailAllowedAsync(user));
        Assert.False(await TwoFactorService.HasStrongMethodAsync(i.Users, user));
        await i.Login.LoginAsync(user.Email!, "Valid123!", false, null, null); await i.NextAsync();
        await i.TwoFactor.SendEmailAsync(user.Id); var first = i.EmailCode();
        var stored = await i.Users.GetAuthenticationTokenAsync(user, TwoFactorService.Provider, "EmailChallenge");
        Assert.DoesNotContain(first, stored!);
        await Assert.ThrowsAsync<BusinessException>(() => i.TwoFactor.SendEmailAsync(user.Id));
        h.Clock.UtcNow = h.Clock.UtcNow.AddSeconds(60);
        await i.TwoFactor.SendEmailAsync(user.Id); var second = i.EmailCode();
        var pending = (await i.TwoFactor.PendingAsync())!;
        Assert.True(await i.TwoFactor.VerifyLoginAsync(pending, "email", second));
        Assert.False(await i.TwoFactor.VerifyLoginAsync(pending, "email", second));
        h.Clock.UtcNow = h.Clock.UtcNow.AddMinutes(1); await i.TwoFactor.SendEmailAsync(user.Id); var expired = i.EmailCode();
        h.Clock.UtcNow = h.Clock.UtcNow.AddMinutes(5);
        // Phiên mới để kiểm riêng hạn mã email, không lẫn hạn pending.
        await i.Login.LoginAsync(user.Email!, "Valid123!", false, null, null); await i.NextAsync();
        Assert.False(await i.TwoFactor.VerifyLoginAsync((await i.TwoFactor.PendingAsync())!, "email", expired));
    }

    [Theory]
    [InlineData(false), InlineData(true)]
    public async Task AdminVaNguoiVuaDuocCapQuyenBatBuocAppKhongEmailKhongTat(bool fromAdminPage)
    {
        using var h = new Harness(); using var i = new TwoFactorIdentity(h); var user = await i.PrepareAsync(h);
        await i.TwoFactor.ChangeAsync(user.Id, TwoFactorChange.EnableEmail, null, null, AccountUrl);
        await i.TwoFactor.SendEmailAsync(user.Id); var code = i.EmailCode();
        await i.SignIn.SignInAsync(user, false); await i.NextAsync();
        var called = false; var gate = new AdminTwoFactorMiddleware(_ => { called = true; return Task.CompletedTask; });
        i.Http.Request.Path = "/Admin/Users";
        await gate.InvokeAsync(i.Http, i.Users); Assert.True(called);
        if (fromAdminPage)
        {
            var admin = await i.PrepareAsync(h, true);
            await new AdminService(h.Db, h.Clock).ChangeRoleAsync(user.Id, true, "Cấp quyền", admin.Id);
        }
        else await AdminService.BootstrapAsync(h.Db, [user.Email!]);
        called = false; await gate.InvokeAsync(i.Http, i.Users);
        Assert.False(called); Assert.Contains("SetupAuthenticator", i.Http.Response.Headers.Location.ToString());
        Assert.True((await i.Login.LoginAsync(user.Email!, "Valid123!", false, null, null)).Succeeded); // cho vào thiết lập
        Assert.False(await i.TwoFactor.EmailAllowedAsync(user));
        await Assert.ThrowsAsync<ForbiddenException>(() => i.TwoFactor.SendEmailAsync(user.Id));
        await Assert.ThrowsAsync<BusinessException>(() => i.TwoFactor.ChangeAsync(user.Id, TwoFactorChange.DisableEmail, "Valid123!", null, AccountUrl));
        var codes = await i.EnableAppAsync(user);
        var pending = new PendingTwoFactor(user, false, "/Admin", h.Clock.GetUtcNow().AddMinutes(5), user.SecurityStamp!);
        Assert.False(await i.TwoFactor.VerifyLoginAsync(pending, "email", code));
        foreach (var change in new[] { TwoFactorChange.Disable, TwoFactorChange.ResetAuthenticator, TwoFactorChange.EnableEmail })
            await Assert.ThrowsAsync<BusinessException>(() => i.TwoFactor.ChangeAsync(user.Id, change, "Valid123!", null, AccountUrl));
        Assert.True((await i.Login.LoginAsync(user.Email!, "Valid123!", false, null, null)).RequiresTwoFactor);
        await i.NextAsync();
        Assert.IsType<LocalRedirectResult>(await i.Controller().LoginTwoFactor("recovery", codes[0], i.TwoFactor));
        Assert.Contains(i.Http.Response.Headers.SetCookie, c => c!.StartsWith("Identity.TwoFactorRememberMe=;")); // .NET 10 không còn tiền tố ".AspNetCore."
        called = false; await gate.InvokeAsync(i.Http, i.Users); Assert.True(called);
    }

    // Người dùng chốt 2026-11-06: không có "nhớ máy". Cookie nhớ máy hợp lệ còn sót (bản trước) cũng không bỏ qua lớp 2,
    // và bị xóa khi đăng nhập xong.
    [Fact]
    public async Task KhongNhoMayCookieCuVanPhaiNhapMa()
    {
        using var h = new Harness(); using var i = new TwoFactorIdentity(h); var user = await i.PrepareAsync(h);
        var codes = await i.EnableAppAsync(user);
        await i.SignIn.RememberTwoFactorClientAsync(user); await i.NextAsync();
        Assert.True((await i.Login.LoginAsync(user.Email!, "Valid123!", false, null, null)).RequiresTwoFactor);
        await i.NextAsync();
        Assert.IsType<LocalRedirectResult>(await i.Controller().LoginTwoFactor("recovery", codes[0], i.TwoFactor));
        Assert.Contains(i.Http.Response.Headers.SetCookie, c => c!.StartsWith("Identity.TwoFactorRememberMe=;"));
        var view = await Tang4ViewTests.Render("Account", "LoginTwoFactor", new TwoFactorLoginPage(true, true));
        Assert.DoesNotContain("rememberBrowser", view); Assert.DoesNotContain("Nhớ máy", WebUtility.HtmlDecode(view));
    }

    // Lỗi thật khi thử 2 cửa sổ: cùng một mã app vào được 2 lần. Mỗi bước 30 giây chỉ dùng một lần, không nhận bước cũ hơn.
    [Fact]
    public async Task MaAppChiDungMotLanVaKhongNhanMaCuHon()
    {
        // Mã app đổi theo khung 30 giây của đồng hồ thật: khung đổi giữa các dòng dưới thì "mã thiết lập" thành mã mới và test
        // hỏng ngẫu nhiên (gặp 15/11/2026). Đang ở 5 giây cuối khung thì chờ sang khung mới rồi mới chạy.
        while (DateTimeOffset.UtcNow.ToUnixTimeSeconds() % 30 >= 25) await Task.Delay(250);
        using var h = new Harness(); using var i = new TwoFactorIdentity(h); var user = await i.PrepareAsync(h);
        await i.EnableAppAsync(user); // đã dùng mã của bước hiện tại để xác nhận
        var key = (await i.Users.GetAuthenticatorKeyAsync(user))!;
        PendingTwoFactor Pending() => new(user, false, "/", h.Clock.GetUtcNow().AddMinutes(5), user.SecurityStamp!);
        Assert.False(await i.TwoFactor.VerifyLoginAsync(Pending(), "app", TwoFactorIdentity.Totp(key))); // mã xác nhận thiết lập
        Assert.True(await i.TwoFactor.VerifyLoginAsync(Pending(), "app", TwoFactorIdentity.Totp(key, 1)));
        Assert.False(await i.TwoFactor.VerifyLoginAsync(Pending(), "app", TwoFactorIdentity.Totp(key, 1))); // dùng lại
        Assert.False(await i.TwoFactor.VerifyLoginAsync(Pending(), "app", TwoFactorIdentity.Totp(key, -1))); // cũ hơn
        Assert.True(await i.TwoFactor.VerifyLoginAsync(Pending(), "app", TwoFactorIdentity.Totp(key, 2)));
        Assert.Equal(TwoFactorService.MatchedStep(key, TwoFactorIdentity.Totp(key, 1), DateTimeOffset.UtcNow),
            DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30 + 1);
    }

    // Người dùng yêu cầu 2026-11-06: khóa app xác thực không được nằm dạng rõ trong DB.
    [Fact]
    public async Task KhoaAppXacThucMaHoaTrongDbVaKhoaCuDangRoBiBoQua()
    {
        using var h = new Harness(); using var i = new TwoFactorIdentity(h); var user = await i.PrepareAsync(h);
        await i.EnableAppAsync(user);
        var key = (await i.Users.GetAuthenticatorKeyAsync(user))!;
        var row = h.Db.UserTokens.Single(t => t.UserId == user.Id && t.LoginProvider == "[AspNetUserStore]" && t.Name == "AuthenticatorKey");
        Assert.DoesNotContain(key, row.Value!); Assert.StartsWith("CfDJ8", row.Value);
        // Mã TOTP vẫn tính được từ khóa đã giải mã.
        Assert.True(await i.Users.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultAuthenticatorProvider, TwoFactorIdentity.Totp(key)));
        // Khóa lưu dạng rõ (trước khi mã hóa) không giải mã được → coi như chưa có app, không ném lỗi.
        row.Value = key; await h.Db.SaveChangesAsync();
        Assert.Null(await i.Users.GetAuthenticatorKeyAsync(user));
        Assert.False(await TwoFactorService.HasAuthenticatorAsync(i.Users, user));
        // Lỗi thật khi thử trình duyệt: mở trang thiết lập tạo key mới nhưng cờ xác nhận của key cũ còn lại
        // → key chưa ai quét được coi là app đã bật, Admin qua cổng /Admin. Cờ gắn với đúng key nên phải vẫn là false.
        await i.TwoFactor.SetupAsync(user.Id);
        Assert.NotNull(await i.Users.GetAuthenticatorKeyAsync(user));
        Assert.False(await TwoFactorService.HasAuthenticatorAsync(i.Users, user));
    }

    [Theory]
    [InlineData(TwoFactorChange.Disable), InlineData(TwoFactorChange.Regenerate), InlineData(TwoFactorChange.ResetAuthenticator), InlineData(TwoFactorChange.DisableEmail)]
    public async Task SaiMatKhauKhongDuocThaoTacNhayCam(TwoFactorChange change)
    {
        using var h = new Harness(); using var i = new TwoFactorIdentity(h); var user = await i.PrepareAsync(h);
        await i.EnableAppAsync(user); await i.TwoFactor.ChangeAsync(user.Id, TwoFactorChange.EnableEmail, null, null, AccountUrl);
        var stamp = user.SecurityStamp; var count = i.Email.Messages.Count;
        await Assert.ThrowsAsync<BusinessException>(() => i.TwoFactor.ChangeAsync(user.Id, change, "wrong", null, AccountUrl));
        Assert.True(user.TwoFactorEnabled); Assert.Equal(stamp, user.SecurityStamp); Assert.Equal(count, i.Email.Messages.Count);
        Assert.Equal(1, user.AccessFailedCount);
    }

    [Fact]
    public async Task KhongMatKhauXacNhanBangMaHaiLopHienTai()
    {
        using var h = new Harness(); using var i = new TwoFactorIdentity(h); var user = await i.PrepareAsync(h);
        var codes = await i.EnableAppAsync(user); Assert.True((await i.Users.RemovePasswordAsync(user)).Succeeded);
        await Assert.ThrowsAsync<BusinessException>(() => i.TwoFactor.ChangeAsync(user.Id, TwoFactorChange.Regenerate, null, "wrong", AccountUrl));
        Assert.Equal(10, (await i.TwoFactor.ChangeAsync(user.Id, TwoFactorChange.Regenerate, null, codes[0], AccountUrl)).Length);
    }

    [Theory]
    [InlineData(TwoFactorChange.Disable), InlineData(TwoFactorChange.DisableEmail), InlineData(TwoFactorChange.ResetAuthenticator)]
    public async Task MoiThayDoiGuiThongBaoKhongChuaKhoaHayMa(TwoFactorChange change)
    {
        using var h = new Harness(); using var i = new TwoFactorIdentity(h); var user = await i.PrepareAsync(h);
        var codes = await i.EnableAppAsync(user);
        var key = (await i.Users.GetAuthenticatorKeyAsync(user))!;
        await i.TwoFactor.ChangeAsync(user.Id, TwoFactorChange.EnableEmail, null, null, AccountUrl);
        var before = i.Email.Messages.Count;
        await i.TwoFactor.ChangeAsync(user.Id, change, "Valid123!", null, AccountUrl);
        Assert.True(i.Email.Messages.Count > before);
        foreach (var message in i.Email.Messages)
        {
            Assert.Contains("Nếu không phải bạn", message); Assert.Contains(AccountUrl, message);
            Assert.DoesNotContain(key, message); Assert.DoesNotMatch("[0-9]{6}", message);
            foreach (var code in codes) Assert.DoesNotContain(code, message);
        }
        var status = await i.TwoFactor.StatusAsync(user);
        Assert.Equal(change != TwoFactorChange.Disable, status.Enabled);
        Assert.Equal(change != TwoFactorChange.DisableEmail && change != TwoFactorChange.Disable, status.Email);
    }

    [Fact]
    public async Task ResetMatKhauVoHieuPendingCu()
    {
        using var h = new Harness(); using var i = new TwoFactorIdentity(h); var user = await i.PrepareAsync(h);
        await i.EnableAppAsync(user);
        await TwoFactorService.BeginAsync(i.SignIn, user, false, "/", h.Clock.GetUtcNow()); await i.NextAsync();
        var pending = (await i.TwoFactor.PendingAsync())!; Assert.NotNull(pending);
        var reset = await i.Users.GeneratePasswordResetTokenAsync(user);
        Assert.True((await i.Users.ResetPasswordAsync(user, reset, "NewValid123!")).Succeeded);
        Assert.Null(await i.TwoFactor.PendingAsync());
        Assert.False((await i.Login.CompleteAsync(user, false, i.Guests, pending.SecurityStamp, "app")).Succeeded);
    }

    [Fact]
    public async Task VuaXacMinhEmailNhungDuocCapAdminTruocHoanTatPhaiDangNhapLai()
    {
        using var h = new Harness(); using var i = new TwoFactorIdentity(h); var user = await i.PrepareAsync(h);
        await i.EnableAppAsync(user); await i.TwoFactor.ChangeAsync(user.Id, TwoFactorChange.EnableEmail, null, null, AccountUrl);
        await i.Login.LoginAsync(user.Email!, "Valid123!", false, null, null); await i.NextAsync();
        var pending = (await i.TwoFactor.PendingAsync())!;
        await i.TwoFactor.SendEmailAsync(user.Id);
        Assert.True(await i.TwoFactor.VerifyLoginAsync(pending, "email", i.EmailCode()));
        await AdminService.BootstrapAsync(h.Db, [user.Email!]);
        Assert.False((await i.Login.CompleteAsync(user, false, i.Guests, pending.SecurityStamp, "email")).Succeeded);
        Assert.DoesNotContain(i.Http.Response.Headers.SetCookie, c => c!.StartsWith(".AspNetCore.Identity.Application="));
    }
}
