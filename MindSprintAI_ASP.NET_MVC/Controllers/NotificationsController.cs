using Microsoft.AspNetCore.Mvc;
using MindSprintAI_ASP.NET_MVC.Services;

namespace MindSprintAI_ASP.NET_MVC.Controllers;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class NotificationsController(NotificationService notifications) : AppController
{
    public async Task<IActionResult> Index(int page = 1) => View(await notifications.ListAsync(UserId, page));
    public async Task<IActionResult> Count() => Json(new NotificationNotice(await notifications.UnreadAsync(UserId)));
    public async Task<IActionResult> Details(long id) => View(await notifications.ReadAsync(id, UserId));

    [HttpPost]
    public async Task<IActionResult> Open(long id) => LocalRedirect(await notifications.MarkReadAsync(id, UserId));

    [HttpPost]
    public async Task<IActionResult> Read(long id)
    {
        await notifications.MarkReadAsync(id, UserId);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> ReadAll()
    {
        await notifications.ReadAllAsync(UserId);
        return RedirectToAction(nameof(Index));
    }
}
