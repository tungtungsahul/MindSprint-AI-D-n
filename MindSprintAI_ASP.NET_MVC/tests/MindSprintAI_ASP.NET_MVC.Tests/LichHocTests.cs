using MindSprintAI_ASP.NET_MVC.Data.Entities;
using MindSprintAI_ASP.NET_MVC.Services;

namespace MindSprintAI_ASP.NET_MVC.Tests;

// Lịch học + chuỗi ngày học chuyển từ MindSprint-AI-D-n (timetable + StudyStatsController).
[Collection("db")]
public class LichHocTests
{
    private static StudySlotInput Slot(DayOfWeek day, int startHour, int endHour, string? note = null, StudyTopic? topic = null) =>
        new(day, new TimeOnly(startHour, 0), new TimeOnly(endHour, 0), topic, note);

    [Fact]
    public async Task GioHocTheoQuyTacNhom()
    {
        using var h = new Harness();
        var u = await h.UserAsync("Minh");
        var plan = new StudyScheduleService(h.Db);
        var slot = await plan.CreateAsync(u, Slot(DayOfWeek.Monday, 19, 21, "  Ôn từ vựng  "));
        Assert.Equal((StudyTopic.Mixed, "Ôn từ vựng"), (slot.Topic, slot.Note));
        foreach (var (bad, message) in new (StudySlotInput, string)[] {
            (Slot(DayOfWeek.Monday, 5, 7), "Giờ học phải nằm trong khoảng 06:00 đến 23:00."),
            (new(DayOfWeek.Monday, new TimeOnly(22, 0), new TimeOnly(23, 30), null, null), "Giờ học phải nằm trong khoảng 06:00 đến 23:00."),
            (Slot(DayOfWeek.Monday, 9, 9), "Giờ kết thúc phải muộn hơn giờ bắt đầu học."),
            (Slot(DayOfWeek.Monday, 10, 9), "Giờ kết thúc phải muộn hơn giờ bắt đầu học."),
            (Slot(DayOfWeek.Monday, 9, 10, new string('a', 41)), "Ghi chú tối đa 40 ký tự."),
            (new(null, new TimeOnly(9, 0), new TimeOnly(10, 0), null, null), "Chọn ngày trong tuần."),
            (Slot(DayOfWeek.Monday, 9, 10, null, (StudyTopic)9), "Chủ đề học tập không hợp lệ.") })
            Assert.Equal(message, (await Assert.ThrowsAsync<BusinessException>(() => plan.CreateAsync(u, bad))).Message);
        // Như nhóm: được chồng giờ; 06:00–23:00 đúng biên vẫn nhận.
        await plan.CreateAsync(u, Slot(DayOfWeek.Monday, 20, 22));
        await plan.CreateAsync(u, Slot(DayOfWeek.Sunday, 6, 23));
        // Thứ Hai đứng đầu, Chủ nhật cuối tuần như lưới của nhóm.
        Assert.Equal([DayOfWeek.Monday, DayOfWeek.Monday, DayOfWeek.Sunday], (await plan.ListAsync(u)).Select(s => s.DayOfWeek));
    }

    [Fact]
    public async Task NguoiKhacKhongSuaXoaDuocGioHoc()
    {
        using var h = new Harness();
        var minh = await h.UserAsync("Minh"); var lan = await h.UserAsync("Lan");
        var plan = new StudyScheduleService(h.Db);
        var slot = await plan.CreateAsync(minh, Slot(DayOfWeek.Tuesday, 8, 9));
        Assert.Equal(StudyScheduleService.NotFound, (await Assert.ThrowsAsync<BusinessException>(() => plan.UpdateAsync(lan, slot.Id, Slot(DayOfWeek.Tuesday, 8, 10)))).Message);
        Assert.Equal(StudyScheduleService.NotFound, (await Assert.ThrowsAsync<BusinessException>(() => plan.DeleteAsync(lan, slot.Id))).Message);
        var edited = await plan.UpdateAsync(minh, slot.Id, Slot(DayOfWeek.Friday, 14, 16, "Lập trình", StudyTopic.Programming));
        Assert.Equal((DayOfWeek.Friday, new TimeOnly(16, 0), StudyTopic.Programming), (edited.DayOfWeek, edited.EndTime, edited.Topic));
        await plan.DeleteAsync(minh, slot.Id);
        Assert.Empty(await plan.ListAsync(minh));
    }

