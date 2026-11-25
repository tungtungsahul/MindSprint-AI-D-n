using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MindSprintAI_ASP.NET_MVC.Controllers;
using MindSprintAI_ASP.NET_MVC.Data;
using MindSprintAI_ASP.NET_MVC.Data.Entities;
using MindSprintAI_ASP.NET_MVC.Models;
using MindSprintAI_ASP.NET_MVC.Services;

namespace MindSprintAI_ASP.NET_MVC.Tests;

public class SecurityRound3AccountPureTests
{
    [Fact]
    public void T1_DangKyChiCanEmailVaTen()
    {
        var model = new RegisterModel { Email = "owner@example.vn", DisplayName = "Chủ email", AcceptTerms = true, AcceptPrivacy = true };
        Assert.True(Validator.TryValidateObject(model, new ValidationContext(model), [], true));
        Assert.Null(typeof(RegisterModel).GetProperty("Password"));
        Assert.Null(typeof(RegisterModel).GetProperty("ConfirmPassword"));
        // Pháp lý C1: thêm đúng 2 ô đồng ý, vẫn không có mật khẩu.
        Assert.Equal(new[] { "AcceptPrivacy", "AcceptTerms", "DisplayName", "Email" }, typeof(RegisterModel).GetProperties().Select(p => p.Name).Order());
    }

    [Fact]
    public async Task T1_LinkCuChiChuyenTrangKhongXacMinh()
    {
        var controller = new AccountController(null!, null!, null!, null!, null!, null!, null!, null!, null!, null!);
        var result = Assert.IsType<RedirectToActionResult>(await controller.ConfirmEmail("user", "old-code"));
        Assert.Equal("RegisterPassword", result.ActionName);
        Assert.Equal("user", result.RouteValues!["userId"]);
        Assert.Equal("old-code", result.RouteValues["code"]);
    }

