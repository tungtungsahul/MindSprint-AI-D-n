using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using MindSprintAI_ASP.NET_MVC.Data;
using MindSprintAI_ASP.NET_MVC.Services;

namespace MindSprintAI_ASP.NET_MVC.Controllers;

[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class AiController(AiAssistantService ai, AppDbContext db) : AppController
{
    public async Task<IActionResult> State(int id) => Json(await ai.StateAsync(id, UserId));
    public async Task<IActionResult> History(int id) => Json(await ai.HistoryAsync(id, UserId));
    public async Task<IActionResult> Conversation(int id) => Json(await ai.ConversationAsync(id, UserId));
    public async Task<IActionResult> Index(bool trash = false)
    {
        ViewData["Trash"] = trash; return View(await ai.ListAsync(UserId, trash));
    }
    public async Task<IActionResult> Open(int id)
    {
        var conversation = await ai.ConversationAsync(id, UserId);
        ViewData["SourceVersion"] = await db.SourceTexts.Where(s => s.StudySetId == conversation.StudySetId)
            .Select(s => s.Version).SingleOrDefaultAsync();
        return View(conversation);
    }

    [HttpPost, EnableRateLimiting("ai-question")]
    public async Task<IActionResult> Ask(int id, string question, int? conversationId, long? attemptId, int? questionId,
        CancellationToken cancellationToken) => Json(await ai.AskAsync(id, question, conversationId, attemptId, questionId, UserId, cancellationToken));

    [HttpPost]
    public async Task<IActionResult> Delete(int id)
    {
        await ai.DeleteAsync(id, UserId); Success("Đã xóa hội thoại. Bạn khôi phục được trong 30 ngày ở Thùng rác trợ lý."); return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Restore(int id)
    {
        await ai.RestoreAsync(id, UserId); Success("Đã khôi phục hội thoại."); return RedirectToAction(nameof(Index), new { trash = true });
    }

    [HttpPost]
    public async Task<IActionResult> Settings(int id, bool allowAiAssistant, bool allowAiAfterTest)
    {
        await ai.SettingsAsync(id, allowAiAssistant, allowAiAfterTest, UserId);
        Success("Đã đổi quyền dùng trợ lý AI."); return RedirectToAction("Details", "Assignments", new { id });
    }
}
