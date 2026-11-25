using System.Net;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using MindSprintAI_ASP.NET_MVC.Controllers;
using MindSprintAI_ASP.NET_MVC.Data.Entities;
using MindSprintAI_ASP.NET_MVC.Models;
using MindSprintAI_ASP.NET_MVC.Services;

namespace MindSprintAI_ASP.NET_MVC.Tests;

public class RaSoatTkAiHvTests
{
    private static string SavedKeyError(BusinessException error) => (string)typeof(ApiKeysController)
        .GetMethod("SavedKeyError", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.Invoke(null, [error])!;
    [Theory]
    [InlineData(401, "{}", true), InlineData(403, "{}", true)]
    [InlineData(400, "API_KEY_INVALID", true), InlineData(400, "API key not valid", true)]
    [InlineData(400, "ACCESS_TOKEN_TYPE_UNSUPPORTED", true)]
    [InlineData(429, "{}", false), InlineData(500, "{}", false), InlineData(503, "{}", false)]
    [InlineData(400, "Invalid JSON payload", false), InlineData(404, "{}", false)]
    public async Task R8_PhanBietMaSaiVaLoiTam(int status, string body, bool invalid)
    {
        var client = new GeminiClient(new HttpClient(new FakeGeminiHandler { Status = (HttpStatusCode)status, ErrorBody = body }),
            new ConfigurationBuilder().Build());
        var error = await Assert.ThrowsAsync<BusinessException>(() => client.CheckKeyAsync("AIza_fake_key_1234567890"));
        var message = SavedKeyError(error);
        if (invalid)
        {
            Assert.Equal(error.Message + " Mã đã được lưu, hãy kiểm tra lại mã.", message);
            Assert.DoesNotContain("Bạn vẫn dùng thử được", message);
        }
        else Assert.StartsWith("Đã lưu nhưng chưa kiểm tra được mã lúc này.", message);
        Assert.False(PlainLanguageAudit.HasJargon(message));
    }

    [Theory]
    [InlineData(false), InlineData(true)]
    public async Task R8_MatMangVaHetGioLaLoiTam(bool timeout)
    {
        var client = new GeminiClient(new HttpClient(new NetworkFailure(timeout)), new ConfigurationBuilder().Build());
        var error = await Assert.ThrowsAsync<BusinessException>(() => client.CheckKeyAsync("AIza_fake_key_1234567890"));
        Assert.StartsWith("Đã lưu nhưng chưa kiểm tra được mã lúc này.", SavedKeyError(error));
    }

    private sealed class NetworkFailure(bool timeout) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw (timeout ? new TaskCanceledException() : new HttpRequestException());
    }

    [Theory]
    [InlineData(AccountLockKind.Protective), InlineData(AccountLockKind.Violation)]
    public void R10_HuongDanChiDanhChoNguoiBiKhoa(AccountLockKind kind)
    {
        var row = new AccountLock { Kind = kind, Reason = "Thử" };
        var admin = AccountLockService.Message(row); var user = AccountLockService.MessageForUser(row);
        Assert.DoesNotContain("Muốn mở khóa", admin); Assert.DoesNotContain("Hãy liên hệ", admin);
        Assert.StartsWith(admin, user);
        Assert.Contains(kind == AccountLockKind.Protective ? "Muốn mở khóa" : "Hãy liên hệ quản trị viên.", user);
        if (kind == AccountLockKind.Violation) Assert.Contains("đến khi quản trị viên mở khóa", admin);
        foreach (var file in new[] { "Services/LoginService.cs", "Services/ExternalLoginService.cs" })
        {
            Assert.Contains("AccountLockService.MessageForUser(active)", Source(file));
            Assert.DoesNotContain("AccountLockService.Message(active)", Source(file));
        }
    }