    [Theory]
    [InlineData("valid"), InlineData("wrong"), InlineData("expired"), InlineData("used"), InlineData("other-user"), InlineData("missing-user"), InlineData("malformed-code")]
    public async Task T1_GetKiemTokenIdentityThatVaKhongDoiTaiKhoan(string state)
    {
        using var db = DatabaseFixture.NewContext(); // Chỉ tạo context; store dưới đây không truy cập SQL.
        var services = new ServiceCollection();
        services.AddLogging(); services.AddDataProtection().UseEphemeralDataProtectionProvider();
        services.AddSingleton(db);
        services.AddIdentityCore<ApplicationUser>().AddUserStore<TokenOnlyStore>().AddDefaultTokenProviders();
        services.Configure<DataProtectionTokenProviderOptions>(o => o.TokenLifespan = state == "expired" ? TimeSpan.FromSeconds(-1) : TimeSpan.FromHours(24));
        using var provider = services.BuildServiceProvider();
        var users = provider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = ((TokenOnlyStore)provider.GetRequiredService<IUserStore<ApplicationUser>>()).User;
        var token = await users.GenerateEmailConfirmationTokenAsync(user);
        var stamp = user.SecurityStamp;
        if (state == "wrong") token = "wrong";
        if (state == "used") user.EmailConfirmed = true;
        var service = new AccountEmailService(db, users, null!, TimeProvider.System, SecurityRound1Fixtures.Links);
        var controller = new AccountController(users, null!, null!, null!, null!, null!, null!, null!, null!, service);
        var code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        if (state == "malformed-code") code = "!invalid!";
        var userId = state == "other-user" ? "other" : state == "missing-user" ? null! : user.Id;
        var result = Assert.IsType<ViewResult>(await controller.RegisterPassword(userId, code));
        if (state == "valid")
        {
            var model = Assert.IsType<SetPasswordModel>(result.Model);
            Assert.Equal(user.Id, model.UserId); Assert.Equal(code, model.Code);
        }
        else Assert.Equal(AccountEmailService.InvalidSetupLink, result.Model);
        if (state != "valid")
        {
            var post = Assert.IsType<ViewResult>(await controller.RegisterPassword(new SetPasswordModel
            { UserId = userId, Code = code, Password = "Owner123!", ConfirmPassword = "Owner123!" }));
            Assert.Equal(AccountEmailService.InvalidSetupLink, post.Model);
        }
        Assert.Equal(state == "used", user.EmailConfirmed);
        Assert.Null(user.PasswordHash); Assert.Equal(stamp, user.SecurityStamp);
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public void T1_FormDatMatKhauGiuLoiCu()
    {
        var model = new SetPasswordModel { Password = "", ConfirmPassword = "different" };
        var errors = new List<ValidationResult>();
        Assert.False(Validator.TryValidateObject(model, new ValidationContext(model), errors, true));
        Assert.Contains(errors, e => e.ErrorMessage == "Nhập mật khẩu.");
        Assert.Contains(errors, e => e.ErrorMessage == "Hai mật khẩu không khớp.");
    }

    [Fact]
    public async Task T1_RazorHienDungCauVaKhongConMatKhauTrenDangKy()
    {
        var register = await Render("Register", new RegisterModel());
        Assert.Contains("MindSprint AI sẽ gửi link vào email này để bạn đặt mật khẩu.", register);
        Assert.Contains("name=\"Email\"", register); Assert.Contains("name=\"DisplayName\"", register);
        Assert.DoesNotContain("type=\"password\"", register);
        var setup = await Render("SetPassword", new SetPasswordModel { UserId = "u", Code = "code" });
        Assert.Contains("Đặt mật khẩu", setup); Assert.Contains("Mật khẩu</label>", setup);
        Assert.Contains("Nhập lại mật khẩu</label>", setup); Assert.Contains("Lưu mật khẩu</button>", setup);
        Assert.Contains("action=\"/Account/RegisterPassword\"", setup);
        Assert.Contains("Ít nhất 8 ký tự, gồm chữ hoa, chữ thường, chữ số và ký tự đặc biệt.", setup);
        foreach (var signedIn in new[] { false, true })
        {
            var message = await Render("Message", "Đã xác minh email và đặt mật khẩu. Bạn có thể đăng nhập.", signedIn);
            Assert.Contains("Đã xác minh email và đặt mật khẩu. Bạn có thể đăng nhập.", message);
            Assert.True(message.Contains("/Account/Login"), message);
        }
    }

    private static async Task<string> Render(string name, object model, bool signedIn = false)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        { ApplicationName = typeof(AccountController).Assembly.GetName().Name, EnvironmentName = "Development" });
        builder.Services.AddControllersWithViews().AddApplicationPart(typeof(AccountController).Assembly);
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        var sources = new List<EndpointDataSource>();
        builder.Services.AddSingleton<EndpointDataSource>(_ => new CompositeEndpointDataSource(sources));
        await using var app = builder.Build(); app.MapDefaultControllerRoute();
        // Gắn tuyến cho LinkGenerator mà không khởi động máy chủ HTTP.
        sources.AddRange(((IEndpointRouteBuilder)app).DataSources);
        using var scope = app.Services.CreateScope();
        var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        if (signedIn) http.User = new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "other-account")], "test"));
        http.Request.Scheme = "http"; http.Request.Host = new("localhost");
        http.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(), "Render test"));
        var route = new RouteData(); route.Values["controller"] = "Account"; route.Values["action"] = name;
        var action = new ActionContext(http, route, new ActionDescriptor());
        var view = scope.ServiceProvider.GetRequiredService<IRazorViewEngine>().GetView(null, $"/Views/Account/{name}.cshtml", false);
        Assert.True(view.Success);
        var data = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary()) { Model = model };
        if (name == "Message") data["ShowLoginLink"] = true;
        using var writer = new StringWriter();
        await view.View.RenderAsync(new ViewContext(action, view.View, data,
            scope.ServiceProvider.GetRequiredService<ITempDataDictionaryFactory>().GetTempData(http), writer, new HtmlHelperOptions()));
        return WebUtility.HtmlDecode(writer.ToString());
    }

    // Dùng token provider thật với thuộc tính người dùng thật; FindById không cần database cho các ca GET.
    private sealed class TokenOnlyStore(AppDbContext db) : UserStore<ApplicationUser, IdentityRole, AppDbContext>(db)
    {
        public ApplicationUser User { get; } = new() { Id = "owner", Email = "owner@example.vn", SecurityStamp = Guid.NewGuid().ToString() };
        public ApplicationUser Other { get; } = new() { Id = "other", Email = "other@example.vn", SecurityStamp = Guid.NewGuid().ToString() };
        public override Task<ApplicationUser?> FindByIdAsync(string userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(userId == User.Id ? User : userId == Other.Id ? Other : null);
    }
}

