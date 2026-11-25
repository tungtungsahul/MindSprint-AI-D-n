using Microsoft.AspNetCore.Mvc;
using MindSprintAI_ASP.NET_MVC.Data.Entities;
using MindSprintAI_ASP.NET_MVC.Services;

namespace MindSprintAI_ASP.NET_MVC.Controllers;

public record NotebookPage(List<NotebookRow> Notebooks, Notebook? Current, string? Question = null, GroundedAnswer? Answer = null);

// Trang "Sổ tay AI" của nhóm. Mọi thao tác là form thường (chạy cả khi JS lỗi); nói/nghe là tiện ích JS (js/notebook.js).
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class NotebooksController(NotebookService notebooks) : AppController
{
    public async Task<IActionResult> Index(int? id) => View(await PageAsync(id));

    [HttpPost]
    public async Task<IActionResult> Create(string? title)
    {
        var notebook = await notebooks.CreateAsync(title, UserId);
        return RedirectToAction(nameof(Index), new { id = notebook.Id });
    }

    [HttpPost]
    public async Task<IActionResult> Delete(int id)
    {
        await notebooks.DeleteAsync(id, UserId);
        Success("Đã xóa sổ tay.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, RequestSizeLimit(AiGenerateService.MaxFileBytes + 1024 * 1024), RequestFormLimits(MultipartBodyLengthLimit = AiGenerateService.MaxFileBytes + 1024 * 1024)]
    public async Task<IActionResult> AddSource(int id, string? title, string? text, IFormFile? file)
    {
        if (file is { Length: > 0 } && NotebookService.ImageTypes.Contains(Path.GetExtension(file.FileName).ToLowerInvariant()))
            await notebooks.AddImageSourceAsync(id, title, file, UserId);
        else if (file is { Length: > 0 })
        {
            var content = await AiGenerateService.ExtractAsync(file);
            var kind = Path.GetExtension(file.FileName).Equals(".pdf", StringComparison.OrdinalIgnoreCase) ? NotebookSourceKind.Pdf : NotebookSourceKind.File;
            await notebooks.AddSourceAsync(id, string.IsNullOrWhiteSpace(title) ? Path.GetFileNameWithoutExtension(file.FileName) : title, content, kind, Path.GetFileName(file.FileName), UserId);
        }
        else await notebooks.AddSourceAsync(id, title, text, NotebookSourceKind.Text, null, UserId);
        Success("Đã thêm nguồn.");
        return RedirectToAction(nameof(Index), new { id });
    }

    // Nguồn từ đường link (như nhóm): máy chủ tải trang công khai, lấy phần chữ chính hoặc nội dung PDF.
    [HttpPost]
    public async Task<IActionResult> AddUrl(int id, string? url, [FromServices] IHttpClientFactory http)
    {
        var source = await NotebookUrlReader.ReadAsync(http.CreateClient(NotebookUrlReader.ClientName), url, HttpContext.RequestAborted);
        await notebooks.AddSourceAsync(id, source.Title, source.Content, NotebookSourceKind.Url, source.Url.Length > 260 ? source.Url[..260] : source.Url, UserId);
        Success("Đã thêm nguồn từ đường link.");
        return RedirectToAction(nameof(Index), new { id });
    }

    // ---- Gọi từ js/notebook.js (fetch kèm mã chống giả mạo; lỗi nghiệp vụ trả JSON qua BusinessExceptionFilter) ----
    [HttpPost]
    public async Task<IActionResult> AskJson(int id, string? question)
    {
        var answer = await notebooks.AskAsync(id, question, UserId, HttpContext.RequestAborted);
        return Json(new { answer = answer.Answer, citations = answer.Citations.Select(c => c.ExcerptText) });
    }

    [HttpPost]
    public async Task<IActionResult> Suggestions(int id, [FromServices] NotebookAiService ai) => Json(await ai.SuggestAsync(id, UserId));

    [HttpPost]
    public async Task<IActionResult> Studio(int id, string? type, string? focus, [FromServices] NotebookAiService ai) =>
        Json(new { type, result = await ai.StudioAsync(id, type, focus, UserId) });

    // Studio Flashcard/Trắc nghiệm làm thử tại chỗ; "rows" là bản nháp để gửi tiếp sang Import/Recheck khi muốn lưu thành bộ học.
    [HttpPost]
    public async Task<IActionResult> StudioItems(int id, string kind = "quiz")
    {
        var cards = kind == "cards";
        var preview = await notebooks.CreateStudySetAsync(id, 10, cards, UserId);
        return Json(new { kind = cards ? "cards" : "quiz", preview.NewSetTitle, preview.FileName, preview.Notice, rows = preview.Rows, items = Services.StudioItems.From(preview, cards) });
    }

    [HttpPost]
    public async Task<IActionResult> Tutor(int id, [FromBody] TutorInput input, [FromServices] NotebookAiService ai) =>
        Json(await ai.TutorAsync(id, input, UserId));

    [HttpPost]
    public async Task<IActionResult> DeleteSource(int id, int sourceId)
    {
        await notebooks.DeleteSourceAsync(id, sourceId, UserId);
        return RedirectToAction(nameof(Index), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> Ask(int id, string? question) =>
        View(nameof(Index), await PageAsync(id) with { Question = question, Answer = await notebooks.AskAsync(id, question, UserId, HttpContext.RequestAborted) });

    [HttpPost]
    public async Task<IActionResult> AddNote(int id, string? title, string? content)
    {
        await notebooks.AddNoteAsync(id, title, content, UserId);
        Success("Đã lưu ghi chú.");
        return RedirectToAction(nameof(Index), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> DeleteNote(int id, int noteId)
    {
        await notebooks.DeleteNoteAsync(id, noteId, UserId);
        return RedirectToAction(nameof(Index), new { id });
    }

    // Thẻ ghi nhớ / trắc nghiệm từ sổ tay → trang xem trước nhập (sửa, bỏ dòng rồi mới lưu vào bộ học mới).
    [HttpPost]
    public async Task<IActionResult> CreateStudySet(int id, string kind = "cards", int count = 10) =>
        View("~/Views/Import/Preview.cshtml", await notebooks.CreateStudySetAsync(id, count, kind == "cards", UserId));

    private async Task<NotebookPage> PageAsync(int? id)
    {
        var list = await notebooks.ListAsync(UserId);
        var currentId = id ?? list.FirstOrDefault()?.Id;
        return new(list, currentId is { } c ? await notebooks.GetAsync(c, UserId) : null);
    }
}
