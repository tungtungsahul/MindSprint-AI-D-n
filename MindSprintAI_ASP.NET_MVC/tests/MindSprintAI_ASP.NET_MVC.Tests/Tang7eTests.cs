using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MindSprintAI_ASP.NET_MVC.Controllers;
using MindSprintAI_ASP.NET_MVC.Data;
using MindSprintAI_ASP.NET_MVC.Data.Entities;
using MindSprintAI_ASP.NET_MVC.Models;
using MindSprintAI_ASP.NET_MVC.Services;

namespace MindSprintAI_ASP.NET_MVC.Tests;

public class Tang7ePureTests
{
    internal const string Signals = "{\"screen\":\"1920x1080x24\",\"timezone\":\"Asia/Bangkok\",\"language\":\"vi\",\"platform\":\"Win32\",\"cores\":\"8\",\"memory\":\"8\",\"canvas\":\"c\",\"webgl\":\"w\",\"audio\":\"a\",\"fonts\":\"f\"}";
    internal static DeviceSignalService SignalService(TimeProvider? clock = null, byte value = 1)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["DeviceSignals:HmacKey"] = Convert.ToBase64String(Enumerable.Repeat(value, 32).ToArray()) }).Build();
        var protection = new EphemeralDataProtectionProvider();
        return new(new(config, new GeoEnvironment(), protection), protection, clock ?? TimeProvider.System);
    }

    [Fact]
    public void F7_HmacOnDinhChuanHoaThuTuVaKhacKhoa()
    {
        var service = SignalService(); var first = service.Read(Signals, IPAddress.Parse("192.168.1.4"), true);
        var reverse = JsonSerializer.Serialize(JsonSerializer.Deserialize<Dictionary<string, string>>(Signals)!.Reverse().ToDictionary());
        Assert.Equal(first, service.Read(reverse, IPAddress.Parse("192.168.1.99"), true));
        Assert.NotEqual(first.LightHash, SignalService(value: 2).Read(Signals, null, true).LightHash);
        Assert.Equal(64, first.LightHash!.Length); Assert.Equal(64, first.FullHash!.Length);
        Assert.NotEqual(first.LightHash, first.FullHash);
        Assert.Null(service.Read(Signals, null, false).FullHash);
    }

    [Theory]
    [InlineData(null), InlineData(""), InlineData("{}"), InlineData("{broken"), InlineData("[]"), InlineData("null")]
    public void F7_ThieuHoacLoiLaChuaBiet(string? json)
    {
        var hint = SignalService().Read(json, null, true);
        Assert.Null(hint.LightHash); Assert.Null(hint.FullHash);
    }

    [Fact]
    public void F7_ChiLuuKetQuaKhongCoCotThongSoGoc()
    {
        using var db = DatabaseFixture.NewContext();
        foreach (var type in new[] { typeof(Attempt), typeof(KnownDevice), typeof(DeviceChangeRequest) })
        {
            var entity = db.Model.FindEntityType(type)!;
            Assert.DoesNotContain(entity.GetProperties(), p => Regex.IsMatch(p.Name, "Raw|Signals|Canvas|WebGl|Fonts|Audio", RegexOptions.IgnoreCase));
            Assert.Equal(64, entity.FindProperty("LightHash")!.GetMaxLength());
        }
        Assert.Null(SignalService().Read(new string('a', 16001), null, true).LightHash);
    }

    [Fact]
    public void F9_KhoaKhongDocDuocThiChuaBietKhongChanLamBai()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["DeviceSignals:HmacKey"] = "invalid-base64" }).Build();
        var protection = new EphemeralDataProtectionProvider();
        var service = new DeviceSignalService(new(config, new GeoEnvironment(), protection), protection, TimeProvider.System);
        var result = service.Read(Signals, null, true);
        Assert.Null(result.LightHash); Assert.Null(result.FullHash);
    }

    [Fact]
    public async Task F7_KhoaSinhMotLanBaoVeVaTaiSuDungSauKhoiDong()
    {
        var directory = Path.Combine(Path.GetTempPath(), "mindsprint-7e-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var protectionPath = new DirectoryInfo(Path.Combine(directory, "protection"));
            var firstProvider = DataProtectionProvider.Create(protectionPath);
            var keys = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() => DeviceSignalKey.Load(null, directory, firstProvider))));
            Assert.All(keys, k => Assert.Equal(keys[0], k));
            Assert.Equal(keys[0], DeviceSignalKey.Load(null, directory, DataProtectionProvider.Create(protectionPath)));
            Assert.NotEqual(keys[0], File.ReadAllBytes(Path.Combine(directory, "device-signals.key")));
            Assert.Throws<InvalidOperationException>(() => DeviceSignalKey.Load(Convert.ToBase64String(new byte[31]), directory, firstProvider));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData("192.168.1.42", "192.168.1.0/24")]
    [InlineData("::ffff:192.168.1.99", "192.168.1.0/24")]
    [InlineData("2001:db8:1234:1::1", "20010DB81234/48")]
    public void F8_DaiMang(string address, string prefix) => Assert.Equal(prefix, DeviceSignalService.Prefix(IPAddress.Parse(address)));

    [Theory]
    [InlineData("a", "a", "net", "net", "Có vẻ vẫn là thiết bị cũ của học viên")]
    [InlineData("a", "b", "net", "net", "Đây là một thiết bị khác")]
    [InlineData("a", "a", "net", "other", "Cùng loại thiết bị nhưng khác mạng")]
    [InlineData(null, "a", "net", "net", null)]
    [InlineData("a", "a", null, "net", null)]
    public void F8c_GhiChuChinhXacVaChuaBiet(string? oldHash, string? newHash, string? oldNetwork, string? newNetwork, string? title)
    {
        var note = DeviceHintRules.Note(oldHash, null, oldNetwork, newHash, null, newNetwork);
        Assert.Equal(title, note?.Title);
        if (note != null) Assert.False(string.IsNullOrWhiteSpace(note.Detail));
    }

    [Fact]
    public void F8c_UuTienDayDuNeuCoCaHaiNeuKhongDungMucNhe()
    {
        Assert.Equal("Đây là một thiết bị khác", DeviceHintRules.Note("same", "one", "net", "same", "two", "net")!.Title);
        Assert.Equal("Có vẻ vẫn là thiết bị cũ của học viên", DeviceHintRules.Note("same", "one", "net", "same", null, "net")!.Title);
    }

    [Theory]
    [InlineData(1, false), InlineData(2, true), InlineData(3, true)]
    public void H5_BienHaiGiay(int seconds, bool counted)
    {
        var now = DateTime.UtcNow; var leave = new ScreenLeave { Kind = ScreenLeaveKind.Hidden, StartedAt = now };
        ScreenMonitoringRules.Update(leave, now.AddSeconds(seconds), true);
        Assert.Equal(counted, leave.Counted); Assert.Equal(seconds, leave.DurationSeconds);
        ScreenMonitoringRules.Update(leave, now.AddHours(1), true); Assert.Equal(seconds, leave.DurationSeconds);
    }

    [Fact]
    public void H8_MatTinHieuKhongTinhVaBannerDungNguyenVan()
    {
        var now = DateTime.UtcNow; var loss = new ScreenLeave { Kind = ScreenLeaveKind.SignalLoss, StartedAt = now };
        ScreenMonitoringRules.Update(loss, now.AddMinutes(5), true); Assert.False(loss.Counted);
        Assert.Equal("Bạn vừa rời màn hình làm bài 12 giây. Giảng viên đã được báo. Còn 2 lần nữa bài sẽ tự nộp.", ScreenMonitoringRules.Banner(12, 1, 3));
        Assert.Equal("Bạn vừa rời màn hình làm bài 1 giây. Giảng viên đã được báo.", ScreenMonitoringRules.Banner(1, 0, null));
    }

    [Theory]
    [InlineData(0), InlineData(21)]
    public void H8_KiemTraNguongOMayChu(int n) => Assert.Throws<BusinessException>(() => AssignmentService.ApplyDeviceSettings(new(), false, true, n));

    [Fact]
    public void F1_H1_H8_PresetChiChuaTuyChonVaMacDinhTat()
    {
        var a = new Assignment(); Assert.False(a.DeviceLock); Assert.False(a.MonitorScreen); Assert.Null(a.AutoSubmitLeaves);
        AssignmentService.ApplyDeviceSettings(a, true, true, 3);
        var json = JsonSerializer.Serialize(AssignmentPreset.From(a));
        var input = SettingPresetRules.Assignment(json, AssignmentMode.Test).ToInput(1, AssignmentMode.Test);
        Assert.True(input.DeviceLock); Assert.True(input.MonitorScreen); Assert.Equal(3, input.AutoSubmitLeaves);
        Assert.DoesNotContain("QuizId", json); Assert.DoesNotContain("QuestionIds", json);
        Assert.Throws<BusinessException>(() => AssignmentService.ApplyDeviceSettings(a, true, false, 3));
    }


    [Fact]
    public async Task H3_LiveModelHienTongSoLanDangRoiVaKhongLoDuLieuMay()
    {
        var now = new DateTime(2026, 11, 8, 3, 42, 0, DateTimeKind.Utc);
        var a = Tang4RuleTests.Build(1); a.MonitorScreen = true;
        a.ScreenLeaves.Add(new() { Kind = ScreenLeaveKind.OtherWindow, StartedAt = now.AddSeconds(-12), Counted = true });
        var summary = ScreenMonitoringRules.Summary(a, now);
        Assert.Equal(1, summary.Count); Assert.Equal(12, summary.TotalSeconds); Assert.True(summary.Away); Assert.True(summary.Highlight);
        var html = WebUtility.HtmlDecode(await Tang4ViewTests.Render("Assignments", "_ScreenSummary", summary));
        Assert.Contains("Đang rời màn hình", html); Assert.Contains("Rời màn hình 1 lần", html); Assert.Contains("12 giây", html);
    }

    private static void FormMatches(string html, Type controller, string action)
    {
        var method = controller.GetMethods().Single(m => m.Name == action && m.IsDefined(typeof(HttpPostAttribute), true));
        var names = method.GetParameters().Where(p => !p.IsDefined(typeof(FromServicesAttribute)) && p.Name != "attemptId")
            .Select(p => p.Name!).ToArray();
        var forms = Regex.Matches(html, "<form\\b[^>]*>[\\s\\S]*?</form>").Select(m => m.Value).ToArray();
        var form = forms.FirstOrDefault(f => names.All(n => f.Contains($"name=\"{n}\"", StringComparison.OrdinalIgnoreCase)));
        Assert.NotNull(form); Assert.Contains("__RequestVerificationToken", form);
    }

    [Fact]
    public async Task MoiFormKhopThamSoVaEncodeLyDo()
    {
        var a = Tang4RuleTests.Build(1); a.Id = 7; a.Participant.Assignment.Id = 9; a.Participant.Assignment.DeviceLock = true;
        var consent = await Tang4ViewTests.Render("Learn", "Consent", new DeviceConsentModel(a.Participant.Assignment));
        FormMatches(consent, typeof(LearnController), "Start");
        var retake = await Tang4ViewTests.Render("Learn", "Consent", new DeviceConsentModel(a.Participant.Assignment, RetakeId: 4));
        FormMatches(retake, typeof(LearnController), "StartRetake");
        var takeover = await Tang4ViewTests.Render("Learn", "Takeover", a); FormMatches(takeover, typeof(LearnController), "Takeover");
        a.DeviceChanges.Add(new() { Id = 5, Status = DeviceChangeStatus.Pending });
        FormMatches(await Tang4ViewTests.Render("Learn", "Takeover", a), typeof(LearnController), "CancelDeviceChange");
        var card = await Tang4ViewTests.Render("Assignments", "_DeviceChange", new DeviceChangeCard(5, 7, "<img src=x>", DeviceHintRules.Note("a", null, "n", "a", null, "n")));
        FormMatches(card, typeof(AssignmentsController), "DecideDeviceChange"); Assert.DoesNotContain("<img src=x>", card);
        var details = await Tang4ViewTests.Render("Assignments", "Details", new AssignmentDetailsModel { Assignment = a.Participant.Assignment, Code = "123", OpenAttempts = 1, Rows = [] });
        FormMatches(details, typeof(AssignmentsController), "ChangeDeviceSettings");
        FormMatches(await Tang4ViewTests.Render("Shared", "_MachineHint", new object()), typeof(DeviceSignalsController), "Light");
    }

    [Fact]
    public async Task G0_QuetChuNguoiDungTrongTrangMoiVaThongBaoMoi()
    {
        // (G0, 7f) mở rộng tới mọi view/partial, email/chuông, lỗi nghiệp vụ, TempData, DisplayText và JS.
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var violations = PlainLanguageAudit.Violations(root);
        Assert.True(violations.Length == 0, string.Join(Environment.NewLine, violations));
        var a = Tang4RuleTests.Build(1); a.Participant.Assignment.DeviceLock = true; a.MonitorScreen = true;
        a.ScreenLeaves.Add(new() { Kind = ScreenLeaveKind.Hidden, StartedAt = DateTime.UtcNow, EndedAt = DateTime.UtcNow.AddSeconds(20), DurationSeconds = 20, Counted = true, ReportedOffline = true });
        var texts = new List<string>
        {
            await Tang4ViewTests.Render("Learn", "Consent", new DeviceConsentModel(a.Participant.Assignment)),
            await Tang4ViewTests.Render("Learn", "Takeover", a),
            await Tang4ViewTests.Render("Shared", "_DeviceSettings", new AssignmentInput()),
            await Tang4ViewTests.Render("Shared", "_MachineHint", new object()),
            await Tang4ViewTests.Render("Assignments", "_ScreenSummary", new ScreenSummary(1, 12, true, true, "10:42", 12, true, 2, 90, 1)),
            await Tang4ViewTests.Render("Assignments", "_ScreenLog", a),
            await Tang4ViewTests.Render("Assignments", "_DeviceChange", new DeviceChangeCard(1, 1, null, DeviceHintRules.Note("a", null, "n", "a", null, "n"))),
            ScreenMonitoringRules.Banner(12, 1, 3)!
        };
        // Phần nguồn vị trí theo giấy phép 7c giữ nguyên; quét toàn bộ câu cảnh báo mới dùng cho cả email/chuông.
        texts.Add(NewDeviceService.AlertText("Chrome trên Windows", DateTime.UtcNow, "192.168.1.2", "Hà Nội", false));
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../../Services/AttemptDeviceChanges.cs"));
        texts.AddRange(Regex.Matches(source, "BellLecturersAsync\\(a, NotificationType\\.[A-Za-z]+, \"([^\"]+)\"").Select(m => m.Groups[1].Value));
        source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../../Services/ScreenMonitoring.cs"));
        texts.AddRange(Regex.Matches(source, "BellLecturersAsync\\(a, NotificationType\\.[A-Za-z]+, \"([^\"]+)\"").Select(m => m.Groups[1].Value));
        foreach (var html in texts)
        {
            var visible = WebUtility.HtmlDecode(Regex.Replace(Regex.Replace(html, "<script\\b[^>]*>[\\s\\S]*?</script>", "", RegexOptions.IgnoreCase), "<[^>]*>", " "));
            Assert.DoesNotMatch(new Regex(@"\b(cookie|IP|token|session|SignalR|HMAC|hash|fingerprint|script)\b|vân tay|mã băm|tab focus", RegexOptions.IgnoreCase), visible);
        }
    }

    [Fact]
    public void F7b_KhongThuDayDuKhiBaiKhongBatTinhNang()
    {
        using var db = DatabaseFixture.NewContext(); var device = new TestDevice();
        var service = new AttemptService(db, TimeProvider.System, [new ChoiceGrader()], device, new NullLearnNotifier(), new());
        var assignment = new Assignment { Quiz = new() };
        var create = typeof(AttemptService).GetMethod("CreateAttempt", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var a = (Attempt)create.Invoke(service, [assignment, new Participant { Assignment = assignment }, null, true, new DeviceSignalResult("light", "full", "n")])!;
        Assert.Null(a.FullHash);
    }

    [Fact]
    public void F3_TuChoiQuyenTra403VaCacHanhDongGhiLaPost()
    {
        var http = new DefaultHttpContext(); http.Request.Headers.Accept = "application/json";
        var context = new ExceptionContext(new ActionContext(http, new RouteData(), new ActionDescriptor()), []) { Exception = new ForbiddenException() };
        new BusinessExceptionFilter(null!).OnException(context);
        Assert.Equal(403, Assert.IsType<ObjectResult>(context.Result).StatusCode);
        Assert.True(context.ExceptionHandled);
        foreach (var (controller, action) in new[]
        {
            (typeof(AssignmentsController), "DecideDeviceChange"), (typeof(AssignmentsController), "ChangeDeviceSettings"),
            (typeof(LearnController), "CancelDeviceChange"), (typeof(LearnController), "ScreenEvent"), (typeof(DeviceSignalsController), "Light")
        }) Assert.True(controller.GetMethod(action)!.IsDefined(typeof(HttpPostAttribute)));
    }
}