[Collection("db")]
public class SecurityRound3AccountDbTests
{
    [Fact]
    public async Task T1_GuiLaiDungLinkMoiVaDevLinkChiHienKhiThucSuGui()
    {
        using var h = new Harness(); using var i = new SafetyIdentity(h);
        var user = await i.PrepareAsync(await h.UserAsync("Chờ đặt mật khẩu"));
        user.EmailConfirmed = false; Assert.True((await i.Users.UpdateAsync(user)).Succeeded);
        var env = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" }).Environment;
        i.Http.Connection.RemoteIpAddress = IPAddress.Loopback;
        var controller = new AccountController(i.Users, i.SignIn, null!, env, null!, Tang5bPureTests.Turnstile(), null!, null!,
            SecurityRound1Fixtures.Links, SecurityRound1Fixtures.Emails(h, i.Users, i.Email))
        { ControllerContext = new() { HttpContext = i.Http }, TempData = new TempDataDictionary(i.Http, new FakeTempData()) };
        SecurityRound4PureTests.VerifiedForm(i.Http);
        Assert.Equal("CheckEmail", Assert.IsType<ViewResult>(await controller.ResendConfirmation(user.Email!)).ViewName);
        Assert.Contains("/Account/RegisterPassword?", Assert.IsType<string>(controller.TempData["DevLink"]));
        Assert.Single(i.Email.Messages);
        await controller.ResendConfirmation(user.Email!);
        Assert.False(controller.TempData.ContainsKey("DevLink")); Assert.Single(i.Email.Messages);
        h.Clock.UtcNow = h.Clock.UtcNow.AddMinutes(1);
        await controller.ResendConfirmation(user.Email!); Assert.Equal(2, i.Email.Messages.Count);
        user.EmailConfirmed = true; Assert.True((await i.Users.UpdateAsync(user)).Succeeded);
        await controller.ResendConfirmation(user.Email!);
        Assert.False(controller.TempData.ContainsKey("DevLink")); Assert.Equal(2, i.Email.Messages.Count);
    }

