using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MindSprintAI_ASP.NET_MVC.Controllers;
using MindSprintAI_ASP.NET_MVC.Data.Entities;
using MindSprintAI_ASP.NET_MVC.Models;
using MindSprintAI_ASP.NET_MVC.Services;
using Xunit;

namespace MindSprintAI_ASP.NET_MVC.Tests;

// Pháp lý đợt 1 (docs/PHAP_LY_VAN_BAN.md): trang Chính sách/Điều khoản, ô đồng ý, thông báo AI, xóa tài khoản, dọn dữ liệu.
public class PhapLyPureTests
{
    private static string Html(string s) => WebUtility.HtmlDecode(s);

    [Fact]
    public void C1_HaiODongYBatBuocTick()
    {
        static List<string> Errors(object model)
        {
            var results = new List<ValidationResult>();
            Validator.TryValidateObject(model, new ValidationContext(model), results, true);
            return results.SelectMany(r => r.MemberNames).ToList();
        }
        var blank = new RegisterModel { Email = "a@b.vn", DisplayName = "An" };
        Assert.Equal(new[] { "AcceptTerms", "AcceptPrivacy" }, Errors(blank));
        Assert.Empty(Errors(new RegisterModel { Email = "a@b.vn", DisplayName = "An", AcceptTerms = true, AcceptPrivacy = true }));
        Assert.Equal(new[] { "AcceptPrivacy" }, Errors(new AcceptPolicyModel { AcceptTerms = true }));
    }

    [Fact]
    public void C2_CongDocPhienBanTuClaim()
    {
        static ClaimsPrincipal User(string? version)
        {
            var id = new ClaimsIdentity([new(ClaimTypes.NameIdentifier, "u")], "test");
            if (version != null) id.AddClaim(new(LegalPolicy.Claim, version));
            return new(id);
        }
        Assert.True(LegalPolicy.NeedsAcceptance(User(null)));
        Assert.True(LegalPolicy.NeedsAcceptance(User((LegalPolicy.CurrentVersion - 1).ToString())));
        Assert.False(LegalPolicy.NeedsAcceptance(User(LegalPolicy.CurrentVersion.ToString())));
        Assert.False(LegalPolicy.NeedsAcceptance(new ClaimsPrincipal(new ClaimsIdentity()))); // chưa đăng nhập
    }

    [Fact]
    public void CacTrangKhongCanDongYVanMoDuoc()
    {
        // Chỉ các trang này được mở khi chưa đồng ý: đọc chính sách/trợ giúp, đồng ý, đăng xuất, xóa tài khoản (không đồng ý bản mới), trang lỗi.
        var open = typeof(LearnController).Assembly.GetTypes().Where(t => typeof(Controller).IsAssignableFrom(t))
            .SelectMany(t => t.GetMethods().Where(m => m.IsDefined(typeof(AllowWithoutPolicyAttribute))).Select(m => t.Name.Replace("Controller", "") + "/" + m.Name))
            .Distinct().OrderBy(x => x).ToArray();
        Assert.Equal(new[] { "Account/AcceptPolicy", "Account/DeleteAccount", "Account/DeleteAccountConfirm", "Account/Logout", "Home/Error", "Home/Help", "Home/Privacy", "Home/Terms" }, open);
    }

