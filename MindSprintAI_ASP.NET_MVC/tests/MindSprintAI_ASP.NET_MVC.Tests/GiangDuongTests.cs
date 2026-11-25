using System.Net;
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MindSprintAI_ASP.NET_MVC.Controllers;
using MindSprintAI_ASP.NET_MVC.Services;

namespace MindSprintAI_ASP.NET_MVC.Tests;

// Người dùng chốt 13/11/2026: cả giảng đường dùng chung Wi-Fi trường (một địa chỉ mạng) vẫn vào tài khoản cùng lúc được.
public static class AccountPolicy
{
    public static readonly string[] EntryActions = ["Register", "RegisterPassword", "ResendConfirmation", "Login", "ForgotPassword",
        "LoginTwoFactor", "SendTwoFactorEmail", "ExternalLogin", "PasskeyRequestOptions", "PasskeyLogin", "TwoFactorPasskeyOptions", "TwoFactorPasskeyLogin"];

    public static string Expected(string action) => EntryActions.Contains(action) ? AccountRateLimits.EntryPolicy : "account-post";
}

public class GiangDuongPureTests
{
    [Fact]
    public void CacBuocVaoTaiKhoanDungHanRongConThaoTacTrongTaiKhoanGiuHanCu()
    {
        var methods = typeof(AccountController).GetMethods().Where(m => m.IsDefined(typeof(HttpPostAttribute))
            && m.GetCustomAttribute<EnableRateLimitingAttribute>() is { PolicyName: "account-post" or AccountRateLimits.EntryPolicy }).ToList();
        Assert.All(methods, m => Assert.Equal(AccountPolicy.Expected(m.Name), m.GetCustomAttribute<EnableRateLimitingAttribute>()!.PolicyName));
        Assert.Equal(AccountPolicy.EntryActions.Order(), methods.Where(m => AccountPolicy.EntryActions.Contains(m.Name)).Select(m => m.Name).Distinct().Order());
        Assert.Contains(methods, m => m.Name == "SetupAuthenticator" && AccountPolicy.Expected(m.Name) == "account-post");
    }

    [Fact]
    public async Task CaGiangDuongChungMotDiaChiMangDangNhapDuoc()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddAccountRateLimits();
        await using var app = builder.Build();
        var pipeline = new ApplicationBuilder(app.Services);
        pipeline.UseRateLimiter();
        pipeline.Run(_ => Task.CompletedTask);
        var request = pipeline.Build();
        async Task<int> Send(string policy)
        {
            var http = new DefaultHttpContext { RequestServices = app.Services };
            http.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.40"); // Wi-Fi trường
            http.Request.Method = "POST"; http.Response.Body = new MemoryStream();
            http.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(new EnableRateLimitingAttribute(policy)), policy));
            await request(http);
            return http.Response.StatusCode;
        }
        for (var i = 0; i < AccountRateLimits.EntryPerMinute; i++) Assert.Equal(200, await Send(AccountRateLimits.EntryPolicy));
        Assert.Equal(429, await Send(AccountRateLimits.EntryPolicy)); // vẫn có trần chống dồn dập
        Assert.Equal(200, await Send("account-post")); // thao tác trong tài khoản đếm riêng
    }

    [Fact]
    public void HanChatTinhTheoTungEmail()
    {
        var email = $"gd-{Guid.NewGuid():N}@example.test";
        for (var i = 0; i < AccountRateLimits.PerEmailPerMinute; i++) Assert.True(AccountRateLimits.TryEmail(i % 2 == 0 ? email : " " + email.ToUpperInvariant()));
        Assert.False(AccountRateLimits.TryEmail(email));
        Assert.True(AccountRateLimits.TryEmail($"khac-{Guid.NewGuid():N}@example.test"));
        Assert.True(AccountRateLimits.TryEmail(""));
    }
}

[Collection("db")]
public class GiangDuongDbTests
{
    private static string Network() { var n = Guid.NewGuid().ToString("N"); return $"2001:db8:{n[..4]}:{n[4..8]}::1"; }

    [Fact]
    public async Task VaiBanGoNhamMaKhongBatCaGiangDuongXacMinh()
    {
        using var h = new Harness(); var guard = new JoinCodeGuardService(h.Db, h.Codes, h.Clock, Tang5bPureTests.Turnstile());
        var wifi = Network();
        for (var i = 0; i < 30; i++) // 30 bạn, mỗi bạn gõ nhầm một lần
            await Assert.ThrowsAsync<BusinessException>(() => guard.ResolveAsync("sai", null, null, wifi, $"may-{i}"));
        Assert.False(await guard.RequiresAsync(null, wifi, "may-moi"));
    }

    [Fact]
    public async Task MotTrinhDuyetSaiMuoiLanChiTrinhDuyetDoPhaiXacMinh()
    {
        using var h = new Harness(); var guard = new JoinCodeGuardService(h.Db, h.Codes, h.Clock, Tang5bPureTests.Turnstile());
        var wifi = Network();
        for (var i = 0; i < JoinCodeGuardService.BrowserLimit; i++)
            await Assert.ThrowsAsync<BusinessException>(() => guard.ResolveAsync("sai", null, null, wifi, "may-do"));
        Assert.True(await guard.RequiresAsync(null, wifi, "may-do"));
        Assert.False(await guard.RequiresAsync(null, wifi, "may-ban-khac"));
    }

    [Fact]
    public async Task DoMaXoaTrinhDuyetVanBiChanTheoKhuVucMang()
    {
        using var h = new Harness(); var guard = new JoinCodeGuardService(h.Db, h.Codes, h.Clock, Tang5bPureTests.Turnstile());
        var wifi = Network();
        for (var i = 0; i < JoinCodeGuardService.NetworkLimit; i++) // mỗi lần một mã trình duyệt mới
            await Assert.ThrowsAsync<BusinessException>(() => guard.ResolveAsync("sai", null, null, wifi, Guid.NewGuid().ToString("N")));
        Assert.True(await guard.RequiresAsync(null, wifi, "may-moi"));
        Assert.True(await guard.RequiresAsync(null, wifi));
        Assert.False(await guard.RequiresAsync(null, Network(), "may-moi"));
    }
}