    [Fact]
    public void GioHocTiepTheoTinhTuGioVietNam()
    {
        var slots = new List<StudySlot> {
            new() { Id = 1, DayOfWeek = DayOfWeek.Saturday, StartTime = new(8, 0), EndTime = new(9, 0) },
            new() { Id = 2, DayOfWeek = DayOfWeek.Saturday, StartTime = new(20, 0), EndTime = new(21, 0) },
            new() { Id = 3, DayOfWeek = DayOfWeek.Monday, StartTime = new(7, 0), EndTime = new(8, 0) } };
        var saturdayNoon = new DateTime(2026, 11, 14, 12, 0, 0);
        Assert.Equal((2, 0), StudyScheduleService.Next(slots, saturdayNoon) is { } a ? (a.Slot.Id, a.DaysAhead) : default);
        Assert.Equal((3, 2), StudyScheduleService.Next(slots, saturdayNoon.AddHours(10)) is { } b ? (b.Slot.Id, b.DaysAhead) : default);
        // Đang trong giờ học (chưa hết) vẫn là giờ "tiếp theo".
        Assert.Equal(2, StudyScheduleService.Next(slots, saturdayNoon.AddHours(8).AddMinutes(30))!.Value.Slot.Id);
        Assert.Null(StudyScheduleService.Next([], saturdayNoon));
    }

    [Fact]
    public void ChuoiNgayHocNhuNhom()
    {
        var today = new DateOnly(2026, 11, 15);
        var days = new[] { today.AddDays(-10), today.AddDays(-9), today.AddDays(-8), today.AddDays(-2), today.AddDays(-1), today };
        Assert.Equal(new StudyStreak(3, 3, today), StudyStatsService.Streak(days, today));
        // Người dùng chốt 15/11: hôm nay chưa học thì chuỗi vẫn giữ (tính từ hôm qua); bỏ trọn một ngày thì về 0.
        Assert.Equal(2, StudyStatsService.Streak(days[..^1], today).Current);
        Assert.Equal(0, StudyStatsService.Streak(days[..^2], today).Current); // hôm qua cũng không học → đứt
        Assert.Equal(0, StudyStatsService.Streak(days[..3], today).Current);
        Assert.Equal(new StudyStreak(0, 0, null), StudyStatsService.Streak([], today));
    }

    [Fact]
    public async Task HoatDongThatTinhVaoNgayHocVaDoChinhXac()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(1);
        var stats = new StudyStatsService(h.Db, h.Clock);
        Assert.Equal(new StudyToday(0, 0, 0), await stats.TodayAsync(s.Student));
        var at = await h.Attempts.StartAsync(s.Assignment.Id, s.Student);
        var q = at.Questions.Single();
        var right = QuestionSnapshot.FromJson(q.SnapshotJson).Options.Single(o => o.IsCorrect).Id;
        await h.Attempts.AnswerAsync(at.Id, q.Id, Guid.NewGuid(), [right], s.Student, questionVersion: q.QuestionVersion);
        Assert.Equal(new StudyToday(0, 1, 1), await stats.TodayAsync(s.Student));
        Assert.Equal(100, (await stats.TodayAsync(s.Student)).Accuracy);
        Assert.Equal(1, (await stats.StreakAsync(s.Student)).Current);
        // Người khác không nhận hoạt động của Minh.
        Assert.Equal(0, (await stats.StreakAsync(s.Lecturer)).Current);
    }
}
