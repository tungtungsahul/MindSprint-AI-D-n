using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using MindSprintAI_ASP.NET_MVC.Controllers;
using MindSprintAI_ASP.NET_MVC.Models;
using MindSprintAI_ASP.NET_MVC.Data;
using MindSprintAI_ASP.NET_MVC.Data.Entities;
using MindSprintAI_ASP.NET_MVC.Services;

namespace MindSprintAI_ASP.NET_MVC.Tests;

public class SecurityRound4PureTests
{
    internal static void VerifiedForm(HttpContext http) => http.Request.Form = new FormCollection(
        new Dictionary<string, StringValues> { ["cf-turnstile-response"] = "verified-by-fake-cloudflare" });
    [Theory]
    [InlineData("Register", null), InlineData("Register", "bad")]
    [InlineData("ResendConfirmation", null), InlineData("ResendConfirmation", "bad")]
    [InlineData("ForgotPassword", null), InlineData("ForgotPassword", "bad")]
    public async Task U1_MoiPostBatBuocXacMinhTruocKhiDungTaiKhoan(string action, string? token)
    {
        // Các dịch vụ tài khoản để null: nếu chạm tới trước xác minh thì ca này thất bại.
        var controller = new AccountController(null!, null!, null!, null!, null!, Tang5bPureTests.Turnstile(false),
            null!, null!, null!, null!) { ControllerContext = new() { HttpContext = new DefaultHttpContext() } };
        controller.Request.Form = new FormCollection(new Dictionary<string, StringValues>
            { ["cf-turnstile-response"] = token });
        var input = new RegisterModel { Email = "kept@example.vn", DisplayName = "Tên giữ lại" };
        var result = Assert.IsType<ViewResult>(action switch
        {
            "Register" => await controller.Register(input),
            "ResendConfirmation" => await controller.ResendConfirmation(input.Email),
            _ => await controller.ForgotPassword(input.Email)
        });
        Assert.Equal(true, controller.ViewData["ChallengeRequired"]);
        Assert.Equal(Tang5bPureTests.Turnstile().SiteKey, controller.ViewData["TurnstileSiteKey"]);
        var expected = (await Tang5bPureTests.Turnstile(false).VerifyAsync(token, null)).Error;
        Assert.Contains(controller.ModelState.Values.SelectMany(v => v.Errors), e => e.ErrorMessage == expected);
        if (action == "Register") Assert.Same(input, result.Model);
        else { Assert.Equal(action, result.ViewName); Assert.Equal(input.Email, result.Model); }
    }

    [Theory]
    [InlineData("Register"), InlineData("ForgotPassword"), InlineData("ResendConfirmation")]
    public async Task U1_FormHienXacMinhVaGiuDuLieu(string view)
    {
        var controller = new AccountController(null!, null!, null!, null!, null!, Tang5bPureTests.Turnstile(),
            null!, null!, null!, null!) { ControllerContext = new() { HttpContext = new DefaultHttpContext() } };
        _ = view switch { "Register" => controller.Register(), "ForgotPassword" => controller.ForgotPassword(), _ => controller.ResendConfirmation() };
        Assert.Equal(true, controller.ViewData["ChallengeRequired"]);
        object model = view == "Register" ? new RegisterModel { Email = "kept@example.vn", DisplayName = "Tên giữ lại" } : "kept@example.vn";
        var html = WebUtility.HtmlDecode(await Tang4ViewTests.Render("Account", view, model,
            new() { ["ChallengeRequired"] = controller.ViewData["ChallengeRequired"], ["TurnstileSiteKey"] = controller.ViewData["TurnstileSiteKey"] }));
        Assert.Contains("cf-turnstile", html); Assert.Contains($"data-sitekey=\"{Tang5bPureTests.Turnstile().SiteKey}\"", html);
        Assert.Contains("kept@example.vn", html);
        if (view == "Register") Assert.Contains("Tên giữ lại", html);
    }
}

[Collection("db")]
public class SecurityRound4DbTests
{
    private static PasswordResetService Reset(Harness h, SafetyIdentity i) =>
        new(i.Users, i.Email, h.Clock, i.Locks, SecurityRound1Fixtures.Emails(h, i.Users, i.Email), h.Db);

