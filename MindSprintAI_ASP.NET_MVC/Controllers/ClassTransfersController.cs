using Microsoft.AspNetCore.Mvc;
using MindSprintAI_ASP.NET_MVC.Services;

namespace MindSprintAI_ASP.NET_MVC.Controllers;

public class ClassTransfersController(ClassTransferService transfers) : AppController
{
    public async Task<IActionResult> Details(int id)
    {
        ViewData["UserId"] = UserId;
        return View(await transfers.GetAsync(id, UserId));
    }

    [HttpPost]
    public async Task<IActionResult> Offer(int classroomId, string email, bool fromUserStays)
    {
        await transfers.OfferAsync(classroomId, email, fromUserStays, UserId);
        Success(InvitationService.SentMessage);
        return RedirectToAction("Index", "Invitations");
    }

    [HttpPost]
    public async Task<IActionResult> Decide(int id, bool accept)
    {
        await transfers.DecideAsync(id, accept, UserId);
        Success(accept ? "Đã nhận lớp." : "Đã từ chối chuyển lớp.");
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> Cancel(int id)
    {
        await transfers.CancelAsync(id, UserId);
        Success("Đã hủy đề nghị chuyển lớp.");
        return RedirectToAction(nameof(Details), new { id });
    }
}
