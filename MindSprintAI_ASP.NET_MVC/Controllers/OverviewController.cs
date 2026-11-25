using Microsoft.AspNetCore.Mvc;
using MindSprintAI_ASP.NET_MVC.Services;

namespace MindSprintAI_ASP.NET_MVC.Controllers;

// Trang "Tổng quan" của nhóm. Khách bị NotGuestFilter chặn.
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class OverviewController(OverviewService overview) : AppController
{
    public async Task<IActionResult> Index() => View(await overview.GetAsync(UserId));
}