    [Fact]
    public async Task U1_BaLoaiThuChungHanMotPhutVaNamThuTrong24GioKeCaScopeMoi()
    {
        using var h = new Harness(); using var i = new SafetyIdentity(h);
        var user = await i.PrepareAsync(await h.UserAsync("Không đưa tên này vào thư"));
        user.EmailConfirmed = false; Assert.True((await i.Users.UpdateAsync(user)).Succeeded);
        var start = h.Clock.UtcNow;
        var emails = SecurityRound1Fixtures.Emails(h, i.Users, i.Email);
        Assert.NotNull(await emails.SendRegistrationAsync(user));
        Assert.Contains("Chào bạn,", Assert.Single(i.Email.Messages));
        Assert.DoesNotContain(user.DisplayName, i.Email.Messages[0]);
        user.EmailConfirmed = true; Assert.True((await i.Users.UpdateAsync(user)).Succeeded);
        Assert.Null(await Reset(h, i).RequestAsync(user.Email!.ToUpperInvariant(), (_, _, _) => throw new Exception("Đang phải chờ")));
        h.Clock.UtcNow = start.AddMinutes(1);
        await emails.SendRegistrationAsync(user); // Bạn đã có tài khoản.
        Assert.Contains("Bạn đã có tài khoản", i.Email.Subjects.Last());
        for (var n = 2; n < 5; n++)
        {
            h.Clock.UtcNow = start.AddMinutes(n);
            Assert.NotNull(await Reset(h, i).RequestAsync(user.Email!, (_, _, _) => "https://localhost/reset"));
        }
        Assert.Equal(5, i.Email.Messages.Count);
        h.Clock.UtcNow = start.AddHours(24).AddTicks(-1);
        using var h2 = new Harness(); using var i2 = new SafetyIdentity(h2); h2.Clock.UtcNow = h.Clock.UtcNow;
        var user2 = (await i2.Users.FindByIdAsync(user.Id))!;
        Assert.Null(await SecurityRound1Fixtures.Emails(h2, i2.Users, i2.Email).SendRegistrationAsync(user2));
        Assert.Null(await Reset(h2, i2).RequestAsync(user.Email!, (_, _, _) => throw new Exception("Đã đủ 5 thư")));
        Assert.Empty(i2.Email.Messages);
        h2.Clock.UtcNow = start.AddHours(24);
        Assert.NotNull(await Reset(h2, i2).RequestAsync(user.Email!, (_, _, _) => "https://localhost/reset"));
        Assert.Single(i2.Email.Messages);
        h2.Clock.UtcNow = start.AddHours(24).AddSeconds(59);
        Assert.Null(await Reset(h2, i2).RequestAsync(user.Email!, (_, _, _) => throw new Exception("Chưa đủ phút")));
        h2.Clock.UtcNow = start.AddHours(24).AddMinutes(1);
        Assert.NotNull(await Reset(h2, i2).RequestAsync(user.Email!, (_, _, _) => "https://localhost/reset"));
    }

    [Fact]
    public async Task U1_MocCuVanDuocTinhVaHaiYeuCauChoThuThuNamChiMotDuocGui()
    {
        using var h = new Harness(); using var i = new SafetyIdentity(h);
        var user = await i.PrepareAsync(await h.UserAsync("Thư thứ năm"));
        user.EmailConfirmed = false; Assert.True((await i.Users.UpdateAsync(user)).Succeeded);
        var emails = SecurityRound1Fixtures.Emails(h, i.Users, i.Email);
        Assert.True((await i.Users.SetAuthenticationTokenAsync(user, "AccountEmail", "PasswordReset", h.Clock.GetUtcNow().ToString("O"))).Succeeded);
        Assert.Null(await emails.SendRegistrationAsync(user));
        for (var n = 0; n < 3; n++) { h.Clock.UtcNow = h.Clock.UtcNow.AddMinutes(1); Assert.NotNull(await emails.SendRegistrationAsync(user)); }
        h.Clock.UtcNow = h.Clock.UtcNow.AddMinutes(1);
        using var h2 = new Harness(); using var i2 = new SafetyIdentity(h2); h2.Clock.UtcNow = h.Clock.UtcNow;
        var user2 = (await i2.Users.FindByIdAsync(user.Id))!;
        var sends = await Task.WhenAll(emails.SendRegistrationAsync(user),
            SecurityRound1Fixtures.Emails(h2, i2.Users, i2.Email).SendRegistrationAsync(user2));
        Assert.Single(sends, s => s != null); Assert.Equal(4, i.Email.Messages.Count + i2.Email.Messages.Count);
        h.Clock.UtcNow = h.Clock.UtcNow.AddMinutes(1);
        Assert.Null(await emails.SendRegistrationAsync(user));
    }

