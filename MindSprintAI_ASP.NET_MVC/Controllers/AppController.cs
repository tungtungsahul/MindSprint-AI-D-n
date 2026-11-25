using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MindSprintAI_ASP.NET_MVC.Controllers;

// Controller chỉ nhận yêu cầu và gọi service; lỗi nghiệp vụ do BusinessExceptionFilter xử lý.
[Authorize]
public abstract class AppController : Controller
{
    protected string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    protected void Success(string message) => TempData["Success"] = message;

    // (NK1, NK3) Các form tự giữ trang nhập liệu vẫn dùng cùng điểm ghi lỗi với filter.
    protected Task RecordRejectionAsync(Exception exception) =>
        HttpContext.RequestServices?.GetService<Services.AuditLog>()?.RejectedAsync(exception) ?? Task.CompletedTask;

    // Form dài (soạn câu, giao bài, nhập Excel): lỗi nghiệp vụ thì hiện lại đúng form với chữ vừa nhập thay vì
    // chuyển trang (BusinessExceptionFilter chuyển về trang trước nên mất hết ô đã điền).
    protected async Task<IActionResult> KeepFormOnErrorAsync(Func<Task<IActionResult>> action, Func<Task<IActionResult>> showAgain)
    {
        try { return await action(); }
        catch (Services.BusinessException ex)
        {
            await RecordRejectionAsync(ex);
            // Bỏ thay đổi chưa lưu mà service đã gắn vào DbContext trước khi từ chối, để trang hiện lại đọc đúng dữ liệu thật.
            HttpContext.RequestServices?.GetService<Data.AppDbContext>()?.ChangeTracker.Clear();
            TempData["Error"] = ex.Message;
            // Form biết là đang hiện lại sau lỗi (ví dụ Giao bài mở sẵn mục "Cài đặt thêm" vì lỗi có thể nằm trong đó).
            ModelState.AddModelError(string.Empty, ex.Message);
            return await showAgain();
        }
    }
}
