using System.Security.Cryptography;
using System.Text;
using System.Net;
using Microsoft.EntityFrameworkCore;
using MindSprintAI_ASP.NET_MVC.Data;
using MindSprintAI_ASP.NET_MVC.Data.Entities;

namespace MindSprintAI_ASP.NET_MVC.Services;

public class JoinCodeGuardService(AppDbContext db, JoinCodeService codes,
    TimeProvider clock, TurnstileService turnstile)
{
    // (73) Người đăng nhập: 10 lần sai theo tài khoản. Khách (người dùng chốt 13/11/2026): 10 lần sai theo trình duyệt,
    // và 50 lần sai theo khu vực mạng — cả giảng đường chung Wi-Fi trường là một khu vực mạng, vài bạn gõ nhầm
    // không được bắt cả lớp xác minh; kẻ dò mã xóa mã trình duyệt để lách vẫn bị chặn ở mức khu vực mạng.
    public const int UserLimit = 10, BrowserLimit = 10, NetworkLimit = 50;
    private const int UserKind = 1, NetworkKind = 2, BrowserKind = 3;
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    private static (int Kind, string Hash, int Limit) Counter(int kind, string key, int limit) =>
        (kind, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("MindSprintAI:73:" + kind + ":" + key))), limit); // chỉ lưu SHA-256

    private static List<(int Kind, string Hash, int Limit)> Counters(string? userId, string? remoteIp, string? browserId)
    {
        if (userId != null) return [Counter(UserKind, userId, UserLimit)];
        var network = DeviceSignalService.Prefix(IPAddress.TryParse(remoteIp, out var ip) ? ip : null) ?? "unknown-network";
        var list = new List<(int, string, int)> { Counter(NetworkKind, network, NetworkLimit) };
        if (!string.IsNullOrEmpty(browserId)) list.Add(Counter(BrowserKind, browserId, BrowserLimit));
        return list;
    }

    private bool Over(JoinCodeFailure? row, int limit) => row != null && row.LastFailedAt > Now.AddHours(-24) && row.FailCount >= limit;

    public async Task<bool> RequiresAsync(string? userId, string? remoteIp, string? browserId = null)
    {
        foreach (var (kind, hash, limit) in Counters(userId, remoteIp, browserId))
        {
            var row = await db.JoinCodeFailures.AsNoTracking().SingleOrDefaultAsync(x => x.KeyType == kind && x.KeyHash == hash);
            // (73) đạt ngưỡng thì lần nhập kế tiếp phải xác minh; 24 giờ không sai thì bắt đầu lại.
            if (Over(row, limit)) return true;
        }
        return false;
    }

    public async Task<JoinCode> ResolveAsync(string? code, string? userId, string? token, string? remoteIp, string? browserId = null)
    {
        // Thử tải P3 14/11/2026: khóa bộ đếm của cả Wi-Fi ở MỌI lần nhập mã (kể cả mã đúng) → cả giảng đường xếp một hàng,
        // 12/650 người lỗi 500. Người dùng duyệt: kiểm ngưỡng bằng đọc không khóa; chỉ nhập SAI mới khóa bộ đếm để cộng.
        if (await RequiresAsync(userId, remoteIp, browserId))
        {
            var verified = await turnstile.VerifyAsync(token, remoteIp);
            if (!verified.Verified) throw new BusinessException(verified.Error!);
        }
        // Quy tắc 5b (người dùng chốt 12/11): mã đúng không đặt lại bộ đếm, với cả khách lẫn người đã đăng nhập; bộ đếm tự hết sau 24 giờ không sai.
        try { return await codes.ResolveAsync(code); }
        catch (BusinessException)
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            foreach (var (kind, hash, _) in Counters(userId, remoteIp, browserId))
            {
                // (73) khóa cả khoảng trống của unique index để hai tab cùng sai không làm mất lần sai.
                await db.Database.ExecuteSqlInterpolatedAsync($"SELECT [Id] FROM [JoinCodeFailures] WITH (UPDLOCK, HOLDLOCK) WHERE [KeyType] = {kind} AND [KeyHash] = {hash}");
                var row = await db.JoinCodeFailures.SingleOrDefaultAsync(x => x.KeyType == kind && x.KeyHash == hash);
                if (row != null) await db.Entry(row).ReloadAsync();
                else db.JoinCodeFailures.Add(row = new JoinCodeFailure { KeyType = kind, KeyHash = hash });
                if (row.LastFailedAt <= Now.AddHours(-24)) row.FailCount = 0;
                if (row.FailCount == 0) row.FirstFailedAt = Now;
                row.FailCount++; row.LastFailedAt = Now;
            }
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            throw;
        }
    }
}