[Collection("db")]
public class Tang7eTests
{
    private static async Task<(Scenario S, Attempt A)> Start(Harness h, bool locked = true, bool monitor = false, int? limit = null)
    {
        var s = await h.ScenarioAsync();
        AssignmentService.ApplyDeviceSettings(s.Assignment, locked, monitor, limit); await h.Db.SaveChangesAsync();
        return (s, await h.Attempts.StartAsync(s.Assignment.Id, s.Student, true, new("old-light", "old-full", "192.168.1.0/24")));
    }

    [Fact]
    public async Task F1_TatKhoaVanChuyenNhuCu()
    {
        using var h = new Harness(); var (s, a) = await Start(h, false); var deadline = a.DeadlineAt;
        h.Device.Id = "new-machine"; await h.Attempts.TakeoverAsync(a.Id, s.Student);
        Assert.Equal(h.Device.Id, a.ActiveDeviceId); Assert.Empty(a.DeviceChanges); Assert.Equal(deadline, a.DeadlineAt);
    }

    [Fact]
    public async Task F2_KhoaChiTaoMotYeuCauKhongDoiMayVaTuHuyDuoc()
    {
        using var h = new Harness(); var (s, a) = await Start(h); var old = a.ActiveDeviceId; var deadline = a.DeadlineAt;
        // (F11) test chuông cũ phải chọn nhận, mặc định mới là tắt.
        (await h.Db.Users.FindAsync(s.Lecturer))!.DeviceChangeBellEnabled = true; await h.Db.SaveChangesAsync();
        h.Device.Id = "new-machine";
        await h.Attempts.TakeoverAsync(a.Id, s.Student, "hết pin", new("new-light", "new-full", "192.168.1.0/24"));
        await h.Attempts.TakeoverAsync(a.Id, s.Student); h.Device.Id = "third-machine"; await h.Attempts.TakeoverAsync(a.Id, s.Student);
        var r = Assert.Single(a.DeviceChanges); Assert.Equal(DeviceChangeStatus.Pending, r.Status);
        Assert.Equal(old, a.ActiveDeviceId); Assert.Equal(deadline, a.DeadlineAt); Assert.Equal("new-machine", r.RequestingDeviceId);
        Assert.Equal("new-full", r.FullHash); Assert.Equal("hết pin", r.Reason);
        Assert.Single(await h.Db.Notifications.Where(n => n.UserId == s.Lecturer && n.Type == NotificationType.DeviceChange).ToListAsync());
        await h.Attempts.CancelDeviceChangeAsync(a.Id, s.Student); Assert.Equal(DeviceChangeStatus.Cancelled, r.Status);
        await h.Attempts.TakeoverAsync(a.Id, s.Student); Assert.Equal(2, a.DeviceChanges.Count);
    }

