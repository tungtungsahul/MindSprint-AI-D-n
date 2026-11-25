using System.Security.Claims;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.MicrosoftAccount;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using MindSprintAI_ASP.NET_MVC.Controllers;
using MindSprintAI_ASP.NET_MVC.Data.Entities;
using MindSprintAI_ASP.NET_MVC.Models;
using MindSprintAI_ASP.NET_MVC.Services;

namespace MindSprintAI_ASP.NET_MVC.Tests;

public class Tang6cPureTests
{
    internal static IConfiguration Config(params (string Key, string Value)[] extra) => new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string?>
        {
            ["Authentication:Google:ClientId"] = "test-id", ["Authentication:Google:ClientSecret"] = "test-secret",
            ["Authentication:Microsoft:ClientId"] = "test-id", ["Authentication:Microsoft:ClientSecret"] = "test-secret"
        }.Concat(extra.Select(x => new KeyValuePair<string, string?>(x.Key, x.Value)))).Build();

    // Lỗi thật khi thử: Google tự chọn tài khoản đang đăng nhập nên không nối được tài khoản thứ hai.
    [Fact]
    public void LuonHienTrangChonTaiKhoan() => Assert.Equal(
        "https://accounts.google.com/o/oauth2/v2/auth?client_id=x&state=y&prompt=select_account",
        ExternalProviderConfig.WithSelectAccount("https://accounts.google.com/o/oauth2/v2/auth?client_id=x&state=y"));

    [Fact]
    public async Task KhongCauHinhThiKhongDangKyProviderVaKhongCoNut()
    {
        var services = new ServiceCollection(); services.AddLogging();
        services.AddAuthentication().AddQuizExternalProviders(new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();
        Assert.Empty(await provider.GetRequiredService<IAuthenticationSchemeProvider>().GetAllSchemesAsync());
        var html = await Tang4ViewTests.Render("Account", "Login", new MindSprintAI_ASP.NET_MVC.Models.LoginModel());
        Assert.DoesNotContain("data-action=\"ExternalLogin\"", html);
    }

    [Fact]
    public void PhienGanDayPhaiDungNguoiDungVaKhongNhanThoiGianTuongLai()
    {
        var now = DateTimeOffset.UtcNow;
        ClaimsPrincipal Principal(string id, DateTimeOffset time) => new(new ClaimsIdentity([
            new(ClaimTypes.NameIdentifier, id), new(TwoFactorService.SignInTimeClaim, time.ToUnixTimeSeconds().ToString())], "test"));
        Assert.True(TwoFactorService.IsRecentSignIn(Principal("a", now), "a", now));
        Assert.False(TwoFactorService.IsRecentSignIn(Principal("a", now.AddMinutes(-10).AddSeconds(-1)), "a", now));
        Assert.False(TwoFactorService.IsRecentSignIn(Principal("b", now), "a", now));
        Assert.False(TwoFactorService.IsRecentSignIn(Principal("a", now.AddMinutes(1)), "a", now));
        var malformed = Principal("a", now);
        var identity = (ClaimsIdentity)malformed.Identity!;
        identity.RemoveClaim(identity.FindFirst(TwoFactorService.SignInTimeClaim)!);
        identity.AddClaim(new(TwoFactorService.SignInTimeClaim, long.MinValue.ToString()));
        Assert.False(TwoFactorService.IsRecentSignIn(malformed, "a", now));
    }

    [Fact]
    public async Task ProviderChiDangKyKhiDuHaiGiaTriVaOptionsKhongLuuToken()
    {
        var services = new ServiceCollection(); services.AddLogging();
        services.AddAuthentication().AddQuizExternalProviders(Config());
        using var sp = services.BuildServiceProvider();
        Assert.Equal(new[] { "Google", "Microsoft" }, (await sp.GetRequiredService<IAuthenticationSchemeProvider>().GetAllSchemesAsync()).Select(s => s.Name).Order());
        var google = sp.GetRequiredService<IOptionsMonitor<GoogleOptions>>().Get("Google");
        var microsoft = sp.GetRequiredService<IOptionsMonitor<MicrosoftAccountOptions>>().Get("Microsoft");
        Assert.False(google.SaveTokens); Assert.False(microsoft.SaveTokens);
        Assert.Equal("/signin-google", google.CallbackPath); Assert.Equal("/signin-microsoft", microsoft.CallbackPath);
        Assert.Contains("/common/", microsoft.AuthorizationEndpoint); Assert.Contains("/common/", microsoft.TokenEndpoint);
        using var json = JsonDocument.Parse("{\"email_verified\":true}");
        var identity = new ClaimsIdentity();
        foreach (var action in google.ClaimActions) action.Run(json.RootElement, identity, "Google");
        Assert.True(bool.Parse(identity.FindFirst(ExternalProviderConfig.GoogleVerifiedClaim)!.Value));
        var partial = new ServiceCollection(); partial.AddLogging();
        partial.AddAuthentication().AddQuizExternalProviders(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["Authentication:Google:ClientId"] = "only-id", ["Authentication:Microsoft:ClientSecret"] = "only-secret" }).Build());
        using var missing = partial.BuildServiceProvider();
        Assert.Empty(await missing.GetRequiredService<IAuthenticationSchemeProvider>().GetAllSchemesAsync());
    }

    private static void FormMatches(string html, string action)
    {
        var method = typeof(AccountController).GetMethod(action)!;
        var names = method.GetParameters().Where(p => !p.IsDefined(typeof(FromServicesAttribute))).Select(p => p.Name!).Order();
        var form = Assert.Single(Regex.Matches(html, "<form\\b[^>]*>[\\s\\S]*?</form>").Select(m => m.Value), f => f.Contains($"data-action=\"{action}\""));
        var fields = Regex.Matches(form, "name=\"([^\"]+)\"").Select(m => m.Groups[1].Value).Where(n => n != "__RequestVerificationToken").Distinct().Order();
        Assert.Equal(names, fields); Assert.Contains("__RequestVerificationToken", form);
    }

    [Theory]
    [InlineData(true, false), InlineData(false, true), InlineData(false, false)]
    public async Task MoiFormKhopThamSoVaXacNhanLai(bool password, bool twoFactor)
    {
        var status = new TwoFactorStatus(twoFactor, twoFactor, false, false, password, 0);
        var html = await Tang4ViewTests.Render("Account", "Index", new ApplicationUser(), new()
        { ["ExternalLogins"] = new ExternalLoginPage([new("Microsoft", "key-not-token", "Microsoft")], ["Google"], password, status) });
        FormMatches(html, "LinkLogin"); FormMatches(html, "RemoveLogin");
        if (!password) FormMatches(html, "SetPassword"); else Assert.DoesNotContain("data-action=\"SetPassword\"", html);
        if (!password && !twoFactor)
        {
            Assert.Contains("10 phút", WebUtility.HtmlDecode(html));
            var passkeys = await Tang4ViewTests.Render("Account", "Index", new ApplicationUser(), new()
            { ["TwoFactor"] = status, ["Passkeys"] = new PasskeyPage([new("aGVsbG8", "Laptop", DateTime.UtcNow, null)], false) });
            FormMatches(passkeys, "DeletePasskey");
            var deletion = Assert.Single(Regex.Matches(passkeys, "<form\\b[^>]*>[\\s\\S]*?</form>").Select(m => m.Value), f => f.Contains("data-action=\"DeletePasskey\""));
            Assert.Contains("10 phút", WebUtility.HtmlDecode(deletion)); Assert.DoesNotContain("required", deletion);
        }
        var login = await Tang4ViewTests.Render("Account", "Login", new LoginModel { ReturnUrl = "/Account" }, configureServices:
            services => services.AddAuthentication().AddQuizExternalProviders(new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?> { ["Authentication:Google:ClientId"] = "test-id", ["Authentication:Google:ClientSecret"] = "test-secret" }).Build()));
        FormMatches(login, "ExternalLogin"); Assert.Contains("Đăng nhập bằng Google", WebUtility.HtmlDecode(login));
    }

    [Fact]
    public void MoiPostCoCsrfRateLimitNoStoreVaCallbackKhongMoQuanLyChoKhach()
    {
        foreach (var name in new[] { "ExternalLogin", "LinkLogin", "RemoveLogin", "SetPassword" })
        {
            var method = typeof(AccountController).GetMethod(name)!;
            Assert.NotNull(method.GetCustomAttribute<HttpPostAttribute>());
            Assert.NotNull(method.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
            Assert.Equal(AccountPolicy.Expected(method.Name), method.GetCustomAttribute<EnableRateLimitingAttribute>()!.PolicyName);
            Assert.True(method.GetCustomAttribute<ResponseCacheAttribute>()!.NoStore);
        }
        foreach (var name in new[] { "ExternalLoginCallback", "LinkLoginCallback" })
        {
            var method = typeof(AccountController).GetMethod(name)!;
            Assert.NotNull(method.GetCustomAttribute<HttpGetAttribute>());
            Assert.True(method.GetCustomAttribute<ResponseCacheAttribute>()!.NoStore);
        }
    }

    [Fact]
    public void RefreshKhongGiaHanThoiDiemDangNhapVaTenTheoDangKy()
    {
        var old = new ClaimsPrincipal(new ClaimsIdentity([new(ClaimTypes.NameIdentifier, "a"), new(TwoFactorService.SignInTimeClaim, "123")], "test"));
        var fresh = new ClaimsPrincipal(new ClaimsIdentity([new(ClaimTypes.NameIdentifier, "a")], "test"));
        TwoFactorService.PreserveSignInTime(old, fresh);
        Assert.Equal("123", fresh.FindFirstValue(TwoFactorService.SignInTimeClaim));
        var info = Tang6cTests.Info(name: "   ");
        Assert.Equal(info.Principal.FindFirstValue(ClaimTypes.Email)!.Split('@')[0], ExternalLoginService.DisplayName(info, info.Principal.FindFirstValue(ClaimTypes.Email)!));
        info = Tang6cTests.Info(name: "  " + new string('a', 110) + "  ");
        Assert.Equal(100, ExternalLoginService.DisplayName(info, "x@test.vn").Length);
    }
}

