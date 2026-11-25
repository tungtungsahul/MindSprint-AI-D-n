using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using MindSprintAI_ASP.NET_MVC.Data;

namespace MindSprintAI_ASP.NET_MVC.Services;

// (NK4) Xóa theo lô; không tải bảng nhật ký vào bộ nhớ.
public sealed class LogRetentionWorker(IServiceScopeFactory scopes, TimeProvider clock,
    IConfiguration config, IWebHostEnvironment env, ILogger<LogRetentionWorker> logger) : BackgroundService
{
    public static async Task<int> PurgeAsync(AppDbContext db, DateTime now, CancellationToken ct = default)
    {
        var cutoff = now.AddDays(-365); var total = 0;
        while (true)
        {
            var ids = db.AuditEntries.Where(e => e.At < cutoff).OrderBy(e => e.Id).Take(5000).Select(e => e.Id);
            var count = await db.AuditEntries.Where(e => ids.Contains(e.Id)).ExecuteDeleteAsync(ct);
            total += count;
            if (count < 5000) return total;
        }
    }
    public static int PurgeFiles(string directory, DateTime now)
    {
        if (!Directory.Exists(directory)) return 0;
        var count = 0;
        foreach (var file in Directory.EnumerateFiles(directory, "mindsprint-*.log"))
        {
            var match = Regex.Match(Path.GetFileName(file), @"^mindsprint-(\d{8})(?:_\d+)?\.log$");
            // (NK4) Dùng ngày ghi trên tên file: không xóa nhầm file mới vừa chép sang máy.
            if (!match.Success || !DateTime.TryParseExact(match.Groups[1].Value, "yyyyMMdd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var date) || date.Date >= now.Date.AddDays(-30)) continue;
            File.Delete(file); count++;
        }
        return count;
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromDays(1), clock);
        while (!stoppingToken.IsCancellationRequested)
        {
            using var run = new WorkerRun(logger, nameof(LogRetentionWorker)); // (NK1, NK4)
            try
            {
                using var scope = scopes.CreateScope();
                var now = clock.GetUtcNow().UtcDateTime;
                var rows = await PurgeAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(), now, stoppingToken);
                var files = PurgeFiles(SystemLogging.DirectoryPath(config, env.ContentRootPath), now);
                run.Count = rows + files;
                logger.LogInformation("Log retention run finished: {Rows} rows, {Files} files", rows, files);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch { run.Failed = true; logger.LogWarning("Log retention run failed"); }
            run.Dispose();
            try { if (!await timer.WaitForNextTickAsync(stoppingToken)) return; }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
        }
    }
}
