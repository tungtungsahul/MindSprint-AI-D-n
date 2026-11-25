using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MindSprintAI_ASP.NET_MVC.Data.Entities;
using MindSprintAI_ASP.NET_MVC.Models;
using MindSprintAI_ASP.NET_MVC.Services;

namespace MindSprintAI_ASP.NET_MVC.Controllers;

[Authorize(Policy = "SystemAdmin")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class AdminController(AdminService admin, AccountLockService locks, ContentReportService reports, ClassTransferService transfers,
    UnhideRequestService unhide) : AppController
{
    // (NK5) Cùng guard SystemAdmin và AdminTwoFactorMiddleware với toàn khu Admin.
    [HttpGet]
    public async Task<IActionResult> Audit([FromQuery] AuditFilter filter, [FromServices] AuditQueryService logs) =>
        View(await logs.QueryAsync(UserId, filter));

    public async Task<IActionResult> ClassTransfers() => View(await transfers.AdminClassesAsync(UserId));

    [HttpPost]
    public async Task<IActionResult> TransferClass(int id, string email)
    {
        await transfers.AdminOfferAsync(id, email, UserId);
        Success(InvitationService.SentMessage);
        return RedirectToAction(nameof(ClassTransfers));
    }
    public async Task<IActionResult> Index(string? search, int page = 1)
    {
        ViewData["PendingUnhideCount"] = await unhide.PendingCountAsync(UserId);
        ViewData["DemoJob"] = HttpContext.RequestServices.GetService<DemoJobRunner>()?.Current;
        return View(await admin.UsersAsync(UserId, search, page));
    }

    // Dữ liệu mẫu lớp học (17/11): lớp mẫu thuộc chính quản trị viên đang bấm. Chạy nền vì lâu hơn giới hạn chờ của nginx.
    [HttpPost]
    public IActionResult SeedClassDemo([FromServices] DemoJobRunner jobs)
    {
        // Việc chạy nền sau khi yêu cầu này đã xong: chỉ dùng userId được truyền vào, không đọc lại User/HttpContext (lỗi trên server 17/11).
        var started = jobs.TryStart("Tạo dữ liệu mẫu", UserId, async (sp, userId) =>
        {
            var r = await sp.GetRequiredService<ClassDemoSeedService>().SeedAsync(userId);
            return ($"Đã tạo {r.Students} học viên mẫu, 2 giảng viên mẫu, {r.Classes} lớp, {r.Assignments} bài giao, {r.Attempts} lượt làm. Vào Lớp tôi dạy để xem.", r.Password);
        });
        Success(started ? "Đang tạo dữ liệu mẫu (khoảng 1–2 phút). Tải lại trang này để xem kết quả." : "Đang có một việc dữ liệu mẫu chạy, hãy chờ xong.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public IActionResult RemoveClassDemo([FromServices] DemoJobRunner jobs)
    {
        var started = jobs.TryStart("Xóa dữ liệu mẫu", UserId, async (sp, userId) =>
        {
            var n = await sp.GetRequiredService<ClassDemoSeedService>().RemoveAsync(userId, sp.GetRequiredService<AccountDeletionService>(),
                sp.GetRequiredService<ClassroomService>(), sp.GetRequiredService<AssignmentService>(), sp.GetRequiredService<StudyLibraryService>());
            return ($"Đã xóa dữ liệu mẫu: các lớp và bộ học mẫu, {n} tài khoản mẫu.", null);
        });
        Success(started ? "Đang xóa dữ liệu mẫu. Tải lại trang này sau ít phút để xem kết quả." : "Đang có một việc dữ liệu mẫu chạy, hãy chờ xong.");
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> UnhideRequests() => View(await unhide.PendingAsync(UserId));

    // Ghi rõ tên view: DecideUnhide gọi lại hàm này để hiện lại form khi bị từ chối.
    public async Task<IActionResult> UnhideRequest(int id) => View(nameof(UnhideRequest), await unhide.GetAsync(id, UserId));

    [HttpPost]
    public Task<IActionResult> DecideUnhide(int id, bool approve, string? note) => KeepFormOnErrorAsync(async () =>
    {
        await unhide.DecideAsync(id, approve, note, UserId);
        Success(approve ? "Đã bỏ ẩn bộ học." : "Đã từ chối yêu cầu.");
        return RedirectToAction(nameof(UnhideRequests));
    }, async () =>
    {
        ViewData[approve ? "ApprovalNote" : "RejectionNote"] = note;
        return await UnhideRequest(id);
    });

    public async Task<IActionResult> Lock(string id, int? reportId)
    {
        if (reportId != null) id = await reports.PendingOwnerAsync(reportId.Value, UserId);
        var target = await admin.UserAsync(id, UserId);
        return View(new AdminLockModel { UserId = target.Id, DisplayName = target.DisplayName,
            ReportId = reportId, Kind = reportId == null ? AccountLockKind.Protective : AccountLockKind.Violation });
    }

    [HttpPost]
    public async Task<IActionResult> Lock(AdminLockModel model)
    {
        if (!ModelState.IsValid) throw new BusinessException("Ngày giờ không hợp lệ. Nhập theo giờ Việt Nam.");
        if (model.ReportId is { } rid)
            await reports.LockReportedUserAsync(rid, model.Reason, VnTime.FromInput(model.EndsAt), UserId, locks);
        else await locks.LockAsync(model.UserId, model.Kind, model.Reason, VnTime.FromInput(model.EndsAt), UserId);
        Success("Đã khóa tài khoản và dừng các lượt đang làm.");
        return RedirectToAction(nameof(Index));
    }

    // Pháp lý C9: xóa thay khi người dùng gửi yêu cầu qua email; cùng điều kiện chặn như tự xóa.
    public async Task<IActionResult> DeleteUser(string id, [FromServices] AccountDeletionService deletion, string? reason = null)
    {
        var target = await admin.UserAsync(id, UserId);
        ViewData["Block"] = target.DeletedAt == null ? await deletion.BlockReasonAsync(id, byAdmin: true) : "Tài khoản này đã được xóa.";
        return View(new AdminDeleteUserModel { UserId = target.Id, DisplayName = target.DisplayName, Email = target.Email, Reason = reason });
    }

    [HttpPost]
    public Task<IActionResult> DeleteUser(AdminDeleteUserModel model, [FromServices] AccountDeletionService deletion) => KeepFormOnErrorAsync(async () =>
    {
        await deletion.DeleteByAdminAsync(model.UserId, model.Reason, UserId);
        Success("Đã xóa tài khoản.");
        return RedirectToAction(nameof(Index));
    }, () => DeleteUser(model.UserId, deletion, model.Reason)); // giữ lý do vừa nhập khi bị chặn

    [HttpPost]
    public async Task<IActionResult> Unlock(string id)
    {
        await locks.UnlockAsync(id, UserId);
        Success("Đã mở khóa tài khoản.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Role(string id, bool grant, string? reason)
    {
        await admin.ChangeRoleAsync(id, grant, reason, UserId);
        Success("Đã cập nhật quyền quản trị viên.");
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> History()
    {
        var rows = await admin.HistoryAsync(UserId);
        ViewData["Names"] = await admin.UserLabelsAsync(rows.SelectMany(r => new[] { r.AdminId, r.TargetUserId }).Distinct().ToList());
        return View(rows);
    }
    public async Task<IActionResult> Reports(int page = 1)
    {
        ViewData["Page"] = Math.Max(1, page);
        return View(await reports.QueueAsync(UserId, page));
    }
    public async Task<IActionResult> Report(int id) => View(await reports.ViewAsync(id, UserId));

    [HttpPost]
    public async Task<IActionResult> HandleReport(int id, bool hide, string? reason)
    {
        await reports.HandleAsync(id, hide, reason, UserId);
        Success(hide ? "Đã ẩn bộ học và đóng ngay các bài giao đang chạy của bộ học này." : "Đã bác bỏ báo cáo.");
        return RedirectToAction(nameof(Reports));
    }
}
