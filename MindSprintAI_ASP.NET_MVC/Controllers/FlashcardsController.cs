using Microsoft.AspNetCore.Mvc;
using MindSprintAI_ASP.NET_MVC.Data.Entities;
using MindSprintAI_ASP.NET_MVC.Models;
using MindSprintAI_ASP.NET_MVC.Services;

namespace MindSprintAI_ASP.NET_MVC.Controllers;

[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public class FlashcardsController(FlashcardService cards) : AppController
{
    public async Task<IActionResult> Editor(int id)
    {
        var (set, list) = await cards.GetEditorAsync(id, UserId);
        ViewData["Cards"] = list;
        ViewData["SrsDueCount"] = await cards.PersonalDueCountAsync(id, UserId); // (D5b)
        return View(nameof(Editor), set); // ghi rõ tên: AddMany gọi lại hàm này khi hiện lại trang sau lỗi
    }

    [HttpPost]
    public async Task<IActionResult> Save(int studySetId, int? cardId, string? front, string? back, string? example)
    {
        var savedId = await cards.SaveAsync(studySetId, cardId, front, back, UserId, example);
        Success(cardId == null ? "Đã thêm thẻ (ở cuối danh sách)." : "Đã lưu thẻ.");
        return RedirectToAction(nameof(Editor), null, new { id = studySetId }, $"card-{savedId}");
    }

    // Dán nhiều thẻ (nhập hàng loạt của nhóm). Lỗi thì hiện lại trang Soạn thẻ còn nguyên đoạn đã dán để sửa.
    [HttpPost]
    public Task<IActionResult> AddMany(int studySetId, string? text) => KeepFormOnErrorAsync(async () =>
    {
        var added = await cards.AddManyAsync(studySetId, text, UserId);
        Success($"Đã thêm {added} thẻ vào cuối danh sách.");
        return RedirectToAction(nameof(Editor), new { id = studySetId });
    }, async () =>
    {
        ViewData["PasteText"] = text;
        return await Editor(studySetId);
    });

    [HttpPost]
    public async Task<IActionResult> Delete(int studySetId, int cardId)
    {
        await cards.DeleteAsync(studySetId, cardId, UserId);
        Success("Đã xóa thẻ. Có thể khôi phục trong Thùng rác.");
        return RedirectToAction(nameof(Editor), new { id = studySetId });
    }

    [AllowGuest]
    public async Task<IActionResult> Study(int id, int? assignmentId, bool forgotten = false, bool dueOnly = false)
    {
        // Trang dùng để nói đúng lý do khi không có thẻ nào (chưa có thẻ / không có thẻ Chưa nhớ / không có thẻ đến hạn).
        ViewData["Forgotten"] = forgotten;
        ViewData["DueOnly"] = dueOnly;
        return View(await cards.GetSessionAsync(id, assignmentId, UserId, forgotten, dueOnly));
    }

    [HttpPost, AllowGuest]
    public async Task<IActionResult> ChangeMode(int studySetId, int? assignmentId, FlashcardMode mode)
    {
        if (!ModelState.IsValid) throw new BusinessException("Chế độ không hợp lệ.");
        await cards.ChangeModeAsync(studySetId, assignmentId, mode, UserId);
        return RedirectToAction(nameof(Study), new { id = studySetId, assignmentId });
    }

    [AllowGuest]
    public async Task<IActionResult> History(int id, int? assignmentId, int? participantId, int page = 1) =>
        View(await cards.HistoryAsync(id, assignmentId, UserId, participantId, page));

    [HttpPost, AllowGuest]
    public async Task<IActionResult> Rate(int studySetId, int? assignmentId, int cardId, FlashcardRating rating, FlashcardMode? mode = null)
    {
        if (!ModelState.IsValid) throw new BusinessException("Đánh giá không hợp lệ.");
        var (counts, status) = await cards.RateAsync(studySetId, assignmentId, cardId, rating, UserId, mode);
        return Json(new { ok = true, studied = counts.Studied, required = counts.Total,
            dueCount = counts.DueCount, nextDueDate = counts.NextDueDate?.ToString("dd/MM/yyyy"),
            status = status == null ? null : DisplayText.Of(status.Value) });
    }
}