    [Fact]
    public async Task U3_QuenMatKhauChuaXacMinhGuiThuDatMatKhauVaVanChungHan()
    {
        using var h = new Harness(); using var i = new SafetyIdentity(h);
        var user = await i.PrepareAsync(await h.UserAsync("Chưa xác minh"));
        user.EmailConfirmed = false; Assert.True((await i.Users.UpdateAsync(user)).Succeeded);
        var reset = Reset(h, i);
        for (var n = 0; n < 5; n++)
        {
            Assert.Contains("/Account/RegisterPassword?", await reset.RequestAsync(user.Email!, (_, _, _) => throw new Exception("Không dựng link reset")));
            Assert.Null(await reset.RequestAsync(user.Email!, (_, _, _) => throw new Exception("Đang phải chờ")));
            h.Clock.UtcNow = h.Clock.UtcNow.AddMinutes(1);
        }
        Assert.Null(await reset.RequestAsync(user.Email!, (_, _, _) => throw new Exception("Đủ trần")));
        Assert.Equal(5, i.Email.Messages.Count);
        Assert.All(i.Email.Subjects, s => Assert.Equal($"Đặt mật khẩu cho tài khoản {Brand.Name}", s));
        Assert.False(user.EmailConfirmed); Assert.True(await i.Users.CheckPasswordAsync(user, "Valid123!"));
    }

    [Theory]
    [InlineData(false, false), InlineData(true, false), InlineData(true, true)]
    public async Task U3_GoogleNhanTaiKhoanChuaXacMinhXoaMatKhauDoiStampVaGiu2Lop(bool oldPassword, bool secondStep)
    {
        using var h = new Harness(); using var i = new TwoFactorIdentity(h, externalConfig: Tang6cPureTests.Config());
        var user = await i.PrepareAsync(h);
        if (!oldPassword) Assert.True((await i.Users.RemovePasswordAsync(user)).Succeeded);
        if (secondStep) await i.EnableAppAsync(user);
        var key = await i.Users.GetAuthenticatorKeyAsync(user);
        user.EmailConfirmed = false; Assert.True((await i.Users.UpdateAsync(user)).Succeeded);
        var stamp = user.SecurityStamp;
        var oldToken = await i.Users.GenerateEmailConfirmationTokenAsync(user);
        var audit = new AuditLog(h.Db, new HttpContextAccessor { HttpContext = i.Http },
            i.Http.RequestServices.GetRequiredService<IServiceScopeFactory>(), h.Clock, NullLogger<AuditLog>.Instance);
        audit.Attach();
        var info = Tang6cTests.Info(email: user.Email);
        var outcome = await i.External.SignInAsync(info, returnUrl: "/StudySets");
        Assert.Equal(!secondStep, outcome.Succeeded); Assert.Equal(secondStep, outcome.RequiresTwoFactor);
        using var fresh = DatabaseFixture.NewContext(); var saved = (await fresh.Users.FindAsync(user.Id))!;
        Assert.True(saved.EmailConfirmed); Assert.Null(saved.PasswordHash); Assert.NotEqual(stamp, saved.SecurityStamp);
        Assert.Equal(user.Id, (await i.Users.FindByLoginAsync("Google", info.ProviderKey))!.Id);
        Assert.False(await i.Users.CheckPasswordAsync(user, "Valid123!"));
        Assert.False(await SecurityRound1Fixtures.Emails(h, i.Users, i.Email).CanSetPasswordAsync(user.Id, oldToken));
        Assert.Equal(key, await i.Users.GetAuthenticatorKeyAsync(user));
        var events = await fresh.AuditEntries.Where(e => e.TargetId == user.Id).ToListAsync();
        Assert.Contains(events, e => e.Action == AuditAction.EmailConfirmed);
        Assert.Contains(events, e => e.Action == AuditAction.ExternalLinked);
        await i.NextAsync();
        if (secondStep)
        {
            Assert.False(i.Http.User.Identity?.IsAuthenticated == true);
            var pending = Assert.IsType<PendingTwoFactor>(await i.TwoFactor.PendingAsync());
            Assert.Equal(user.Id, pending.User.Id); Assert.Equal("/StudySets", pending.ReturnUrl);
        }
        else Assert.Equal(user.Id, i.Http.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value);
    }