    [Theory]
    [InlineData(false), InlineData(true)]
    public async Task T1_ChuEmailDatMatKhauKhongGiuMatKhauDangKyTruoc(bool oldPassword)
    {
        using var h = new Harness(); using var identity = new TwoFactorIdentity(h);
        var users = identity.Users;
        var audit = new AuditLog(h.Db, new HttpContextAccessor { HttpContext = identity.Http },
            identity.Http.RequestServices.GetRequiredService<IServiceScopeFactory>(), h.Clock, NullLogger<AuditLog>.Instance);
        audit.Attach();
        var user = new ApplicationUser { UserName = Guid.NewGuid() + "@test.vn", DisplayName = "<Tên & thật>" };
        user.Email = user.UserName;
        Assert.True((oldPassword ? await users.CreateAsync(user, "Attacker123!") : await users.CreateAsync(user)).Succeeded);
        await users.SetTwoFactorEnabledAsync(user, true);
        await users.ResetAuthenticatorKeyAsync(user);
        var authenticator = await users.GetAuthenticatorKeyAsync(user);
        var passkey = new UserPasskeyInfo(Guid.NewGuid().ToByteArray(), [4, 5, 6], DateTimeOffset.UtcNow, 0, ["internal"], true, false, false, [1], [2]);
        Assert.True((await users.AddOrUpdatePasskeyAsync(user, passkey)).Succeeded);
        var stamp = user.SecurityStamp;
        var service = SecurityRound1Fixtures.Emails(h, users, identity.Email);
        var link = (await service.SendRegistrationAsync(user))!;
        var query = QueryHelpers.ParseQuery(new Uri(link).Query);
        var code = query["code"].ToString();
        var raw = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code));
        Assert.StartsWith("https://localhost:7228/Account/RegisterPassword?", link);
        Assert.Equal($"Đặt mật khẩu cho tài khoản {Brand.Name}", Assert.Single(identity.Email.Subjects));
        var safeLink = WebUtility.HtmlEncode(link);
        Assert.Equal($"<p>Chào bạn, bấm link sau để xác minh email và đặt mật khẩu cho tài khoản {Brand.Name}:</p><p><a href=\"{safeLink}\">{safeLink}</a></p><p>Link dùng được trong 24 giờ. Nếu bạn không đăng ký, hãy bỏ qua thư này.</p>", Assert.Single(identity.Email.Messages));
        var controller = new AccountController(users, identity.SignIn, null!, null!, null!, null!, null!, null!, null!, service);
        Assert.IsType<SetPasswordModel>(Assert.IsType<ViewResult>(await controller.RegisterPassword(user.Id, code)).Model);
        Assert.False(user.EmailConfirmed);
        var result = Assert.IsType<ViewResult>(await controller.RegisterPassword(new SetPasswordModel
        { UserId = user.Id, Code = code, Password = "OwnerPassword123!", ConfirmPassword = "OwnerPassword123!" }));
        Assert.Equal("Đã xác minh email và đặt mật khẩu. Bạn có thể đăng nhập.", result.Model);
        Assert.Equal(true, controller.ViewData["ShowLoginLink"]);
        using var fresh = DatabaseFixture.NewContext(); var saved = (await fresh.Users.FindAsync(user.Id))!;
        Assert.True(saved.EmailConfirmed); Assert.NotEqual(stamp, saved.SecurityStamp);
        Assert.True(await users.CheckPasswordAsync(user, "OwnerPassword123!"));
        Assert.False(await users.CheckPasswordAsync(user, "Attacker123!"));
        Assert.True(user.TwoFactorEnabled); Assert.Equal(authenticator, await users.GetAuthenticatorKeyAsync(user));
        Assert.Single(await users.GetPasskeysAsync(user));
        var events = await fresh.AuditEntries.Where(e => e.TargetType == "User" && e.TargetId == user.Id).ToListAsync();
        Assert.Contains(events, e => e.Action == AuditAction.EmailConfirmed);
        Assert.Contains(events, e => e.Action == AuditAction.PasswordSet);
        Assert.False(await service.CanSetPasswordAsync(user.Id, raw));
        Assert.Equal(AccountEmailService.InvalidSetupLink,
            (await Assert.ThrowsAsync<BusinessException>(() => service.SetPasswordAsync(user.Id, raw, "AgainPassword123!"))).Message);
    }

    [Fact]
    public async Task T1_MatKhauYeuRollbackCaXacMinhVaMatKhauCu()
    {
        using var h = new Harness(); using var identity = new TwoFactorIdentity(h);
        var audit = new AuditLog(h.Db, new HttpContextAccessor { HttpContext = identity.Http },
            identity.Http.RequestServices.GetRequiredService<IServiceScopeFactory>(), h.Clock, NullLogger<AuditLog>.Instance);
        audit.Attach();
        var user = new ApplicationUser { UserName = Guid.NewGuid() + "@test.vn", DisplayName = "Chờ xác minh" };
        user.Email = user.UserName;
        Assert.True((await identity.Users.CreateAsync(user, "Attacker123!")).Succeeded);
        var stamp = user.SecurityStamp; var hash = user.PasswordHash;
        var token = await identity.Users.GenerateEmailConfirmationTokenAsync(user);
        var service = SecurityRound1Fixtures.Emails(h, identity.Users, identity.Email);
        await Assert.ThrowsAsync<BusinessException>(() => service.SetPasswordAsync(user.Id, token, "x"));
        using var fresh = DatabaseFixture.NewContext(); var saved = (await fresh.Users.FindAsync(user.Id))!;
        Assert.False(saved.EmailConfirmed); Assert.Equal(hash, saved.PasswordHash); Assert.Equal(stamp, saved.SecurityStamp);
        Assert.False(await fresh.AuditEntries.AnyAsync(e => e.TargetId == user.Id
            && (e.Action == AuditAction.EmailConfirmed || e.Action == AuditAction.PasswordSet)));
        await h.Db.Entry(user).ReloadAsync();
        Assert.True(await service.CanSetPasswordAsync(user.Id, token));
    }
}
