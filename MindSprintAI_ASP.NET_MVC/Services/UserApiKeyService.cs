using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using MindSprintAI_ASP.NET_MVC.Data;
using MindSprintAI_ASP.NET_MVC.Data.Entities;

namespace MindSprintAI_ASP.NET_MVC.Services;

public sealed class UserApiKeyService(AppDbContext db, IDataProtectionProvider protection, TimeProvider clock)
{
    public const string Purpose = "MindSprintAI.Gemini.UserApiKey.v1";
    public const string AiNoticeMissing = "Trước khi dùng trợ lý AI, hãy đọc thông báo và đồng ý trong trang Tài khoản.";
    private readonly IDataProtector protector = protection.CreateProtector(Purpose);

    // Hai loại key của Google: "AIza…" (standard, cũ) và "AQ.…" (auth key, AI Studio tạo mặc định từ 28/05/2026) có dấu chấm.
    public static bool ValidFormat(string? key) => key is { Length: >= 20 and <= 200 }
        && key.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.');

    // (Hướng 2) người dùng hay dán kèm dấu cách, xuống dòng, ngoặc kép, ký tự ẩn hoặc cả đoạn chữ: lấy đúng phần key.
    public static string? NormalizePasted(string? pasted)
    {
        if (pasted == null) return null;
        // Ký tự ẩn (zero-width, BOM…) hay dính theo khi sao chép từ trang web.
        pasted = new string(pasted.Where(c => char.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.Format).ToArray());
        var google = System.Text.RegularExpressions.Regex.Match(pasted, @"AIza[0-9A-Za-z_\-]{35}|AQ\.[0-9A-Za-z_\-.]{16,197}");
        if (google.Success) return google.Value.TrimEnd('.');
        return new string(pasted.Where(c => !char.IsWhiteSpace(c) && c is not '"' and not '\'' and not '`').ToArray());
    }

    // Báo ký tự nào sai để người dùng tự sửa, không lặp lại chữ/số của key.
    public static string FormatError(string? key)
    {
        if (string.IsNullOrEmpty(key)) return "Chưa nhận được API key Gemini. Hãy dán API key Gemini vào ô rồi bấm Lưu.";
        var bad = key.Where(c => !(char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.')).Distinct()
            .ToList();
        if (bad.Any(c => char.IsControl(c) || char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.Format || char.IsLetterOrDigit(c)))
            return "Mã có ký tự vô hình hoặc chữ có dấu. Hãy sao chép lại mã từ trang Google.";
        var length = key.Length is < 20 or > 200 ? $" API key Gemini nhận được dài {key.Length} ký tự (cần 20–200)." : "";
        var chars = bad.Count > 0 ? $" Có ký tự không dùng được: {string.Join(", ", bad.Select(c => $"“{c}”"))}." : "";
        return "API key Gemini không đúng định dạng." + length + chars + " Hãy bấm Copy key (sao chép mã) trong Google AI Studio rồi dán lại.";
    }

    public async Task EnsureAccountAsync(string userId)
    {
        if (!await db.Users.AnyAsync(u => u.Id == userId && !u.IsGuest)) throw new ForbiddenException();
    }

    public async Task<string?> LastFourAsync(string userId)
    {
        await EnsureAccountAsync(userId);
        return await db.UserApiKeys.Where(k => k.UserId == userId).Select(k => k.LastFour).SingleOrDefaultAsync();
    }

    // Key lưu trước khi có thông báo C5 (rỗng = chưa đồng ý; không có key = null).
    public async Task<bool?> NoticeAcceptedAsync(string userId)
    {
        await EnsureAccountAsync(userId);
        return await db.UserApiKeys.Where(k => k.UserId == userId).Select(k => (bool?)(k.AiNoticeAcceptedAt != null)).SingleOrDefaultAsync();
    }

    public async Task AcceptNoticeAsync(string userId, bool noticeAccepted)
    {
        await EnsureAccountAsync(userId);
        if (!noticeAccepted) throw new BusinessException("Hãy tick vào ô \"Tôi đã hiểu và đồng ý\" để tiếp tục.");
        var row = await db.UserApiKeys.FindAsync(userId) ?? throw new BusinessException("Nhập API key Gemini để dùng trợ lý.");
        row.AiNoticeAcceptedAt ??= clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync();
    }

    public async Task SaveAsync(string? apiKey, string userId, bool noticeAccepted)
    {
        await EnsureAccountAsync(userId);
        // Pháp lý C5: phải đồng ý thông báo gửi sang Google Gemini ở nước ngoài thì mới lưu key.
        if (!noticeAccepted) throw new BusinessException("Hãy tick vào ô \"Tôi đã hiểu và đồng ý\" trước khi lưu API key Gemini.");
        apiKey = NormalizePasted(apiKey);
        if (!ValidFormat(apiKey)) throw new BusinessException(FormatError(apiKey));
        var row = await db.UserApiKeys.FindAsync(userId);
        if (row == null) { row = new() { UserId = userId }; db.UserApiKeys.Add(row); }
        row.EncryptedKey = protector.Protect(apiKey!); row.LastFour = apiKey![^4..];
        row.CreatedAt = clock.GetUtcNow().UtcDateTime; row.LastUsedAt = null;
        row.AiNoticeAcceptedAt = row.CreatedAt;
        db.Audit?.Add(AuditAction.GeminiSaved, "User", userId, userId); // (NK3)
        await db.SaveChangesAsync();
    }

    internal async Task<string> UseAsync(string userId)
    {
        await EnsureAccountAsync(userId);
        var row = await db.UserApiKeys.FindAsync(userId) ?? throw new BusinessException("Nhập API key Gemini để dùng trợ lý.");
        // Key lưu từ trước khi có thông báo C5: không gửi gì sang Gemini cho tới khi người dùng đồng ý.
        if (row.AiNoticeAcceptedAt == null) throw new BusinessException(AiNoticeMissing);
        string key;
        try { key = protector.Unprotect(row.EncryptedKey); }
        catch (System.Security.Cryptography.CryptographicException)
        { throw new BusinessException("Không đọc được API key Gemini đã lưu. Hãy nhập lại API key Gemini."); }
        row.LastUsedAt = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync();
        return key;
    }

    public async Task DeleteAsync(string userId)
    {
        await EnsureAccountAsync(userId);
        await using var tx = await db.Database.BeginTransactionAsync(); // (NK3)
        await db.UserApiKeys.Where(k => k.UserId == userId).ExecuteDeleteAsync();
        // Xóa cả entity đang theo dõi để request sau không đọc lại giá trị cũ.
        foreach (var entry in db.ChangeTracker.Entries<UserApiKey>().ToList()) entry.State = EntityState.Detached;
        db.Audit?.Add(AuditAction.GeminiDeleted, "User", userId, userId); // (NK3)
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }
}