    [Theory]
    [InlineData(false), InlineData(true)]
    public async Task R17_LoiKhongTuMoLaiFormKhiKhongConQuyen(bool canRequest)
    {
        var a = Tang4RuleTests.Build(0).Participant.Assignment;
        var html = await Render("Learn", "Assignment", new LearnerAssignmentModel
        { Assignment = a, Attempts = [], CanStart = false, CanRequestRetake = canRequest }, true, error: "Bạn đã dùng hết số lần được làm lại của bài này.");
        Assert.Equal(canRequest, html.Contains("b-retake-form"));
    }

    [Fact]
    public async Task R9R12_LoiMoiVaGoiYPasskeyCoDuongVaoDung()
    {
        var html = await Render("Account", "Login", new LoginModel(), false);
        var controls = Regex.Match(html, "<div data-passkey-controls hidden>[\\s\\S]*?</div>").Value;
        Assert.Contains("Dùng vân tay, khuôn mặt hoặc mã PIN của thiết bị.", controls);
        Assert.Matches("class=\"acc-toc-link\" asp-controller=\"Invitations\" asp-action=\"Index\"", Source("Views/Account/Index.cshtml"));
    }

    [Fact]
    public async Task R3_CauBoTrongKhongBaoDaTraLoi()
    {
        var a = Tang4RuleTests.Build(1); a.Status = AttemptStatus.Ended;
        var html = await Render("Learn", "Review", a, true, new()
        { ["Questions"] = AttemptService.ProjectQuestions(a, false), ["ShowScores"] = true });
        Assert.Contains(">Bỏ trống</span>", html);
        Assert.DoesNotContain("Đã trả lời", html);
        Assert.DoesNotContain("Đã ghi nhận câu trả lời", html);
    }

    [Theory]
    [InlineData(0, "Bạn chưa vào lớp nào.", true)]
    [InlineData(2, "Bạn đang chờ giảng viên duyệt vào lớp.", false)]
    [InlineData(3, "Lớp của bạn chưa có bài nào.", false)]
    public async Task R6R7_TrangTrongTheoTrangThaiLop(int status, string expected, bool steps)
    {
        var html = await Render("Learn", "Index", new List<Assignment>(), true, new()
        { ["Participations"] = new List<Participant>(), ["Classes"] = status == 0 ? new List<ClassMember>() : [new() { Status = (ClassMemberStatus)status }] });
        Assert.Contains(expected, html);
        Assert.Equal(steps, html.Contains("learn-empty-steps"));
        if (status == 2) Assert.DoesNotContain("Lớp của bạn chưa có bài nào.", html);
    }

    [Fact]
    public async Task R6_CoBaiKhongMoiVaoLopDeNhanBaiDauTien()
    {
        var html = await Render("Learn", "Index", new List<Assignment> { Tang4RuleTests.Build(0).Participant.Assignment }, true, new()
        { ["Participations"] = new List<Participant>(), ["Classes"] = new List<ClassMember>() });
        Assert.DoesNotContain("learn-empty-steps", html);
    }

    [Theory]
    [InlineData(null, true), InlineData(1, false)]
    public async Task R4_DongHoChungChiChoLuotDau(int? retake, bool shared)
    {
        var a = Tang4RuleTests.Build(0).Participant.Assignment; a.Mode = AssignmentMode.Test;
        var html = await Render("Learn", "Consent", new DeviceConsentModel(a, RetakeId: retake), true);
        Assert.Equal(shared, html.Contains("Đồng hồ làm bài chạy theo giờ chung của cả lớp."));
    }

    [Theory]
    [InlineData(AttemptKind.SelfReview, false), InlineData(AttemptKind.SelfReview, true)]
    [InlineData(AttemptKind.Normal, false), InlineData(AttemptKind.Normal, true)]
    public async Task R11_ThoatChiCanhBaoDongHoKhiCoDongHo(AttemptKind kind, bool timed)
    {
        var a = Tang4RuleTests.Build(0); a.Participant.Assignment.Mode = AssignmentMode.Test;
        a.Kind = kind; a.DeadlineAt = timed ? DateTime.UtcNow.AddMinutes(10) : null;
        var html = await Render("Learn", "Play", a, true, new() { ["Questions"] = AttemptService.ProjectQuestions(a, false) });
        Assert.Equal(timed && kind != AttemptKind.SelfReview, html.Contains("Thoát ra ngoài? Đồng hồ làm bài vẫn chạy"));
        if (kind == AttemptKind.SelfReview) Assert.DoesNotContain("Hạn nộp của bài vẫn tính như cũ.", html);
    }

