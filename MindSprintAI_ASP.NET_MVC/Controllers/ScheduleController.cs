using Microsoft.AspNetCore.Mvc;
using MindSprintAI_ASP.NET_MVC.Data.Entities;
using MindSprintAI_ASP.NET_MVC.Services;

namespace MindSprintAI_ASP.NET_MVC.Controllers;

public record SchedulePage(List<StudySlot> Slots, StudyStreak Streak, StudyToday Today, (StudySlot Slot, int DaysAhead)? Next);

// Trang "Lịch học" (thời khóa biểu tuần) của nhóm. Khách bị NotGuestFilter chặn.
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class ScheduleController(StudyScheduleService plan, StudyStatsService stats, TimeProvider clock) : AppController
{
    public async Task<IActionResult> Index()
    {
        var slots = await plan.ListAsync(UserId);
        return View(new SchedulePage(slots, await stats.StreakAsync(UserId), await stats.TodayAsync(UserId),
            StudyScheduleService.Next(slots, clock.GetUtcNow().UtcDateTime.AddHours(7))));
    }

    // Chuông báo giờ học (js/alarm.js, mọi trang): chỉ giờ học của chính người dùng; ngày 0 = Chủ nhật như JS getDay().
    public async Task<IActionResult> Slots() => Json((await plan.ListAsync(UserId)).Select(s => new
    {
        s.Id, Day = (int)s.DayOfWeek, Start = s.StartTime.ToString("HH:mm"), End = s.EndTime.ToString("HH:mm"),
        Topic = s.Topic switch { StudyTopic.English => "Tiếng Anh", StudyTopic.Programming => "Lập trình", StudyTopic.General => "Kiến thức chung", _ => "Tất cả danh mục" },
        s.Note
    }));

    [HttpPost]
    public async Task<IActionResult> Create(StudySlotInput input)
    {
        await plan.CreateAsync(UserId, input);
        Success("Đã thêm giờ học.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int id, StudySlotInput input)
    {
        await plan.UpdateAsync(UserId, id, input);
        Success("Đã lưu giờ học.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Delete(int id)
    {
        await plan.DeleteAsync(UserId, id);
        Success("Đã xóa giờ học.");
        return RedirectToAction(nameof(Index));
    }
}
