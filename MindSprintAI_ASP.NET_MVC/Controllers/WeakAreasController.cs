using Microsoft.AspNetCore.Mvc;
using MindSprintAI_ASP.NET_MVC.Services;

namespace MindSprintAI_ASP.NET_MVC.Controllers;

// Trang "Điểm yếu" của nhóm. Khách bị NotGuestFilter chặn.
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class WeakAreasController(WeakAreaService weak) : AppController
{
    public async Task<IActionResult> Index() => View(await weak.GetAsync(UserId));

    [HttpPost]
    public async Task<IActionResult> CreateReviewSet([FromServices] StudyLibraryService library, [FromServices] TimeProvider clock)
    {
        var set = await weak.CreateReviewSetAsync(UserId, library, clock);
        Success($"Đã tạo bộ học \"{set.Title}\" từ các thẻ chưa nhớ và câu làm sai. Bấm Ôn thẻ để bắt đầu.");
        return RedirectToAction("Details", "StudySets", new { id = set.Id });
    }
}