    [Theory]
    [InlineData(false), InlineData(true)]
    public async Task R18_ChiXinDuongDanMoiKhiDuongDanHong(bool invalid)
    {
        var html = await Render("Account", "ResetPassword", new ResetPasswordModel(), false, new() { ["LinkInvalid"] = invalid },
            error: invalid ? "Đường dẫn không hợp lệ hoặc đã hết hạn." : "Mật khẩu không khớp.");
        Assert.Equal(invalid, html.Contains("Xin link mới"));
    }

    [Theory]
    [InlineData("Password", false), InlineData("ConfirmPassword", false)]
    [InlineData("UserId", true), InlineData("Code", true), InlineData("Nonce", true)]
    public async Task R18_ControllerPhanBietLoiMatKhauVaLoiDuongDan(string field, bool invalid)
    {
        var controller = new AccountController(null!, null!, null!, null!, null!, null!, null!, null!, null!, null!);
        controller.ModelState.AddModelError(field, "Không hợp lệ.");
        Assert.IsType<ViewResult>(await controller.ResetPassword(new ResetPasswordModel()));
        Assert.Equal(invalid, controller.ViewData["LinkInvalid"]);
    }

    [Fact]
    public void R20_MaLopChoDanDauCach()
    {
        var source = Source("Views/Classrooms/Join.cshtml");
        Assert.Contains("maxlength=\"9\"", source); Assert.Contains("pattern=\"[0-9 ]{8,9}\"", source);
        Assert.Contains("Where(c => !char.IsWhiteSpace(c))", Source("Controllers/ClassroomsController.cs"));
    }