[Collection("db")]
public class Tang6cTests
{
    private const string AccountUrl = "https://localhost/Account";
    internal static ExternalLoginInfo Info(string provider = "Google", string? email = "new", string verified = "true", string? key = null, string? name = " Tên Google ")
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, key ?? Guid.NewGuid().ToString("N")), new("email_verified", verified) };
        if (email != null) claims.Add(new(ClaimTypes.Email, email == "new" ? Guid.NewGuid() + "@test.vn" : email));
        if (name != null) claims.Add(new(ClaimTypes.Name, name));
        return new(new ClaimsPrincipal(new ClaimsIdentity(claims, provider)), provider, claims[0].Value, provider);
    }
    private static TwoFactorIdentity Identity(Harness h, IConfiguration? config = null) => new(h, externalConfig: config ?? Tang6cPureTests.Config());
    private static async Task SignIn(TwoFactorIdentity i, ApplicationUser user)
    {
        Assert.True((await i.Login.CompleteAsync(user, false)).Succeeded); await i.NextAsync();
    }
    private static async Task<ApplicationUser> ProviderUser(TwoFactorIdentity i, ExternalLoginInfo? info = null)
    {
        info ??= Info(); Assert.True((await i.External.SignInAsync(info)).Succeeded);
        await i.NextAsync(); return (await i.Users.FindByLoginAsync(info.LoginProvider, info.ProviderKey))!;
    }
    private static async Task Link(TwoFactorIdentity i, ApplicationUser user, ExternalLoginInfo info, string? password = "Valid123!")
    {
        info.AuthenticationProperties = await i.External.BeginLinkAsync(user.Id, info.LoginProvider, password, null, "/Account/LinkLoginCallback");
        await i.External.LinkAsync(user.Id, info, AccountUrl);
        await i.SignIn.SignInWithClaimsAsync(user, false, i.Http.User.Claims.Where(c => c.Type is TwoFactorService.SignInTimeClaim or "amr"));
        await i.NextAsync();
    }

    [Theory]
    [InlineData("Google"), InlineData("Microsoft")]
    public async Task EmailMoiTaoTaiKhoanKhongMatKhauVaGopKhach(string provider)
    {
        using var h = new Harness(); var s = await h.ScenarioAsync();
        await h.Assignments.ChangeStrangerPolicyAsync(s.Assignment.Id, StrangerPolicy.Allow, s.Lecturer);
        using var i = Identity(h); var token = GuestService.NewToken();
        var guest = await i.Guests.JoinAsync(s.Assignment.Id, "Khách", token);
        // (T7) giữ cookie thiết bị như trình duyệt thật; thiếu nó thì đăng nhập không cấp được mã phiên.
        i.Http.Request.Headers.Cookie = $"{GuestService.CookieName}={token}; {LearnDevice.CookieName}={i.Cookies[LearnDevice.CookieName]}";
        var info = Info(provider); var outcome = await i.External.SignInAsync(info, i.Guests);
        Assert.True(outcome.Succeeded); Assert.True(outcome.MergedGuest);
        var user = (await i.Users.FindByLoginAsync(provider, info.ProviderKey))!;
        Assert.True(user.EmailConfirmed); Assert.False(await i.Users.HasPasswordAsync(user));
        Assert.Equal(user.Email, user.UserName); Assert.Equal("Tên Google", user.DisplayName);
        Assert.Equal(user.Id, guest.MergedIntoUserId);
        await i.NextAsync(); Assert.Equal(user.Id, i.Http.User.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.Null(i.Http.User.FindFirst("amr")); Assert.NotNull(i.Http.User.FindFirst(TwoFactorService.SignInTimeClaim));
    }

    [Theory]
    [InlineData("Google", null, "true"), InlineData("Microsoft", null, "true"), InlineData("Google", "new", "false")]
    public async Task ThieuEmailHoacGoogleChuaXacMinhBiTuChoi(string provider, string? email, string verified)
    {
        using var h = new Harness(); using var i = Identity(h); var count = await h.Db.Users.CountAsync();
        await Assert.ThrowsAsync<BusinessException>(() => i.External.SignInAsync(Info(provider, email, verified)));
        Assert.Equal(count, await h.Db.Users.CountAsync()); await i.NextAsync(); Assert.False(i.Http.User.Identity?.IsAuthenticated == true);
    }

    [Fact]
    public async Task TrungEmailKhongTuNoi()
    {
        using var h = new Harness(); using var i = Identity(h); var user = await i.PrepareAsync(h);
        var outcome = await i.External.SignInAsync(Info(email: user.Email));
        Assert.False(outcome.Succeeded); Assert.Equal(ExternalLoginService.Collision, outcome.Error);
        Assert.Empty(await i.Users.GetLoginsAsync(user)); await i.NextAsync(); Assert.False(i.Http.User.Identity?.IsAuthenticated == true);
    }

    [Fact]
    public async Task LienKetCuDangNhapVaResetSoLanSai()
    {
        using var h = new Harness(); using var i = Identity(h); var info = Info(); var user = await ProviderUser(i, info);
        await i.SignIn.SignOutAsync(); await i.NextAsync(); await i.Users.AccessFailedAsync(user);
        Assert.True((await i.External.SignInAsync(info)).Succeeded); await i.NextAsync();
        Assert.Equal(user.Id, i.Http.User.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.Equal(0, (await i.Users.FindByIdAsync(user.Id))!.AccessFailedCount);
    }

    [Theory]
    [InlineData(false), InlineData(true)]
    public async Task HaiLopVaAdminChiCoPasskeyPhaiChoBuocHai(bool admin)
    {
        using var h = new Harness(); using var i = Identity(h); var user = await i.PrepareAsync(h, admin); var info = Info(email: user.Email);
        Assert.True((await i.Users.AddLoginAsync(user, info)).Succeeded);
        if (admin) Assert.True((await i.Users.AddOrUpdatePasskeyAsync(user, TestPasskeyHandler.NewKey())).Succeeded);
        else await i.EnableAppAsync(user);
        // Đi qua callback thật, chỉ cookie external được dựng cục bộ; không gọi OAuth.
        var properties = i.SignIn.ConfigureExternalAuthenticationProperties(info.LoginProvider, "/Account/ExternalLoginCallback");
        await i.Http.SignInAsync(IdentityConstants.ExternalScheme, info.Principal, properties); await i.NextAsync();
        var result = Assert.IsType<RedirectToActionResult>(await i.Controller().ExternalLoginCallback("/Account", i.External));
        Assert.Equal("LoginTwoFactor", result.ActionName); await i.NextAsync();
        Assert.False(i.Http.User.Identity?.IsAuthenticated == true);
        var pending = await i.TwoFactor.PendingAsync(); Assert.NotNull(pending); Assert.Equal(user.Id, pending.User.Id); Assert.Equal("/Account", pending.ReturnUrl);
        Assert.False((await i.Http.AuthenticateAsync(IdentityConstants.ExternalScheme)).Succeeded);
    }

    [Fact]
    public async Task CompleteTrucTiepKhongCoYeuToHaiKhongBaoGioChoAdminCoPhuongThucManhVao()
    {
        using var h = new Harness(); using var i = Identity(h); var user = await i.PrepareAsync(h, true);
        await i.Users.AddOrUpdatePasskeyAsync(user, TestPasskeyHandler.NewKey());
        Assert.False((await i.Login.CompleteAsync(user, false, verifiedMethod: null)).Succeeded);
        await i.NextAsync(); Assert.False(i.Http.User.Identity?.IsAuthenticated == true);
    }

    [Theory]
    [InlineData(false), InlineData(true)]
    public async Task TaiKhoanKhoaBiTuChoi(bool identityLock)
    {
        using var h = new Harness(); using var i = Identity(h); var user = await i.PrepareAsync(h); var info = Info(email: user.Email);
        await i.Users.AddLoginAsync(user, info);
        if (identityLock) await i.Users.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddHours(1));
        else { h.Db.AccountLocks.Add(new() { UserId = user.Id, Kind = AccountLockKind.Protective, StartsAt = h.Clock.UtcNow }); await h.Db.SaveChangesAsync(); }
        var outcome = await i.External.SignInAsync(info); Assert.False(outcome.Succeeded);
        if (!identityLock) Assert.Equal("Tài khoản đang tạm khóa để bảo vệ. Muốn mở khóa, hãy bấm Quên mật khẩu ở trang đăng nhập và làm theo email.", outcome.Error);
        await i.NextAsync(); Assert.False(i.Http.User.Identity?.IsAuthenticated == true);
    }

    [Fact]
    public async Task NoiPhaiXacNhanLaiVaCallbackPhaiCoBangChungDungPhien()
    {
        using var h = new Harness(); using var i = Identity(h); var user = await i.PrepareAsync(h); await SignIn(i, user);
        await Assert.ThrowsAsync<BusinessException>(() => i.External.BeginLinkAsync(user.Id, "Google", "sai", null, "/Account"));
        Assert.Equal(1, user.AccessFailedCount);
        var info = Info(); await Assert.ThrowsAsync<BusinessException>(() => i.External.LinkAsync(user.Id, info, AccountUrl));
        info.AuthenticationProperties = await i.External.BeginLinkAsync(user.Id, "Google", "Valid123!", null, "/Account");
        h.Clock.UtcNow = h.Clock.UtcNow.AddMinutes(11);
        await Assert.ThrowsAsync<BusinessException>(() => i.External.LinkAsync(user.Id, info, AccountUrl));
        Assert.Empty(await i.Users.GetLoginsAsync(user));
    }

    [Fact]
    public async Task KhongNoiKeyDaThuocTaiKhoanKhac()
    {
        using var h = new Harness(); using var i = Identity(h); var first = await i.PrepareAsync(h); var info = Info();
        await i.Users.AddLoginAsync(first, info); var other = await i.PrepareAsync(h); await SignIn(i, other);
        var ex = await Assert.ThrowsAsync<BusinessException>(() => Link(i, other, info));
        Assert.Equal("Tài khoản Google/Microsoft này đã nối với tài khoản khác.", ex.Message);
        Assert.Empty(await i.Users.GetLoginsAsync(other));
    }

    [Fact]
    public async Task NoiGoVaDatMatKhauGuiEmailKhongToken()
    {
        using var h = new Harness(); using var i = Identity(h); var info = Info(); var user = await ProviderUser(i, info);
        var other = Info("Microsoft", key: "DO-NOT-EMAIL-KEY");
        other.AuthenticationTokens = [new() { Name = "access_token", Value = "DO-NOT-EMAIL-TOKEN" }];
        await Link(i, user, other, null); await i.Controller().SetPassword("Valid123!", null, null, i.External); await i.NextAsync();
        await i.External.RemoveAsync(user.Id, "Microsoft", other.ProviderKey, "Valid123!", null, AccountUrl);
        Assert.Equal(3, i.Email.Messages.Count);
        foreach (var body in i.Email.Messages)
        {
            var text = WebUtility.HtmlDecode(body); Assert.Contains("Nếu không phải bạn", text); Assert.Contains("/Account", text); // SetPassword qua controller dùng FakeUrl ("/Account/Index")
            Assert.DoesNotContain("DO-NOT-EMAIL", text); Assert.DoesNotContain("token=", text); Assert.DoesNotContain("code=", text);
        }
    }

    [Fact]
    public async Task KhongGoCachCuoiVaCoMatKhauThiGoDuoc()
    {
        using var h = new Harness(); using var i = Identity(h); var info = Info(); var user = await ProviderUser(i, info);
        var ex = await Assert.ThrowsAsync<BusinessException>(() => i.External.RemoveAsync(user.Id, "Google", info.ProviderKey, null, null, AccountUrl));
        Assert.Contains("cuối cùng", ex.Message); Assert.Single(await i.Users.GetLoginsAsync(user));
        await i.Controller().SetPassword("Valid123!", null, null, i.External); await i.NextAsync();
        await i.External.RemoveAsync(user.Id, "Google", info.ProviderKey, "Valid123!", null, AccountUrl);
        Assert.Empty(await i.Users.GetLoginsAsync(user)); Assert.True(await i.Users.HasPasswordAsync(user));
    }

    [Theory]
    [InlineData("aA1!"), InlineData("abcdefgh"), InlineData("Valid123!")]
    public async Task DatMatKhauTheoPolicyVaKhongGhiDeMatKhauCu(string newPassword)
    {
        using var h = new Harness(); using var i = Identity(h); var user = await ProviderUser(i);
        if (newPassword != "Valid123!")
        {
            await Assert.ThrowsAsync<BusinessException>(() => i.External.SetPasswordAsync(user.Id, newPassword, null, null, AccountUrl));
            Assert.False(await i.Users.HasPasswordAsync(user)); return;
        }
        await i.Controller().SetPassword(newPassword, null, null, i.External); await i.NextAsync();
        Assert.True(await i.Users.CheckPasswordAsync(user, newPassword));
        await Assert.ThrowsAsync<BusinessException>(() => i.External.SetPasswordAsync(user.Id, "Other123!", newPassword, null, AccountUrl));
        Assert.True(await i.Users.CheckPasswordAsync(user, newPassword));
    }

    [Fact]
    public async Task PhienKhongMatKhauKhongHaiLopChiDungTrongMuoiPhut()
    {
        using var h = new Harness(); using var i = Identity(h); var user = await ProviderUser(i);
        Assert.True(await i.TwoFactor.ReauthenticateAsync(user, null, null));
        h.Clock.UtcNow = h.Clock.UtcNow.AddMinutes(10); Assert.True(await i.TwoFactor.ReauthenticateAsync(user, null, null));
        h.Clock.UtcNow = h.Clock.UtcNow.AddSeconds(1);
        var ex = await Assert.ThrowsAsync<BusinessException>(() => i.TwoFactor.ReauthenticateAsync(user, null, null));
        Assert.Equal(TwoFactorService.RecentSignInRequired, ex.Message);
        await Assert.ThrowsAsync<BusinessException>(() => i.External.BeginLinkAsync(user.Id, "Microsoft", null, null, "/Account"));
        await Assert.ThrowsAsync<BusinessException>(() => i.External.SetPasswordAsync(user.Id, "Valid123!", null, null, AccountUrl));
        Assert.False(await i.Users.HasPasswordAsync(user));
    }

    [Fact]
    public async Task CallbackNoiThatDonCookieVaRefreshGiuNguyenMocDangNhap()
    {
        using var h = new Harness(); using var i = Identity(h); var user = await i.PrepareAsync(h); await SignIn(i, user);
        var signedAt = i.Http.User.FindFirstValue(TwoFactorService.SignInTimeClaim);
        h.Clock.UtcNow = h.Clock.UtcNow.AddMinutes(3);
        var info = Info(); var challenge = Assert.IsType<ChallengeResult>(await i.Controller().LinkLogin("Google", "Valid123!", null, i.External));
        await i.Http.SignInAsync(IdentityConstants.ExternalScheme, info.Principal, challenge.Properties); await i.NextAsync();
        var c = i.Controller(); var result = Assert.IsType<RedirectToActionResult>(await c.LinkLoginCallback(i.External));
        Assert.Equal("Index", result.ActionName); Assert.False(c.TempData.ContainsKey("Error"));
        Assert.Single(await i.Users.GetLoginsAsync(user)); await i.NextAsync();
        Assert.Equal(signedAt, i.Http.User.FindFirstValue(TwoFactorService.SignInTimeClaim));
        Assert.Equal(user.SecurityStamp, i.Http.User.FindFirstValue(i.SignIn.Options.ClaimsIdentity.SecurityStampClaimType));
        Assert.False((await i.Http.AuthenticateAsync(IdentityConstants.ExternalScheme)).Succeeded);
    }

    [Fact]
    public async Task CallbackNoiKhongNhanXsrfCuaNguoiKhac()
    {
        using var h = new Harness(); using var i = Identity(h); var user = await i.PrepareAsync(h); await SignIn(i, user);
        var props = await i.External.BeginLinkAsync(user.Id, "Google", "Valid123!", null, "/Account");
        props.Items["XsrfId"] = "another-user";
        await i.Http.SignInAsync(IdentityConstants.ExternalScheme, Info().Principal, props); await i.NextAsync();
        var c = i.Controller(); await c.LinkLoginCallback(i.External);
        Assert.True(c.TempData.ContainsKey("Error")); Assert.Empty(await i.Users.GetLoginsAsync(user));
        await i.NextAsync(); Assert.False((await i.Http.AuthenticateAsync(IdentityConstants.ExternalScheme)).Succeeded);
    }

    [Fact]
    public async Task CallbackNoiKhongKeoDaiMuoiPhutCuaS2()
    {
        using var h = new Harness(); using var i = Identity(h); var user = await ProviderUser(i);
        h.Clock.UtcNow = h.Clock.UtcNow.AddMinutes(9); var info = Info("Microsoft");
        info.AuthenticationProperties = await i.External.BeginLinkAsync(user.Id, "Microsoft", null, null, "/Account");
        h.Clock.UtcNow = h.Clock.UtcNow.AddMinutes(2);
        var ex = await Assert.ThrowsAsync<BusinessException>(() => i.External.LinkAsync(user.Id, info, AccountUrl));
        Assert.Equal(TwoFactorService.RecentSignInRequired, ex.Message); Assert.Single(await i.Users.GetLoginsAsync(user));
    }

    [Theory]
    [InlineData("unlink"), InlineData("passkey"), InlineData("disable"), InlineData("regenerate")]
    public async Task MoiHanhDongNhayCamDungChungChanPhienCu(string action)
    {
        using var h = new Harness(); using var i = Identity(h); var info = Info(); var user = await ProviderUser(i, info);
        var key = TestPasskeyHandler.NewKey(); Assert.True((await i.Users.AddOrUpdatePasskeyAsync(user, key)).Succeeded);
        h.Clock.UtcNow = h.Clock.UtcNow.AddMinutes(11);
        var ex = await Assert.ThrowsAsync<BusinessException>(async () =>
        {
            switch (action)
            {
                case "unlink": await i.External.RemoveAsync(user.Id, "Google", info.ProviderKey, null, null, AccountUrl); break;
                case "passkey": await i.Passkeys.DeleteAsync(user.Id, Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(key.CredentialId), null, null, AccountUrl); break;
                case "disable": await i.TwoFactor.ChangeAsync(user.Id, TwoFactorChange.Disable, null, null, AccountUrl); break;
                default: await i.TwoFactor.ChangeAsync(user.Id, TwoFactorChange.Regenerate, null, null, AccountUrl); break;
            }
        });
        Assert.Equal(TwoFactorService.RecentSignInRequired, ex.Message);
        Assert.Single(await i.Users.GetLoginsAsync(user)); Assert.Single(await i.Users.GetPasskeysAsync(user));
    }

    [Fact]
    public async Task AdminTheoCauHinhApChoTaiKhoanMoi()
    {
        using var h = new Harness(); var address = Guid.NewGuid() + "@test.vn";
        using var i = Identity(h, Tang6cPureTests.Config(("Admin:Emails:0", address)));
        var user = await ProviderUser(i, Info(email: address)); Assert.True(user.IsSystemAdmin);
        Assert.Null(i.Http.User.FindFirst("amr")); // Chưa có phương thức mạnh: middleware yêu cầu thiết lập.
    }

    [Fact]
    public async Task ProviderLaBi400VaReturnUrlNgoaiKhongDuocGiuaLai()
    {
        using var h = new Harness(); using var i = Identity(h); var c = i.Controller();
        Assert.IsType<BadRequestResult>(await c.ExternalLogin("Apple", "/", i.External));
        Assert.IsType<BadRequestResult>(await c.LinkLogin("Other", null, null, i.External));
        Assert.IsType<BadRequestResult>(await c.RemoveLogin("Other", "key", null, null, i.External));
        var result = Assert.IsType<ChallengeResult>(await c.ExternalLogin("Google", "https://evil.example", i.External));
        Assert.DoesNotContain("evil.example", result.Properties!.RedirectUri!);
        var info = Info(); var properties = i.SignIn.ConfigureExternalAuthenticationProperties("Google", "/Account/ExternalLoginCallback");
        await i.Http.SignInAsync(IdentityConstants.ExternalScheme, info.Principal, properties); await i.NextAsync();
        var redirect = Assert.IsType<LocalRedirectResult>(await i.Controller().ExternalLoginCallback("https://evil.example", i.External));
        Assert.Equal("/", redirect.Url); await i.NextAsync();
        Assert.False((await i.Http.AuthenticateAsync(IdentityConstants.ExternalScheme)).Succeeded);
    }
}