    [Theory]
    [InlineData(true), InlineData(false)]
    public async Task F3_F4_DuyetDoiMayDayMayCuTuChoiGiuNguyen(bool approve)
    {
        using var h = new Harness(); var notices = new RecordingLearnNotifier(); h.Notifier = notices;
        var (s, a) = await Start(h); var old = a.ActiveDeviceId!; var deadline = a.DeadlineAt;
        h.Connections.Add("old-connection", a.Id, old); h.Device.Id = "new-machine";
        await h.Attempts.TakeoverAsync(a.Id, s.Student); var r = Assert.Single(a.DeviceChanges);
        await h.Attempts.DecideDeviceChangeAsync(a.Id, r.Id, approve, s.Lecturer);
        Assert.Equal(approve ? h.Device.Id : old, a.ActiveDeviceId); Assert.Equal(deadline, a.DeadlineAt);
        Assert.Equal(approve ? DeviceChangeStatus.Approved : DeviceChangeStatus.Denied, r.Status); Assert.Equal(s.Lecturer, r.DecidedById);
        Assert.Equal(approve, notices.MovedConnections.Contains("old-connection"));
        var state = await h.Attempts.DeviceChangeStateAsync(a.Id, s.Student);
        Assert.Equal(approve, state.Active); Assert.Equal(approve ? null : "Giảng viên chưa cho chuyển thiết bị", state.Message);
    }

