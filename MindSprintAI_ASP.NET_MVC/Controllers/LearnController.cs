using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using MindSprintAI_ASP.NET_MVC.Data.Entities;
using MindSprintAI_ASP.NET_MVC.Models;
using MindSprintAI_ASP.NET_MVC.Services;

namespace MindSprintAI_ASP.NET_MVC.Controllers;

// Phía học viên: danh sách bài, làm bài, xem lại.
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public class LearnController(AssignmentService assignments, AttemptService attempts, ClassroomService classrooms, RetakeService retakes, SurveyService surveys, DeviceSignalService? deviceSignals = null) : AppController
{
    // Trang Luyện tập 3 tab (người dùng chọn 15/11): mở trang mà không còn bài cần làm thì vào thẳng tab Trắc nghiệm & Tự luận;
    // bấm tab "Bài được giao" (tab=assigned) thì luôn ở lại đây.
    public async Task<IActionResult> Index(string? tab = null)
    {
        var participations = await attempts.ListMyParticipationsAsync(UserId);
        var list = await assignments.ListForLearnerAsync(UserId);
        if (tab != "assigned" && HomeService.LearnerTodo(list, participations, DateTime.UtcNow).Count == 0)
            return RedirectToAction("Practice", "Vocab");
        ViewData["Participations"] = participations;
        ViewData["Classes"] = (await classrooms.ListMyClassesAsync(UserId)).Where(m => m.Role == ClassRole.Student).ToList();
        ViewData["SelfReviewAssignmentIds"] = await attempts.SelfReviewAssignmentIdsAsync(list, UserId);
        return View(list);
    }

    [AllowGuest]
    public async Task<IActionResult> Assignment(int id, [FromServices] RankingService? ranking = null)
    {
        var model = await attempts.GetAssignmentPageAsync(id, UserId);
        // Xếp hạng bài này (17/11): khách chưa có lượt thì không có bảng, trang vẫn mở bình thường.
        if (ranking != null && !model.IsWaiting)
            try { ViewData["Ranking"] = await ranking.ForAssignmentAsync(id, UserId); } catch (ForbiddenException) { }
        return View(model.IsWaiting ? "Waiting" : "Assignment", model);
    }

    [HttpGet, AllowGuest]
    public async Task<IActionResult> TestStatus(int id) => Json(await attempts.GetTestStatusAsync(id, UserId));

    public async Task<IActionResult> SurveyResults(int id) => View(await surveys.GetForLearnerAsync(id, UserId));

    [HttpPost, AllowGuest]
    public async Task<IActionResult> Start(int id, bool consent = false, string? signals = null, long? attemptId = null)
    {
        if (attemptId is long existing)
        {
            var current = await attempts.GetForLearnerAsync(existing, UserId);
            if (current.Participant.AssignmentId != id) throw new ForbiddenException();
            if (!consent) throw new BusinessException("Bạn cần bấm Tôi đồng ý trước khi bắt đầu bài này.");
            await attempts.ConsentAsync(existing, UserId, ReadSignals(signals));
            return RedirectToAction(nameof(Play), new { id = existing });
        }
        if (!consent && AttemptService.NeedsConsent((await attempts.GetAssignmentPageAsync(id, UserId)).Assignment))
            return RedirectToAction(nameof(Consent), new { id });
        var attempt = await attempts.StartAsync(id, UserId, consent, consent ? ReadSignals(signals) : null);
        return RedirectToAction(nameof(Play), new { id = attempt.Id });
    }

    private DeviceSignalResult? ReadSignals(string? signals) => deviceSignals?.Read(signals, HttpContext.Connection.RemoteIpAddress, true);

    [HttpPost]
    public async Task<IActionResult> SelfReview(int id)
    {
        var attempt = await attempts.StartSelfReviewAsync(id, UserId);
        return RedirectToAction(nameof(Play), new { id = attempt.Id });
    }

    [HttpGet, AllowGuest]
    public async Task<IActionResult> Consent(int id, long? attemptId = null, int? retakeId = null)
    {
        var page = await attempts.GetAssignmentPageAsync(id, UserId);
        if (attemptId is long existing && (await attempts.GetForLearnerAsync(existing, UserId)).Participant.AssignmentId != id) throw new ForbiddenException();
        if (retakeId is int request && await retakes.ConsentAssignmentAsync(request, UserId) != id) throw new ForbiddenException();
        return View(new DeviceConsentModel(page.Assignment, attemptId, retakeId));
    }

    [AllowGuest]
    public async Task<IActionResult> Play(long id, [FromServices] PaperTestService? papers = null)
    {
        var (attempt, needsTakeover) = await attempts.OpenPlayAsync(id, UserId);
        if (!AttemptRules.IsOpen(attempt)) return RedirectToAction(nameof(Review), new { id });
        if (attempt.Kind != AttemptKind.SelfReview && AttemptService.NeedsConsent(attempt.Participant.Assignment) && attempt.DeviceConsentAt == null)
            return RedirectToAction(nameof(Consent), new { id = attempt.Participant.AssignmentId, attemptId = id });
        if (needsTakeover) return View("Takeover", attempt);
        if (attempt.Participant.Assignment.Form == AssignmentForm.Document)
        {
            ViewData["Submission"] = await (papers ?? HttpContext.RequestServices.GetRequiredService<PaperTestService>()).GetSubmissionAsync(id);
            return View("Paper", attempt);
        }
        ViewData["Questions"] = await attempts.GetPlayQuestionsAsync(attempt, UserId);
        ViewData["Mastery"] = MasteryRules.Progress(attempt);
        return View(attempt);
    }

    [HttpPost, AllowGuest]
    public async Task<IActionResult> OpenQuestion(long id, [FromBody] OpenQuestionRequest request)
    {
        if (request.QuestionVersion == null) throw new BusinessException("Hãy tải lại bài để nhận bản câu hỏi mới.");
        return PartialView("_PlayQuestionBody", await attempts.OpenQuestionAsync(id, request.AttemptQuestionId, UserId, request.Retry, request.QuestionVersion));
    }

    [HttpPost, AllowGuest]
    public async Task<IActionResult> Answer(long id, [FromBody] AnswerRequest request)
    {
        if (request.QuestionVersion == null) throw new BusinessException("Hãy tải lại bài để nhận bản câu hỏi mới.");
        var result = await attempts.AnswerAsync(id, request.AttemptQuestionId, request.ClientRequestId, request.Selected ?? [], UserId, request.Blanks, request.Value, request.QuestionVersion);
        var state = await attempts.GetStateAsync(id, UserId);
        // (63) thêm hạn dự phòng, vẫn giữ bộ lọc phản hồi Kiểm tra không có đáp án.
        var data = JsonSerializer.SerializeToNode(result, new JsonSerializerOptions(JsonSerializerDefaults.Web))!.AsObject();
        data["deadlineAt"] = JsonSerializer.SerializeToNode(state.DeadlineAt);
        data["time"] = state.Time;
        data["ended"] = state.Ended;
        data["moved"] = state.Moved;
        data["reviewUrl"] = state.ReviewUrl;
        data["questionTimers"] = JsonSerializer.SerializeToNode(state.QuestionTimers, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return Json(data);
    }

    [HttpPost, AllowGuest]
    public async Task<IActionResult> Draft(long id, [FromBody] DraftRequest request)
    {
        if (request.QuestionVersion == null) throw new BusinessException("Hãy tải lại bài để nhận bản câu hỏi mới.");
        await attempts.SaveDraftAsync(id, request.AttemptQuestionId, request.Selected ?? [], UserId, request.Blanks, request.Value, request.QuestionVersion);
        return await State(id);
    }

    [HttpPost, AllowGuest]
    public async Task<IActionResult> Flag(long id, [FromBody] QuestionRef request)
    {
        await attempts.ToggleFlagAsync(id, request.AttemptQuestionId, UserId);
        return await State(id);
    }

    [HttpGet, AllowGuest]
    public async Task<IActionResult> State(long id) => Json(await attempts.GetStateAsync(id, UserId));

    [HttpPost, AllowGuest]
    public async Task<IActionResult> Takeover(long id, string? reason = null, string? signals = null)
    {
        await attempts.TakeoverAsync(id, UserId, reason, ReadSignals(signals));
        return RedirectToAction(nameof(Play), new { id });
    }

    [HttpPost, AllowGuest]
    public async Task<IActionResult> CancelDeviceChange(long id)
    {
        await attempts.CancelDeviceChangeAsync(id, UserId);
        return RedirectToAction(nameof(Play), new { id });
    }

    [HttpGet, AllowGuest]
    public async Task<IActionResult> DeviceChangeState(long id) => Json(await attempts.DeviceChangeStateAsync(id, UserId));

    [HttpPost, AllowGuest]
    public async Task<IActionResult> ScreenEvent(long id, string eventType, ScreenLeaveKind? kind, DateTimeOffset? clientAt,
        ScreenLeaveKind[]? offlineKind, int[]? offlineAgo, int[]? offlineSeconds)
    {
        if (!ModelState.IsValid) throw new BusinessException("Thông tin theo dõi không hợp lệ.");
        // (H10) offlineSeconds = -1: lúc báo vẫn đang ở ngoài màn hình.
        List<OfflineLeave>? offline = null;
        if (offlineKind is { Length: > 0 })
        {
            if (offlineAgo?.Length != offlineKind.Length || offlineSeconds?.Length != offlineKind.Length) throw new BusinessException("Thông tin theo dõi không hợp lệ.");
            offline = offlineKind.Select((k, i) => new OfflineLeave(k, offlineAgo[i], offlineSeconds[i] < 0 ? null : offlineSeconds[i])).ToList();
        }
        return Json(await attempts.ScreenEventAsync(id, UserId, eventType, kind, clientAt, offline));
    }

    [HttpPost, AllowGuest]
    public async Task<IActionResult> Exit(long id)
    {
        await attempts.PauseAsync(id, UserId);
        var a = await attempts.GetForLearnerAsync(id, UserId);
        return RedirectToAction(nameof(Assignment), new { id = a.Participant.AssignmentId });
    }

    [HttpPost, AllowGuest]
    public async Task<IActionResult> Finish(long id)
    {
        await attempts.FinishAsync(id, UserId);
        return RedirectToAction(nameof(Review), new { id });
    }

    [AllowGuest]
    public async Task<IActionResult> Review(long id, [FromServices] PaperTestService? papers = null)
    {
        var attempt = await attempts.GetForLearnerAsync(id, UserId);
        if (AttemptRules.IsOpen(attempt)) return RedirectToAction(nameof(Play), new { id });
        if (attempt.Participant.Assignment.Form == AssignmentForm.Document)
        {
            ViewData["Submission"] = await (papers ?? HttpContext.RequestServices.GetRequiredService<PaperTestService>()).GetSubmissionAsync(id);
            ViewData["ShowScores"] = AttemptService.CanShowScores(attempt.Participant);
            return View("PaperReview", attempt);
        }
        ViewData["Questions"] = await attempts.GetPlayQuestionsAsync(attempt, UserId);
        ViewData["ShowScores"] = AttemptService.CanShowScores(attempt.Participant);
        ViewData["CriticalText"] = AttemptService.CanShowScores(attempt.Participant)
            ? CriticalScoreRules.Text(attempt.CriticalLabel, attempt.CriticalReasons) : null;
        ViewData["HiddenContentNotice"] = await attempts.GetHiddenContentNoticeAsync(attempt.Participant.Assignment);
        if (attempt.Kind == AttemptKind.SelfReview) return View("~/Views/Assignments/SelfReview.cshtml", attempt);
        return View(attempt);
    }

    [HttpPost]
    public Task<IActionResult> RequestRetake(int id, string? reason) => KeepFormOnErrorAsync(async () =>
    {
        await retakes.RequestAsync(id, reason, UserId);
        Success("Đã gửi yêu cầu làm lại.");
        return RedirectToAction(nameof(Assignment), new { id });
    }, async () =>
    {
        ViewData["RetakeReason"] = reason;
        return View("Assignment", await attempts.GetAssignmentPageAsync(id, UserId));
    });

    [HttpPost]
    public async Task<IActionResult> StartRetake(int id, bool consent = false, string? signals = null)
    {
        var assignmentId = await retakes.ConsentAssignmentAsync(id, UserId);
        if (!consent && AttemptService.NeedsConsent((await attempts.GetAssignmentPageAsync(assignmentId, UserId)).Assignment))
            return RedirectToAction(nameof(Consent), new { id = assignmentId, retakeId = id });
        var attempt = await retakes.StartAsync(id, UserId, consent, consent ? ReadSignals(signals) : null);
        return RedirectToAction(nameof(Play), new { id = attempt.Id });
    }
}
