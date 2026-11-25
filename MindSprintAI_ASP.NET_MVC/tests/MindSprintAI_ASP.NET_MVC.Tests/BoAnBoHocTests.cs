using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MindSprintAI_ASP.NET_MVC.Controllers;
using MindSprintAI_ASP.NET_MVC.Data.Entities;
using MindSprintAI_ASP.NET_MVC.Services;

namespace MindSprintAI_ASP.NET_MVC.Tests;

[Collection("db")]
public class BoAnBoHocTests
{
    private static UnhideRequestService Service(Harness h) => new(h.Db, h.Library, new(h.Db, h.Clock, h.Notifier), h.Clock);

    private static async Task<string> AdminAsync(Harness h)
    {
        var id = await h.UserAsync("Quản trị viên");
        (await h.Db.Users.FindAsync(id))!.IsSystemAdmin = true;
        await h.Db.SaveChangesAsync();
        return id;
    }

    private static async Task HideAsync(Harness h, Scenario s, string admin)
    {
        var reports = new ContentReportService(h.Db, h.Clock, h.Assignments);
        var id = await reports.SubmitAsync(new(ReportTarget.Quiz, s.Quiz.Id.ToString(), s.Assignment.Id),
            ContentReportService.Reasons[0], null, s.Student);
        await reports.HandleAsync(id, true, "Nội dung cần sửa", admin);
    }

