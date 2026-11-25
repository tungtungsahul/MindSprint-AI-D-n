using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using MindSprintAI_ASP.NET_MVC.Controllers;
using MindSprintAI_ASP.NET_MVC.Data.Entities;
using MindSprintAI_ASP.NET_MVC.Services;

namespace MindSprintAI_ASP.NET_MVC.Tests;

public class Tang7e2PureTests
{
    [Fact]
    public async Task F11_MacDinhTatMigrationChiThemVaFormMatches()
    {
        // (F11) cả tài khoản mới lẫn dòng cũ đều mặc định tắt.
        Assert.False(new ApplicationUser().DeviceChangeBellEnabled);
        Assert.False(new ApplicationUser().ScreenLeaveBellEnabled);
        foreach (var enabled in new[] { false, true })
        {
            var html = await Tang4ViewTests.Render("Account", "Index", new ApplicationUser
                { DeviceChangeBellEnabled = enabled, ScreenLeaveBellEnabled = enabled, CurrentMode = UserMode.Lecturer });
            foreach (var action in new[] { "DeviceChangeBell", "ScreenLeaveBell" })
            {
                var form = Assert.Single(Regex.Matches(html, "<form\\b[^>]*>[\\s\\S]*?</form>").Select(m => m.Value),
                    f => f.Contains($"data-action=\"{action}\""));
                FormMatches(form, action);
                Assert.Contains("form-switch", form); Assert.Contains("role=\"switch\"", form);
                Assert.Equal(enabled, form.Contains("checked"));
                var input = Regex.Match(form, "<input[^>]*type=\"checkbox\"[^>]*>").Value;
                var id = Regex.Match(input, "id=\"([^\"]+)\"").Groups[1].Value;
                Assert.Contains($"for=\"{id}\"", form);
                Assert.Contains("name=\"enabled\" value=\"false\"", form);
            }
            var visible = WebUtility.HtmlDecode(html);
            Assert.Contains("Đồng hồ làm bài của học viên vẫn chạy", visible);
        }
        var guest = await Tang4ViewTests.Render("Account", "Index", new ApplicationUser { IsGuest = true });
        Assert.DoesNotContain("data-action=\"DeviceChangeBell\"", guest);
        Assert.DoesNotContain("data-action=\"ScreenLeaveBell\"", guest);
        // Chế độ Học viên: hai chuông này chỉ dành cho việc của GV nên ẩn (chốt 12/11).
        var learner = await Tang4ViewTests.Render("Account", "Index", new ApplicationUser { CurrentMode = UserMode.Learner });
        Assert.DoesNotContain("sec-teaching-bell", learner);
        var attempt = Tang4RuleTests.Build(1); attempt.Participant.Assignment.DeviceLock = true;
        Assert.Contains("Nếu chờ lâu, hãy báo trực tiếp cho giảng viên.",
            WebUtility.HtmlDecode(await Tang4ViewTests.Render("Learn", "Takeover", attempt)));
    }

    private static void FormMatches(string form, string action)
    {
        var method = typeof(AccountController).GetMethod(action)!;
        Assert.True(method.IsDefined(typeof(HttpPostAttribute)));
        Assert.True(method.IsDefined(typeof(ValidateAntiForgeryTokenAttribute)));
        foreach (var p in method.GetParameters().Where(p => !p.IsDefined(typeof(FromServicesAttribute))))
            Assert.Contains($"name=\"{p.Name}\"", form);
        Assert.Contains("__RequestVerificationToken", form);
    }

    [Theory]
    [InlineData(NotificationType.DeviceChange, "DeviceChangeBellEnabled")]
    [InlineData(NotificationType.ScreenLeave, "ScreenLeaveBellEnabled")]
    public void F11_LocTuyChonTrongSql(NotificationType type, string column)
    {
        using var db = DatabaseFixture.NewContext();
        var service = new AttemptService(db, TimeProvider.System, [], new TestDevice(), new NullLearnNotifier(), new());
        var query = (IQueryable<string>)typeof(AttemptService).GetMethod("BellLecturers", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(service, [1, type])!;
        var sql = query.ToQueryString();
        Assert.Contains("JOIN", sql); Assert.Contains("WHERE", sql);
        Assert.Contains($"[{column}] = CAST(1 AS bit)", sql);
        Assert.Contains("[Role] IN (1, 2)", sql);
    }
}

[Collection("db")]
public class Tang7e2Tests
{
    [Theory]
    [InlineData(false, false), InlineData(true, false), InlineData(false, true), InlineData(true, true)]
    public async Task F11_ChinhPhuTheoTuyChonRiengVaLiveLuonHien(bool mainDevice, bool mainScreen)
    {
        using var h = new Harness(); var s = await h.ScenarioAsync();
        s.Assignment.DeviceLock = true; s.Assignment.MonitorScreen = true;
        var main = (await h.Db.Users.FindAsync(s.Lecturer))!;
        main.DeviceChangeBellEnabled = mainDevice; main.ScreenLeaveBellEnabled = mainScreen;
        var assistantId = await h.UserAsync("GV phụ");
        var assistant = (await h.Db.Users.FindAsync(assistantId))!;
        assistant.DeviceChangeBellEnabled = !mainDevice; assistant.ScreenLeaveBellEnabled = !mainScreen;
        h.Db.ClassMembers.Add(new() { ClassroomId = s.Room.Id, UserId = assistantId,
            Role = ClassRole.AssistantLecturer, Status = ClassMemberStatus.Active });
        await h.Db.SaveChangesAsync();
        var a = await h.Attempts.StartAsync(s.Assignment.Id, s.Student, true);
        var oldDevice = h.Device.Id; h.Device.Id = "other-machine";
        await h.Attempts.TakeoverAsync(a.Id, s.Student);
        foreach (var (id, enabled) in new[] { (s.Lecturer, mainDevice), (assistantId, !mainDevice) })
            Assert.Equal(enabled ? 1 : 0, await h.Db.Notifications.CountAsync(n => n.UserId == id && n.Type == NotificationType.DeviceChange));
        var row = (await h.Gradebook.GetReportAsync(s.Assignment.Id, s.Lecturer)).Rows.Single(r => r.UserId == s.Student);
        Assert.NotNull(row.DeviceChange);
        h.Device.Id = oldDevice;
        await h.Attempts.ScreenEventAsync(a.Id, s.Student, "start", ScreenLeaveKind.Hidden);
        h.Clock.UtcNow = h.Clock.UtcNow.AddSeconds(3); await h.Attempts.CheckScreenAsync(a.Id);
        row = (await h.Gradebook.GetReportAsync(s.Assignment.Id, assistantId)).Rows.Single(r => r.UserId == s.Student);
        Assert.True(row.Screen!.Away); Assert.Equal(1, row.Screen.Count); Assert.True(a.LeaveBellSent);
        foreach (var (id, enabled) in new[] { (s.Lecturer, mainScreen), (assistantId, !mainScreen) })
            Assert.Equal(enabled ? 1 : 0, await h.Db.Notifications.CountAsync(n => n.UserId == id && n.Type == NotificationType.ScreenLeave));
    }

