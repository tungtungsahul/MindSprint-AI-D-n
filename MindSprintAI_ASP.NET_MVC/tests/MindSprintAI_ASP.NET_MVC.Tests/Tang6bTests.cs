using System.Reflection;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using MindSprintAI_ASP.NET_MVC.Data.Entities;
using MindSprintAI_ASP.NET_MVC.Models;
using MindSprintAI_ASP.NET_MVC.Services;
using MindSprintAI_ASP.NET_MVC.Controllers;

namespace MindSprintAI_ASP.NET_MVC.Tests;

[Collection("db")] // dùng Harness (DB thật): không chạy song song với test DB.
public class Tang6bPureTests
{
    [Fact]
    public void MoiEndpointPasskeyLaPostCoRateLimitVaNoStore()
    {
        foreach (var name in new[] { "PasskeyCreationOptions", "RegisterPasskey", "RenamePasskey", "DeletePasskey",
            "PasskeyRequestOptions", "PasskeyLogin", "TwoFactorPasskeyOptions", "TwoFactorPasskeyLogin" })
        {
            var method = typeof(AccountController).GetMethod(name);
            Assert.NotNull(method);
            Assert.NotNull(method.GetCustomAttribute<HttpPostAttribute>());
            Assert.Equal(AccountPolicy.Expected(method.Name), method.GetCustomAttribute<EnableRateLimitingAttribute>()!.PolicyName);
            Assert.True(method.GetCustomAttribute<ResponseCacheAttribute>()!.NoStore);
            Assert.NotNull(method.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
        }
    }

    [Theory]
    [InlineData(null), InlineData(""), InlineData("   ")]
    public void TenBatBuoc(string? value) => Assert.Throws<BusinessException>(() => PasskeyService.ValidateName(value));

    [Fact]
    public void TenGioiHanVaKhongTuLoaiHtml()
    {
        Assert.Equal("Laptop", PasskeyService.ValidateName(" Laptop "));
        Assert.Equal(new string('a', 50), PasskeyService.ValidateName(new string('a', 50)));
        Assert.Throws<BusinessException>(() => PasskeyService.ValidateName(new string('a', 51)));
        Assert.Equal("<script>", PasskeyService.ValidateName("<script>")); // Razor chịu trách nhiệm encode.
    }

    private static void FormMatches(string html, string action)
    {
        var method = typeof(AccountController).GetMethod(action)!;
        var names = method.GetParameters().Where(p => !p.IsDefined(typeof(FromServicesAttribute))).Select(p => p.Name!).Order().ToArray();
        var form = Assert.Single(Regex.Matches(html, "<form\\b[^>]*>[\\s\\S]*?</form>").Select(m => m.Value), f => f.Contains($"data-action=\"{action}\""));
        var fields = Regex.Matches(form, "name=\"([^\"]+)\"").Select(m => m.Groups[1].Value)
            .Where(n => n != "__RequestVerificationToken").Distinct().Order().ToArray();
        Assert.Equal(names, fields); Assert.Contains("__RequestVerificationToken", form);
    }

    [Theory]
    [InlineData(true), InlineData(false)]
    public async Task MoiFormKhopThamSoVaTenPasskeyDuocEncode(bool password)
    {
        var html = await Tang4ViewTests.Render("Account", "Index", new ApplicationUser(), new()
        {
            ["Passkeys"] = new PasskeyPage([new("aGVsbG8", "<script>alert(1)</script>", new(2026, 11, 6, 8, 0, 0), null)], password)
        });
        foreach (var name in new[] { "RegisterPasskey", "RenamePasskey", "DeletePasskey" }) FormMatches(html, name);
        Assert.DoesNotContain("<script>alert(1)</script>", html); Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("15:00 06/11/2026", html); Assert.Contains("Chưa dùng", WebUtility.HtmlDecode(html));
        Assert.Contains("PasskeyCreationOptions", html); Assert.Contains("aria-live=\"polite\"", html);
        var login = await Tang4ViewTests.Render("Account", "Login", new LoginModel());
        FormMatches(login, "PasskeyLogin"); Assert.Contains("PasskeyRequestOptions", login);
        var pending = await Tang4ViewTests.Render("Account", "LoginTwoFactor", new TwoFactorLoginPage(false, false, true));
        FormMatches(pending, "TwoFactorPasskeyLogin"); Assert.Contains("TwoFactorPasskeyOptions", pending);
        Assert.DoesNotContain("TwoFactorPasskeyLogin", await Tang4ViewTests.Render("Account", "LoginTwoFactor", new TwoFactorLoginPage(true, true)));
        var onlyPasskey = await Tang4ViewTests.Render("Account", "LoginTwoFactor", new TwoFactorLoginPage(false, false, true, false));
        Assert.Contains("TwoFactorPasskeyLogin", onlyPasskey); Assert.DoesNotContain("name=\"method\"", onlyPasskey);
    }

    [Fact]
    public void BangPasskeyVaModelKhopSnapshot()
    {
        // MindSprint gộp migration Tang6b vào KhoiTao: kiểm bảng passkey có trên model và model khớp snapshot.
        using var db = DatabaseFixture.NewContext();
        Assert.Contains(db.Model.GetEntityTypes(), t => t.GetTableName() == "AspNetUserPasskeys");
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task CookieOptionsIdentityDungNamPhutVaAssertionSaiKhongDangNhap()
    {
        using var h = new Harness(); var fake = new TestPasskeyHandler(); using var i = new TwoFactorIdentity(h, fake);
        await i.Passkeys.RequestOptionsAsync(false); await i.NextAsync();
        var result = await i.Passkeys.LoginAsync("{}", false, false);
        Assert.False(result.Outcome.Succeeded); Assert.Equal(PasskeyService.Invalid, result.Outcome.Error);
        Assert.Equal(1, fake.Assertions); Assert.False(i.Http.User.Identity?.IsAuthenticated == true);
        await i.NextAsync();
        Assert.False((await i.Passkeys.LoginAsync("{}", false, false)).Outcome.Succeeded);
        Assert.Equal(1, fake.Assertions); // Cookie đã bị Identity dùng/xóa.
        await i.Passkeys.RequestOptionsAsync(false); await i.NextAsync();
        h.Clock.UtcNow = h.Clock.UtcNow.AddMinutes(6); await i.NextAsync();
        Assert.False((await i.Passkeys.LoginAsync("{}", false, false)).Outcome.Succeeded);
        Assert.Equal(1, fake.Assertions);
    }

    [Fact]
    public async Task OptionsThatYeuCauUvVaResidentKey()
    {
        using var h = new Harness(); using var i = new TwoFactorIdentity(h);
        var configured = i.Http.RequestServices.GetRequiredService<IOptions<IdentityPasskeyOptions>>().Value;
        Assert.Equal("required", configured.UserVerificationRequirement); Assert.Equal("required", configured.ResidentKeyRequirement);
        using var options = JsonDocument.Parse(await i.SignIn.MakePasskeyRequestOptionsAsync(null));
        Assert.Equal("required", options.RootElement.GetProperty("userVerification").GetString());
    }

    [Fact]
    public async Task AttestationVaAssertionKhongDungStateCuaNhau()
    {
        using var h = new Harness(); var fake = new TestPasskeyHandler(); using var i = new TwoFactorIdentity(h, fake);
        await i.SignIn.MakePasskeyCreationOptionsAsync(new() { Id = "test-user", Name = "test", DisplayName = "Test" }); await i.NextAsync();
        Assert.Equal(PasskeyService.Invalid, (await i.Passkeys.LoginAsync("{}", false, false)).Outcome.Error);
        Assert.Equal(0, fake.Assertions);
        await i.Passkeys.RequestOptionsAsync(false); await i.NextAsync();
        var ex = await Assert.ThrowsAsync<BusinessException>(() => i.Passkeys.RegisterAsync("test-user", "Máy", "{}", "https://localhost/Account"));
        Assert.Equal(PasskeyService.Invalid, ex.Message);
    }
}

// Chỉ thay WebAuthn crypto/thiết bị; cookie, UserManager/EF, service và CompleteAsync vẫn là thật.
internal sealed class TestPasskeyHandler : IPasskeyHandler<ApplicationUser>
{
    public Func<UserManager<ApplicationUser>> Users { get; set; } = null!;
    public UserPasskeyInfo Key { get; set; } = NewKey();
    public bool RejectAttestation { get; set; }
    public bool RejectAssertion { get; set; }
    public bool Verified { get; set; } = true;
    public Func<Task>? AfterAssertion { get; set; }
    public int Assertions { get; private set; }
    public string? RestrictedTo { get; private set; }
    public static UserPasskeyInfo NewKey() => new(RandomNumberGenerator.GetBytes(32), [1, 2, 3], DateTimeOffset.UtcNow,
        0, ["internal"], true, true, true, [4, 5], [6, 7]);
    public Task<PasskeyCreationOptionsResult> MakeCreationOptionsAsync(PasskeyUserEntity userEntity, HttpContext context) =>
        Task.FromResult(new PasskeyCreationOptionsResult { CreationOptionsJson = "{}", AttestationState = JsonSerializer.Serialize(userEntity) });
    public Task<PasskeyRequestOptionsResult> MakeRequestOptionsAsync(ApplicationUser? user, HttpContext context)
    {
        RestrictedTo = user?.Id;
        return Task.FromResult(new PasskeyRequestOptionsResult { RequestOptionsJson = "{}", AssertionState = user?.Id ?? "*" });
    }
    public Task<PasskeyAttestationResult> PerformAttestationAsync(PasskeyAttestationContext context) => Task.FromResult(RejectAttestation
        ? PasskeyAttestationResult.Fail(new PasskeyException("test failed"))
        : PasskeyAttestationResult.Success(Key, JsonSerializer.Deserialize<PasskeyUserEntity>(context.AttestationState!)!));
    public async Task<PasskeyAssertionResult<ApplicationUser>> PerformAssertionAsync(PasskeyAssertionContext context)
    {
        Assertions++;
        if (!RejectAssertion && context.CredentialJson != "{}")
        {
            var id = JsonSerializer.Deserialize<string>(context.CredentialJson)!;
            var user = await Users().FindByPasskeyIdAsync(WebEncoders.Base64UrlDecode(id));
            if (user != null && (context.AssertionState == "*" || context.AssertionState == user.Id))
            {
                var key = (await Users().GetPasskeyAsync(user, WebEncoders.Base64UrlDecode(id)))!;
                var updated = new UserPasskeyInfo(key.CredentialId, key.PublicKey, key.CreatedAt, key.SignCount + 1,
                    key.Transports, Verified, key.IsBackupEligible, key.IsBackedUp, key.AttestationObject, key.ClientDataJson);
                if (AfterAssertion != null) await AfterAssertion();
                return PasskeyAssertionResult.Success(updated, user);
            }
        }
        return PasskeyAssertionResult.Fail<ApplicationUser>(new PasskeyException("test failed"));
    }
}

[Collection("db")]
public class Tang6bTests
{
    private const string AccountUrl = "https://localhost/Account";
    private static async Task<string> Add(TwoFactorIdentity i, TestPasskeyHandler fake, ApplicationUser user, string name = "Laptop")
    {
        fake.Users = () => i.Users; fake.Key = TestPasskeyHandler.NewKey();
        var id = WebEncoders.Base64UrlEncode(fake.Key.CredentialId);
        await i.Passkeys.CreationOptionsAsync(user.Id); await i.NextAsync();
        await i.Passkeys.RegisterAsync(user.Id, name, "{}", AccountUrl); await i.NextAsync();
        return id;
    }
    private static async Task<(LoginOutcome Outcome, string ReturnUrl)> Login(TwoFactorIdentity i, string id, bool pending = false)
    {
        await i.Passkeys.RequestOptionsAsync(pending); await i.NextAsync();
        return await i.Passkeys.LoginAsync(JsonSerializer.Serialize(id), pending, true, i.Guests);
    }

    [Fact]
    public async Task ThemDoiTenNgayTaoVaGioiHanMuoi()
    {
        using var h = new Harness(); var fake = new TestPasskeyHandler(); using var i = new TwoFactorIdentity(h, fake);
        var user = await i.PrepareAsync(h); var id = await Add(i, fake, user);
        var first = Assert.Single((await i.Passkeys.ListAsync(user)).Items);
        Assert.Equal("Laptop", first.Name); Assert.Null(first.LastUsedAt);
        await i.Passkeys.RenameAsync(user.Id, id, " Điện thoại ");
        Assert.Equal("Điện thoại", Assert.Single((await i.Passkeys.ListAsync(user)).Items).Name);
        await Assert.ThrowsAsync<BusinessException>(() => i.Passkeys.RenameAsync(user.Id, id, " "));
        for (var n = 1; n < 10; n++) await Add(i, fake, user, "Máy " + n);
        await Assert.ThrowsAsync<BusinessException>(() => i.Passkeys.CreationOptionsAsync(user.Id));
        // Kể cả tự gửi register để bỏ qua nút/options vẫn bị kiểm giới hạn dưới khóa hàng user.
        await i.SignIn.MakePasskeyCreationOptionsAsync(new() { Id = user.Id, Name = user.UserName!, DisplayName = user.DisplayName }); await i.NextAsync();
        fake.Key = TestPasskeyHandler.NewKey();
        await Assert.ThrowsAsync<BusinessException>(() => i.Passkeys.RegisterAsync(user.Id, "Máy 11", "{}", AccountUrl));
        Assert.Equal(10, (await i.Users.GetPasskeysAsync(user)).Count);
    }

    [Fact]
    public async Task AttestationSaiKhacNguoiHoacTenSaiKhongThem()
    {
        using var h = new Harness(); var fake = new TestPasskeyHandler(); using var i = new TwoFactorIdentity(h, fake);
        var user = await i.PrepareAsync(h); var other = await i.PrepareAsync(h);
        await i.Passkeys.CreationOptionsAsync(user.Id); await i.NextAsync();
        await Assert.ThrowsAsync<BusinessException>(() => i.Passkeys.RegisterAsync(user.Id, new string('a', 51), "{}", AccountUrl));
        await Assert.ThrowsAsync<BusinessException>(() => i.Passkeys.RegisterAsync(other.Id, "Máy", "{}", AccountUrl));
        fake.RejectAttestation = true;
        await i.Passkeys.CreationOptionsAsync(user.Id); await i.NextAsync();
        await Assert.ThrowsAsync<BusinessException>(() => i.Passkeys.RegisterAsync(user.Id, "Máy", "{}", AccountUrl));
        Assert.Empty(await i.Users.GetPasskeysAsync(user)); Assert.Empty(await i.Users.GetPasskeysAsync(other));
    }

    [Fact]
    public async Task XoaCanXacNhanSaiTinhThatBaiVaEmailKhongChuaCredential()
    {
        using var h = new Harness(); var fake = new TestPasskeyHandler(); using var i = new TwoFactorIdentity(h, fake);
        var user = await i.PrepareAsync(h); var id = await Add(i, fake, user, "TEN_KHONG_DUA_VAO_EMAIL");
        var ex = await Assert.ThrowsAsync<BusinessException>(() => i.Passkeys.DeleteAsync(user.Id, id, "sai", null, AccountUrl));
        Assert.Contains("Không xác nhận", ex.Message); Assert.Equal(1, user.AccessFailedCount);
        Assert.Single(await i.Users.GetPasskeysAsync(user));
        await i.Passkeys.DeleteAsync(user.Id, id, "Valid123!", null, AccountUrl);
        Assert.Empty(await i.Users.GetPasskeysAsync(user)); Assert.Equal(0, user.AccessFailedCount);
        Assert.Equal(2, i.Email.Messages.Count);
        foreach (var message in i.Email.Messages)
        {
            Assert.Contains("Nếu không phải bạn", message); Assert.Contains(AccountUrl, message);
            Assert.DoesNotContain(id, message); Assert.DoesNotContain("TEN_KHONG_DUA_VAO_EMAIL", message);
            Assert.DoesNotContain("publicKey", message); Assert.DoesNotContain("credential", message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task KhongCoMatKhauDungMaHienTaiVaGiuCachDangNhapCuoi()
    {
        using var h = new Harness(); var fake = new TestPasskeyHandler(); using var i = new TwoFactorIdentity(h, fake);
        var user = await i.PrepareAsync(h); var id = await Add(i, fake, user);
        var codes = await i.TwoFactor.ChangeAsync(user.Id, TwoFactorChange.EnableEmail, null, null, AccountUrl);
        Assert.True((await i.Users.RemovePasswordAsync(user)).Succeeded);
        Assert.Equal(1, await PasskeyService.CountSignInMethodsAsync(i.Users, user));
        var ex = await Assert.ThrowsAsync<BusinessException>(() => i.Passkeys.DeleteAsync(user.Id, id, null, codes[0], AccountUrl));
        Assert.Contains("cuối cùng", ex.Message); Assert.Single(await i.Users.GetPasskeysAsync(user));
        await Add(i, fake, user, "Điện thoại");
        await i.Passkeys.DeleteAsync(user.Id, id, null, codes[1], AccountUrl);
        Assert.Single(await i.Users.GetPasskeysAsync(user));
    }

    [Fact]
    public async Task AdminChiCoPasskeyQuaCongNhungMatKhauVanCanBuocHaiVaPasskeyHoanTat()
    {
        using var h = new Harness(); var fake = new TestPasskeyHandler(); using var i = new TwoFactorIdentity(h, fake);
        var user = await i.PrepareAsync(h, true); var id = await Add(i, fake, user);
        Assert.False(user.TwoFactorEnabled); Assert.True(await TwoFactorService.HasStrongMethodAsync(i.Users, user));
        Assert.False(await TwoFactorService.HasAuthenticatorAsync(i.Users, user));
        var password = await i.Login.LoginAsync(user.Email!, "Valid123!", true, null, null, i.Guests, "/Admin");
        Assert.False(password.Succeeded); Assert.True(password.RequiresTwoFactor); await i.NextAsync();
        Assert.False(i.Http.User.Identity?.IsAuthenticated == true); Assert.NotNull(await i.TwoFactor.PendingAsync());
        var page = Assert.IsType<ViewResult>(await i.Controller().LoginTwoFactor(i.TwoFactor));
        Assert.True(Assert.IsType<TwoFactorLoginPage>(page.Model).Passkey);
        await i.Passkeys.RequestOptionsAsync(true); await i.NextAsync();
        var result = Assert.IsType<JsonResult>(await i.Controller().TwoFactorPasskeyLogin(JsonSerializer.Serialize(id), i.Passkeys));
        Assert.Equal(user.Id, fake.RestrictedTo); Assert.Contains("/Admin", JsonSerializer.Serialize(result.Value));
        await i.NextAsync(); Assert.True(i.Http.User.HasClaim("amr", "mfa")); Assert.Null(await i.TwoFactor.PendingAsync());
        var entered = false; i.Http.Request.Path = "/Admin";
        await new AdminTwoFactorMiddleware(_ => { entered = true; return Task.CompletedTask; }).InvokeAsync(i.Http, i.Users);
        Assert.True(entered);
        var ex = await Assert.ThrowsAsync<BusinessException>(() => i.Passkeys.DeleteAsync(user.Id, id, "Valid123!", null, AccountUrl));
        Assert.Contains("Quản trị viên", ex.Message);
    }

    [Fact]
    public async Task AdminCoAppDuocXoaPasskeyCuoi()
    {
        using var h = new Harness(); var fake = new TestPasskeyHandler(); using var i = new TwoFactorIdentity(h, fake);
        var user = await i.PrepareAsync(h, true); var id = await Add(i, fake, user); await i.EnableAppAsync(user);
        await i.Passkeys.DeleteAsync(user.Id, id, "Valid123!", null, AccountUrl);
        Assert.Empty(await i.Users.GetPasskeysAsync(user)); Assert.True(await TwoFactorService.HasStrongMethodAsync(i.Users, user));
    }

    [Fact]
    public async Task SauOptionsPasskeyVanDungDuocMaAppVaPendingKhongKeoDaiHan()
    {
        using var h = new Harness(); var fake = new TestPasskeyHandler(); using var i = new TwoFactorIdentity(h, fake);
        var user = await i.PrepareAsync(h); await Add(i, fake, user); await i.EnableAppAsync(user);
        await i.Login.LoginAsync(user.Email!, "Valid123!", false, null, null); await i.NextAsync();
        var end = (await i.TwoFactor.PendingAsync())!.ExpiresAt;
        await i.Passkeys.RequestOptionsAsync(true); await i.NextAsync();
        Assert.Equal(end, (await i.TwoFactor.PendingAsync())!.ExpiresAt);
        var controller = i.Controller();
        Assert.IsType<LocalRedirectResult>(await controller.LoginTwoFactor("app", TwoFactorIdentity.Totp((await i.Users.GetAuthenticatorKeyAsync(user))!, 1), i.TwoFactor));
        await i.NextAsync(); Assert.Null(await i.TwoFactor.PendingAsync());
        await i.SignIn.SignOutAsync(); await i.NextAsync();
        await i.Login.LoginAsync(user.Email!, "Valid123!", false, null, null); await i.NextAsync();
        await i.Passkeys.RequestOptionsAsync(true); await i.NextAsync();
        h.Clock.UtcNow = h.Clock.UtcNow.AddMinutes(6); await i.NextAsync();
        Assert.Null(await i.TwoFactor.PendingAsync());
        Assert.Equal(PasskeyService.Expired, (await i.Passkeys.LoginAsync("{}", true, false)).Outcome.Error);
        Assert.Equal(PasskeyService.Expired, (await Assert.ThrowsAsync<BusinessException>(() => i.Passkeys.RequestOptionsAsync(true))).Message);
    }

    [Fact]
    public async Task KhongSuaXoaPasskeyNguoiKhacVaSecurityStampThayDoiHuyPending()
    {
        using var h = new Harness(); var fake = new TestPasskeyHandler(); using var i = new TwoFactorIdentity(h, fake);
        var user = await i.PrepareAsync(h, true); var id = await Add(i, fake, user); var other = await i.PrepareAsync(h);
        await Assert.ThrowsAsync<BusinessException>(() => i.Passkeys.RenameAsync(other.Id, id, "Chiếm"));
        await Assert.ThrowsAsync<BusinessException>(() => i.Passkeys.DeleteAsync(other.Id, id, "Valid123!", null, AccountUrl));
        await i.Login.LoginAsync(user.Email!, "Valid123!", false, null, null); await i.NextAsync();
        await i.Passkeys.RequestOptionsAsync(true); await i.NextAsync();
        await i.Users.UpdateSecurityStampAsync(user);
        Assert.Null(await i.TwoFactor.PendingAsync());
        Assert.Equal(PasskeyService.Expired, (await i.Passkeys.LoginAsync(JsonSerializer.Serialize(id), true, false)).Outcome.Error);
        Assert.False(i.Http.User.Identity?.IsAuthenticated == true);
    }

    [Fact]
    public async Task NguoiThuongCoPasskeyKhongTuBatBuocHaiNhungNeuBatThiCoNutPasskey()
    {
        using var h = new Harness(); var fake = new TestPasskeyHandler(); using var i = new TwoFactorIdentity(h, fake);
        var user = await i.PrepareAsync(h); await Add(i, fake, user);
        Assert.True((await i.Login.LoginAsync(user.Email!, "Valid123!", false, null, null)).Succeeded);
        await i.SignIn.SignOutAsync(); await i.NextAsync();
        await i.TwoFactor.ChangeAsync(user.Id, TwoFactorChange.EnableEmail, null, null, AccountUrl);
        Assert.True((await i.Login.LoginAsync(user.Email!, "Valid123!", false, null, null)).RequiresTwoFactor); await i.NextAsync();
        var page = Assert.IsType<ViewResult>(await i.Controller().LoginTwoFactor(i.TwoFactor));
        Assert.True(Assert.IsType<TwoFactorLoginPage>(page.Model).Passkey);
    }

    [Fact]
    public async Task PasskeyDangNhapKhongHoiMaVaCapNhatLanDungResetThatBai()
    {
        using var h = new Harness(); var fake = new TestPasskeyHandler(); using var i = new TwoFactorIdentity(h, fake);
        var user = await i.PrepareAsync(h); var id = await Add(i, fake, user); await i.EnableAppAsync(user);
        await i.Users.AccessFailedAsync(user);
        var result = await Login(i, id);
        Assert.True(result.Outcome.Succeeded); Assert.False(result.Outcome.RequiresTwoFactor); Assert.Equal(0, user.AccessFailedCount);
        Assert.Equal(h.Clock.UtcNow, Assert.Single((await i.Passkeys.ListAsync(user)).Items).LastUsedAt);
        await i.NextAsync(); Assert.Equal(user.Id, i.Http.User.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.Null(await i.TwoFactor.PendingAsync());
    }

    [Theory]
    [InlineData("guest"), InlineData("email"), InlineData("locked"), InlineData("identity-lock")]
    public async Task PasskeyKhongVuotTaiKhoanKhachChuaXacMinhHoacKhoa(string state)
    {
        using var h = new Harness(); var fake = new TestPasskeyHandler(); using var i = new TwoFactorIdentity(h, fake);
        var user = await i.PrepareAsync(h); var id = await Add(i, fake, user);
        if (state == "guest") user.IsGuest = true;
        if (state == "email") user.EmailConfirmed = false;
        if (state == "identity-lock") user.LockoutEnd = DateTimeOffset.UtcNow.AddHours(1);
        await i.Users.UpdateAsync(user);
        if (state == "locked") h.Db.AccountLocks.Add(new() { UserId = user.Id, Kind = AccountLockKind.Protective, StartsAt = h.Clock.UtcNow });
        await h.Db.SaveChangesAsync();
        var result = await Login(i, id);
        Assert.False(result.Outcome.Succeeded); Assert.False(i.Http.User.Identity?.IsAuthenticated == true);
        Assert.DoesNotContain(i.Http.Response.Headers.SetCookie, c => c!.StartsWith(".AspNetCore.Identity.Application="));
    }

    [Fact]
    public async Task AssertionSaiUvThieuVaPendingCuaNguoiKhacKhongDangNhap()
    {
        using var h = new Harness(); var fake = new TestPasskeyHandler(); using var i = new TwoFactorIdentity(h, fake);
        var user = await i.PrepareAsync(h, true); var id = await Add(i, fake, user); var other = await i.PrepareAsync(h);
        var otherId = await Add(i, fake, other);
        fake.RejectAssertion = true; Assert.Equal(PasskeyService.Invalid, (await Login(i, id)).Outcome.Error);
        fake.RejectAssertion = false; fake.Verified = false;
        Assert.Equal(PasskeyService.Invalid, (await Login(i, id)).Outcome.Error); fake.Verified = true;
        Assert.True((await i.Login.LoginAsync(user.Email!, "Valid123!", false, null, null)).RequiresTwoFactor); await i.NextAsync();
        Assert.Equal(PasskeyService.Invalid, (await Login(i, otherId, true)).Outcome.Error);
        Assert.False(i.Http.User.Identity?.IsAuthenticated == true);
        Assert.True((await Login(i, id, true)).Outcome.Succeeded); // Thử lại bằng đúng người pending.
    }

    [Fact]
    public async Task PasskeyDiQuaCompleteGopKetQuaKhach()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync();
        await h.Assignments.ChangeStrangerPolicyAsync(s.Assignment.Id, StrangerPolicy.Allow, s.Lecturer);
        var fake = new TestPasskeyHandler(); using var i = new TwoFactorIdentity(h, fake);
        var user = await i.PrepareAsync(h); var id = await Add(i, fake, user);
        var token = GuestService.NewToken(); var guest = await i.Guests.JoinAsync(s.Assignment.Id, "Hoa", token);
        i.Cookies[GuestService.CookieName] = token; await i.NextAsync();
        var result = await Login(i, id);
        Assert.True(result.Outcome.Succeeded); Assert.True(result.Outcome.MergedGuest);
        Assert.Equal(user.Id, guest.MergedIntoUserId);
        Assert.Equal(user.Id, (await h.Db.Participants.SingleAsync(p => p.GuestId == guest.Id)).UserId);
        Assert.Contains("qem_guest=;", i.Http.Response.Headers.SetCookie.ToString());
    }

    [Fact]
    public async Task BoDemCredentialDocMoiSauKhoaKhongDungBanEfDaTrack()
    {
        using var h = new Harness(); var fake = new TestPasskeyHandler(); using var i = new TwoFactorIdentity(h, fake);
        var user = await i.PrepareAsync(h); var id = await Add(i, fake, user);
        fake.AfterAssertion = async () =>
        {
            // Yêu cầu khác đã ghi bộ đếm trong lúc handler yêu cầu này giữ bản cũ trong EF.
            await using var concurrent = DatabaseFixture.NewContext();
            var key = await concurrent.UserPasskeys.SingleAsync(p => p.UserId == user.Id);
            key.Data.SignCount = 1; await concurrent.SaveChangesAsync();
        };
        Assert.Equal(PasskeyService.Invalid, (await Login(i, id)).Outcome.Error);
        Assert.False(i.Http.User.Identity?.IsAuthenticated == true);
    }
}