    [Fact]
    public async Task ChiChuBoHocDangBiAnGuiDuocYeuCauHopLe()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(); var service = Service(h);
        Assert.Equal("Bộ học này không bị ẩn.", (await Assert.ThrowsAsync<BusinessException>(() =>
            service.RequestAsync(s.Quiz.StudySetId, "Tôi đã sửa", s.Lecturer))).Message);
        await HideAsync(h, s, await AdminAsync(h));
        await Assert.ThrowsAsync<ForbiddenException>(() => service.RequestAsync(s.Quiz.StudySetId, "Xin xem lại", s.Student));
        foreach (var text in new[] { "  ", new string('a', 1001) })
            Assert.Equal("Hãy viết vài dòng cho quản trị viên: bạn đã sửa gì, hoặc vì sao bạn thấy việc ẩn là nhầm (tối đa 1000 ký tự).",
                (await Assert.ThrowsAsync<BusinessException>(() => service.RequestAsync(s.Quiz.StudySetId, text, s.Lecturer))).Message);
    }

    [Fact]
    public async Task HaiTabCungGuiChiTaoMotYeuCauCho()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(); await HideAsync(h, s, await AdminAsync(h));
        async Task<string?> SendAsync()
        {
            using var other = new Harness();
            try { await Service(other).RequestAsync(s.Quiz.StudySetId, "Tôi đã sửa", s.Lecturer); return null; }
            catch (BusinessException ex) { return ex.Message; }
        }
        var results = await Task.WhenAll(SendAsync(), SendAsync());
        Assert.Single(results, r => r == null);
        Assert.Single(results, r => r == "Bạn đã gửi yêu cầu, đang chờ quản trị viên xem.");
        Assert.Equal(1, await h.Db.UnhideRequests.CountAsync(r => r.StudySetId == s.Quiz.StudySetId && r.Status == UnhideRequestStatus.Pending));
    }

    [Fact]
    public async Task DuyetBoAnThongBaoChuGiuBaiCuDongVaGiaoMoiDuoc()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(); var admin = await AdminAsync(h);
        await HideAsync(h, s, admin);
        var service = Service(h); var id = await service.RequestAsync(s.Quiz.StudySetId, "  Tôi đã sửa câu 3  ", s.Lecturer);
        var notice = await h.Db.Notifications.SingleAsync(n => n.UserId == admin && n.Type == NotificationType.ContentReview);
        Assert.Equal($"/Admin/UnhideRequest/{id}", NotificationService.Payload(notice).Url);
        var first = s.Quiz.Questions.First(); first.Prompt = "Câu vừa sửa";
        h.Db.Flashcards.Add(new() { StudySetId = s.Quiz.StudySetId, Front = "Mặt trước vừa sửa", Back = "Mặt sau" });
        await h.Db.SaveChangesAsync();
        var details = await service.GetAsync(id, admin);
        Assert.Equal("Tôi đã sửa câu 3", details.Request.Message);
        Assert.Equal(3, details.Request.StudySet.Quizzes.SelectMany(q => q.Questions).Count());
        Assert.Contains(details.Request.StudySet.Quizzes.SelectMany(q => q.Questions), q => q.Prompt == "Câu vừa sửa");
        Assert.Equal("Mặt trước vừa sửa", Assert.Single(details.Cards).Front);
        await service.GetAsync(id, admin);
        Assert.Equal(2, await h.Db.AdminAccessLogs.CountAsync(l => l.AdminId == admin));
        await Assert.ThrowsAsync<BusinessException>(() => service.DecideAsync(id, true, new string('a', 501), admin));
        await service.DecideAsync(id, true, null, admin);
        h.Db.ChangeTracker.Clear();
        var set = await h.Db.StudySets.FindAsync(s.Quiz.StudySetId);
        Assert.Null(set!.HiddenByAdminAt); Assert.Null(set.HiddenReason);
        Assert.Equal(UnhideRequestStatus.Approved, (await h.Db.UnhideRequests.FindAsync(id))!.Status);
        Assert.Equal(AssignmentStatus.Closed, (await h.Db.Assignments.FindAsync(s.Assignment.Id))!.Status);
        Assert.Equal(CloseReason.HiddenByAdmin, (await h.Db.Assignments.FindAsync(s.Assignment.Id))!.CloseReason);
        notice = await h.Db.Notifications.SingleAsync(n => n.UserId == s.Lecturer && n.Type == NotificationType.ContentReview);
        Assert.Equal($"Bộ học “{set.Title}” đã được bỏ ẩn. Bạn giao bài lại được; các bài giao đã đóng lúc ẩn vẫn đóng.", NotificationService.Payload(notice).Text);
        Assert.NotNull(await h.Assignments.CreatePracticeAsync(s.Room.Id, s.Quiz.Id, new(null, null, 100), s.Lecturer));
        Assert.Equal("Yêu cầu này đã được xử lý.", (await Assert.ThrowsAsync<BusinessException>(() => service.DecideAsync(id, false, "Xem lại", admin))).Message);
    }

    [Fact]
    public async Task TuChoiCanLyDoVaChuNhanLyDoRoiGuiLaiDuoc()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(); var admin = await AdminAsync(h);
        await HideAsync(h, s, admin);
        var service = Service(h); var id = await service.RequestAsync(s.Quiz.StudySetId, "Xin xem lại", s.Lecturer);
        foreach (var note in new[] { " ", new string('a', 501) })
            Assert.Equal("Hãy ghi lý do từ chối (tối đa 500 ký tự).", (await Assert.ThrowsAsync<BusinessException>(() =>
                service.DecideAsync(id, false, note, admin))).Message);
        await service.DecideAsync(id, false, "  Câu 3 vẫn cần sửa  ", admin);
        Assert.NotNull((await h.Db.StudySets.FindAsync(s.Quiz.StudySetId))!.HiddenByAdminAt);
        var notice = await h.Db.Notifications.SingleAsync(n => n.UserId == s.Lecturer && n.Type == NotificationType.ContentReview);
        Assert.Contains("chưa được chấp nhận: Câu 3 vẫn cần sửa", NotificationService.Payload(notice).Text);
        Assert.NotEqual(id, await service.RequestAsync(s.Quiz.StudySetId, "Đã sửa lại", s.Lecturer));
    }

    [Fact]
    public async Task ChiQuanTriVienDuocXemVaQuyetDinh()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(); var admin = await AdminAsync(h);
        await HideAsync(h, s, admin);
        var service = Service(h); var id = await service.RequestAsync(s.Quiz.StudySetId, "Xin xem lại", s.Lecturer);
        await Assert.ThrowsAsync<ForbiddenException>(() => service.GetAsync(id, s.Lecturer));
        await Assert.ThrowsAsync<ForbiddenException>(() => service.DecideAsync(id, true, null, s.Lecturer));
        await Assert.ThrowsAsync<ForbiddenException>(() => service.PendingAsync(s.Student));
    }

    [Fact]
    public async Task ChuongChiGuiQuanTriVienKhongBiKhoa()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(); var admin = await AdminAsync(h);
        var locked = await AdminAsync(h);
        h.Db.AccountLocks.Add(new() { UserId = locked, LockedById = admin, StartsAt = h.Clock.UtcNow.AddMinutes(-1),
            Kind = AccountLockKind.Protective, Reason = "Tạm khóa" });
        await h.Db.SaveChangesAsync();
        await HideAsync(h, s, admin);
        var id = await Service(h).RequestAsync(s.Quiz.StudySetId, "Xin xem lại", s.Lecturer);
        // CSDL thử có thể có Admin của test khác: chỉ xét hai Admin của test này.
        Assert.True(await h.Db.Notifications.AnyAsync(n => n.Type == NotificationType.ContentReview && n.UserId == admin));
        Assert.False(await h.Db.Notifications.AnyAsync(n => n.Type == NotificationType.ContentReview && n.UserId == locked));
        await Assert.ThrowsAsync<ForbiddenException>(() => Service(h).GetAsync(id, locked));
        await Assert.ThrowsAsync<ForbiddenException>(() => Service(h).DecideAsync(id, true, null, locked));
    }

    [Fact]
    public async Task TaoVaDoiSoLuotChiNhanTuMotDenMotTram()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync();
        var error = await Assert.ThrowsAsync<BusinessException>(() => h.Assignments.CreatePracticeAsync(s.Room.Id,
            s.Quiz.Id, new(null, null, 101), s.Lecturer));
        Assert.Equal("Số lượt tối đa phải từ 1 đến 100.", error.Message);
        var a = await h.Assignments.CreatePracticeAsync(s.Room.Id, s.Quiz.Id, new(null, null, 100), s.Lecturer);
        Assert.Equal(100, a.MaxAttempts);
        var unlimited = await h.Assignments.CreatePracticeAsync(s.Room.Id, s.Quiz.Id, new(null, null, null), s.Lecturer);
        Assert.Null(unlimited.MaxAttempts);
        error = await Assert.ThrowsAsync<BusinessException>(() => h.Assignments.ChangeMaxAttemptsAsync(a.Id, 101, "Thêm lượt", s.Lecturer));
        Assert.Equal("Số lượt tối đa phải từ 1 đến 100.", error.Message);
    }

    [Fact]
    public async Task TongLuotChungVaCongRiengKhongQuaMotTram()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(settings: new(null, null, 90));
        var error = await Assert.ThrowsAsync<BusinessException>(() => h.Assignments.ChangeExtraAttemptsAsync(s.Assignment.Id,
            s.Student, 11, "Luyện thêm", s.Lecturer));
        Assert.Equal("Tổng số lượt của học viên này (chung + cộng riêng) tối đa 100, nên lượt cộng riêng tối đa 10.", error.Message);
        h.Db.ChangeTracker.Clear();
        await h.Assignments.ChangeExtraAttemptsAsync(s.Assignment.Id, s.Student, 10, "Luyện thêm", s.Lecturer);
        error = await Assert.ThrowsAsync<BusinessException>(() => h.Assignments.ChangeMaxAttemptsAsync(s.Assignment.Id, 91, "Thêm lượt", s.Lecturer));
        Assert.Equal("Có học viên đã được cộng riêng 10 lượt, nên số lượt tối đa chung chỉ đặt được tới 90.", error.Message);
    }
}

