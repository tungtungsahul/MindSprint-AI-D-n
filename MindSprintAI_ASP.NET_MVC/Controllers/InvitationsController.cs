using Microsoft.AspNetCore.Mvc;
using MindSprintAI_ASP.NET_MVC.Services;

namespace MindSprintAI_ASP.NET_MVC.Controllers;

public class InvitationsController(InvitationService invitations, ClassTransferService transfers) : AppController
{
    public async Task<IActionResult> Index() => View(await invitations.ListAsync(UserId));

    [HttpPost]
    public async Task<IActionResult> Assistant(long id, bool accept)
    {
        await invitations.DecideAssistantAsync(id, accept, UserId);
        Success(accept ? "Đã chấp nhận lời mời giảng viên phụ." : "Đã từ chối lời mời.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Transfer(long id, bool accept)
    {
        await transfers.DecideInvitationAsync(id, accept, UserId);
        Success(accept ? "Đã nhận lớp." : "Đã từ chối lời mời.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Revoke(long id)
    {
        await invitations.RevokeAsync(id, UserId);
        Success("Đã thu hồi lời mời.");
        return RedirectToAction(nameof(Index));
    }
}