    [Fact]
    public void ThongTinChuDuAnLayTuCauHinh()
    {
        var blank = LegalInfo.From(new ConfigurationBuilder().Build());
        Assert.Equal("[ĐIỀN: họ tên]", blank.OwnerName); Assert.Equal("[ĐIỀN: email liên hệ]", blank.ContactEmail);
        Assert.Equal("[ĐIỀN: ngày hiệu lực]", blank.EffectiveDate); Assert.Equal("[ĐIỀN: tên miền]", blank.Domain);
        var set = LegalInfo.From(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Legal:OwnerName"] = "Nguyễn A", ["Legal:ContactEmail"] = "lienhe@mindsprint.example",
            ["Legal:EffectiveDate"] = "01/12/2026", ["App:BaseUrl"] = "https://mindsprint.example/",
        }).Build());
        Assert.Equal(new LegalInfo("Nguyễn A", "lienhe@mindsprint.example", "01/12/2026", "mindsprint.example"), set);
    }

    [Fact]
    public async Task TrangChinhSachVaDieuKhoanHienDungChu()
    {
        var info = new LegalInfo("Nguyễn A", "lienhe@mindsprint.example", "01/12/2026", "mindsprint.example");
        var privacy = Html(await Tang4ViewTests.Render("Home", "Privacy", info));
        foreach (var text in new[] { "Chính sách quyền riêng tư", "MindSprint AI (mindsprint.example) là trang ôn tập miễn phí do Nguyễn A làm",
            "lienhe@mindsprint.example", "Cloudflare (đường hầm đưa trang web ra Internet)", "Cloudflare Turnstile",
            "Gmail của Google", "Google Gemini", "thông tin cá nhân có trong file hay nội dung (tên, mã số, số điện thoại…) cho việc cải thiện này",
            "7 ngày, sau đó tự xóa", "tự xóa sau 6 tháng kể từ lần đăng nhập cuối", "Người dùng đã xóa", "từ đủ 16 tuổi",
            "báo cho bạn qua email trong vòng 72 giờ", "Có hiệu lực từ 01/12/2026" })
            Assert.Contains(text, privacy);
        Assert.DoesNotContain("Microsoft", privacy);
        var terms = Html(await Tang4ViewTests.Render("Home", "Terms", info));
        foreach (var text in new[] { "Điều khoản sử dụng", "chịu trách nhiệm trước pháp luật về tính trung thực",
            "Cho phép dùng nội dung để cải thiện MindSprint AI", "Phần nội dung này được giữ đến khi bạn xóa tài khoản",
            "Giới hạn trách nhiệm", "Trong phạm vi pháp luật cho phép", "Phần trên không áp dụng nếu thiệt hại do lỗi của MindSprint AI" })
            Assert.Contains(text, terms);
    }

    [Fact]
    public async Task CacODongYKhongTickSan()
    {
        var register = Html(await Tang4ViewTests.Render("Account", "Register", new RegisterModel()));
        Assert.Contains("Tôi từ đủ 16 tuổi, đã đọc và đồng ý với", register);
        Assert.Contains("Tôi đồng ý để MindSprint AI xử lý dữ liệu của tôi như", register);
        Assert.DoesNotContain("checked", register);
        var accept = Html(await Tang4ViewTests.Render("Account", "AcceptPolicy", new AcceptPolicyModel()));
        Assert.Contains("Hãy đọc và đồng ý với Điều khoản sử dụng", accept); Assert.Contains("Không đồng ý, đăng xuất", accept);
        Assert.DoesNotContain("checked", accept);
        var updated = Html(await Tang4ViewTests.Render("Account", "AcceptPolicy", new AcceptPolicyModel { Updated = true }));
        Assert.Contains("MindSprint AI vừa cập nhật Điều khoản sử dụng", updated);
        var join = Html(await Tang4ViewTests.Render("Classrooms", "Join", new object(), new() { ["GuestNameRequired"] = true, ["GuestName"] = "Hoa" }));
        Assert.Contains("Tôi từ đủ 16 tuổi. Tôi đồng ý để MindSprint AI lưu tên tôi nhập", join);
        Assert.Contains("name=\"acceptPolicy\"", join); Assert.Contains("value=\"Hoa\"", join); Assert.DoesNotContain("checked", join);
        var noGuest = Html(await Tang4ViewTests.Render("Classrooms", "Join", new object()));
        Assert.DoesNotContain("acceptPolicy", noGuest);
    }

    [Fact]
    public async Task CacDongThongBao()
    {
        Assert.Contains("MindSprint AI ghi nhận địa chỉ mạng (IP), vị trí ước tính", Html(await Tang4ViewTests.Render("Account", "Login", new LoginModel())));
        var ai = Html(await Tang4ViewTests.Render("Shared", "_AiAssistant", new AiPanelModel(1)));
        Assert.Contains(">Trợ lý AI<", ai); Assert.Contains("Câu trả lời do AI tạo ra nên có thể sai", ai);
        var account = Html(await Tang4ViewTests.Render("Account", "Index", new ApplicationUser(), new()
        { ["GeminiLastFour"] = "ABCD", ["GeminiNoticeAccepted"] = false, ["DeleteBlock"] = "Bạn đang là giảng viên của 2 lớp. Hãy chuyển lớp" }));
        Assert.Contains("Trợ lý AI dùng Google Gemini ở nước ngoài", account);
        Assert.Contains("id=\"gemini-notice\"", account);
        Assert.Contains("Tôi đã hiểu và đồng ý", account);
        Assert.Contains("Bạn đang là giảng viên của 2 lớp", account); Assert.Contains("Không khôi phục được.", account);
        Assert.DoesNotContain("Gửi thư xác nhận xóa tài khoản", account); // bị chặn thì không có form gửi thư
        var free = Html(await Tang4ViewTests.Render("Account", "Index", new ApplicationUser(), new() { ["GeminiNoticeAccepted"] = null }));
        Assert.Contains("Gửi thư xác nhận xóa tài khoản", free); Assert.DoesNotContain("id=\"gemini-notice\"", free);
        Assert.Contains("Google Gemini ở nước ngoài. Đừng đưa thông tin riêng tư",
            Html(File.ReadAllText(Path.Combine(Tang8PureTests.Root, "Views", "SourceTexts", "Edit.cshtml"))));
        Assert.Contains("Nếu không đồng ý, bạn hãy liên hệ giảng viên để được sắp xếp cách khác.",
            File.ReadAllText(Path.Combine(Tang8PureTests.Root, "Views", "Learn", "Consent.cshtml")));
    }
}