    [Fact]
    public async Task F11_KhongAiBatChuongVanLiveVaKhongBaoLanDauMuon()
    {
        using var h = new Harness(); var notices = new RecordingLearnNotifier(); h.Notifier = notices;
        var s = await h.ScenarioAsync();
        s.Assignment.DeviceLock = true; s.Assignment.MonitorScreen = true; await h.Db.SaveChangesAsync();
        var a = await h.Attempts.StartAsync(s.Assignment.Id, s.Student, true); var old = h.Device.Id;
        notices.LecturerEvents.Clear();
        h.Device.Id = "other-machine"; await h.Attempts.TakeoverAsync(a.Id, s.Student); h.Device.Id = old;
        Assert.Contains(notices.LecturerEvents, n => n.AssignmentId == s.Assignment.Id);
        notices.LecturerEvents.Clear();
        await h.Attempts.ScreenEventAsync(a.Id, s.Student, "start", ScreenLeaveKind.Hidden);
        h.Clock.UtcNow = h.Clock.UtcNow.AddSeconds(3); await h.Attempts.CheckScreenAsync(a.Id);
        Assert.Contains(notices.LecturerEvents, n => n.AssignmentId == s.Assignment.Id);
        Assert.True(a.LeaveBellSent);
        var row = (await h.Gradebook.GetReportAsync(s.Assignment.Id, s.Lecturer)).Rows.Single(r => r.UserId == s.Student);
        Assert.NotNull(row.DeviceChange); Assert.True(row.Screen!.Away); Assert.Equal(1, row.Screen.Count);
        Assert.Empty(await h.Db.Notifications.Where(n => n.UserId == s.Lecturer && (n.Type == NotificationType.DeviceChange || n.Type == NotificationType.ScreenLeave)).ToListAsync());
        var lecturer = (await h.Db.Users.FindAsync(s.Lecturer))!;
        lecturer.ScreenLeaveBellEnabled = true; lecturer.DeviceChangeBellEnabled = true; await h.Db.SaveChangesAsync();
        await h.Attempts.ScreenEventAsync(a.Id, s.Student, "end");
        await h.Attempts.ScreenEventAsync(a.Id, s.Student, "start", ScreenLeaveKind.Hidden);
        h.Clock.UtcNow = h.Clock.UtcNow.AddSeconds(3); await h.Attempts.CheckScreenAsync(a.Id);
        Assert.Empty(await h.Db.Notifications.Where(n => n.UserId == s.Lecturer && n.Type == NotificationType.ScreenLeave).ToListAsync());
        await h.Attempts.CancelDeviceChangeAsync(a.Id, s.Student);
        h.Device.Id = "other-machine"; await h.Attempts.TakeoverAsync(a.Id, s.Student);
        Assert.Single(await h.Db.Notifications.Where(n => n.UserId == s.Lecturer && n.Type == NotificationType.DeviceChange).ToListAsync());
    }

    [Fact]
    public async Task F11_LuuRiengHaiTuyChonVaCamKhach()
    {
        using var h = new Harness(); var id = await h.UserAsync("GV");
        var user = (await h.Db.Users.FindAsync(id))!;
        var service = new ReminderService(h.Db, h.Clock, null!, null!, null!, null!, null!, null!);
        await service.SetLecturerBellEnabledAsync(id, NotificationType.DeviceChange, true);
        await h.Db.Entry(user).ReloadAsync();
        Assert.True(user.DeviceChangeBellEnabled); Assert.False(user.ScreenLeaveBellEnabled);
        await service.SetLecturerBellEnabledAsync(id, NotificationType.ScreenLeave, true);
        await service.SetLecturerBellEnabledAsync(id, NotificationType.DeviceChange, false);
        await h.Db.Entry(user).ReloadAsync();
        Assert.False(user.DeviceChangeBellEnabled); Assert.True(user.ScreenLeaveBellEnabled);
        Assert.True(user.EmailRemindersEnabled); Assert.True(user.SrsBellEnabled);
        user.IsGuest = true; await h.Db.SaveChangesAsync();
        foreach (var type in new[] { NotificationType.DeviceChange, NotificationType.ScreenLeave })
            await Assert.ThrowsAsync<ForbiddenException>(() => service.SetLecturerBellEnabledAsync(id, type, false));
        await h.Db.Entry(user).ReloadAsync(); Assert.True(user.ScreenLeaveBellEnabled);
    }
}