    [Theory]
    [InlineData("Google", true), InlineData("Microsoft", false), InlineData("Microsoft", true)]
    public async Task U3_GoogleDaXacMinhVaMicrosoftKhongTuNoi(string provider, bool confirmed)
    {
        using var h = new Harness(); using var i = new TwoFactorIdentity(h, externalConfig: Tang6cPureTests.Config());
        var user = await i.PrepareAsync(h); user.EmailConfirmed = confirmed; Assert.True((await i.Users.UpdateAsync(user)).Succeeded);
        var stamp = user.SecurityStamp; var password = user.PasswordHash;
        var outcome = await i.External.SignInAsync(Tang6cTests.Info(provider, user.Email));
        Assert.False(outcome.Succeeded); Assert.Equal(ExternalLoginService.Collision, outcome.Error);
        using var fresh = DatabaseFixture.NewContext(); var saved = (await fresh.Users.FindAsync(user.Id))!;
        Assert.Equal(confirmed, saved.EmailConfirmed); Assert.Equal(stamp, saved.SecurityStamp); Assert.Equal(password, saved.PasswordHash);
        Assert.Empty(await i.Users.GetLoginsAsync(user));
    }

    [Fact]
    public async Task U1_TranThuTaiKhoanKhongChanMa2LopQuaEmail()
    {
        using var h = new Harness(); using var i = new TwoFactorIdentity(h);
        var user = await i.PrepareAsync(h);
        await i.TwoFactor.ChangeAsync(user.Id, TwoFactorChange.EnableEmail, null, null, "https://localhost/Account");
        var emails = SecurityRound1Fixtures.Emails(h, i.Users, i.Email);
        for (var n = 0; n < 5; n++) { Assert.True(await emails.TryReserveAsync(user)); h.Clock.UtcNow = h.Clock.UtcNow.AddMinutes(1); }
        Assert.False(await emails.TryReserveAsync(user));
        var before = i.Email.Messages.Count;
        await i.TwoFactor.SendEmailAsync(user.Id);
        Assert.Equal(before + 1, i.Email.Messages.Count); Assert.Contains("Mã đăng nhập:", i.Email.Messages.Last());
        await Assert.ThrowsAsync<BusinessException>(() => i.TwoFactor.SendEmailAsync(user.Id));
    }

    [Fact]
    public async Task U3_GoogleKhongXacMinhEmailKhongDuocNhanTaiKhoan()
    {
        using var h = new Harness(); using var i = new TwoFactorIdentity(h, externalConfig: Tang6cPureTests.Config());
        var user = await i.PrepareAsync(h); user.EmailConfirmed = false; Assert.True((await i.Users.UpdateAsync(user)).Succeeded);
        var stamp = user.SecurityStamp;
        await Assert.ThrowsAsync<BusinessException>(() => i.External.SignInAsync(Tang6cTests.Info(email: user.Email, verified: "false")));
        using var fresh = DatabaseFixture.NewContext(); var saved = (await fresh.Users.FindAsync(user.Id))!;
        Assert.False(saved.EmailConfirmed); Assert.Equal(stamp, saved.SecurityStamp); Assert.NotNull(saved.PasswordHash);
        Assert.Empty(await i.Users.GetLoginsAsync(user));
    }

