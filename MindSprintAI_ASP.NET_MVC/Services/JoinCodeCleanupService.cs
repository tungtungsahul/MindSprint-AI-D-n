using Microsoft.EntityFrameworkCore;
using MindSprintAI_ASP.NET_MVC.Data;

namespace MindSprintAI_ASP.NET_MVC.Services;

// (73, ERD 3.2) chỉ dọn bộ đếm cũ; lịch sử khóa và báo cáo luôn được giữ lại.
public sealed class JoinCodeCleanupService(IServiceScopeFactory scopes, TimeProvider clock, ILogger<JoinCodeCleanupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromDays(1), clock);
        while (!stoppingToken.IsCancellationRequested)
        {
            using var run = new WorkerRun(logger, nameof(JoinCodeCleanupService)); // (NK1)
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var cutoff = clock.GetUtcNow().UtcDateTime.AddDays(-30);
                run.Count = await db.JoinCodeFailures.Where(x => x.LastFailedAt < cutoff).ExecuteDeleteAsync(stoppingToken);
                // Pháp lý: máy 6 tháng, đăng ký dở 7 ngày; lỗi riêng để không che việc dọn bộ đếm và ghi đúng việc nào lỗi.
                try { run.Count += await RetentionCleanup.RunAsync(db, clock.GetUtcNow().UtcDateTime, stoppingToken); }
                catch (Exception ex) when (ex is not OperationCanceledException) { run.Failed = true; logger.LogWarning(ex, "Chưa dọn được máy cũ và tài khoản đăng ký dở; sẽ thử lại sau."); }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex) { run.Failed = true; logger.LogWarning(ex, "Chưa dọn được bộ đếm mã cũ; sẽ thử lại sau."); }
            run.Dispose();
            try { if (!await timer.WaitForNextTickAsync(stoppingToken)) return; }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
        }
    }
}
