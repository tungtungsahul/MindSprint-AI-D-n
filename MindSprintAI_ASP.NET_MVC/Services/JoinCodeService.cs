using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using MindSprintAI_ASP.NET_MVC.Data;
using MindSprintAI_ASP.NET_MVC.Data.Entities;

namespace MindSprintAI_ASP.NET_MVC.Services;

// Mã 8 số ngẫu nhiên, không bao giờ trùng mã đã từng cấp (39, 44).
public class JoinCodeService(AppDbContext db, TimeProvider clock)
{
    public async Task<JoinCode> IssueAsync(JoinCodeTarget target, int targetId)
    {
        for (var i = 0; i < 20; i++)
        {
            var code = RandomNumberGenerator.GetInt32(0, 100_000_000).ToString("D8");
            if (await db.JoinCodes.AnyAsync(c => c.Code == code)) continue;
            var row = new JoinCode { Code = code, TargetType = target, TargetId = targetId };
            db.JoinCodes.Add(row);
            return row; // unique index trên Code là chốt chặn cuối khi hai yêu cầu trùng nhau
        }
        throw new InvalidOperationException("Không sinh được mã mới sau 20 lần thử.");
    }

    public Task<JoinCode?> GetActiveAsync(JoinCodeTarget target, int targetId) =>
        db.JoinCodes.FirstOrDefaultAsync(c => c.TargetType == target && c.TargetId == targetId && c.IsActive);

    private async Task<JoinCode?> UndoCandidateAsync(JoinCodeTarget target, int targetId)
    {
        var current = await db.JoinCodes.AsNoTracking().SingleOrDefaultAsync(c => c.TargetType == target
            && c.TargetId == targetId && c.IsActive);
        // Mã được khôi phục giữ ReplacedAt: đánh dấu đã hoàn tác, không lùi tiếp theo chuỗi cũ.
        if (current == null || current.ReplacedAt != null) return null;
        var cutoff = clock.GetUtcNow().UtcDateTime.AddHours(-48);
        return await db.JoinCodes.AsNoTracking().SingleOrDefaultAsync(c => c.TargetType == target && c.TargetId == targetId
            && !c.IsActive && c.ReplacedByCodeId == current.Id && c.ReplacedAt >= cutoff);
    }

    public async Task<DateTime?> UndoUntilAsync(JoinCodeTarget target, int targetId) =>
        (await UndoCandidateAsync(target, targetId))?.ReplacedAt?.AddHours(48);

    // Bên gọi giữ khóa lớp/bài giao và giao dịch, giống ReplaceAsync.
    public async Task<JoinCode> UndoAsync(JoinCodeTarget target, int targetId)
    {
        var candidate = await UndoCandidateAsync(target, targetId)
            ?? throw new BusinessException("Không dùng lại mã cũ được: đã quá 48 giờ hoặc mã cũ đã được dùng lại rồi.");
        var current = (await GetActiveAsync(target, targetId))!;
        await db.Entry(current).ReloadAsync();
        current.IsActive = false;
        current.ReplacedAt = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync();
        var old = (await db.JoinCodes.FindAsync(candidate.Id))!;
        await db.Entry(old).ReloadAsync();
        old.IsActive = true;
        await db.SaveChangesAsync();
        return old;
    }

    // "Đổi mã": mã cũ hết hiệu lực ngay (44). Gọi SaveChanges ở nơi gọi.
    public async Task<JoinCode> ReplaceAsync(JoinCodeTarget target, int targetId)
    {
        var old = await GetActiveAsync(target, targetId);
        if (old != null)
        {
            old.IsActive = false;
            old.ReplacedAt = clock.GetUtcNow().UtcDateTime;
            await db.SaveChangesAsync(); // tắt mã cũ trước để không vướng index "một mã hoạt động"
        }
        var fresh = await IssueAsync(target, targetId);
        await db.SaveChangesAsync();
        if (old != null)
        {
            old.ReplacedByCodeId = fresh.Id;
            await db.SaveChangesAsync();
        }
        return fresh;
    }

    public async Task<JoinCode> ResolveAsync(string? input)
    {
        var code = (input ?? "").Trim();
        if (code.Length != 8 || !code.All(char.IsAsciiDigit))
            throw new BusinessException("Mã gồm đúng 8 chữ số.");
        var row = await db.JoinCodes.FirstOrDefaultAsync(c => c.Code == code)
            ?? throw new BusinessException("Mã không tồn tại. Hãy hỏi giảng viên mã mới.");
        if (!row.IsActive) throw new BusinessException("Mã không tồn tại. Hãy hỏi giảng viên mã mới.");
        return row;
    }
}
