using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MindSprintAI_ASP.NET_MVC.Controllers;
using MindSprintAI_ASP.NET_MVC.Data;
using MindSprintAI_ASP.NET_MVC.Data.Entities;
using MindSprintAI_ASP.NET_MVC.Hubs;
using MindSprintAI_ASP.NET_MVC.Services;

namespace MindSprintAI_ASP.NET_MVC.Tests;

[Collection("db")] // dùng Harness (DB thật): không chạy song song với test DB.
public class Tang7c2PureTests
{
    [Fact]
    public async Task CookieThatKhongTruyVanMoiRequestVaPhienCuBiXoaSauMotPhut()
    {
        // Không mở SQL: phát cookie thuần rồi chạy cookie handler/validator đăng ký giống Program.cs.
        using var h = new Harness(); using var i = new TwoFactorIdentity(h);
        await i.Http.SignInAsync(IdentityConstants.ApplicationScheme, Session("legacy"));
        await i.NextAsync(); Assert.True(i.Http.User.Identity!.IsAuthenticated);
        h.Clock.UtcNow = h.Clock.UtcNow.AddSeconds(59);
        await i.NextAsync(); Assert.True(i.Http.User.Identity!.IsAuthenticated);
        h.Clock.UtcNow = h.Clock.UtcNow.AddSeconds(2);
        await i.NextAsync(); Assert.False(i.Http.User.Identity!.IsAuthenticated);
        Assert.Contains(i.Http.Response.Headers.SetCookie, header => header!.Contains("expires="));
    }

