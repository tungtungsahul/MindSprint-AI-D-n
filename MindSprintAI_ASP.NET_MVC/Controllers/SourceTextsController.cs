using Microsoft.AspNetCore.Mvc;
using MindSprintAI_ASP.NET_MVC.Services;

namespace MindSprintAI_ASP.NET_MVC.Controllers;

[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class SourceTextsController(SourceTextService source, AiAssistantService ai) : AppController
{
    public async Task<IActionResult> Edit(int id) => View(await source.EditorAsync(id, UserId));

    [HttpPost]
    public async Task<IActionResult> Save(int id, string? content, int version)
    {
        await source.SaveAsync(id, content, version, UserId);
        Success("Đã lưu văn bản nguồn."); return RedirectToAction(nameof(Edit), new { id });
    }

    public async Task<IActionResult> Read(int id) => View(await ai.ReadSourceAsync(id, UserId));
}
