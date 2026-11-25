using System.Security.Claims;
using System.Text.Json;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MindSprintAI_ASP.NET_MVC.Controllers;
using MindSprintAI_ASP.NET_MVC.Data;
using MindSprintAI_ASP.NET_MVC.Data.Entities;
using MindSprintAI_ASP.NET_MVC.Services;

namespace MindSprintAI_ASP.NET_MVC.Tests;

public class AvatarGooglePureTests
{
    [Theory]
    [InlineData("https://lh3.googleusercontent.com/photo=s96-c", "https://lh3.googleusercontent.com/photo=s128-c")]
    [InlineData("https://lh3.googleusercontent.com/photo=s2048-c-k?x=1#crop", "https://lh3.googleusercontent.com/photo=s128-c-k?x=1#crop")]
    [InlineData("https://LH3.GOOGLEUSERCONTENT.COM/photo", "https://lh3.googleusercontent.com/photo")]
    [InlineData("https://lh3.googleusercontent.com/photo?size=96", "https://lh3.googleusercontent.com/photo?size=96")]
    public void UrlHopLeVaCoNhoGiuPhanCatAnh(string input, string expected) => Assert.Equal(expected, AvatarService.GoogleUrl(input));

    [Theory]
    [InlineData("http://lh3.googleusercontent.com/a")]
    [InlineData("https://googleusercontent.com.evil.test/a")]
    [InlineData("https://googleusercontent.com/a")]
    [InlineData("https://user@lh3.googleusercontent.com/a")]
    [InlineData("https://user:pass@lh3.googleusercontent.com/a")]
    [InlineData("https://@lh3.googleusercontent.com/a")]
    [InlineData("/photo=s96-c")]
    [InlineData("//lh3.googleusercontent.com/a")]
    [InlineData("javascript:alert(1)")]
    [InlineData(null)]
    [InlineData("")]
    public void TuChoiUrlKhongHopLe(string? input) => Assert.Null(AvatarService.GoogleUrl(input));

    [Fact]
    public void GioiHan500KyTuKeCaSauDoiCo()
    {
        const string prefix = "https://lh3.googleusercontent.com/";
        var exact = prefix + new string('a', 500 - prefix.Length);
        Assert.Equal(exact, AvatarService.GoogleUrl(exact));
        Assert.Null(AvatarService.GoogleUrl(exact + "a"));
        Assert.Null(AvatarService.GoogleUrl(exact[..^5] + "=s1-c"));
    }

