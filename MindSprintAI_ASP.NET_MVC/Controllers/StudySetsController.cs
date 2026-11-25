using Microsoft.AspNetCore.Mvc;
using MindSprintAI_ASP.NET_MVC.Models;
using MindSprintAI_ASP.NET_MVC.Services;
using MindSprintAI_ASP.NET_MVC.Data.Entities;

namespace MindSprintAI_ASP.NET_MVC.Controllers;

public class StudySetsController(StudyLibraryService library, StudySetCloneService clones,
    UserSettingPresetService presets, TrashService trash) : AppController
{
    public async Task<IActionResult> Trash(string? ownerId = null)
    {
        var items = await trash.ListAsync(UserId, ownerId);
        ViewData["RestoreTargets"] = await trash.GetRestoreTargetsAsync(UserId);
        return View(items);
    }

    [HttpPost]
    public async Task<IActionResult> Restore(TrashKind kind, int id, TrashRestoreMode? restoreMode = null, int? targetStudySetId = null)
    {
        await trash.RestoreAsync(kind, id, UserId, restoreMode, targetStudySetId);
        Success("Đã khôi phục.");
        return RedirectToAction(nameof(Trash));
    }

    [HttpPost]
    public async Task<IActionResult> ResetQuestionPreset(int quizId, QuestionType type)
    {
        await library.GetOwnedQuizAsync(quizId, UserId);
        if (!Enum.IsDefined(type)) throw new BusinessException("Dạng câu không hợp lệ.");
        await presets.ResetAsync($"Question.{type}", UserId);
        Success("Đã đặt lại mặc định của dạng câu này.");
        return RedirectToAction(nameof(Quiz), new { id = quizId, type });
    }
    public async Task<IActionResult> Clone(int id) => View(await clones.GetOwnedAsync(id, UserId));

    [HttpPost]
    public async Task<IActionResult> Clone(int id, int[] quizIds, bool flashcards, bool matchingSettings, bool sourceText = false)
    {
        await clones.CloneAsync(id, new(quizIds, flashcards, matchingSettings, sourceText), UserId);
        Success("Đã nhân bản bộ học. Hãy kiểm tra và đánh dấu Sẵn sàng giao cho từng quiz.");
        return RedirectToAction(nameof(Index));
    }
    // Ô tìm ở đầu trang ("Tìm trang hoặc bộ thẻ" của nhóm): tên bộ học của chính mình + chủ đề từ vựng hệ thống, chỉ tên và đường dẫn.
    [HttpGet, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> SearchItems([FromServices] VocabCatalog vocab) => Json(
        (await library.ListOwnedAsync(UserId)).Select(x => new { name = "Bộ học: " + x.Title, url = Url.Action("Details", new { id = x.Id }) })
            .Concat(vocab.Topics.Select(t => new { name = $"Từ vựng {t.CategoryName}: {t.Topic}", url = Url.Action("Topic", "Vocab", new { category = t.Category, topic = t.Topic }) })));

    public async Task<IActionResult> Index([FromServices] UnhideRequestService requests)
    {
        ViewData["Counts"] = await library.GetOwnedCountsAsync(UserId);
        var counts = await library.SrsDueCountsAsync(UserId); // (D5b)
        ViewData["SrsDueCounts"] = counts.Sets;
        ViewData["SrsDueTotal"] = counts.Total;
        ViewData["PendingUnhideSetIds"] = await requests.PendingOwnedSetIdsAsync(UserId);
        return View(await library.ListOwnedAsync(UserId));
    }

    public async Task<IActionResult> Details(int id, [FromServices] UnhideRequestService requests)
    {
        var set = await library.GetOwnedContentsAsync(id, UserId);
        ViewData["UnhideRequest"] = await requests.LatestOwnedAsync(id, UserId);
        ViewData["Counts"] = (await library.GetOwnedCountsAsync(UserId)).GetValueOrDefault(id);
        ViewData["SrsDue"] = (await library.SrsDueCountsAsync(UserId)).Sets.GetValueOrDefault(id);
        ViewData["SourceLength"] = await library.SourceLengthAsync(id);
        // Ghi rõ tên view: RequestUnhide gọi lại hàm này để hiện lại form khi bị từ chối.
        return View(nameof(Details), set);
    }

    [HttpPost]
    public Task<IActionResult> RequestUnhide(int id, string? message, [FromServices] UnhideRequestService requests) => KeepFormOnErrorAsync(async () =>
    {
        await requests.RequestAsync(id, message, UserId);
        Success("Đã gửi yêu cầu cho quản trị viên. Bạn sẽ nhận thông báo khi có kết quả.");
        return RedirectToAction(nameof(Details), new { id });
    }, async () =>
    {
        ViewData["UnhideMessage"] = message;
        return await Details(id, requests);
    });

    [HttpPost]
    public async Task<IActionResult> Create(string title, string? category)
    {
        await library.CreateStudySetAsync(title, UserId, category);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Rename(int id, string title, string? category)
    {
        await library.RenameStudySetAsync(id, title, UserId, category);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Delete(int id)
    {
        await library.DeleteStudySetAsync(id, UserId);
        Success("Đã xóa bộ học.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> CreateQuiz(int studySetId, string title)
    {
        var quiz = await library.CreateQuizAsync(studySetId, title, UserId);
        return RedirectToAction(nameof(Quiz), new { id = quiz.Id });
    }

    // Trình soạn quiz; editId = câu đang sửa (không có thì form là "thêm câu mới").
    public async Task<IActionResult> Quiz(int id, int? editId, QuestionType type = QuestionType.SingleChoice)
    {
        var quiz = await library.GetOwnedQuizAsync(id, UserId);
        var editing = editId == null ? null : quiz.Questions.FirstOrDefault(q => q.Id == editId && q.DeletedAt == null);
        var settings = new Dictionary<int, QuestionPreset>();
        foreach (var t in Enum.GetValues<QuestionType>()) settings[(int)t] = await presets.QuestionAsync(t, UserId);
        if (!Enum.IsDefined(type)) throw new BusinessException("Dạng câu không hợp lệ.");
        return View(await EditorAsync(quiz, editing != null ? QuestionForm.From(editing) : settings[(int)type].ToForm(id, type), settings));
    }

    private async Task<QuizEditorModel> EditorAsync(Quiz quiz, QuestionForm form, Dictionary<int, QuestionPreset> settings) => new()
    {
        Quiz = quiz, Form = form, Presets = settings,
        WasAssigned = await library.WasAssignedAsync(quiz.Id),
        HasAttempts = await library.HasAttemptsAsync(quiz.Id), Usage = await library.GetUsageAsync(quiz.Id, UserId)
    };

    [HttpPost]
    public Task<IActionResult> SaveQuestion(QuestionForm form) => KeepFormOnErrorAsync(async () =>
    {
        // Tên ô trong ModelState là tên kỹ thuật (tiếng Anh) nên không đưa ra cho GV (G0).
        if (!ModelState.IsValid) throw new BusinessException("Có ô nhập chưa đúng định dạng (ví dụ ô điểm hoặc ô số có chữ). Hãy kiểm tra lại rồi lưu.");
        if (form.Type is QuestionType.TextInput or QuestionType.NumberInput)
            await library.SaveInputQuestionAsync(form.QuizId, form.QuestionId, form.Version, form.ToInputQuestion(), UserId, form.EditKind, form.ExtraSessionMinutes);
        else await library.SaveChoiceQuestionAsync(form.QuizId, form.QuestionId, form.Version, form.ToInput(), UserId, form.EditKind, form.ExtraSessionMinutes);
        Success(form.QuestionId == null ? "Đã thêm câu hỏi." : "Đã lưu câu hỏi.");
        return RedirectToAction(nameof(Quiz), new { id = form.QuizId });
    }, async () =>
    {
        // Hiện lại trình soạn với đúng câu vừa gõ thay vì chuyển trang (mất chữ).
        var quiz = await library.GetOwnedQuizAsync(form.QuizId, UserId);
        var settings = new Dictionary<int, QuestionPreset>();
        foreach (var t in Enum.GetValues<QuestionType>()) settings[(int)t] = await presets.QuestionAsync(t, UserId);
        return View(nameof(Quiz), await EditorAsync(quiz, form, settings));
    });

    [HttpPost]
    public async Task<IActionResult> DeleteQuestion(int quizId, int questionId)
    {
        await library.DeleteQuestionAsync(quizId, questionId, UserId);
        return RedirectToAction(nameof(Quiz), new { id = quizId });
    }

    [HttpPost]
    public async Task<IActionResult> MoveQuestion(int quizId, int questionId, int direction)
    {
        await library.MoveQuestionAsync(quizId, questionId, direction, UserId);
        return RedirectToAction(nameof(Quiz), new { id = quizId });
    }

    [HttpPost]
    public async Task<IActionResult> MarkReady(int quizId)
    {
        var errors = await library.MarkReadyAsync(quizId, UserId);
        if (errors.Count == 0) Success("Quiz đã Sẵn sàng giao.");
        else TempData["Error"] = "Chưa đánh dấu Sẵn sàng giao được. Cần sửa: " + string.Join("; ", errors);
        return RedirectToAction(nameof(Quiz), new { id = quizId });
    }

    [HttpPost]
    public async Task<IActionResult> MarkDraft(int quizId)
    {
        await library.MarkDraftAsync(quizId, UserId);
        return RedirectToAction(nameof(Quiz), new { id = quizId });
    }

    [HttpPost]
    public async Task<IActionResult> DeleteQuiz(int quizId)
    {
        await library.DeleteQuizAsync(quizId, UserId);
        Success("Đã xóa quiz.");
        return RedirectToAction(nameof(Index));
    }
}
