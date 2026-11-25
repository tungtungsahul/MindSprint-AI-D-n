using Microsoft.AspNetCore.Mvc;
using MindSprintAI_ASP.NET_MVC.Models;
using MindSprintAI_ASP.NET_MVC.Services;

namespace MindSprintAI_ASP.NET_MVC.Controllers;

public class ReportsController(ContentReportService reports) : AppController
{
    public async Task<IActionResult> Index([FromServices] AssignmentService assignments, int page = 1, string? status = null, string? q = null, int? classId = null) =>
        View(await assignments.GetReportPageAsync(UserId, page, status, q, classId));

    public async Task<IActionResult> Create([FromQuery] ReportContext context)
    {
        ViewData["TargetName"] = await reports.ValidateTargetAsync(context, UserId);
        return View(new ReportForm { Context = context });
    }

    [HttpPost]
    public async Task<IActionResult> Create(ReportForm model)
    {
        if (!ModelState.IsValid) throw new BusinessException("Dữ liệu báo cáo không hợp lệ.");
        await reports.SubmitAsync(model.Context, model.Reason, model.Note, UserId);
        return View("../Account/Message", "Đã gửi báo cáo cho quản trị viên. Nếu đã có báo cáo cùng nội dung đang chờ xử lý, hai báo cáo được gộp làm một.");
    }
}