    [Fact]
    public async Task F3_GvPhuDuocDuyetGvLopKhacBiCam()
    {
        using var h = new Harness(); var (s, a) = await Start(h); h.Device.Id = "new-machine";
        await h.Attempts.TakeoverAsync(a.Id, s.Student); var r = Assert.Single(a.DeviceChanges);
        var other = await h.ScenarioAsync();
        await Assert.ThrowsAsync<ForbiddenException>(() => h.Attempts.DecideDeviceChangeAsync(a.Id, r.Id, true, other.Lecturer));
        var assistant = await h.UserAsync("GV phụ");
        h.Db.ClassMembers.Add(new() { ClassroomId = s.Room.Id, UserId = assistant, Role = ClassRole.AssistantLecturer,
            Status = ClassMemberStatus.Active, JoinedAt = h.Clock.UtcNow, AddedAt = h.Clock.UtcNow }); await h.Db.SaveChangesAsync();
        await h.Attempts.DecideDeviceChangeAsync(a.Id, r.Id, true, assistant); Assert.Equal(DeviceChangeStatus.Approved, r.Status);
    }

    [Theory]
    [InlineData(false), InlineData(true)]
    public async Task F5_KetThucTuHuyYeuCau(bool expiry)
    {
        using var h = new Harness(); var (s, a) = await Start(h); var old = h.Device.Id;
        h.Device.Id = "new-machine"; await h.Attempts.TakeoverAsync(a.Id, s.Student); var r = Assert.Single(a.DeviceChanges);
        h.Device.Id = old;
        if (expiry) { h.Clock.UtcNow = a.DeadlineAt!.Value.AddSeconds(1); await h.Attempts.GetForLearnerAsync(a.Id, s.Student); }
        else await h.Attempts.FinishAsync(a.Id, s.Student);
        Assert.Equal(DeviceChangeStatus.Cancelled, r.Status); Assert.NotNull(r.DecidedAt);
    }

