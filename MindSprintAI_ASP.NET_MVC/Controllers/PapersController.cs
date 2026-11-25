using Microsoft.AspNetCore.Mvc;
using MindSprintAI_ASP.NET_MVC.Data.Entities;
using MindSprintAI_ASP.NET_MVC.Models;
using MindSprintAI_ASP.NET_MVC.Services;

namespace MindSprintAI_ASP.NET_MVC.Controllers;

// Bài làm theo đề tài liệu: GV tạo bài + chấm; học viên lưu bài viết, tải tệp; phát tệp đề và tệp bài nộp sau khi kiểm quyền.
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public class PapersController(PaperTestService papers, ClassroomService classrooms) : AppController
{
    private const long UploadLimit = PaperFileStore.MaxBytes + 1024 * 1024;

    public async Task<IActionResult> Create(int classroomId)
    {
        await classrooms.EnsureLecturerAsync(classroomId, UserId);
        return View(new PaperInput { ClassroomId = classroomId });
    }

    [HttpPost, RequestSizeLimit(UploadLimit), RequestFormLimits(MultipartBodyLengthLimit = UploadLimit)]
    public Task<IActionResult> Create(PaperInput input, IFormFile? paper) => KeepFormOnErrorAsync(async () =>
    {
        var a = await papers.CreateAsync(input.ClassroomId, new PaperSettings(input.Title, VnTime.FromInput(input.OpensAt), input.DurationMinutes,
            input.DeviceLock, input.MonitorScreen, input.MonitorScreen ? input.AutoSubmitLeaves : null, input.AudiencePolicy), paper, UserId);
        Success("Đã giao bài làm theo đề. Bấm \"Bắt đầu\" khi cả lớp sẵn sàng.");
        return RedirectToAction("Details", "Assignments", new { id = a.Id });
    }, () => Task.FromResult<IActionResult>(View(input)));

    [HttpPost, RequestSizeLimit(UploadLimit), RequestFormLimits(MultipartBodyLengthLimit = UploadLimit)]
    public async Task<IActionResult> Replace(int id, IFormFile? paper)
    {
        await papers.ReplacePaperAsync(id, paper, UserId);
        Success("Đã đổi đề.");
        return RedirectToAction("Details", "Assignments", new { id });
    }

    // Đề (PDF) hiện ngay trong trang làm bài; GV của lớp và người có lượt của bài mới mở được.
    [AllowGuest]
    public async Task<IActionResult> Exam(int id)
    {
        var (file, name) = await papers.OpenPaperAsync(id, UserId);
        Response.Headers.ContentDisposition = $"inline; filename*=UTF-8''{Uri.EscapeDataString(name)}";
        return File(file, "application/pdf");
    }

    [AllowGuest]
    public async Task<IActionResult> File(long id)
    {
        var (file, info) = await papers.OpenFileAsync(id, UserId);
        // PDF/ảnh xem được ngay; Word luôn tải về. Content-Type lấy theo chữ ký tệp lúc nhận (kèm nosniff toàn trang).
        var inline = info.ContentType is "application/pdf" or "image/jpeg" or "image/png" or "image/webp";
        Response.Headers.ContentDisposition = $"{(inline ? "inline" : "attachment")}; filename*=UTF-8''{Uri.EscapeDataString(info.FileName)}";
        return File(file, info.ContentType);
    }

    [HttpPost, AllowGuest]
    public async Task<IActionResult> Save(long id, string? text) =>
        Json(new { savedAt = VnTime.Format(await papers.SaveTextAsync(id, text, UserId)) });

    [HttpPost, AllowGuest, RequestSizeLimit(UploadLimit), RequestFormLimits(MultipartBodyLengthLimit = UploadLimit)]
    public async Task<IActionResult> Upload(long id, IFormFile? file)
    {
        var row = await papers.AddFileAsync(id, file, UserId);
        return Json(new { id = row.Id, name = row.FileName, size = row.SizeBytes, url = Url.Action(nameof(File), new { id = row.Id }) });
    }

    [HttpPost, AllowGuest]
    public async Task<IActionResult> DeleteFile(long id, long fileId)
    {
        await papers.DeleteFileAsync(id, fileId, UserId);
        return Json(new { ok = true });
    }

    // GV: danh sách bài nộp + chấm.
    public async Task<IActionResult> Submissions(int id)
    {
        var (a, rows) = await papers.ListAsync(id, UserId);
        ViewData["Rows"] = rows;
        return View(a);
    }

    public async Task<IActionResult> Grade(long id)
    {
        var (attempt, submission, a) = await papers.GetForGradingAsync(id, UserId);
        ViewData["Submission"] = submission;
        ViewData["Assignment"] = a;
        return View(attempt);
    }

    [HttpPost]
    public Task<IActionResult> Grade(long id, decimal? score, string? comment) => KeepFormOnErrorAsync(async () =>
    {
        await papers.GradeAsync(id, score, comment, UserId);
        Success("Đã lưu điểm.");
        var assignmentId = await papers.AssignmentIdOfAsync(id);
        return RedirectToAction(nameof(Submissions), null, new { id = assignmentId }, $"bai-{id}");
    }, async () =>
    {
        ViewData["ScoreInput"] = score; ViewData["CommentInput"] = comment;
        return await Grade(id);
    });
}