public class BoAnBoHocPureTests
{
    [Fact]
    public void ChuMoiKhongViPhamG0() => Assert.Empty(PlainLanguageAudit.Violations(Tang8PureTests.Root));

    [Fact]
    public void HienLaiFormKhiLoiTimDungTenTrang()
    {
        // KeepFormOnErrorAsync gọi lại action GET từ action POST: View() không ghi tên sẽ tìm trang theo tên action POST (lỗi 500 đã gặp khi thử).
        var root = Tang8PureTests.Root;
        Assert.Contains("return View(nameof(Details), set);", File.ReadAllText(Path.Combine(root, "Controllers/StudySetsController.cs")));
        Assert.Contains("View(nameof(UnhideRequest), await unhide.GetAsync(id, UserId))", File.ReadAllText(Path.Combine(root, "Controllers/AdminController.cs")));
    }

    [Fact]
    public async Task DanhSachTrongVaLoiGiangVienRutGonDungMotTramHaiMuoiKyTu()
    {
        var html = WebUtility.HtmlDecode(await RenderAsync("Admin", "UnhideRequests", new List<UnhideRequest>()));
        Assert.Contains("Không có yêu cầu nào đang chờ.", html);
        html = WebUtility.HtmlDecode(await RenderAsync("Admin", "UnhideRequests", new List<UnhideRequest>
        {
            new() { StudySet = new() { Title = "Bộ học" }, Requester = new() { DisplayName = "Lan" }, Message = new string('a', 121) }
        }));
        Assert.Contains(new string('a', 120) + "…", html);
        Assert.DoesNotContain(new string('a', 121), html);
    }

