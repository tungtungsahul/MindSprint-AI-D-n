namespace MindSprintAI_ASP.NET_MVC.Services;

// Tạo/xóa dữ liệu mẫu lớp học mất 1–2 phút, lâu hơn giới hạn chờ của nginx (60 giây): chạy nền, trang Quản trị hiện trạng thái.
// Mỗi lúc chỉ một việc. shortcut: trạng thái nằm trong bộ nhớ, khởi động lại app thì mất dòng kết quả (dữ liệu đã tạo vẫn còn).
public sealed class DemoJobRunner(IServiceScopeFactory scopes, ILogger<DemoJobRunner> log)
{
    public sealed record Status(string Label, string UserId, bool Running, string? Message, string? Password, bool Failed, DateTime At);

    private readonly object gate = new();
    public Status? Current { get; private set; }

    // work nhận userId thay vì tự đọc từ yêu cầu web: việc chạy sau khi yêu cầu đã kết thúc, HttpContext không còn dùng được.
    public bool TryStart(string label, string userId, Func<IServiceProvider, string, Task<(string Message, string? Password)>> work)
    {
        lock (gate)
        {
            if (Current is { Running: true }) return false;
            Current = new(label, userId, true, null, null, false, DateTime.UtcNow);
        }
        _ = Task.Run(async () =>
        {
            Status done;
            try
            {
                using var scope = scopes.CreateScope();
                var (message, password) = await work(scope.ServiceProvider, userId);
                done = new(label, userId, false, message, password, false, DateTime.UtcNow);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Việc dữ liệu mẫu {Label} thất bại", label);
                done = new(label, userId, false, ex is BusinessException ? ex.Message : "Có lỗi khi chạy. Xem nhật ký hệ thống.", null, true, DateTime.UtcNow);
            }
            lock (gate) Current = done;
        });
        return true;
    }
}
