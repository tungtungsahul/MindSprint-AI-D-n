using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MindSprintAI_ASP.NET_MVC.Models;
using MindSprintAI_ASP.NET_MVC.Services;

namespace MindSprintAI_ASP.NET_MVC.Controllers;

// Pháp lý C9: người dùng tự xóa tài khoản qua link xác nhận gửi email (1 giờ, dùng 1 lần).
public partial class AccountController
{
    [HttpPost, EnableRateLimiting("account-post"), AllowWithoutPolicy] // người không đồng ý chính sách mới vẫn xóa được tài khoản (C2)
    public async Task<IActionResult> DeleteAccount([FromServices] AccountDeletionService deletion)
    {
        var email = (await users.GetUserAsync(User))?.Email;
        ShowDevLink(await deletion.RequestAsync(UserId, (userId, token, nonce) =>
            links.To(Url.Action(nameof(DeleteAccountConfirm), "Account", new { userId, code = Encode(token), nonce })!)));
        return View("CheckEmail", $"MindSprint AI đã gửi thư xác nhận tới {email}. Mở thư và bấm link trong vòng 1 giờ để xóa tài khoản.");
    }

    // GET chỉ hiện nút xác nhận: trình quét link trong email không được tự xóa tài khoản.
    [AllowAnonymous, AllowWithoutPolicy, HttpGet, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> DeleteAccountConfirm(string userId, string code, string nonce, [FromServices] AccountDeletionService deletion)
    {
        var email = await deletion.CheckLinkAsync(userId, Decode(code), nonce);
        if (email == null) return View("Message", AccountDeletionService.InvalidLink);
        ViewData["Email"] = email;
        return View(new LockConfirmationModel(userId, code, nonce));
    }

    [AllowAnonymous, AllowWithoutPolicy, HttpPost]
    public async Task<IActionResult> DeleteAccountConfirm(LockConfirmationModel model, [FromServices] AccountDeletionService deletion)
    {
        if (!ModelState.IsValid) return View("Message", AccountDeletionService.InvalidLink);
        await deletion.ConfirmAsync(model.UserId, Decode(model.Code), model.Nonce);
        if (User.Identity?.IsAuthenticated == true && UserId == model.UserId) await signIn.SignOutAsync();
        Success("Tài khoản của bạn đã được xóa.");
        return Redirect("/");
    }
}