    [Theory]
    [InlineData(UnhideRequestStatus.Pending)]
    [InlineData(UnhideRequestStatus.Rejected)]
    public async Task ChuBoHocThayDungTrangThaiVaGiuLoiDaGo(UnhideRequestStatus status)
    {
        var html = WebUtility.HtmlDecode(await RenderAsync("StudySets", "Details", new StudySet
        { Id = 1, Title = "Bộ học", HiddenByAdminAt = DateTime.UtcNow, HiddenReason = "Cần sửa" }, new()
        {
            ["UnhideRequest"] = new UnhideRequest { Status = status, CreatedAt = new(2026, 11, 11, 8, 0, 0, DateTimeKind.Utc), DecisionNote = "Sửa câu 3" },
            ["UnhideMessage"] = "Tôi đã sửa câu 3"
        }));
        if (status == UnhideRequestStatus.Pending)
        {
            Assert.Contains("Đã gửi yêu cầu xem xét lại lúc", html);
            Assert.Contains("15:00", html);
            Assert.DoesNotContain("name=\"message\"", html);
        }
        else
        {
            Assert.Contains("Lần trước chưa được chấp nhận: Sửa câu 3", html);
            Assert.Contains("Tôi đã sửa câu 3</textarea>", html);
            Assert.Contains("__RequestVerificationToken", html);
        }
    }

    [Fact]
    public async Task ChiTietAdminChiDocHienDapAnVaHaiFormCoBaoVe()
    {
        var row = new UnhideRequest
        {
            Id = 5, Requester = new() { DisplayName = "Lan", Email = "lan@example.vn" }, Message = "<script>alert(1)</script>",
            StudySet = new() { Title = "Bộ học", HiddenReason = "Cần sửa", Quizzes = [new()
            { Title = "Bài 1", Questions = [new() { Prompt = "2 + 2?", Options = [new() { Text = "4", IsCorrect = true }] }] }] }
        };
        var html = await RenderAsync("Admin", "UnhideRequest", new UnhideRequestDetails(row,
            [new() { Front = "Mặt trước", Back = "Mặt sau" }]));
        Assert.DoesNotContain("<script>", html);
        html = WebUtility.HtmlDecode(html);
        Assert.Contains("lan@example.vn", html);
        Assert.Contains("Đáp án đúng:", html);
        Assert.Contains("Mặt trước", html); Assert.Contains("Mặt sau", html);
        Assert.Contains("name=\"approve\" value=\"true\"", html);
        Assert.Contains("name=\"approve\" value=\"false\"", html);
        Assert.Contains("data-confirm=\"Bỏ ẩn bộ học này? Giảng viên giao bài lại được ngay.\"", html);
        Assert.Contains("data-confirm=\"Từ chối yêu cầu này? Giảng viên sẽ thấy lý do bạn ghi.\"", html);
        Assert.Contains("__RequestVerificationToken", html);
        Assert.Contains("b-back-icon", html);
    }