    [Fact]
    public async Task F2_DongThoiChiMotYeuCauDangCho()
    {
        using var h = new Harness(); var (s, a) = await Start(h);
        async Task Request(string machine)
        {
            await using var db = DatabaseFixture.NewContext();
            var service = new AttemptService(db, h.Clock, [new ChoiceGrader()], new TestDevice { Id = machine }, new NullLearnNotifier(), new());
            await service.TakeoverAsync(a.Id, s.Student);
        }
        await Task.WhenAll(Request("machine-b"), Request("machine-c"));
        Assert.Single(await h.Db.DeviceChangeRequests.Where(r => r.AttemptId == a.Id && r.Status == DeviceChangeStatus.Pending).ToListAsync());
        await h.Db.Entry(a).ReloadAsync(); Assert.Equal(h.Device.Id, a.ActiveDeviceId);
    }

    [Fact]
    public async Task F5_HuyLuotCungHuyYeuCau()
    {
        using var h = new Harness(); var (s, a) = await Start(h); h.Device.Id = "machine-b";
        await h.Attempts.TakeoverAsync(a.Id, s.Student);
        var r = Assert.Single(a.DeviceChanges);
        a.Status = AttemptStatus.Cancelled; a.CancelledAt = h.Clock.UtcNow; await h.Db.SaveChangesAsync();
        Assert.Equal(DeviceChangeStatus.Cancelled, r.Status); Assert.Equal(a.CancelledAt, r.DecidedAt);
    }

