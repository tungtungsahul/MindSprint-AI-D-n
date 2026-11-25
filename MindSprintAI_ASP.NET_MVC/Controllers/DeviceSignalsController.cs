using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MindSprintAI_ASP.NET_MVC.Services;

namespace MindSprintAI_ASP.NET_MVC.Controllers;

[AllowAnonymous, AutoValidateAntiforgeryToken]
public sealed class DeviceSignalsController(DeviceSignalService signals) : Controller
{
    [HttpPost, AllowGuest, RequestSizeLimit(20000)]
    public IActionResult Light(string? signalsJson)
    {
        signals.RememberLight(HttpContext, signalsJson);
        return NoContent();
    }
}