    [Fact]
    public async Task U4_TokenCuaAKhongDatDuocMatKhauChoBChuaXacMinhCoThat()
    {
        using var h = new Harness(); using var i = new SafetyIdentity(h);
        var a = await i.PrepareAsync(await h.UserAsync("A")); var b = await i.PrepareAsync(await h.UserAsync("B"));
        a.EmailConfirmed = false; b.EmailConfirmed = false;
        Assert.True((await i.Users.UpdateAsync(a)).Succeeded); Assert.True((await i.Users.UpdateAsync(b)).Succeeded);
        var stamp = b.SecurityStamp; var password = b.PasswordHash;
        var token = await i.Users.GenerateEmailConfirmationTokenAsync(a);
        var emails = SecurityRound1Fixtures.Emails(h, i.Users, i.Email);
        Assert.True(await emails.CanSetPasswordAsync(a.Id, token)); Assert.False(await emails.CanSetPasswordAsync(b.Id, token));
        Assert.Equal(AccountEmailService.InvalidSetupLink, (await Assert.ThrowsAsync<BusinessException>(() => emails.SetPasswordAsync(b.Id, token, "Attacker123!"))).Message);
        using var fresh = DatabaseFixture.NewContext(); var saved = (await fresh.Users.FindAsync(b.Id))!;
        Assert.False(saved.EmailConfirmed); Assert.Equal(stamp, saved.SecurityStamp); Assert.Equal(password, saved.PasswordHash);
        Assert.True(await emails.CanSetPasswordAsync(a.Id, token));
    }