[Collection("db")]
public class PhapLyTests
{
    private static readonly IDataProtectionProvider Protection = new EphemeralDataProtectionProvider();
    private const string Key = "AIzaSyD-phaply-test-key-0123456789abcd";
    // UseAsync là internal: chỉ AiAssistantService gọi; test gọi qua reflection.
    private static Task<string> Use(UserApiKeyService keys, string user) => (Task<string>)typeof(UserApiKeyService)
        .GetMethod("UseAsync", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(keys, [user])!;

    private static AccountDeletionService Deletion(Harness h, TwoFactorIdentity i) =>
        new(h.Db, i.Users, i.Email, new AccountEmailService(h.Db, i.Users, i.Email, h.Clock, SecurityRound1Fixtures.Links), h.Clock);

    private static string LinkOf(string link, string name) =>
        Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(new Uri(link).Query)[name]!;

    private static async Task<(string userId, string token, string nonce)> RequestAsync(Harness h, TwoFactorIdentity i, string userId)
    {
        string? token = null, nonce = null;
        var link = await Deletion(h, i).RequestAsync(userId, (u, t, n) => { token = t; nonce = n; return "https://rekal.test/x"; });
        Assert.NotNull(link);
        return (userId, token!, nonce!);
    }

    [Fact]
    public async Task CongChanNguoiChuaDongYVaMoNgaySauKhiDongY()
    {
        using var h = new Harness();
        var id = await h.UserAsync("Chưa đồng ý");
        var filter = new PolicyGateFilter(h.Db);
        AuthorizationFilterContext Context(string method, string accept, string action = "Index")
        {
            var http = new DefaultHttpContext { User = new(new ClaimsIdentity([new(ClaimTypes.NameIdentifier, id)], "test")) };
            http.Request.Method = method; http.Request.Path = "/Learn"; http.Request.Headers.Accept = accept;
            var descriptor = new ControllerActionDescriptor { MethodInfo = typeof(LearnController).GetMethod(action)! };
            if (action == "AcceptPolicy") descriptor.MethodInfo = typeof(AccountController).GetMethods().First(m => m.Name == "AcceptPolicy");
            return new(new ActionContext(http, new RouteData(), descriptor), []);
        }
        var page = Context("GET", "text/html");
        await filter.OnAuthorizationAsync(page);
        Assert.Equal("AcceptPolicy", Assert.IsType<RedirectToActionResult>(page.Result).ActionName);
        // Việc đang làm dở (gửi câu trả lời, nộp bài) không bị cắt ngang: chỉ chặn lúc mở trang.
        var ajax = Context("POST", "application/json");
        await filter.OnAuthorizationAsync(ajax);
        Assert.Null(ajax.Result);
        var allowed = Context("GET", "text/html", "AcceptPolicy");
        await filter.OnAuthorizationAsync(allowed);
        Assert.Null(allowed.Result);

        // Vừa đồng ý: claim còn cũ nhưng DB đã ghi nên cổng mở ngay.
        await h.Db.Users.Where(u => u.Id == id).ExecuteUpdateAsync(s => s.SetProperty(u => u.PolicyVersion, LegalPolicy.CurrentVersion));
        var after = Context("GET", "text/html");
        await filter.OnAuthorizationAsync(after);
        Assert.Null(after.Result);
    }

    [Fact]
    public async Task KhachMoiGhiLaiLucDongY()
    {
        using var h = new Harness();
        var s = await h.ScenarioAsync();
        await h.Db.Assignments.Where(a => a.Id == s.Assignment.Id).ExecuteUpdateAsync(x => x.SetProperty(a => a.StrangerPolicy, StrangerPolicy.Allow));
        var guest = await new GuestService(h.Db, h.Clock, h.Notifier).JoinAsync(s.Assignment.Id, "Hoa", GuestService.NewToken());
        var user = await h.Db.Users.AsNoTracking().SingleAsync(u => u.Id == guest.UserId);
        Assert.Equal(LegalPolicy.CurrentVersion, user.PolicyVersion); Assert.Equal(h.Clock.UtcNow, user.PolicyAcceptedAt);
    }

    [Fact]
    public async Task C5_KhongDongYThiKhongLuuKeyVaKeyCuKhongGuiGemini()
    {
        using var h = new Harness();
        var user = await h.UserAsync("An");
        var keys = new UserApiKeyService(h.Db, Protection, h.Clock);
        await Assert.ThrowsAsync<BusinessException>(() => keys.SaveAsync(Key, user, false));
        Assert.Null(await keys.NoticeAcceptedAsync(user));
        await keys.SaveAsync(Key, user, true);
        Assert.True(await keys.NoticeAcceptedAsync(user));
        Assert.Equal(Key, await Use(keys, user));

        // Key lưu trước khi có thông báo.
        await h.Db.UserApiKeys.Where(k => k.UserId == user).ExecuteUpdateAsync(s => s.SetProperty(k => k.AiNoticeAcceptedAt, (DateTime?)null));
        h.Db.ChangeTracker.Clear();
        Assert.False(await keys.NoticeAcceptedAsync(user));
        var e = await Assert.ThrowsAsync<BusinessException>(() => Use(keys, user));
        Assert.Equal(UserApiKeyService.AiNoticeMissing, e.Message);
        await Assert.ThrowsAsync<BusinessException>(() => keys.AcceptNoticeAsync(user, false));
        await keys.AcceptNoticeAsync(user, true);
        Assert.Equal(Key, await Use(keys, user));
    }

    [Fact]
    public async Task C9_DieuKienChanXoaTaiKhoan()
    {
        using var h = new Harness();
        using var i = new TwoFactorIdentity(h);
        var s = await h.ScenarioAsync();
        var deletion = Deletion(h, i);
        Assert.StartsWith("Bạn đang là giảng viên của 1 lớp.", await deletion.BlockReasonAsync(s.Lecturer));
        await Assert.ThrowsAsync<BusinessException>(() => deletion.RequestAsync(s.Lecturer, (_, _, _) => ""));
        Assert.Null(await deletion.BlockReasonAsync(s.Student));
        await h.Attempts.StartAsync(s.Assignment.Id, s.Student);
        Assert.Equal("Bạn đang có bài làm dở. Hãy nộp bài trước khi xóa tài khoản.", await deletion.BlockReasonAsync(s.Student));

        var admin = await h.UserAsync("Quản trị duy nhất");
        await h.Db.Users.Where(u => u.IsSystemAdmin).ExecuteUpdateAsync(x => x.SetProperty(u => u.IsSystemAdmin, false));
        await h.Db.Users.Where(u => u.Id == admin).ExecuteUpdateAsync(x => x.SetProperty(u => u.IsSystemAdmin, true));
        Assert.Equal("Bạn là quản trị viên duy nhất nên chưa xóa được tài khoản.", await deletion.BlockReasonAsync(admin));
        await Assert.ThrowsAsync<BusinessException>(() => deletion.DeleteByAdminAsync(admin, "tự xóa", admin));
    }

    [Fact]
    public async Task C9_BoHocDangDuocLopKhacDungThiChan()
    {
        using var h = new Harness();
        using var i = new TwoFactorIdentity(h);
        var s = await h.ScenarioAsync();
        // Lớp được chuyển cho giảng viên khác nhưng bài giao vẫn dùng bộ học của người cũ.
        var other = await h.UserAsync("GV mới");
        await h.Db.ClassMembers.Where(m => m.ClassroomId == s.Room.Id && m.UserId == s.Lecturer)
            .ExecuteUpdateAsync(x => x.SetProperty(m => m.UserId, other));
        await h.Db.Assignments.Where(a => a.Id == s.Assignment.Id).ExecuteUpdateAsync(x => x.SetProperty(a => a.CreatedById, other));
        Assert.Equal("Bộ học của bạn đang được dùng trong lớp của giảng viên khác. Hãy nhờ họ gỡ bài giao đó trước khi xóa tài khoản.",
            await Deletion(h, i).BlockReasonAsync(s.Lecturer));
    }

    [Fact]
    public async Task C9_TuXoaQuaLinkEmail_AnDanhHoaVaGiuDiem()
    {
        using var h = new Harness();
        using var i = new TwoFactorIdentity(h);
        var s = await h.ScenarioAsync();
        var attempt = await h.Attempts.StartAsync(s.Assignment.Id, s.Student);
        await h.Attempts.FinishAsync(attempt.Id, s.Student);
        var keys = new UserApiKeyService(h.Db, Protection, h.Clock);
        await keys.SaveAsync(Key, s.Student, true);
        h.Db.KnownDevices.Add(new() { UserId = s.Student, DeviceHash = "d", Label = "Chrome", FirstSeenAt = h.Clock.UtcNow, LastSeenAt = h.Clock.UtcNow, LastSignInIp = "1.2.3.4" });
        h.Db.Notifications.Add(new() { UserId = s.Student, Type = NotificationType.DeviceChange, PayloadJson = "{}", CreatedAt = h.Clock.UtcNow });
        await h.Db.SaveChangesAsync();
        var set = await h.Library.CreateStudySetAsync("Bộ riêng", s.Student);
        var email = (await h.Db.Users.AsNoTracking().SingleAsync(u => u.Id == s.Student)).Email;

        var (userId, token, nonce) = await RequestAsync(h, i, s.Student);
        Assert.Contains("Xác nhận xóa tài khoản", i.Email.Subjects.Last());
        Assert.Contains("trong vòng 1 giờ", i.Email.Messages.Last());
        Assert.Equal(email, await Deletion(h, i).CheckLinkAsync(userId, token, nonce));
        Assert.Null(await Deletion(h, i).CheckLinkAsync(userId, token, "khac"));
        var other = await h.UserAsync("NguoiKhac"); // không bị chặn, để ca này thật sự kiểm token
        Assert.Null(await Deletion(h, i).BlockReasonAsync(other));
        Assert.Equal(AccountDeletionService.InvalidLink,
            (await Assert.ThrowsAsync<BusinessException>(() => Deletion(h, i).ConfirmAsync(other, token, nonce))).Message);
        Assert.Null((await h.Db.Users.AsNoTracking().SingleAsync(u => u.Id == other)).DeletedAt);

        await Deletion(h, i).ConfirmAsync(userId, token, nonce);
        h.Db.ChangeTracker.Clear();
        var user = await h.Db.Users.SingleAsync(u => u.Id == s.Student);
        Assert.Equal(AccountDeletionService.DeletedName, user.DisplayName);
        Assert.Null(user.Email); Assert.Null(user.NormalizedEmail); Assert.Null(user.PasswordHash);
        Assert.NotNull(user.DeletedAt); Assert.Equal(DateTimeOffset.MaxValue, user.LockoutEnd);
        Assert.False(await h.Db.UserApiKeys.AnyAsync(k => k.UserId == s.Student));
        Assert.False(await h.Db.KnownDevices.AnyAsync(k => k.UserId == s.Student));
        Assert.False(await h.Db.Notifications.AnyAsync(k => k.UserId == s.Student));
        Assert.False(await h.Db.UserTokens.AnyAsync(k => k.UserId == s.Student));
        Assert.NotNull((await h.Db.StudySets.SingleAsync(x => x.Id == set.Id)).DeletedAt);
        Assert.True(await h.Db.Attempts.AnyAsync(a => a.Id == attempt.Id)); // điểm vẫn còn trong bảng điểm
        Assert.Equal("Người dùng tự xóa tài khoản", (await h.Db.AccountLocks.SingleAsync(l => l.UserId == s.Student)).Reason);

        // Link dùng lại bị từ chối; email cũ đăng ký lại được tài khoản mới.
        var reuse = await Assert.ThrowsAsync<BusinessException>(() => Deletion(h, i).ConfirmAsync(userId, token, nonce));
        Assert.Equal(AccountDeletionService.InvalidLink, reuse.Message);
        Assert.Null(await i.Users.FindByEmailAsync(email!));
    }

    [Fact]
    public async Task C9_LinkHetHanSau1Gio()
    {
        using var h = new Harness();
        using var i = new TwoFactorIdentity(h);
        var user = await h.UserAsync("HetHan");
        var (userId, token, nonce) = await RequestAsync(h, i, user);
        h.Clock.UtcNow = h.Clock.UtcNow.AddMinutes(61);
        Assert.Null(await Deletion(h, i).CheckLinkAsync(userId, token, nonce));
        await Assert.ThrowsAsync<BusinessException>(() => Deletion(h, i).ConfirmAsync(userId, token, nonce));
        Assert.Null((await h.Db.Users.AsNoTracking().SingleAsync(u => u.Id == user)).DeletedAt);
    }

    [Fact]
    public async Task C9_QuanTriVienXoaThayBatBuocLyDo()
    {
        using var h = new Harness();
        using var i = new TwoFactorIdentity(h);
        var admin = await h.UserAsync("Admin");
        await h.Db.Users.Where(u => u.Id == admin).ExecuteUpdateAsync(x => x.SetProperty(u => u.IsSystemAdmin, true));
        var target = await h.UserAsync("NguoiYeuCau");
        var deletion = Deletion(h, i);
        await Assert.ThrowsAsync<BusinessException>(() => deletion.DeleteByAdminAsync(target, " ", admin));
        await Assert.ThrowsAsync<ForbiddenException>(() => deletion.DeleteByAdminAsync(admin, "x", target)); // không phải quản trị viên
        await deletion.DeleteByAdminAsync(target, "Email yêu cầu ngày 12/11", admin);
        var row = await h.Db.AccountLocks.AsNoTracking().SingleAsync(l => l.UserId == target);
        Assert.Equal("Quản trị viên xóa tài khoản theo yêu cầu: Email yêu cầu ngày 12/11", row.Reason);
        Assert.Equal(admin, row.LockedById);
        Assert.Empty(i.Email.Messages);
    }

    [Fact]
    public async Task DonDuLieuCu_May6ThangVaDangKyDo7Ngay()
    {
        using var h = new Harness();
        var now = h.Clock.UtcNow;
        var old = await h.UserAsync("Đăng ký dở cũ");
        var fresh = await h.UserAsync("Đăng ký dở mới");
        var withData = await h.UserAsync("Đăng ký dở có dữ liệu");
        var confirmed = await h.UserAsync("Đã xác minh");
        await h.Db.Users.Where(u => u.Id == old || u.Id == withData || u.Id == confirmed)
            .ExecuteUpdateAsync(x => x.SetProperty(u => u.CreatedAt, now.AddDays(-8)));
        await h.Db.Users.Where(u => u.Id == confirmed).ExecuteUpdateAsync(x => x.SetProperty(u => u.EmailConfirmed, true));
        await h.Library.CreateStudySetAsync("Có dữ liệu", withData);
        var roleTouched = await h.UserAsync("Từng được cấp quyền"); // khóa ngoại Restrict không được làm hỏng cả lần dọn
        await h.Db.Users.Where(u => u.Id == roleTouched).ExecuteUpdateAsync(x => x.SetProperty(u => u.CreatedAt, now.AddDays(-8)));
        h.Db.AdminRoleChanges.Add(new() { AdminId = confirmed, TargetUserId = roleTouched, Granted = true, Reason = "thử", At = now });
        var active = await h.UserAsync("Có máy");
        h.Db.KnownDevices.Add(new() { UserId = active, DeviceHash = "cu", Label = "Cũ", FirstSeenAt = now.AddDays(-400), LastSeenAt = now.AddDays(-184) });
        h.Db.KnownDevices.Add(new() { UserId = active, DeviceHash = "moi", Label = "Mới", FirstSeenAt = now.AddDays(-400), LastSeenAt = now.AddDays(-10) });
        await h.Db.SaveChangesAsync();

        await RetentionCleanup.RunAsync(h.Db, now);
        var left = await h.Db.Users.Where(u => new[] { old, fresh, withData, confirmed }.Contains(u.Id)).Select(u => u.Id).ToListAsync();
        Assert.DoesNotContain(old, left);
        Assert.Contains(fresh, left); Assert.Contains(withData, left); Assert.Contains(confirmed, left);
        Assert.True(await h.Db.Users.AnyAsync(u => u.Id == roleTouched));
        Assert.Equal(new[] { "moi" }, await h.Db.KnownDevices.Where(d => d.UserId == active).Select(d => d.DeviceHash).ToListAsync());
    }

    [Fact]
    public async Task C3_KhachCuVaoBaiMoiPhaiDongYLai()
    {
        using var h = new Harness();
        var s = await h.ScenarioAsync();
        h.Clock.UtcNow = h.Clock.UtcNow.AddMinutes(1);
        var second = await h.Assignments.CreatePracticeAsync(s.Room.Id, s.Quiz.Id, new PracticeSettings(null, h.Clock.UtcNow.AddDays(2), 3), s.Lecturer);
        await h.Db.Assignments.Where(a => a.Id == s.Assignment.Id || a.Id == second.Id)
            .ExecuteUpdateAsync(x => x.SetProperty(a => a.StrangerPolicy, StrangerPolicy.Allow));
        var guests = new GuestService(h.Db, h.Clock, h.Notifier);
        var token = GuestService.NewToken();
        Assert.True(await guests.NeedsConsentAsync(s.Assignment.Id, token)); // khách mới
        var guest = await guests.JoinAsync(s.Assignment.Id, "Hoa", token);
        Assert.False(await guests.NeedsConsentAsync(s.Assignment.Id, token)); // quay lại đúng bài cũ
        Assert.True(await guests.NeedsConsentAsync(second.Id, token));       // bài mới: giảng viên của bài này phải được đồng ý riêng
        h.Clock.UtcNow = h.Clock.UtcNow.AddMinutes(5);
        await guests.JoinAsync(second.Id, null, token);
        h.Db.ChangeTracker.Clear();
        Assert.Equal(h.Clock.UtcNow, (await h.Db.Users.SingleAsync(u => u.Id == guest.UserId)).PolicyAcceptedAt);
    }

    [Fact]
    public async Task C1_ChuEmailDatMatKhauQuaLinkMoiGhiLanDongY()
    {
        using var h = new Harness();
        using var i = new TwoFactorIdentity(h);
        var user = new ApplicationUser { UserName = Guid.NewGuid() + "@test.vn", DisplayName = "Chờ xác minh" };
        user.Email = user.UserName;
        Assert.True((await i.Users.CreateAsync(user)).Succeeded);
        Assert.Null(user.PolicyVersion); // gõ form Đăng ký chưa tính là chủ email đồng ý
        var token = await i.Users.GenerateEmailConfirmationTokenAsync(user);
        await SecurityRound1Fixtures.Emails(h, i.Users, i.Email).SetPasswordAsync(user.Id, token, "Strong123!x");
        using var fresh = DatabaseFixture.NewContext();
        var saved = (await fresh.Users.FindAsync(user.Id))!;
        Assert.Equal(LegalPolicy.CurrentVersion, saved.PolicyVersion); Assert.Equal(h.Clock.UtcNow, saved.PolicyAcceptedAt);
    }

    [Fact]
    public async Task C9_XoaRoiLopVaXoaDauVetConLai()
    {
        using var h = new Harness();
        using var i = new TwoFactorIdentity(h);
        var s = await h.ScenarioAsync();
        await h.Db.Assignments.Where(a => a.Id == s.Assignment.Id).ExecuteUpdateAsync(x => x.SetProperty(a => a.StrangerPolicy, StrangerPolicy.Allow));
        // Khách đã gộp vào tài khoản học viên (bài Kiểm tra giữ cả dòng khách).
        var guest = await new GuestService(h.Db, h.Clock, h.Notifier).JoinAsync(s.Assignment.Id, "Tên thật của Minh", GuestService.NewToken());
        await h.Db.Guests.Where(g => g.Id == guest.Id).ExecuteUpdateAsync(x => x.SetProperty(g => g.MergedIntoUserId, s.Student));
        var student = await h.Db.Users.AsNoTracking().SingleAsync(u => u.Id == s.Student);
        h.Db.Invitations.Add(new() { Kind = InvitationKind.Assistant, ClassroomId = s.Room.Id, InviterId = s.Lecturer, Email = student.Email!,
            NormalizedEmail = student.NormalizedEmail ?? student.Email!.ToUpperInvariant(), Status = InvitationStatus.Accepted,
            RecipientId = s.Student, CreatedAt = h.Clock.UtcNow, ExpiresAt = h.Clock.UtcNow.AddDays(7) });
        var admin = await h.UserAsync("Admin");
        await h.Db.Users.Where(u => u.Id == admin).ExecuteUpdateAsync(x => x.SetProperty(u => u.IsSystemAdmin, true));
        await h.Db.SaveChangesAsync();

        await Deletion(h, i).DeleteByAdminAsync(s.Student, "Email yêu cầu", admin);
        h.Db.ChangeTracker.Clear();
        Assert.Equal(ClassMemberStatus.Left, (await h.Db.ClassMembers.SingleAsync(m => m.UserId == s.Student && m.ClassroomId == s.Room.Id)).Status);
        var user = await h.Db.Users.SingleAsync(u => u.Id == s.Student);
        Assert.False(user.SrsBellEnabled); Assert.False(user.EmailRemindersEnabled);
        Assert.Equal(AccountDeletionService.DeletedName, (await h.Db.Guests.SingleAsync(g => g.Id == guest.Id)).DisplayName);
        Assert.Equal(AccountDeletionService.DeletedName, (await h.Db.Users.SingleAsync(u => u.Id == guest.UserId)).DisplayName);
        Assert.All(await h.Db.Participants.Where(p => p.GuestId == guest.Id).ToListAsync(),
            p => Assert.Equal(AccountDeletionService.DeletedName, p.GuestDisplayName));
        Assert.Equal("", (await h.Db.Invitations.SingleAsync(x => x.RecipientId == s.Student)).Email);
    }

    [Fact]
    public async Task C9_HetLuotThuThiBaoThatLaChuaGui()
    {
        using var h = new Harness();
        using var i = new TwoFactorIdentity(h);
        var user = await h.UserAsync("HetLuot");
        await RequestAsync(h, i, user);
        var e = await Assert.ThrowsAsync<BusinessException>(() => Deletion(h, i).RequestAsync(user, (_, _, _) => "x"));
        Assert.StartsWith("Chưa gửi được thư lúc này", e.Message);
        Assert.Single(i.Email.Messages);
    }

    [Fact]
    public async Task C9_QuanTriVienDocCauChanChoNguoiKhac()
    {
        using var h = new Harness();
        using var i = new TwoFactorIdentity(h);
        var s = await h.ScenarioAsync();
        Assert.StartsWith("Người này đang là giảng viên của 1 lớp.", await Deletion(h, i).BlockReasonAsync(s.Lecturer, byAdmin: true));
    }
}
