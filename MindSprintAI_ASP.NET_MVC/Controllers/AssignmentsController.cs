using Microsoft.AspNetCore.Mvc;
using MindSprintAI_ASP.NET_MVC.Data.Entities;
using MindSprintAI_ASP.NET_MVC.Models;
using MindSprintAI_ASP.NET_MVC.Services;

namespace MindSprintAI_ASP.NET_MVC.Controllers;

// Phía giảng viên: giao bài, đổi hạn, đóng bài, bảng điểm.
public class AssignmentsController(
    AssignmentService assignments,
    ClassroomService classrooms,
    StudyLibraryService library,
    GradebookService gradebook,
    JoinCodeService codes,
    RetakeService retakes,
    SurveyService surveys,
    RegradeService regrades,
    UserSettingPresetService presets,
    TrashService trash) : AppController
{
    private RegradeService Regrades => regrades;
    public async Task<IActionResult> ChooseClass(int studySetId, int? quizId = null)
    {
        var set = await library.GetOwnedStudySetAsync(studySetId, UserId);
        if (quizId != null && (await library.GetOwnedQuizAsync(quizId.Value, UserId)).StudySetId != set.Id) throw new ForbiddenException();
        ViewData["StudySet"] = set;
        ViewData["QuizId"] = quizId;
        return View((await classrooms.ListMyClassesAsync(UserId)).Where(m => m.Status == ClassMemberStatus.Active && m.Role is ClassRole.MainLecturer or ClassRole.AssistantLecturer).ToList());
    }

    public async Task<IActionResult> Create(int classroomId, AssignmentMode mode = AssignmentMode.Practice, int? studySetId = null, int? quizId = null)
    {
        if (!Enum.IsDefined(mode)) throw new BusinessException("Chế độ không hợp lệ.");
        return View(await CreateModelAsync(classroomId, defaults => defaults(mode), studySetId, quizId));
    }

    // Dùng chung cho trang mới (giá trị mặc định đã nhớ) và trang hiện lại sau lỗi (giá trị GV vừa nhập).
    private async Task<AssignmentCreateModel> CreateModelAsync(int classroomId,
        Func<Func<AssignmentMode, AssignmentInput>, AssignmentInput> form, int? studySetId = null, int? quizId = null)
    {
        var room = await classrooms.GetForLecturerAsync(classroomId, UserId);
        var sets = await library.ListAssignableAsync(classroomId, UserId);
        var ready = sets
            .SelectMany(s => s.Quizzes).Where(q => q.Status == QuizStatus.Ready).ToList();
        var practice = await presets.AssignmentAsync(AssignmentMode.Practice, UserId);
        var test = await presets.AssignmentAsync(AssignmentMode.Test, UserId);
        var input = form(mode => (mode == AssignmentMode.Test ? test : practice).ToInput(classroomId, mode));
        if (studySetId != null && sets.Any(s => s.Id == studySetId))
        {
            input.StudySetId = studySetId;
            input.QuizId = ready.FirstOrDefault(q => q.StudySetId == studySetId && (quizId == null || q.Id == quizId))?.Id ?? 0;
            if (input.QuizId == 0) input.Form = AssignmentForm.FlashcardOnly;
        }
        return new AssignmentCreateModel { Classroom = room, ReadyQuizzes = ready,
            CriticalQuestions = await assignments.CriticalChoicesAsync(ready.Select(q => q.Id).ToList()),
            StudySets = sets, Form = input,
            PracticePreset = practice, TestPreset = test };
    }

    [HttpPost]
    public async Task<IActionResult> ResetPreset(int classroomId, AssignmentMode mode)
    {
        await classrooms.GetForLecturerAsync(classroomId, UserId);
        if (!Enum.IsDefined(mode)) throw new BusinessException("Chế độ không hợp lệ.");
        await presets.ResetAsync($"Assignment.{mode}", UserId);
        Success("Đã đặt lại mặc định của chế độ này.");
        return RedirectToAction(nameof(Create), new { classroomId, mode });
    }

    [HttpPost]
    public async Task<IActionResult> DeleteReport(int id)
    {
        await trash.DeleteReportAsync(id, UserId);
        Success("Đã gỡ bài giao khỏi trang Báo cáo (khôi phục được trong Thùng rác). Học viên vẫn giữ lịch sử và vẫn xem lại bài được.");
        return RedirectToAction("Trash", "StudySets");
    }

    [HttpPost]
    public Task<IActionResult> Create(AssignmentInput input) => KeepFormOnErrorAsync(() => CreateCoreAsync(input),
        async () => View(nameof(Create), await CreateModelAsync(input.ClassroomId, _ => input)));

    private async Task<IActionResult> CreateCoreAsync(AssignmentInput input)
    {
        if (!Enum.IsDefined(input.Mode)) throw new BusinessException("Chế độ giao bài không hợp lệ.");
        if (input.Mode == AssignmentMode.Test && (input.AllowQuizSelfReview || input.AllowCardSelfReview))
            throw new BusinessException("Chỉ cho phép tự ôn lại ở bài Ôn tập.");
        if (input.Mode == AssignmentMode.Test && input.AllowOfflineCards)
            throw new BusinessException("Chỉ bài Ôn tập có thẻ ghi nhớ mới cho ôn thẻ khi mất mạng.");
        if (!ModelState.IsValid) throw new BusinessException("Dữ liệu giao bài không hợp lệ.");
        if (input.Mode == AssignmentMode.Test && input.Form != AssignmentForm.QuizOnly) throw new BusinessException("Bài Kiểm tra chỉ có quiz, không có thẻ ghi nhớ.");
        if (input.Mode == AssignmentMode.Test && (input.MasteryThreshold != null || input.AnswersPerQuestion != null || input.ScoreRetries))
            throw new BusinessException("Bài Kiểm tra không có cài đặt làm chủ.");
        var a = input.Mode == AssignmentMode.Test
            ? await assignments.CreateTestAsync(input.ClassroomId, input.QuizId, new TestSettings(
                VnTime.FromInput(input.OpensAt), input.DurationMinutes ?? 0, input.ScoreScale, input.Rounding,
                input.AudiencePolicy, input.ShuffleQuestions, input.ShuffleOptions, input.IgnoreQuestionTimers, input.StrangerPolicy, input.CriticalSettings(), input.DeviceLock, input.MonitorScreen, input.AutoSubmitLeaves), UserId)
            : await assignments.CreatePracticeAsync(input.ClassroomId, input.QuizId, new PracticeSettings(
            VnTime.FromInput(input.OpensAt), VnTime.FromInput(input.DueAt), input.MaxAttempts,
            input.ScoreSelection, input.ScoreScale, input.Rounding, input.AudiencePolicy, input.ShuffleQuestions, input.ShuffleOptions,
            input.Form, input.StudySetId, input.RequiredParts, input.AllowFlashcardsAfterDue,
            input.MasteryThreshold, input.AnswersPerQuestion, input.ScoreRetries, input.IgnoreQuestionTimers, input.StrangerPolicy,
            input.FlashcardMode, input.CriticalSettings(), input.DeviceLock, input.MonitorScreen, input.AutoSubmitLeaves,
            input.AllowQuizSelfReview, input.AllowCardSelfReview, input.AllowOfflineCards), UserId);
        Success(a.MasteryThreshold != null && a.AnswersPerQuestion != null ? "Đã giao bài. " + AssignmentService.MasteryWarning : "Đã giao bài.");
        return RedirectToAction(nameof(Details), new { id = a.Id });
    }

    // Chi tiết bài giao kèm bảng điểm.
    public async Task<IActionResult> Details(int id, [FromServices] AiAssistantService? ai = null, [FromServices] RankingService? ranking = null)
    {
        var (a, rows) = await gradebook.GetReportAsync(id, UserId);
        if (ranking != null && RankingService.Ranked(a)) ViewData["Ranking"] = await ranking.ForAssignmentAsync(id, UserId);
        if (ai != null) ViewData["AiStats"] = await ai.StatsAsync(id, UserId);
        return View(new AssignmentDetailsModel
        {
            CanReopenPractice = assignments.CanReopenPractice(a),
            Assignment = a, Rows = rows,
            Matrix = await gradebook.GetMatrixAsync(id, UserId, rows),
            CanDeleteReport = await trash.CanDeleteReportAsync(id, UserId),
            HiddenContentReason = await assignments.GetHiddenContentReasonAsync(a),
            Code = (await codes.GetActiveAsync(JoinCodeTarget.Assignment, id))?.Code ?? "",
            UndoCodeUntil = await codes.UndoUntilAsync(JoinCodeTarget.Assignment, id),
            OpenAttempts = await assignments.CountOpenAttemptsAsync(id),
            Waiting = await assignments.GetWaitingAsync(id, UserId),
            RetakeRequests = await retakes.ListForLecturerAsync(id, UserId),
            HasUnusedRetakes = await assignments.HasUnusedRetakesAsync(id),
            ForgottenCards = await gradebook.GetForgottenCardsAsync(id, UserId),
            CanEditMastery = await assignments.CanEditMasteryAsync(id),
            Ungraded = await assignments.IsUngradedAsync(a),
            Surveys = await surveys.GetForLecturerAsync(id, UserId),
            RegradeQuestions = await Regrades.QuestionsAsync(id, UserId)
        });
    }

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Live(int id)
    {
        // (E) GetReport kiểm cùng quyền giảng viên như Details trước khi trả HTML.
        var (a, rows) = await gradebook.GetReportAsync(id, UserId);
        return PartialView("_LiveTable", new AssignmentDetailsModel
        {
            Assignment = a, Rows = rows, Code = "", OpenAttempts = 0,
            Waiting = await assignments.GetWaitingAsync(id, UserId),
            Ungraded = await assignments.IsUngradedAsync(a)
        });
    }

    [HttpPost]
    // Tên tham số phải khớp ô "StrangerPolicy" của partial dùng chung với trang Giao bài.
    public async Task<IActionResult> ChangeStrangerPolicy(int id, StrangerPolicy strangerPolicy)
    {
        await assignments.ChangeStrangerPolicyAsync(id, strangerPolicy, UserId);
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> ChangeDeviceSettings(int id, bool deviceLock, bool monitorScreen, int? autoSubmitLeaves)
    {
        if (!ModelState.IsValid) throw new BusinessException("Số lần rời màn hình không hợp lệ.");
        await assignments.ChangeDeviceSettingsAsync(id, deviceLock, monitorScreen, autoSubmitLeaves, UserId);
        Success("Đã lưu cài đặt thiết bị làm bài và giám sát.");
        return RedirectToAction(nameof(Details), null, new { id }, "cai-dat");
    }

    [HttpPost]
    public async Task<IActionResult> DecideDeviceChange(long id, long requestId, bool approve, [FromServices] AttemptService attempts)
    {
        var assignmentId = await attempts.DecideDeviceChangeAsync(id, requestId, approve, UserId);
        Success(approve ? "Đã cho chuyển thiết bị." : "Đã từ chối chuyển thiết bị. Học viên làm tiếp trên thiết bị cũ.");
        return RedirectToAction(nameof(Details), null, new { id = assignmentId }, "chong-gian-lan");
    }

    [HttpPost]
    public async Task<IActionResult> DecideStranger(int id, int participantId, bool approve)
    {
        await assignments.DecideStrangerAsync(id, participantId, approve, UserId);
        Success(approve ? "Đã duyệt người này vào làm bài." : "Đã từ chối người này.");
        return RedirectToAction(nameof(Details), null, new { id }, "theo-doi");
    }

    [HttpPost]
    public async Task<IActionResult> ChangeExtraMinutes(int id, string learnerId, int minutes, string? reason)
    {
        if (!ModelState.IsValid) throw new BusinessException("Số phút không hợp lệ.");
        await assignments.ChangeExtraMinutesAsync(id, learnerId, minutes, reason, UserId);
        Success("Đã điều chỉnh giờ cộng riêng và báo học viên.");
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> ChangeMaxAttempts(int id, int maxAttempts, string? reason)
    {
        if (!ModelState.IsValid) throw new BusinessException("Số lượt không hợp lệ.");
        await assignments.ChangeMaxAttemptsAsync(id, maxAttempts, reason, UserId);
        Success("Đã đổi số lượt tối đa.");
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> ChangeExtraAttempts(int id, string learnerId, int extraAttempts, string? reason)
    {
        if (!ModelState.IsValid) throw new BusinessException("Số lượt không hợp lệ.");
        await assignments.ChangeExtraAttemptsAsync(id, learnerId, extraAttempts, reason, UserId);
        Success("Đã điều chỉnh lượt cộng riêng.");
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> ChangeCode(int id, bool undo = false)
    {
        await assignments.ChangeCodeAsync(id, UserId, undo);
        Success(undo ? "Đã dùng lại mã cũ. Người đã tham gia vẫn giữ quyền truy cập." : "Đã đổi mã. Mã cũ hết hiệu lực.");
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpGet, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Qr(int id)
    {
        await assignments.GetForLecturerAsync(id, UserId);
        var code = (await codes.GetActiveAsync(JoinCodeTarget.Assignment, id))?.Code
            ?? throw new BusinessException("Bài giao chưa có mã.");
        return View("~/Views/Shared/JoinQr.cshtml", new JoinQrModel(code,
            Url.Action("Join", "Classrooms", new { code }, Request.Scheme)!,
            Url.Action("Join", "Classrooms", null, Request.Scheme)!));
    }

    [HttpPost]
    public async Task<IActionResult> ChangeQuestionTimers(int id, bool ignoreQuestionTimers)
    {
        if (!ModelState.IsValid) throw new BusinessException("Cài đặt giờ riêng không hợp lệ.");
        await assignments.ChangeQuestionTimersAsync(id, ignoreQuestionTimers, UserId);
        Success("Đã đổi cài đặt giờ riêng từng câu.");
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> ChangeMastery(int id, MasterySettings settings)
    {
        if (!ModelState.IsValid) throw new BusinessException("Cài đặt làm chủ không hợp lệ.");
        await assignments.ChangeMasteryAsync(id, settings, UserId);
        Success(settings.MasteryThreshold != null && settings.AnswersPerQuestion != null ? AssignmentService.MasteryWarning : "Đã đổi cài đặt làm chủ.");
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> ChangeOfflineCards(int id, bool allowOfflineCards)
    {
        if (!ModelState.IsValid) throw new BusinessException("Lựa chọn ôn thẻ khi mất mạng không hợp lệ.");
        await assignments.ChangeOfflineCardsAsync(id, allowOfflineCards, UserId);
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> ChangeSelfReviewSettings(int id, bool allowQuizSelfReview, bool allowCardSelfReview)
    {
        if (!ModelState.IsValid) throw new BusinessException("Lựa chọn tự ôn lại không hợp lệ.");
        await assignments.ChangeSelfReviewSettingsAsync(id, allowQuizSelfReview, allowCardSelfReview, UserId);
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> ChangeFlashcardMode(int id, FlashcardMode flashcardMode)
    {
        if (!ModelState.IsValid) throw new BusinessException("Chế độ không hợp lệ.");
        await assignments.ChangeFlashcardModeAsync(id, flashcardMode, UserId);
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> ChangeCriticalSettings(int id, bool thresholdEnabled, decimal? criticalThreshold,
        bool criticalQuestionsEnabled, List<int>? criticalQuestionIds)
    {
        if (!ModelState.IsValid) throw new BusinessException("Cài đặt điểm liệt không hợp lệ.");
        await assignments.ChangeCriticalSettingsAsync(id, new(thresholdEnabled, criticalThreshold, criticalQuestionsEnabled, criticalQuestionIds), UserId);
        Success("Đã lưu và tính lại nhãn điểm liệt; con số điểm giữ nguyên.");
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> ManualGrade(long id, long attemptQuestionId, decimal score)
    {
        if (!ModelState.IsValid) throw new BusinessException("Điểm chấm phải là một con số, ví dụ 0,5.");
        await assignments.ManualGradeAsync(id, attemptQuestionId, score, UserId);
        Success("Đã lưu điểm câu này.");
        return RedirectToAction(nameof(Attempt), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> PublishSurvey(int id, bool publish)
    {
        await surveys.PublishAsync(id, publish, UserId);
        Success(publish ? "Đã công khai kết quả khảo sát cho học viên." : "Đã ẩn kết quả khảo sát với học viên.");
        return RedirectToAction(nameof(Details), null, new { id }, "cham-lai");
    }

    [HttpPost]
    public async Task<IActionResult> PublishOpinion(int id, long submissionId, bool publish)
    {
        await surveys.PublishOpinionAsync(id, submissionId, publish, UserId);
        Success(publish ? "Đã công khai ý kiến này." : "Đã ẩn ý kiến này.");
        return RedirectToAction(nameof(Details), null, new { id }, "cham-lai");
    }

    public async Task<IActionResult> ChangeParts(int id, RequiredParts parts)
    {
        ViewData["CompletedNames"] = await assignments.PreviewRequiredPartsAsync(id, parts, UserId);
        ViewData["Parts"] = parts;
        return View(await assignments.GetForLecturerAsync(id, UserId));
    }

    [HttpPost]
    public async Task<IActionResult> SaveParts(int id, RequiredParts parts)
    {
        await assignments.ChangeRequiredPartsAsync(id, parts, UserId);
        Success("Đã đổi phần bắt buộc và ghi lịch sử bài giao.");
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> ChangeDue(int id, DateTime? dueAt, string? reason)
    {
        await assignments.ChangeDueAsync(id, VnTime.FromInput(dueAt), reason, UserId);
        Success(dueAt == null ? "Đã bỏ hạn nộp." : "Đã đổi hạn nộp.");
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> ReopenPractice(int id, DateTime? dueAt, string? reason)
    {
        if (!ModelState.IsValid) throw new BusinessException("Hạn mới không hợp lệ. Hãy chọn lại ngày và giờ.");
        await assignments.ReopenPracticeAsync(id, VnTime.FromInput(dueAt), reason, UserId);
        Success("Đã mở lại bài ôn tập. Mã vào bài giữ nguyên.");
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> Close(int id, bool? delayed)
    {
        var count = await assignments.RequestCloseAsync(id, delayed, UserId);
        if (count > 0)
        {
            ViewData["OpenAttempts"] = count;
            ViewData["ExtraTimeOpenAttempts"] = await assignments.CountExtraTimeOpenAsync(id, UserId);
            return View("CloseChoice", await assignments.GetForLecturerAsync(id, UserId));
        }
        Success(delayed == true ? "Bài sẽ đóng sau 15 phút (hạn cũ sớm hơn thì giữ hạn cũ). Người đang làm đã được báo hạn mới." : "Đã đóng bài giao.");
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> Start(int id)
    {
        await assignments.StartTestAsync(id, UserId);
        Success("Đã bắt đầu bài kiểm tra. Đồng hồ chung đang chạy.");
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> PublishScores(int id)
    {
        await assignments.PublishScoresAsync(id, UserId);
        Success("Đã công bố điểm.");
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> PublishAnswers(int id, bool confirmed)
    {
        await assignments.PublishAnswersAsync(id, confirmed, UserId);
        Success("Đã công bố đáp án và giải thích.");
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> DecideRetake(int id, bool approve, DateTime? windowEndsAt)
    {
        var assignmentId = await retakes.DecideAsync(id, approve, VnTime.FromInput(windowEndsAt), UserId);
        Success(approve ? "Đã duyệt làm lại." : "Đã từ chối làm lại.");
        return RedirectToAction(nameof(Details), new { id = assignmentId });
    }

    [HttpPost]
    public async Task<IActionResult> KeepGuest(int id, int participantId, string? reason, [FromServices] GuestService guests)
    {
        await guests.KeepTestGuestAsync(id, participantId, reason, UserId);
        Success("Đã giữ lượt làm lúc chưa đăng nhập (khách) và gắn vào tài khoản của học viên.");
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> CancelAttempt(long id, string? reason)
    {
        await retakes.CancelAsync(id, reason, UserId);
        Success("Đã hủy lượt.");
        return RedirectToAction(nameof(Attempt), new { id });
    }

    // Bài làm của một học viên, theo thứ tự câu gốc.
    public async Task<IActionResult> Attempt(long id)
    {
        var (attempt, items) = await gradebook.GetAttemptForLecturerAsync(id, UserId);
        ViewData["Items"] = items;
        ViewData["InputReviews"] = GradebookService.InputReviews(items);
        ViewData["RegradeQuestions"] = await Regrades.QuestionsAsync(attempt.Participant.AssignmentId, UserId);
        return View(attempt);
    }

    public async Task<IActionResult> Regrade(int id) => View(new RegradePageModel(id, await Regrades.QuestionsAsync(id, UserId)));

    [HttpPost]
    public async Task<IActionResult> ApplyRegrade(int id, int questionId, RegradeMethod method, string reason,
        Guid clientRequestId, List<int>? newCorrect)
    {
        if (!ModelState.IsValid) throw new BusinessException("Dữ liệu chấm lại không hợp lệ.");
        await Regrades.ApplyAsync(id, new RegradeInput(questionId, method, reason, clientRequestId, newCorrect ?? []), UserId);
        Success("Đã chấm lại câu cho mọi lượt của bài giao và ghi lịch sử điểm.");
        return RedirectToAction(nameof(Regrade), new { id });
    }
}