    [Fact]
    public async Task F6_KhachCungKhoaVaDongY()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(); s.Assignment.StrangerPolicy = StrangerPolicy.Allow;
        s.Assignment.DeviceLock = true; await h.Db.SaveChangesAsync();
        var guest = await new GuestService(h.Db, h.Clock, h.Notifier).JoinAsync(s.Assignment.Id, "Hoa", GuestService.NewToken());
        await Assert.ThrowsAsync<BusinessException>(() => h.Attempts.StartAsync(s.Assignment.Id, guest.UserId));
        var a = await h.Attempts.StartAsync(s.Assignment.Id, guest.UserId, true); var old = a.ActiveDeviceId;
        h.Device.Id = "new-machine"; await h.Attempts.TakeoverAsync(a.Id, guest.UserId);
        Assert.Equal(old, a.ActiveDeviceId); Assert.Single(a.DeviceChanges); Assert.True(a.MadeAsGuest);
    }

    [Fact]
    public async Task F7b_BatBuocDongYVaLuuTheoLuot()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(); s.Assignment.MonitorScreen = true; await h.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<BusinessException>(() => h.Attempts.StartAsync(s.Assignment.Id, s.Student));
        Assert.False(await h.Db.Attempts.AnyAsync(a => a.Participant.AssignmentId == s.Assignment.Id));
        var a = await h.Attempts.StartAsync(s.Assignment.Id, s.Student, true);
        Assert.Equal(h.Clock.UtcNow, a.DeviceConsentAt); Assert.Null(a.FullHash);
        Assert.Equal(a.Id, (await h.Attempts.StartAsync(s.Assignment.Id, s.Student)).Id);
    }

    [Fact]
    public async Task F7b_LuotLamLaiCungCanDongYRiêng()
    {
        using var h = new Harness(); var (s, a) = await Start(h);
        await h.Attempts.FinishAsync(a.Id, s.Student);
        s.Assignment.Mode = AssignmentMode.Test; s.Assignment.DurationMinutes = 10;
        var request = new RetakeRequest { ParticipantId = a.ParticipantId, Status = RetakeRequestStatus.Approved,
            WindowStartsAt = h.Clock.UtcNow, WindowEndsAt = h.Clock.UtcNow.AddHours(1) };
        h.Db.RetakeRequests.Add(request); await h.Db.SaveChangesAsync();
        var service = new RetakeService(h.Db, h.Assignments, h.Attempts, h.Clock);
        await Assert.ThrowsAsync<BusinessException>(() => service.StartAsync(request.Id, s.Student));
        h.Clock.UtcNow = h.Clock.UtcNow.AddSeconds(1);
        var retake = await service.StartAsync(request.Id, s.Student, true);
        Assert.NotEqual(a.Id, retake.Id); Assert.Equal(h.Clock.UtcNow, retake.DeviceConsentAt);
    }

    [Theory]
    [InlineData(0, false, true), InlineData(31, false, false), InlineData(0, true, false)]
    public async Task F8a_GopDungMayTrong30NgayVaGiuIdClaim(int daysOld, bool otherNetwork, bool reuse)
    {
        using var h = new Harness(); var userId = await h.UserAsync("Minh"); var user = (await h.Db.Users.FindAsync(userId))!;
        var serviceSignals = Tang7ePureTests.SignalService(h.Clock); var email = new Tang7cEmail();
        using var geo = new GeoIpLocation(new ConfigurationBuilder().Build(), new GeoEnvironment());
        var service = new NewDeviceService(h.Db, h.Clock, email, new(h.Db, h.Clock, h.Notifier), geo, NullLogger<NewDeviceService>.Instance, SecurityRound1Fixtures.Links, serviceSignals);
        var first = await service.SeenAsync(user, SignInContext(serviceSignals, 'a', "192.168.1.5")); Assert.NotNull(first);
        var row = await h.Db.KnownDevices.SingleAsync(d => d.Id == first); row.LastSeenAt = h.Clock.UtcNow.AddDays(-daysOld); await h.Db.SaveChangesAsync();
        var second = await service.SeenAsync(user, SignInContext(serviceSignals, 'b', otherNetwork ? "192.168.2.8" : "192.168.1.8"));
        Assert.Equal(reuse, first == second); Assert.Equal(reuse ? 0 : 1, email.Sent.Count);
        Assert.Equal(reuse ? 1 : 2, await h.Db.KnownDevices.CountAsync(d => d.UserId == userId));
        if (reuse) Assert.Equal(DeviceLabel.Hash(new string('b', 64)), row.DeviceHash);
    }

    private static DefaultHttpContext SignInContext(DeviceSignalService service, char device, string address)
    {
        var http = new DefaultHttpContext(); http.Request.Host = new("localhost"); http.Request.Scheme = "https";
        http.Connection.RemoteIpAddress = IPAddress.Parse(address); http.Items[LearnDevice.CookieName] = new string(device, 64);
        service.RememberLight(http, Tang7ePureTests.Signals);
        http.Request.Headers.Cookie = http.Response.Headers.SetCookie.ToString().Split(';')[0];
        return http;
    }

    [Theory]
    [InlineData(1, false), InlineData(2, true), InlineData(12, true)]
    public async Task H2_H5_ThoiGianMayChuKhongTinGioTuTrinhDuyet(int seconds, bool counted)
    {
        using var h = new Harness(); var (s, a) = await Start(h, false, true);
        await h.Attempts.ScreenEventAsync(a.Id, s.Student, "start", ScreenLeaveKind.Hidden, h.Clock.GetUtcNow().AddDays(-5));
        h.Clock.UtcNow = h.Clock.UtcNow.AddSeconds(seconds);
        var reply = await h.Attempts.ScreenEventAsync(a.Id, s.Student, "end", clientAt: h.Clock.GetUtcNow().AddDays(10));
        var leave = Assert.Single(a.ScreenLeaves); Assert.Equal(seconds, leave.DurationSeconds); Assert.Equal(counted, leave.Counted);
        Assert.Equal(counted ? 1 : 0, reply.Count); Assert.Contains($"{seconds} giây", reply.Banner!);
    }

    [Fact]
    public async Task H2_ChanLuotNguoiKhacMayKhongHoatDongVaTuGiaMatTinHieu()
    {
        using var h = new Harness(); var (s, a) = await Start(h, false, true); var stranger = await h.UserAsync("Khác");
        await Assert.ThrowsAsync<ForbiddenException>(() => h.Attempts.ScreenEventAsync(a.Id, stranger, "start", ScreenLeaveKind.Hidden));
        h.Device.Id = "other-machine";
        await Assert.ThrowsAsync<BusinessException>(() => h.Attempts.ScreenEventAsync(a.Id, s.Student, "start", ScreenLeaveKind.Hidden));
        await Assert.ThrowsAsync<BusinessException>(() => h.Attempts.ScreenEventAsync(a.Id, s.Student, "start", ScreenLeaveKind.SignalLoss));
        Assert.Empty(a.ScreenLeaves);
    }

    // Lối nhanh cho tín hiệu "vẫn đang làm" (13/11/2026, sau thử tải 650 khách): chỉ ghi "lần cuối thấy" khi không có gì phải xử lý.
    [Fact]
    public async Task H2_LoiNhanhChiGhiLanCuoiThayVaKhongTaoRoiManHinh()
    {
        using var h = new Harness(); var (s, a) = await Start(h, false, true);
        h.Clock.UtcNow = h.Clock.UtcNow.AddSeconds(10);
        var reply = await h.Attempts.ScreenEventAsync(a.Id, s.Student, "end");
        Assert.Equal(0, reply.Count); Assert.False(reply.Ended); Assert.Null(reply.Banner);
        using var fresh = new Harness();
        var saved = await fresh.Db.Attempts.Include(x => x.ScreenLeaves).SingleAsync(x => x.Id == a.Id);
        Assert.Equal(h.Clock.UtcNow, saved.MonitorHeartbeatAt);
        Assert.Equal(1, saved.MonitorEventCount);
        Assert.Empty(saved.ScreenLeaves);
    }

    [Fact]
    public async Task H2_ImLangQua45GiayVanGhiMatTinHieuQuaLoiDayDu()
    {
        using var h = new Harness(); var (s, a) = await Start(h, false, true);
        await h.Attempts.ScreenEventAsync(a.Id, s.Student, "end");
        h.Clock.UtcNow = h.Clock.UtcNow.AddSeconds(50); // tín hiệu kế tiếp đến muộn: lối nhanh phải nhường lối đầy đủ
        await h.Attempts.ScreenEventAsync(a.Id, s.Student, "end");
        using var fresh = new Harness();
        Assert.Single(await fresh.Db.ScreenLeaves.Where(l => l.AttemptId == a.Id && l.Kind == ScreenLeaveKind.SignalLoss).ToListAsync());
    }

    [Fact]
    public async Task H2_LanRoiDaTinhMaChuaChuongHayChuaTuNopThiKhongDiLoiNhanh()
    {
        // Tài khoản 2 rà 14/11/2026: GV duyệt chuyển máy đóng lần rời (đã tính) mà không chạy chuông/tự nộp.
        using var h = new Harness(); var (s, a) = await Start(h, false, true);
        await using (var db = DatabaseFixture.NewContext())
        {
            var x = await db.Attempts.SingleAsync(x => x.Id == a.Id);
            x.AutoSubmitLeaves = 1; x.LeaveBellSent = false;
            db.ScreenLeaves.Add(new() { AttemptId = a.Id, Kind = ScreenLeaveKind.Hidden, StartedAt = h.Clock.UtcNow, EndedAt = h.Clock.UtcNow.AddSeconds(5), DurationSeconds = 5, Counted = true });
            await db.SaveChangesAsync();
        }
        h.Clock.UtcNow = h.Clock.UtcNow.AddSeconds(10);
        var reply = await h.Attempts.ScreenEventAsync(a.Id, s.Student, "end");
        Assert.True(reply.Ended);
        using var fresh = new Harness();
        var saved = await fresh.Db.Attempts.SingleAsync(x => x.Id == a.Id);
        Assert.True(saved.LeaveBellSent);
        Assert.Equal(AttemptEndReason.ScreenLeaveLimit, saved.EndReason);
    }

    [Fact]
    public async Task H2_MayKhacKhongDuocGhiLanCuoiThay()
    {
        using var h = new Harness(); var (s, a) = await Start(h, true, true);
        var before = (await new Harness().Db.Attempts.SingleAsync(x => x.Id == a.Id)).MonitorHeartbeatAt;
        h.Device.Id = "may-la"; h.Clock.UtcNow = h.Clock.UtcNow.AddSeconds(10);
        await Assert.ThrowsAnyAsync<Exception>(() => h.Attempts.ScreenEventAsync(a.Id, s.Student, "end"));
        using var fresh = new Harness();
        Assert.Equal(before, (await fresh.Db.Attempts.SingleAsync(x => x.Id == a.Id)).MonitorHeartbeatAt);
    }

    [Fact]
    public async Task H10_LanRoiLucMatMangDuocTinhVaKepTrongKhoangMatTinHieu()
    {
        using var h = new Harness(); var (s, a) = await Start(h, false, true);
        await h.Attempts.ScreenEventAsync(a.Id, s.Student, "end");
        var lastSeen = h.Clock.UtcNow;
        h.Clock.UtcNow = h.Clock.UtcNow.AddSeconds(60);
        var reply = await h.Attempts.ScreenEventAsync(a.Id, s.Student, "offline", offline:
            [new(ScreenLeaveKind.Hidden, 50, 20), new(ScreenLeaveKind.OtherWindow, 9999, 9999)]);
        Assert.Equal(2, reply.Count); Assert.NotNull(reply.Banner);
        using var fresh = new Harness();
        var leaves = await fresh.Db.ScreenLeaves.Where(l => l.AttemptId == a.Id && l.ReportedOffline).OrderBy(l => l.Kind).ToListAsync();
        Assert.Equal(h.Clock.UtcNow.AddSeconds(-50), leaves[0].StartedAt); Assert.Equal(20, leaves[0].DurationSeconds);
        Assert.Equal(lastSeen, leaves[1].StartedAt); Assert.Equal(h.Clock.UtcNow, leaves[1].EndedAt); // giờ máy học viên bị kẹp
        // (H11) mất tín hiệu tính từ tín hiệu cuối, không bỏ 45 giây đầu.
        var loss = await fresh.Db.ScreenLeaves.SingleAsync(l => l.AttemptId == a.Id && l.Kind == ScreenLeaveKind.SignalLoss);
        Assert.Equal(60, loss.DurationSeconds);
        var summary = ScreenMonitoringRules.Summary(await fresh.Db.Attempts.Include(x => x.ScreenLeaves).SingleAsync(x => x.Id == a.Id), h.Clock.UtcNow);
        Assert.Equal((1, 60, 2), (summary.LossCount, summary.LossSeconds, summary.OfflineCount));
    }

    [Fact]
    public async Task H10_VanDangONgoaiThiMoLanRoiVaTuNopKhiDuN()
    {
        using var h = new Harness(); var (s, a) = await Start(h, false, true, limit: 1);
        h.Clock.UtcNow = h.Clock.UtcNow.AddSeconds(30);
        var reply = await h.Attempts.ScreenEventAsync(a.Id, s.Student, "offline", offline: [new(ScreenLeaveKind.Hidden, 20, null)]);
        Assert.True(reply.Ended); // đang ở ngoài 20 giây đã đủ 2 giây → lần thứ 1 = N
        using var fresh = new Harness();
        Assert.Equal(AttemptEndReason.ScreenLeaveLimit, (await fresh.Db.Attempts.SingleAsync(x => x.Id == a.Id)).EndReason);
    }

    [Theory]
    [InlineData(ScreenLeaveKind.PageExit), InlineData(ScreenLeaveKind.SignalLoss)]
    public async Task H10_ChiNhanChuyenTabHoacCuaSo(ScreenLeaveKind kind)
    {
        using var h = new Harness(); var (s, a) = await Start(h, false, true);
        await Assert.ThrowsAsync<BusinessException>(() => h.Attempts.ScreenEventAsync(a.Id, s.Student, "offline", offline: [new(kind, 5, 3)]));
        await Assert.ThrowsAsync<BusinessException>(() => h.Attempts.ScreenEventAsync(a.Id, s.Student, "offline",
            offline: Enumerable.Repeat(new OfflineLeave(ScreenLeaveKind.Hidden, 5, 3), ScreenMonitoringRules.OfflineLimit + 1).ToList()));
    }

    [Fact]
    public async Task H2_GioiHan60MoiPhut()
    {
        using var h = new Harness(); var (s, a) = await Start(h, false, true);
        for (var i = 0; i < 60; i++) await h.Attempts.ScreenEventAsync(a.Id, s.Student, "heartbeat");
        await Assert.ThrowsAsync<BusinessException>(() => h.Attempts.ScreenEventAsync(a.Id, s.Student, "heartbeat"));
        h.Clock.UtcNow = h.Clock.UtcNow.AddMinutes(1); await h.Attempts.ScreenEventAsync(a.Id, s.Student, "heartbeat");
        Assert.Equal(1, a.MonitorEventCount);
    }

    [Fact]
    public async Task H3_ChuongLanDauVaLiveCoTongDangRoi()
    {
        using var h = new Harness(); var (s, a) = await Start(h, false, true);
        // (F11) giữ test H3 chỉ báo lần đầu với GV đã bật chuông.
        (await h.Db.Users.FindAsync(s.Lecturer))!.ScreenLeaveBellEnabled = true; await h.Db.SaveChangesAsync();
        for (var i = 0; i < 2; i++)
        {
            await h.Attempts.ScreenEventAsync(a.Id, s.Student, "start", ScreenLeaveKind.OtherWindow);
            h.Clock.UtcNow = h.Clock.UtcNow.AddSeconds(3); await h.Attempts.CheckScreenAsync(a.Id);
            var row = (await h.Gradebook.GetReportAsync(s.Assignment.Id, s.Lecturer)).Rows.Single(r => r.UserId == s.Student);
            Assert.Equal(i + 1, row.Screen!.Count); Assert.True(row.Screen.Away); Assert.Equal((i + 1) * 3, row.Screen.TotalSeconds);
            await h.Attempts.ScreenEventAsync(a.Id, s.Student, "end");
        }
        Assert.Single(await h.Db.Notifications.Where(n => n.UserId == s.Lecturer && n.Type == NotificationType.ScreenLeave).ToListAsync());
    }

    [Fact]
    public async Task H8_LanThuNTuNopChamNhapNhuHetGio()
    {
        using var h = new Harness(); var (s, a) = await Start(h, false, true, 2);
        s.Assignment.Mode = AssignmentMode.Test; s.Assignment.TestStartedAt = h.Clock.UtcNow; s.Assignment.TestEndsAt = a.DeadlineAt;
        // Câu một đáp án không có nháp chờ chấm (chỉ nhiều đáp án/điền mới chấm nháp khi kết thúc): hết giờ và H8 đều để 0 lần trả lời.
        var q = a.Questions[0]; q.DraftJson = JsonSerializer.Serialize(new { Selected = new[] { q.Question.Options.Single(o => o.IsCorrect).Id } }); await h.Db.SaveChangesAsync();
        for (var i = 0; i < 2; i++)
        {
            await h.Attempts.ScreenEventAsync(a.Id, s.Student, "start", ScreenLeaveKind.Hidden);
            h.Clock.UtcNow = h.Clock.UtcNow.AddSeconds(2); await h.Attempts.CheckScreenAsync(a.Id);
            if (i == 0) { Assert.True(AttemptRules.IsOpen(a)); await h.Attempts.ScreenEventAsync(a.Id, s.Student, "end"); }
        }
        Assert.Equal(AttemptEndReason.ScreenLeaveLimit, a.EndReason); Assert.Equal(AttemptStatus.Ended, a.Status);
        Assert.Equal(0, q.AnswerCount); Assert.Empty(q.Submissions);
        Assert.Equal("Tự nộp vì rời màn hình đến lần thứ 2", ScreenMonitoringRules.EndText(a));
        Assert.Equal(CompletionStatus.SubmittedAwaitingScore, a.Participant.CompletionStatus);
    }

    [Fact]
    public async Task H8_MatTinHieuMayChuKhongTinhVaNguongNullKhongNop()
    {
        using var h = new Harness(); var (s, a) = await Start(h, false, true, 1);
        h.Clock.UtcNow = h.Clock.UtcNow.AddSeconds(46); await h.Attempts.CheckScreenAsync(a.Id);
        var loss = Assert.Single(a.ScreenLeaves); Assert.Equal(ScreenLeaveKind.SignalLoss, loss.Kind); Assert.False(loss.Counted); Assert.True(AttemptRules.IsOpen(a));
        await h.Attempts.ScreenEventAsync(a.Id, s.Student, "heartbeat"); Assert.NotNull(loss.EndedAt);
        a.AutoSubmitLeaves = null; await h.Db.SaveChangesAsync();
        for (var i = 0; i < 2; i++)
        { await h.Attempts.ScreenEventAsync(a.Id, s.Student, "start", ScreenLeaveKind.PageExit); h.Clock.UtcNow = h.Clock.UtcNow.AddSeconds(3); await h.Attempts.ScreenEventAsync(a.Id, s.Student, "end"); }
        Assert.True(AttemptRules.IsOpen(a)); Assert.Equal(2, a.ScreenLeaves.Count(l => l.Counted));
    }

    [Fact]
    public async Task H8_DongThoiKhongChamNhapHoacKetThucHaiLan()
    {
        using var h = new Harness(); var (s, a) = await Start(h, false, true, 1);
        await h.Attempts.ScreenEventAsync(a.Id, s.Student, "start", ScreenLeaveKind.OtherWindow); h.Clock.UtcNow = h.Clock.UtcNow.AddSeconds(2);
        async Task Check()
        {
            await using var db = DatabaseFixture.NewContext();
            var service = new AttemptService(db, h.Clock, [new ChoiceGrader()], new TestDevice(), new NullLearnNotifier(), new());
            await service.ScreenEventAsync(a.Id, s.Student, "end");
        }
        await Task.WhenAll(Check(), Check());
        h.Db.ChangeTracker.Clear(); var stored = await h.Db.Attempts.Include(x => x.ScreenLeaves).SingleAsync(x => x.Id == a.Id);
        Assert.Equal(AttemptEndReason.ScreenLeaveLimit, stored.EndReason); Assert.Single(stored.ScreenLeaves); Assert.Equal(h.Clock.UtcNow, stored.EndedAt);
    }

    [Fact]
    public async Task F1_DoiKhoaSauBatDauChiAnhHuongLanChuyenTiep()
    {
        using var h = new Harness(); var (s, a) = await Start(h, false); var old = a.ActiveDeviceId;
        await h.Assignments.ChangeDeviceSettingsAsync(s.Assignment.Id, true, true, 3, s.Lecturer);
        Assert.Equal(old, a.ActiveDeviceId); Assert.False(a.MonitorScreen); Assert.Null(a.AutoSubmitLeaves);
        h.Device.Id = "new-machine"; await h.Attempts.TakeoverAsync(a.Id, s.Student); Assert.Single(a.DeviceChanges); Assert.Equal(old, a.ActiveDeviceId);
    }
}