    [Fact]
    public void R21_MaChiCoDauNhoHonBiTuChoiVaNoiRoKyTu()
    {
        var key = UserApiKeyService.NormalizePasted("<");
        Assert.False(UserApiKeyService.ValidFormat(key));
        Assert.Contains("“<”", UserApiKeyService.FormatError(key));
        Assert.Contains("không đúng định dạng", UserApiKeyService.FormatError(key));
    }
    private static string Source(string path)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "MindSprintAI_ASP.NET_MVC.csproj"))) root = root.Parent;
        return File.ReadAllText(Path.Combine(root!.FullName, path));
    }

    // Cùng cách dựng Razor với Tang4ViewTests; đặt người dùng vào HttpContext để bắt lỗi nhánh IsGuest.
    private static async Task<string> Render(string folder, string name, object model, bool signedIn,
        Dictionary<string, object?>? values = null, string? error = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        { ApplicationName = typeof(LearnController).Assembly.GetName().Name, EnvironmentName = "Development" });
        builder.Services.AddControllersWithViews().AddApplicationPart(typeof(LearnController).Assembly);
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        await using var app = builder.Build();
        app.MapDefaultControllerRoute();
        using var scope = app.Services.CreateScope();
        var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        if (signedIn) http.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "student")], "Test"));
        http.Request.Scheme = "http"; http.Request.Host = new HostString("localhost");
        http.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(), "Render test"));
        var route = new RouteData(); route.Values["controller"] = folder; route.Values["action"] = name;
        var action = new ActionContext(http, route, new ActionDescriptor());
        var view = scope.ServiceProvider.GetRequiredService<IRazorViewEngine>().GetView(null, $"/Views/{folder}/{name}.cshtml", false);
        Assert.True(view.Success);
        var data = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary()) { Model = model };
        if (error != null) data.ModelState.AddModelError("", error);
        foreach (var (key, value) in values ?? []) data[key] = value;
        using var writer = new StringWriter();
        await view.View.RenderAsync(new ViewContext(action, view.View, data,
            scope.ServiceProvider.GetRequiredService<ITempDataDictionaryFactory>().GetTempData(http), writer, new HtmlHelperOptions()));
        return WebUtility.HtmlDecode(writer.ToString());
    }

    [Theory]
    [InlineData(true, "Đúng"), InlineData(false, "Sai")]
    public async Task L2_HocVienDangNhapThayNhanKetQua(bool correct, string label)
    {
        var a = Tang4RuleTests.Build(1); a.Status = AttemptStatus.Ended;
        a.Questions[0].AnswerCount = 1;
        a.Questions[0].Submissions.Add(new() { IsFinal = true, IsCorrect = correct, PayloadJson = "{\"Selected\":[1]}" });
        var html = await Render("Learn", "Review", a, true, new()
        { ["Questions"] = AttemptService.ProjectQuestions(a, false), ["ShowScores"] = true });
        Assert.Contains("Báo lỗi câu này", html);
        Assert.Contains($">{label}</span>", html);
    }

    [Theory]
    [InlineData(AssignmentMode.Test, "Các câu đã trả lời được giữ, bạn vào làm tiếp sau."), InlineData(AssignmentMode.Practice, "Hạn nộp của bài vẫn tính như cũ.")]
    public async Task L1_FormThoatHoiLai(AssignmentMode mode, string warning)
    {
        var a = Tang4RuleTests.Build(1); a.Participant.Assignment.Mode = mode;
        var html = await Render("Learn", "Play", a, true, new() { ["Questions"] = AttemptService.ProjectQuestions(a, false) });
        var exit = Regex.Matches(html, @"<form\b[^>]*>[\s\S]*?</form>").Select(m => m.Value)
            .Single(f => f.Contains(">Thoát</button>"));
        Assert.Contains("data-confirm=", exit); Assert.Contains(warning, exit);
    }

    [Fact]
    public void L8_FormXinLamLaiGiuLyDoKhiLoi()
    {
        var controller = Source("Controllers/LearnController.cs").Split("RequestRetake(int id, string? reason)")[1].Split("[HttpPost]")[0];
        Assert.Contains("KeepFormOnErrorAsync", controller);
        Assert.Contains("ViewData[\"RetakeReason\"] = reason", controller);
        Assert.Contains("View(\"Assignment\"", controller);
        Assert.Contains(">@ViewData[\"RetakeReason\"]</textarea>", Source("Views/Learn/Assignment.cshtml"));
        var service = Source("Services/RetakeService.cs");
        foreach (var text in new[] { "Hãy làm xong lượt đang làm rồi mới xin làm lại.", "Bạn chưa có lượt Kiểm tra đã kết thúc.",
            "Bạn đã dùng hết số lần được làm lại của bài này.", "Bạn đã có yêu cầu làm lại đang chờ hoặc đã được duyệt.", "Bài này không cho xin làm lại." })
            Assert.Contains(text, service);
    }

    [Fact]
    public void A6_ChiHienGuiLaiXacMinhKhiCoEmail() =>
        Assert.Matches(@"@if\s*\(!string.IsNullOrWhiteSpace\(Model.Email\)\)\s*\{\s*<a[^>]*asp-action=""ResendConfirmation""[^>]*asp-route-email=""@Model.Email""", Source("Views/Account/Login.cshtml"));

    [Theory]
    [InlineData("open", "Hãy làm xong lượt đang làm rồi mới xin làm lại.")]
    [InlineData("none", "Bạn chưa có lượt Kiểm tra đã kết thúc.")]
    [InlineData("capacity", "Bạn đã dùng hết số lần được làm lại của bài này.")]
    [InlineData("pending", "Bạn đã có yêu cầu làm lại đang chờ hoặc đã được duyệt.")]
    [InlineData("approved", "Bạn đã có yêu cầu làm lại đang chờ hoặc đã được duyệt.")]
    [InlineData("practice", "Bài này không cho xin làm lại.")]
    public void L8_LoiXinLamLaiNoiDungLyDo(string state, string expected)
    {
        var a = Tang4RuleTests.Build(0); var p = a.Participant;
        p.Assignment.Mode = AssignmentMode.Test; a.Status = AttemptStatus.Ended;
        switch (state)
        {
            case "open": a.Status = AttemptStatus.InProgress; break;
            case "none": p.Attempts.Clear(); break;
            case "capacity": p.Attempts.Add(new Attempt { Status = AttemptStatus.Ended, Kind = AttemptKind.Retake }); break;
            case "pending": p.RetakeRequests.Add(new() { Status = RetakeRequestStatus.Pending }); break;
            case "approved": p.RetakeRequests.Add(new() { Status = RetakeRequestStatus.Approved }); break;
            case "practice": p.Assignment.Mode = AssignmentMode.Practice; break;
        }
        Assert.False(RetakeService.CanRequest(p));
        var error = typeof(RetakeService).GetMethod("RequestError", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        Assert.Equal(expected, error.Invoke(null, [p]));
    }

    [Fact]
    public void L6_ChoTrongVaV18_TenLopTheoCheDo()
    {
        Assert.DoesNotContain("Ô ${", Source("wwwroot/js/play.js"));
        Assert.Contains("Chỗ trống ${empty + 1} chưa điền.", Source("wwwroot/js/play.js"));
        Assert.Contains("Điền vào từng chỗ trống bên dưới.", Source("Views/Learn/_PlayQuestionBody.cshtml"));
        // MindSprint: thanh tab điện thoại cũng có link Lớp; kiểm dòng menu thanh bên (nav-link).
        var menu = Source("Views/Shared/_Layout.cshtml").Split('\n').Single(l => l.Contains("nav-link") && l.Contains("asp-controller=\"Classrooms\"") && l.Contains("asp-action=\"Index\""));
        Assert.Contains("lecturer ? \"Lớp tôi dạy\" : \"Lớp của tôi\"", menu);
    }

    [Theory]
    [InlineData(false), InlineData(true)]
    public async Task L3_ChuaCoBaiHuongDanDungTheoLop(bool hasClass)
    {
        var html = await Render("Learn", "Index", new List<Assignment>(), true, new()
        { ["Participations"] = new List<Participant>(), ["Classes"] = hasClass ? new List<ClassMember> { new() } : [] });
        Assert.Equal(!hasClass, html.Contains("learn-empty-steps"));
        Assert.Equal(hasClass, html.Contains("Bạn đang chờ giảng viên duyệt vào lớp."));
        Assert.Equal(!hasClass, html.Contains("Bạn chưa vào lớp nào."));
    }

    [Fact]
    public async Task L8_LyDoXinLamLaiDuocMaHoaKhiHienLai()
    {
        var a = Tang4RuleTests.Build(0).Participant.Assignment;
        var html = await Tang4ViewTests.Render("Learn", "Assignment", new LearnerAssignmentModel
        { Assignment = a, Attempts = [], CanStart = false, CanRequestRetake = true }, new() { ["RetakeReason"] = "<script>lý do</script>" });
        Assert.Contains("&lt;script&gt;", html); Assert.DoesNotContain("<script>lý do</script>", html);
    }

    [Fact]
    public async Task A1_GuiMaEmailDungTruocFormNhapMa()
    {
        var source = Source("Views/Account/LoginTwoFactor.cshtml");
        Assert.True(source.IndexOf("asp-action=\"SendTwoFactorEmail\"", StringComparison.Ordinal)
            < source.IndexOf("asp-action=\"LoginTwoFactor\"", StringComparison.Ordinal));
        Assert.Contains("Bấm Gửi mã qua email trước, rồi nhập mã trong email.", source);
        var html = await Render("Account", "CheckEmail", "Đã gửi email.", true);
        Assert.Contains("Về Tài khoản của tôi", html); Assert.DoesNotContain("Về trang đăng nhập", html);
        // Thư Gmail hay vào Spam (13/11/2026): trang nhắc xem Thư rác và bấm "Không phải thư rác".
        Assert.Contains("Thư rác (Spam)", html); Assert.Contains("Không phải thư rác", html);
    }
}