    [Fact]
    public async Task TrangChuGiangVien_DuNhuQuizEdu_LoiTat_HanBai_CanXemTheoLop()
    {
        // Trang chủ giảng viên QuizEdu (Home/Lecturer) gộp vào trang chủ nhóm: 3 lối tắt, bài đang mở có chế độ + hạn, "Cần xem" từng lớp có link.
        var teaching = new LecturerDashboard("Lan", "Chào buổi sáng",
            [new(7, "Kiểm tra chương 1", "Web – Nhóm 1", 2, 5, AssignmentMode.Test, new DateTime(2026, 11, 20, 2, 0, 0, DateTimeKind.Utc))],
            [new(3, "Web – Nhóm 1", 4, new ReviewWork(JoinRequests: 3, Manual: 1))]);
        var model = new HomeDashboard("Lan", "Chào buổi sáng", new DateOnly(2026, 11, 15), true, teaching, [], 0, [], 0, 0, [],
            new StudyStreak(0, 0, null), new StudyToday(0, 0, 0));
        var html = WebUtility.HtmlDecode(await RenderAsync("Home", "Index", model)); // máy test mã hóa chữ có dấu thành &#x..;
        foreach (var text in new[] { "Tạo bộ học", "Tạo lớp", "Giao bài", "#new-class-name", "Xem tất cả (1)",
            "Kiểm tra chương 1", "kết thúc", "2 đang làm · 5 đã nộp", "Cần xem", "4 việc chờ", "3 học viên xin vào lớp · 1 bài làm cần chấm tay" })
            Assert.Contains(text, html);
        // Mỗi lớp trong "Cần xem" là một link bấm vào lớp (máy test không có bảng đường dẫn nên chỉ kiểm cấu trúc).
        Assert.Matches(@"class=""home-review"">\s*<li><a [^>]*><svg[^>]*>.*?</svg><span><strong>Web – Nhóm 1</strong>", html);
    }

    private static async Task<string> RenderAsync(string folder, string name, object model, Dictionary<string, object?>? values = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        { ApplicationName = typeof(StudySetsController).Assembly.GetName().Name, EnvironmentName = "Development" });
        builder.Services.AddControllersWithViews().AddApplicationPart(typeof(StudySetsController).Assembly);
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        await using var app = builder.Build();
        app.MapDefaultControllerRoute();
        using var scope = app.Services.CreateScope();
        var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        http.Request.Scheme = "http"; http.Request.Host = new("localhost");
        http.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(), "Render test"));
        var route = new RouteData(); route.Values["controller"] = folder; route.Values["action"] = name;
        var action = new ActionContext(http, route, new ActionDescriptor());
        var view = scope.ServiceProvider.GetRequiredService<IRazorViewEngine>().GetView(null, $"/Views/{folder}/{name}.cshtml", false);
        Assert.True(view.Success);
        var data = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary()) { Model = model };
        foreach (var (key, value) in values ?? []) data[key] = value;
        var temp = scope.ServiceProvider.GetRequiredService<ITempDataDictionaryFactory>().GetTempData(http);
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        await view.View.RenderAsync(new ViewContext(action, view.View, data, temp, writer, new HtmlHelperOptions()));
        return writer.ToString();
    }
}