    [Fact]
    public void KhongLayAnhMicrosoftVaKhongDoiLuaChonAn()
    {
        var user = new ApplicationUser { AvatarHidden = true };
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new(AvatarService.GooglePictureClaim, "https://lh3.googleusercontent.com/a=s96-c")]));
        Assert.False(AvatarService.CaptureGoogle(user, "Microsoft", principal, DateTime.UtcNow));
        Assert.Null(user.AvatarUrl);
        Assert.True(AvatarService.CaptureGoogle(user, "Google", principal, DateTime.UtcNow));
        Assert.True(user.AvatarHidden);
    }

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    public async Task AvatarChungChoLayoutVaTaiKhoan(bool hasUrl, bool hidden, bool image)
    {
        var user = new ApplicationUser { DisplayName = "Cô Lan", AvatarHidden = hidden,
            AvatarUrl = hasUrl ? "https://lh3.googleusercontent.com/a=s96-c" : null, AvatarSource = AvatarSource.Google };
        var html = await Tang4ViewTests.Render("Shared", "_Avatar", user);
        Assert.Equal(image, html.Contains("<img"));
        Assert.Contains("class=\"avatar-letters\">CL</span>", html);
        if (image)
            foreach (var attribute in new[] { "referrerpolicy=\"no-referrer\"", "crossorigin=\"anonymous\"", "loading=\"lazy\"", "decoding=\"async\"", "alt=\"\"", "width=\"40\"", "height=\"40\"" }) Assert.Contains(attribute, html);
        Assert.DoesNotContain("onerror=", html);
        var layout = File.ReadAllText(Path.Combine(Tang8PureTests.Root, "Views/Shared/_Layout.cshtml"));
        Assert.Equal(2, Regex.Matches(layout, "<partial name=\"_Avatar\" model=\"me\"").Count);
        Assert.Contains("~/js/avatar.js", layout);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FormKhopActionVaPlainLanguageAudit(bool hasUrl)
    {
        var html = await Tang4ViewTests.Render("Account", "Index", new ApplicationUser
        { AvatarUrl = hasUrl ? "https://lh3.googleusercontent.com/a=s128-c" : null, AvatarHidden = true });
        var forms = Regex.Matches(html, @"<form\b[^>]*>[\s\S]*?</form>").Select(m => m.Value).ToList();
        foreach (var name in hasUrl ? new[] { "GoogleAvatarVisibility", "RemoveAvatar" } : new[] { "GoogleAvatarVisibility" })
        {
            var method = typeof(AccountController).GetMethod(name)!;
            var form = Assert.Single(forms, f => f.Contains($"data-action=\"{name}\""));
            Assert.Contains("method=\"post\"", form);
            Assert.Contains("__RequestVerificationToken", form);
            var parameters = method.GetParameters().Where(p => p.GetCustomAttribute<FromServicesAttribute>() == null).Select(p => p.Name!).Order();
            var fields = Regex.Matches(form, "name=\"([^\"]+)\"").Select(m => m.Groups[1].Value).Where(n => n != "__RequestVerificationToken").Distinct().Order();
            Assert.Equal(parameters, fields);
            Assert.NotNull(method.GetCustomAttribute<HttpPostAttribute>());
            Assert.NotNull(method.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
            Assert.Equal(AccountPolicy.Expected(method.Name), method.GetCustomAttribute<EnableRateLimitingAttribute>()!.PolicyName);
            Assert.True(method.GetCustomAttribute<ResponseCacheAttribute>()!.NoStore);
            Assert.Contains($"asp-action=\"{name}\"", File.ReadAllText(Path.Combine(Tang8PureTests.Root, "Views/Account/Index.cshtml")));
            Assert.Contains(name == "RemoveAvatar" ? "data-confirm=" : "data-autosave=\"reload\"", form);
        }
        var toggle = Assert.Single(forms, f => f.Contains("data-action=\"GoogleAvatarVisibility\""));
        Assert.DoesNotContain("checked=", toggle);
        if (!hasUrl) Assert.Contains("Tải ảnh của bạn lên, hoặc dùng ảnh tài khoản Google", WebUtility.HtmlDecode(html));
        // Đổi ảnh đại diện (tải ảnh lên, 15/11): form gửi tệp, có mã chống giả mạo; action chỉ cho người đã đăng nhập.
        var upload = Assert.Single(forms, f => f.Contains("id=\"avatar-upload-form\""));
        Assert.Contains("enctype=\"multipart/form-data\"", upload);
        Assert.Contains("__RequestVerificationToken", upload);
        Assert.Contains("name=\"image\"", upload);
        Assert.NotNull(typeof(AccountController).GetMethod("UploadAvatar")!.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
        Assert.False(PlainLanguageAudit.HasJargon(WebUtility.HtmlDecode(html)));
    }

    [Fact]
    public void ModelGiuUrlNganNullableVaGiaTriMacDinh()
    {
        using var db = new AppDbContext(AppDbContextFactory.BuildOptions("Server=localhost;Database=model;Integrated Security=true"));
        var user = db.Model.FindEntityType(typeof(ApplicationUser))!;
        Assert.Equal(500, user.FindProperty(nameof(ApplicationUser.AvatarUrl))!.GetMaxLength());
        Assert.True(user.FindProperty(nameof(ApplicationUser.AvatarUrl))!.IsNullable);
        Assert.True(user.FindProperty(nameof(ApplicationUser.AvatarUpdatedAt))!.IsNullable);
        Assert.Equal(false, user.FindProperty(nameof(ApplicationUser.AvatarHidden))!.GetDefaultValue());
        Assert.Equal(0, (int)AvatarSource.None);
        Assert.Equal(1, (int)AvatarSource.Google);
    }

    [Fact]
    public void GoogleMapPictureVaCoProfile()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthentication().AddQuizExternalProviders(Tang6cPureTests.Config());
        using var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<IOptionsMonitor<GoogleOptions>>().Get("Google");
        Assert.Contains("profile", options.Scope);
        using var json = JsonDocument.Parse("{\"picture\":\"https://lh3.googleusercontent.com/photo=s96-c\"}");
        var identity = new ClaimsIdentity();
        foreach (var action in options.ClaimActions) action.Run(json.RootElement, identity, "Google");
        Assert.Equal("https://lh3.googleusercontent.com/photo=s96-c", identity.FindFirst("urn:google:picture")?.Value);
    }
}

[Collection("db")]
public class AvatarGoogleTests
{
    private static ExternalLoginInfo Info(string? picture, string? key = null)
    {
        var info = Tang6cTests.Info(key: key);
        if (picture != null) ((ClaimsIdentity)info.Principal.Identity!).AddClaim(new(AvatarService.GooglePictureClaim, picture));
        return info;
    }

    [Fact]
    public async Task DangNhapLanDauLuuVaLanSauCapNhatKhongBoLuaChonAn()
    {
        using var h = new Harness(); using var i = new TwoFactorIdentity(h, externalConfig: Tang6cPureTests.Config());
        var info = Info("https://lh3.googleusercontent.com/first=s96-c");
        Assert.True((await i.External.SignInAsync(info)).Succeeded);
        var user = (await i.Users.FindByLoginAsync("Google", info.ProviderKey))!;
        await h.Db.Entry(user).ReloadAsync();
        Assert.Equal("https://lh3.googleusercontent.com/first=s128-c", user.AvatarUrl);
        Assert.Equal(AvatarSource.Google, user.AvatarSource);
        Assert.False(user.AvatarHidden);
        Assert.Equal(h.Clock.UtcNow, user.AvatarUpdatedAt);
        user.AvatarHidden = true; await h.Db.SaveChangesAsync();
        await i.NextAsync(); h.Clock.UtcNow = h.Clock.UtcNow.AddHours(1);
        Assert.True((await i.External.SignInAsync(Info("https://lh3.googleusercontent.com/new=s512-c", info.ProviderKey))).Succeeded);
        await h.Db.Entry(user).ReloadAsync();
        Assert.Equal("https://lh3.googleusercontent.com/new=s128-c", user.AvatarUrl);
        Assert.True(user.AvatarHidden);
        Assert.Equal(h.Clock.UtcNow, user.AvatarUpdatedAt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("https://googleusercontent.com.evil.test/a")]
    public async Task ClaimThieuHoacSaiVanDangNhapVaGiuAnhCu(string? picture)
    {
        using var h = new Harness(); using var i = new TwoFactorIdentity(h, externalConfig: Tang6cPureTests.Config());
        var info = Info(picture);
        Assert.True((await i.External.SignInAsync(info)).Succeeded);
        var user = (await i.Users.FindByLoginAsync("Google", info.ProviderKey))!;
        Assert.Null(user.AvatarUrl); Assert.Null(user.AvatarUpdatedAt); Assert.Equal(AvatarSource.None, user.AvatarSource);
        user.AvatarUrl = "https://lh3.googleusercontent.com/old=s128-c"; user.AvatarSource = AvatarSource.Google;
        var previous = user.AvatarUpdatedAt = h.Clock.UtcNow; await h.Db.SaveChangesAsync();
        await i.NextAsync(); h.Clock.UtcNow = h.Clock.UtcNow.AddHours(1);
        Assert.True((await i.External.SignInAsync(Info(picture, info.ProviderKey))).Succeeded);
        await h.Db.Entry(user).ReloadAsync();
        Assert.Equal("https://lh3.googleusercontent.com/old=s128-c", user.AvatarUrl);
        Assert.Equal(previous, user.AvatarUpdatedAt);
    }

    [Fact]
    public async Task CongTacVaGoAnhChiDoiChinhChuVaDangNhapSauLayLai()
    {
        using var h = new Harness(); using var i = new TwoFactorIdentity(h, externalConfig: Tang6cPureTests.Config());
        var info = Info("https://lh3.googleusercontent.com/me=s96-c");
        Assert.True((await i.External.SignInAsync(info)).Succeeded); await i.NextAsync();
        var user = (await i.Users.FindByLoginAsync("Google", info.ProviderKey))!;
        var otherId = await h.UserAsync("Người khác");
        var avatars = new AvatarService(h.Db, h.Clock);
        var controller = i.Controller();
        Assert.IsType<RedirectToActionResult>(await controller.GoogleAvatarVisibility(false, avatars));
        Assert.True(user.AvatarHidden); Assert.NotNull(user.AvatarUrl);
        Assert.False((await h.Db.Users.SingleAsync(u => u.Id == otherId)).AvatarHidden);
        await controller.GoogleAvatarVisibility(true, avatars); Assert.False(user.AvatarHidden);
        Assert.IsType<RedirectToActionResult>(await controller.RemoveAvatar(avatars));
        await h.Db.Entry(user).ReloadAsync();
        Assert.Null(user.AvatarUrl); Assert.Equal(AvatarSource.None, user.AvatarSource);
        Assert.True((await i.External.SignInAsync(info)).Succeeded);
        Assert.Equal("https://lh3.googleusercontent.com/me=s128-c", user.AvatarUrl);
    }

    [Fact]
    public async Task GoLienKetGoogleXoaAnh()
    {
        using var h = new Harness(); using var i = new TwoFactorIdentity(h, externalConfig: Tang6cPureTests.Config());
        var info = Info("https://lh3.googleusercontent.com/me=s96-c");
        Assert.True((await i.External.SignInAsync(info)).Succeeded);
        var user = (await i.Users.FindByLoginAsync("Google", info.ProviderKey))!;
        Assert.True((await i.Users.AddPasswordAsync(user, "Valid123!")).Succeeded);
        Assert.True((await i.Login.CompleteAsync(user, false)).Succeeded); await i.NextAsync();
        await i.External.RemoveAsync(user.Id, "Google", info.ProviderKey, "Valid123!", null, "https://localhost/Account");
        await h.Db.Entry(user).ReloadAsync();
        Assert.Null(user.AvatarUrl); Assert.Equal(AvatarSource.None, user.AvatarSource);
        Assert.Empty(await i.Users.GetLoginsAsync(user));
    }
}
