using Microsoft.AspNetCore.Mvc;
using MindSprintAI_ASP.NET_MVC.Data.Entities;
using MindSprintAI_ASP.NET_MVC.Services;

namespace MindSprintAI_ASP.NET_MVC.Controllers;

// Trang "Công việc" (Kanban) của nhóm. Khách bị NotGuestFilter chặn như mọi trang cá nhân.
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class KanbanController(KanbanService kanban) : AppController
{
    public async Task<IActionResult> Index() => View(await kanban.ListAsync(UserId));

    [HttpPost]
    public async Task<IActionResult> Create(KanbanInput input)
    {
        await kanban.CreateAsync(UserId, input);
        Success("Đã thêm công việc.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int id, KanbanInput input)
    {
        await kanban.UpdateAsync(UserId, id, input);
        Success("Đã lưu công việc.");
        return RedirectToAction(nameof(Index));
    }

    // Nút mũi tên (form thường) và kéo thả (fetch kèm mã chống giả mạo → lỗi trả JSON qua BusinessExceptionFilter).
    [HttpPost]
    public async Task<IActionResult> Move(int id, KanbanStatus status, int position)
    {
        await kanban.MoveAsync(UserId, id, status, position);
        return Request.Headers.ContainsKey("RequestVerificationToken") ? NoContent() : RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Delete(int id)
    {
        await kanban.DeleteAsync(UserId, id);
        Success("Đã xóa công việc.");
        return RedirectToAction(nameof(Index));
    }
}
