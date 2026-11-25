using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MindSprintAI_ASP.NET_MVC.Services;

namespace MindSprintAI_ASP.NET_MVC.Controllers;

[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class ApiKeysController(UserApiKeyService keys, GeminiClient gemini) : AppController
{
    [HttpPost, EnableRateLimiting("ai-question")]
    public async Task<IActionResult> Save(string? apiKey, bool aiNoticeAccepted = false)
    {
        // Không đưa key vào Model/ViewData/TempData; cả lỗi định dạng cũng chỉ quay về trang trống.
        ModelState.Clear();
        await keys.SaveAsync(apiKey, UserId, aiNoticeAccepted);
        // (Hướng 2) kiểm tra ngay để người dùng biết key dùng được, không đợi tới câu hỏi đầu tiên.
        try { await gemini.CheckKeyAsync(await keys.UseAsync(UserId)); Success("Đã lưu API key Gemini. Dùng được."); }
        catch (BusinessException e) { await RecordRejectionAsync(e); TempData["Error"] = SavedKeyError(e); }
        return RedirectToAction("Index", "Account");
    }

    internal static string SavedKeyError(BusinessException error) => error.Data[GeminiClient.InvalidKeyFlag] is true
        ? error.Message + " Mã đã được lưu, hãy kiểm tra lại mã."
        : "Đã lưu nhưng chưa kiểm tra được mã lúc này. Bạn vẫn dùng thử được; nếu lỗi, hãy kiểm tra lại mã.";

    [HttpPost]
    public async Task<IActionResult> AcceptNotice(bool aiNoticeAccepted = false)
    {
        await keys.AcceptNoticeAsync(UserId, aiNoticeAccepted);
        Success("Đã lưu. Bạn dùng được trợ lý AI.");
        return RedirectToAction("Index", "Account");
    }

    [HttpPost, EnableRateLimiting("ai-question")]
    public async Task<IActionResult> Check()
    {
        try { await gemini.CheckKeyAsync(await keys.UseAsync(UserId)); Success("API key Gemini dùng được."); }
        catch (BusinessException e) { await RecordRejectionAsync(e); TempData["Error"] = e.Message; }
        return RedirectToAction("Index", "Account");
    }

    [HttpPost]
    public async Task<IActionResult> Delete()
    {
        await keys.DeleteAsync(UserId);
        Success("Đã xóa API key Gemini."); return RedirectToAction("Index", "Account");
    }
}