    [Fact]
    public async Task U4_HaiYeuCauLuuCungTokenChiMotThanhCongVaDuLieuNhatQuan()
    {
        using var h = new Harness(); using var i = new TwoFactorIdentity(h);
        var user = await i.PrepareAsync(h); user.EmailConfirmed = false; Assert.True((await i.Users.UpdateAsync(user)).Succeeded);
        var stamp = user.SecurityStamp;
        // Hai scope chia sẻ khóa bảo vệ token, nhưng mỗi scope có DbContext/UserManager riêng.
        var services = new ServiceCollection(); services.AddLogging(); services.AddDataProtection().UseEphemeralDataProtectionProvider();
        services.AddScoped(_ => DatabaseFixture.NewContext());
        services.AddIdentityCore<ApplicationUser>().AddEntityFrameworkStores<AppDbContext>().AddUserStore<ProtectedUserStore>().AddDefaultTokenProviders();
        using var provider = services.BuildServiceProvider(); using var scope1 = provider.CreateScope(); using var scope2 = provider.CreateScope();
        var users1 = scope1.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var users2 = scope2.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var first = (await users1.FindByIdAsync(user.Id))!; var second = (await users2.FindByIdAsync(user.Id))!;
        var token = await users1.GenerateEmailConfirmationTokenAsync(first);
        var db1 = scope1.ServiceProvider.GetRequiredService<AppDbContext>(); var db2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var db in new[] { db1, db2 })
            new AuditLog(db, new HttpContextAccessor(), provider.GetRequiredService<IServiceScopeFactory>(), h.Clock, NullLogger<AuditLog>.Instance).Attach();
        var service1 = new AccountEmailService(db1, users1, i.Email, h.Clock, SecurityRound1Fixtures.Links);
        var service2 = new AccountEmailService(db2, users2, i.Email, h.Clock, SecurityRound1Fixtures.Links);
        using var ready = new CountdownEvent(2);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<bool> Save(AccountEmailService service, string password)
        {
            ready.Signal(); await start.Task;
            try { await service.SetPasswordAsync(user.Id, token, password); return true; }
            catch (BusinessException ex) { Assert.Equal(AccountEmailService.InvalidSetupLink, ex.Message); return false; }
        }
        var t1 = Save(service1, "FirstPassword123!"); var t2 = Save(service2, "SecondPassword123!");
        Assert.True(ready.Wait(TimeSpan.FromSeconds(5))); start.SetResult();
        var results = await Task.WhenAll(t1, t2); Assert.Single(results, x => x);
        using var fresh = DatabaseFixture.NewContext(); var saved = (await fresh.Users.FindAsync(user.Id))!;
        Assert.True(saved.EmailConfirmed); Assert.NotEqual(stamp, saved.SecurityStamp);
        Assert.True(await users1.CheckPasswordAsync(saved, results[0] ? "FirstPassword123!" : "SecondPassword123!"));
        Assert.False(await users1.CheckPasswordAsync(saved, results[0] ? "SecondPassword123!" : "FirstPassword123!"));
        Assert.False(await service1.CanSetPasswordAsync(user.Id, token)); Assert.False(await service2.CanSetPasswordAsync(user.Id, token));
        var events = await fresh.AuditEntries.Where(e => e.TargetId == user.Id).ToListAsync();
        Assert.Single(events, e => e.Action == AuditAction.EmailConfirmed); Assert.Single(events, e => e.Action == AuditAction.PasswordSet);
    }

    [Fact]
    public async Task U2_QuizDaXoaChuyenLopKhongKhoiPhucVaKhongSuaDuocQuaDuongKhac()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(1);
        await h.Assignments.CloseNowAsync(s.Assignment.Id, s.Lecturer); await h.Library.DeleteQuizAsync(s.Quiz.Id, s.Lecturer);
        var receiver = (await h.Db.Users.FindAsync(await h.UserAsync("Nhận lớp")))!;
        receiver.EmailConfirmed = true; await h.Db.SaveChangesAsync();
        var transfers = new ClassTransferService(h.Db, h.Classrooms, new(h.Db, h.Clock), new(h.Db, h.Clock, h.Notifier), h.Clock, h.Invitations);
        var offer = await transfers.OfferAsync(s.Room.Id, receiver.Email!, false, s.Lecturer);
        await transfers.DecideAsync(offer.Id, true, receiver.Id);
        var deleted = s.Quiz.DeletedAt; var archived = s.Quiz.ArchivedAt;
        var trash = new TrashService(h.Db, h.Classrooms, h.Clock);
        const string message = "Quiz này đang dùng trong bài giao của lớp đã chuyển cho giảng viên khác nên không khôi phục được.";
        Assert.Equal(message, (await Assert.ThrowsAsync<BusinessException>(() => trash.RestoreAsync(TrashKind.Quiz, s.Quiz.Id, s.Lecturer))).Message);
        using (var fresh = DatabaseFixture.NewContext())
        {
            var saved = (await fresh.Quizzes.FindAsync(s.Quiz.Id))!;
            Assert.Equal(deleted, saved.DeletedAt); Assert.Equal(archived, saved.ArchivedAt);
        }
        // Mô phỏng quiz từng được khôi phục trước bản vá: service sửa vẫn phải chặn.
        s.Quiz.DeletedAt = null; s.Quiz.ArchivedAt = null; await h.Db.SaveChangesAsync();
        var q = s.Quiz.Questions.Single(); var prompt = q.Prompt; var version = q.Version;
        Assert.Equal(message, (await Assert.ThrowsAsync<BusinessException>(() => h.Library.GetOwnedQuizAsync(s.Quiz.Id, s.Lecturer))).Message);
        Assert.Equal(message, (await Assert.ThrowsAsync<BusinessException>(() => h.Library.SaveChoiceQuestionAsync(s.Quiz.Id, q.Id, version,
            Harness.Single("Không được lưu"), s.Lecturer))).Message);
        using var final = DatabaseFixture.NewContext(); var savedQuestion = (await final.Questions.FindAsync(q.Id))!;
        Assert.Equal(prompt, savedQuestion.Prompt); Assert.Equal(version, savedQuestion.Version);
    }

    [Theory]
    [InlineData(false), InlineData(true)]
    public async Task U2_QuizChuaChuyenVanKhoiPhucVaSuaDuocKeCaBaiCu(bool legacy)
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(1);
        if (legacy) { s.Assignment.StudySetId = null; await h.Db.SaveChangesAsync(); }
        await h.Assignments.CloseNowAsync(s.Assignment.Id, s.Lecturer); await h.Library.DeleteQuizAsync(s.Quiz.Id, s.Lecturer);
        await new TrashService(h.Db, h.Classrooms, h.Clock).RestoreAsync(TrashKind.Quiz, s.Quiz.Id, s.Lecturer);
        var quiz = await h.Library.GetOwnedQuizAsync(s.Quiz.Id, s.Lecturer); var q = quiz.Questions.Single();
        await h.Library.SaveChoiceQuestionAsync(quiz.Id, q.Id, q.Version, Harness.Single("Được sửa"), s.Lecturer);
        using var fresh = DatabaseFixture.NewContext(); Assert.Equal("Được sửa", (await fresh.Questions.FindAsync(q.Id))!.Prompt);
    }
}