    [Fact]
    public void ModelKhopSnapshot()
    {
        using var db = new AppDbContext(AppDbContextFactory.BuildOptions(DatabaseFixture.ConnectionString));
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task PhienCuKhongClaimBiChanHubKhongCanTruyVanDb()
    {
        using var db = new AppDbContext(AppDbContextFactory.BuildOptions(DatabaseFixture.ConnectionString));
        var filter = new DeviceSessionHubFilter(new(db));
        var caller = new DeviceHubCaller(Session("old"));
        using var services = new ServiceCollection().BuildServiceProvider();
        var hub = new LearnHub(null!);
        var invocation = new HubInvocationContext(caller, services, hub, typeof(LearnHub).GetMethod(nameof(LearnHub.JoinAi))!, [1]);
        await Assert.ThrowsAsync<HubException>(async () => await filter.InvokeMethodAsync(invocation, _ => throw new InvalidOperationException("Không được vào hub")));
        Assert.True(caller.Aborted);
        await Assert.ThrowsAsync<HubException>(() => filter.OnConnectedAsync(new(caller, services, hub), _ => throw new InvalidOperationException("Không được kết nối")));
    }

    [Fact]
    public void ClaimThieuSaiLapKhongDuocDungVaRefreshGiuMaPhien()
    {
        var principal = Session("u", "42");
        var fresh = Session("u");
        DeviceSession.Preserve(principal, fresh);
        Assert.Equal(42, DeviceSession.Id(fresh));
        var otherUser = Session("other"); DeviceSession.Preserve(principal, otherUser);
        Assert.Null(DeviceSession.Id(otherUser));
        Assert.Null(DeviceSession.Id(Session("u")));
        Assert.Null(DeviceSession.Id(Session("u", "-1")));
        Assert.Null(DeviceSession.Id(Session("u", "abc")));
        ((ClaimsIdentity)principal.Identity!).AddClaim(new(DeviceSession.Claim, "43"));
        Assert.Null(DeviceSession.Id(principal));
    }

    internal static ClaimsPrincipal Session(string user, string? device = null) => new(new ClaimsIdentity(
        new[] { new Claim(ClaimTypes.NameIdentifier, user) }.Concat(device == null ? [] : new[] { new Claim(DeviceSession.Claim, device) }), "test"));

    [Fact]
    public async Task DanhSachFormKhopThamSoKhongChoXoaThietBiNayVaEncodeDuLieu()
    {
        var now = new DateTime(2026, 11, 8, 1, 0, 0, DateTimeKind.Utc);
        var html = await Tang4ViewTests.Render("Account", "_Devices", new DevicePage([
            new(1, "Firefox trên Windows", now, now, "127.0.0.1", "Hanoi <script>", true),
            new(2, "Chrome trên Android", now, now, null, null, false)]));
        var text = WebUtility.HtmlDecode(html);
        Assert.Contains("Thiết bị này", text); Assert.Contains(VnTime.Format(now), text);
        Assert.Contains("https://db-ip.com", html); Assert.DoesNotContain("Hanoi <script>", html);
        Assert.Contains("180 ngày", text); Assert.Contains("1 phút", text); Assert.Contains(Brand.Name, text);
        foreach (Match match in Regex.Matches(html, "<form\\b[^>]*>[\\s\\S]*?</form>"))
        {
            var form = match.Value;
            var action = Regex.Match(form, "data-action=\"([^\"]+)\"").Groups[1].Value;
            var method = typeof(AccountController).GetMethod(action)!;
            Assert.NotNull(method.GetCustomAttribute<HttpPostAttribute>());
            Assert.NotNull(method.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
            Assert.Equal(AccountPolicy.Expected(method.Name), method.GetCustomAttribute<EnableRateLimitingAttribute>()!.PolicyName);
            var names = method.GetParameters().Where(p => !p.IsDefined(typeof(FromServicesAttribute))).Select(p => p.Name).Order();
            var fields = Regex.Matches(form, "name=\"([^\"]+)\"").Select(m => m.Groups[1].Value).Where(n => n != "__RequestVerificationToken").Distinct().Order();
            Assert.Equal(names, fields); Assert.Contains("__RequestVerificationToken", form);
            Assert.Contains("method=\"post\"", form); Assert.DoesNotContain("value=\"1\"", form);
        }
        Assert.Equal(2, Regex.Matches(html, "<form\\b").Count);
    }
}

internal sealed class DeviceHubCaller(ClaimsPrincipal user) : HubCallerContext
{
    public bool Aborted { get; private set; }
    public override string ConnectionId => "test-connection";
    public override string? UserIdentifier => user.FindFirstValue(ClaimTypes.NameIdentifier);
    public override ClaimsPrincipal User => user;
    public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
    public override IFeatureCollection Features { get; } = new FeatureCollection();
    public override CancellationToken ConnectionAborted => CancellationToken.None;
    public override void Abort() => Aborted = true;
}

[Collection("db")]
public class Tang7c2Tests
{
    private static DeviceSessionService Sessions(Harness h) => new(h.Db);
    private static async Task<long> Login(TwoFactorIdentity i, ApplicationUser user)
    {
        Assert.True((await i.Login.CompleteAsync(user, true)).Succeeded);
        await i.NextAsync();
        return DeviceSession.Id(i.Http.User)!.Value;
    }

    [Fact]
    public async Task T5GhiDeIpViTriKeCaThietBiDauVaKhongLuuCookieGoc()
    {
        using var h = new Harness(); using var i = new TwoFactorIdentity(h); var user = await i.PrepareAsync(h);
        i.Http.Connection.RemoteIpAddress = IPAddress.Parse("::ffff:192.168.1.5");
        i.Http.Request.Headers.UserAgent = "Windows Firefox/144.0";
        var raw = i.Cookies[LearnDevice.CookieName];
        var id = await Login(i, user);
        var first = await h.Db.KnownDevices.SingleAsync(d => d.Id == id);
        Assert.Equal("192.168.1.5", first.LastSignInIp); Assert.Equal("không xác định", first.LastLocation);
        Assert.DoesNotContain(i.Email.Subjects, m => m.Contains("Đăng nhập mới"));
        var firstSeen = first.FirstSeenAt;
        first.LastLocation = "Vị trí cũ"; await h.Db.SaveChangesAsync();
        h.Clock.UtcNow = h.Clock.UtcNow.AddMinutes(5);
        i.Http.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.9");
        Assert.Equal(id, await Login(i, user));
        var row = Assert.Single(await h.Db.KnownDevices.AsNoTracking().Where(d => d.UserId == user.Id).ToListAsync());
        Assert.Equal(firstSeen, row.FirstSeenAt); Assert.Equal(h.Clock.UtcNow, row.LastSeenAt);
        Assert.Equal("10.0.0.9", row.LastSignInIp); Assert.Equal("không xác định", row.LastLocation);
        Assert.Equal(DeviceLabel.Hash(raw), row.DeviceHash);
        Assert.DoesNotContain(raw, System.Text.Json.JsonSerializer.Serialize(row));
        Assert.DoesNotContain(i.Email.Subjects, m => m.Contains("Đăng nhập mới"));
    }

    [Fact]
    public async Task T6ChiLietKeChuSoHuuMoiNhatTruocDanhDauTheoPhienKhongTheoCookie()
    {
        using var h = new Harness(); using var a = new TwoFactorIdentity(h); using var b = new TwoFactorIdentity(h);
        var user = await a.PrepareAsync(h); var first = await Login(a, user);
        h.Clock.UtcNow = h.Clock.UtcNow.AddSeconds(10); var second = await Login(b, user);
        using var other = new TwoFactorIdentity(h); await Login(other, await other.PrepareAsync(h));
        a.Cookies[LearnDevice.CookieName] = b.Cookies[LearnDevice.CookieName]; await a.NextAsync();
        var page = await Sessions(h).ListAsync(user.Id, a.Http.User);
        Assert.Equal(new[] { second, first }, page.Devices.Select(d => d.Id));
        Assert.Equal(first, Assert.Single(page.Devices, d => d.IsCurrent).Id);
    }

    [Fact]
    public async Task T7XoaKickPhienVaCookieSaoChepSauMotPhutPhienKhacVanSongDangNhapLaiCoCanhBao()
    {
        using var h = new Harness(); using var a = new TwoFactorIdentity(h); using var b = new TwoFactorIdentity(h);
        var user = await a.PrepareAsync(h); var first = await Login(a, user); var second = await Login(b, user);
        var originalPrincipal = a.Http.User;
        // (T7) auth cookie gốc được giữ nguyên, đổi cookie nhận diện sang máy khác vẫn bị kick theo claim.
        var cookieName = a.Http.RequestServices.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(IdentityConstants.ApplicationScheme).Cookie.Name!;
        var authCookie = a.Cookies[cookieName];
        var originalDevice = a.Cookies[LearnDevice.CookieName];
        a.Cookies[LearnDevice.CookieName] = b.Cookies[LearnDevice.CookieName]; await a.NextAsync();
        Assert.Equal(first, DeviceSession.Id(a.Http.User));
        await b.Controller().DeleteDevice(first, Sessions(h));
        Assert.False(await h.Db.KnownDevices.AnyAsync(d => d.Id == first));
        h.Clock.UtcNow = h.Clock.UtcNow.AddSeconds(61);
        await a.NextAsync(); await b.NextAsync();
        Assert.False(a.Http.User.Identity!.IsAuthenticated);
        Assert.True(b.Http.User.Identity!.IsAuthenticated); Assert.Equal(second, DeviceSession.Id(b.Http.User));
        // Cùng auth cookie chép lại, dù qem_device là máy còn sống, vẫn bị từ chối.
        await a.NextAsync(); // tiêu thụ Set-Cookie xóa của request vừa bị từ chối trước khi chép cookie vào jar.
        a.Cookies[cookieName] = authCookie; await a.NextAsync();
        Assert.Equal(authCookie, a.Cookies[cookieName]); // xác nhận request này thực sự đã gửi cookie bị thu hồi.
        Assert.False(a.Http.User.Identity!.IsAuthenticated);
        Assert.False(await Sessions(h).IsValidAsync(originalPrincipal));
        a.Cookies[LearnDevice.CookieName] = originalDevice; await a.NextAsync();
        Assert.NotEqual(first, await Login(a, user));
        Assert.Single(a.Email.Subjects, m => m.Contains("Đăng nhập mới"));
    }

    [Fact]
    public async Task T7KhongXoaHienTaiHayCuaNguoiKhacVaRefreshGiuClaim()
    {
        using var h = new Harness(); using var a = new TwoFactorIdentity(h); using var b = new TwoFactorIdentity(h);
        var user = await a.PrepareAsync(h); var current = await Login(a, user);
        var stranger = await Login(b, await b.PrepareAsync(h));
        await Assert.ThrowsAsync<BusinessException>(() => a.Controller().DeleteDevice(current, Sessions(h)));
        await Assert.ThrowsAsync<ForbiddenException>(() => a.Controller().DeleteDevice(stranger, Sessions(h)));
        await Assert.ThrowsAsync<ForbiddenException>(() => a.Controller().DeleteDevice(long.MaxValue, Sessions(h)));
        var signedAt = a.Http.User.FindFirstValue(TwoFactorService.SignInTimeClaim);
        h.Clock.UtcNow = h.Clock.UtcNow.AddSeconds(61); await a.NextAsync();
        Assert.True(a.Http.User.Identity!.IsAuthenticated); Assert.Equal(current, DeviceSession.Id(a.Http.User));
        Assert.Equal(signedAt, a.Http.User.FindFirstValue(TwoFactorService.SignInTimeClaim));
        await a.NextAsync(); // nhận cookie refresh thật, không chỉ principal trong request.
        Assert.Equal(current, DeviceSession.Id(a.Http.User));
        Assert.True(await h.Db.KnownDevices.AnyAsync(d => d.Id == stranger));
    }

    [Fact]
    public async Task T9XoaTatCaTruHienTaiVaKhongXoaTaiKhoanKhac()
    {
        using var h = new Harness(); using var a = new TwoFactorIdentity(h); using var b = new TwoFactorIdentity(h);
        using var c = new TwoFactorIdentity(h); using var d = new TwoFactorIdentity(h);
        var user = await a.PrepareAsync(h); var keep = await Login(a, user); await Login(b, user); await Login(c, user);
        var stranger = await Login(d, await d.PrepareAsync(h));
        await a.Controller().SignOutOtherDevices(Sessions(h));
        Assert.Equal(keep, (await h.Db.KnownDevices.SingleAsync(d => d.UserId == user.Id)).Id);
        h.Clock.UtcNow = h.Clock.UtcNow.AddSeconds(61); await a.NextAsync(); await b.NextAsync(); await c.NextAsync(); await d.NextAsync();
        Assert.True(a.Http.User.Identity!.IsAuthenticated); Assert.False(b.Http.User.Identity!.IsAuthenticated);
        Assert.False(c.Http.User.Identity!.IsAuthenticated);
        Assert.Equal(stranger, DeviceSession.Id(d.Http.User)); Assert.True(d.Http.User.Identity!.IsAuthenticated);
        await a.Controller().SignOutOtherDevices(Sessions(h)); // idempotent khi không còn thiết bị khác.
    }

    [Fact]
    public async Task T10PhienCuKhongClaimBiTuChoiODotKiemTraKeTiep()
    {
        using var h = new Harness(); using var i = new TwoFactorIdentity(h); var user = await i.PrepareAsync(h);
        await i.SignIn.SignInAsync(user, false); await i.NextAsync();
        Assert.True(i.Http.User.Identity!.IsAuthenticated);
        h.Clock.UtcNow = h.Clock.UtcNow.AddSeconds(61); await i.NextAsync();
        Assert.False(i.Http.User.Identity!.IsAuthenticated);
    }

    [Fact]
    public async Task T7ClaimThuocChuKhacBiTuChoiKhiValidate()
    {
        using var h = new Harness(); using var a = new TwoFactorIdentity(h); using var b = new TwoFactorIdentity(h);
        var user = await a.PrepareAsync(h); var wrong = await Login(b, await b.PrepareAsync(h));
        await a.SignIn.SignInWithClaimsAsync(user, false, [new Claim(DeviceSession.Claim, wrong.ToString())]); await a.NextAsync();
        h.Clock.UtcNow = h.Clock.UtcNow.AddSeconds(61); await a.NextAsync();
        Assert.False(a.Http.User.Identity!.IsAuthenticated);
    }

    [Fact]
    public async Task T7CapLaiCookieSauDoi2FaVanGiuPhien()
    {
        using var h = new Harness(); using var i = new TwoFactorIdentity(h); var user = await i.PrepareAsync(h);
        var id = await Login(i, user);
        await i.Controller().SetEmailTwoFactor(true, "Valid123!", null, i.TwoFactor);
        await i.NextAsync();
        Assert.Equal(id, DeviceSession.Id(i.Http.User));
        h.Clock.UtcNow = h.Clock.UtcNow.AddSeconds(61); await i.NextAsync();
        Assert.True(i.Http.User.Identity!.IsAuthenticated); Assert.Equal(id, DeviceSession.Id(i.Http.User));
    }

    [Fact]
    public async Task T8WorkerXoaQua180NgayGiu179NgayVa180Ngay()
    {
        using var h = new Harness(); var user = await h.UserAsync("devices");
        var rows = new[] { 179, 180, 181 }.Select(days => new KnownDevice { UserId = user, DeviceHash = days.ToString(),
            LastSeenAt = h.Clock.UtcNow.AddDays(-days), FirstSeenAt = h.Clock.UtcNow.AddDays(-days), LastSignInIp = "10.0.0.1", LastLocation = "cũ" }).ToArray();
        h.Db.KnownDevices.AddRange(rows); await h.Db.SaveChangesAsync();
        var services = new Tang7cServices(h); await services.Reminders.RunAsync(); await services.Reminders.RunAsync();
        Assert.Equal(2, await h.Db.KnownDevices.CountAsync(d => d.UserId == user));
        Assert.False(await h.Db.KnownDevices.AnyAsync(d => d.Id == rows[2].Id));
    }

    [Fact]
    public async Task T7HubTuChoiPhienThuHoiCaGoiVaKetNoiLaiChoPhepPhienConLai()
    {
        using var h = new Harness(); using var a = new TwoFactorIdentity(h); using var b = new TwoFactorIdentity(h);
        var user = await a.PrepareAsync(h); var id = await Login(a, user); await Login(b, user);
        var filter = new DeviceSessionHubFilter(Sessions(h));
        using var services = new ServiceCollection().BuildServiceProvider(); var hub = new LearnHub(null!);
        var caller = new DeviceHubCaller(a.Http.User);
        var invocation = new HubInvocationContext(caller, services, hub, typeof(LearnHub).GetMethod(nameof(LearnHub.JoinAi))!, [1]);
        var invoked = false;
        ValueTask<object?> Next(HubInvocationContext _) { invoked = true; return ValueTask.FromResult<object?>(null); }
        await filter.InvokeMethodAsync(invocation, Next); Assert.True(invoked); invoked = false;
        await filter.OnConnectedAsync(new(caller, services, hub), _ => Task.CompletedTask);
        await b.Controller().DeleteDevice(id, Sessions(h));
        await Assert.ThrowsAsync<HubException>(async () => await filter.InvokeMethodAsync(invocation, Next));
        await Assert.ThrowsAsync<HubException>(() => filter.OnConnectedAsync(new(caller, services, hub), _ => Task.CompletedTask));
        Assert.False(invoked); Assert.True(caller.Aborted);
        var valid = new DeviceHubCaller(b.Http.User);
        await filter.OnConnectedAsync(new(valid, services, hub), _ => Task.CompletedTask);
        await filter.InvokeMethodAsync(new(valid, services, hub, invocation.HubMethod, [1]), Next);
        Assert.True(invoked); Assert.False(valid.Aborted);
    }
}
