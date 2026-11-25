using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using MindSprintAI_ASP.NET_MVC.Data;
using MindSprintAI_ASP.NET_MVC.Data.Entities;

namespace MindSprintAI_ASP.NET_MVC.Services;

public class AvatarService(AppDbContext db, TimeProvider clock, IWebHostEnvironment? env = null)
{
    public const string GooglePictureClaim = "urn:google:picture";

    public static string? GoogleUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 500
            || !Uri.TryCreate(value, UriKind.Absolute, out var uri) || !uri.IsWellFormedOriginalString()
            || uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length != 0
            || Regex.IsMatch(value, @"^https://[^/?#]*@", RegexOptions.IgnoreCase)
            || !uri.Host.EndsWith(".googleusercontent.com", StringComparison.OrdinalIgnoreCase)) return null;
        // Chỉ thay cỡ khi Google đã trả dạng hỗ trợ =s…; giữ phần cắt ảnh và chuỗi truy vấn.
        var path = Regex.Replace(uri.AbsolutePath, @"=s[0-9]+(?=(?:-[a-zA-Z0-9]+)*$)", "=s128");
        var url = uri.GetLeftPart(UriPartial.Authority) + path + uri.Query + uri.Fragment;
        return url.Length <= 500 ? url : null;
    }

    public static bool CaptureGoogle(ApplicationUser user, string provider, ClaimsPrincipal principal, DateTime now)
    {
        // Ảnh tự tải lên thì giữ, không bị ảnh Google thay mỗi lần đăng nhập.
        if (provider != "Google" || user.AvatarSource == AvatarSource.Upload || GoogleUrl(principal.FindFirstValue(GooglePictureClaim)) is not { } url) return false;
        user.AvatarUrl = url;
        user.AvatarSource = AvatarSource.Google;
        user.AvatarUpdatedAt = now;
        return true;
    }

    public static void Clear(ApplicationUser user, DateTime now)
    {
        user.AvatarUrl = null;
        user.AvatarSource = AvatarSource.None;
        user.AvatarUpdatedAt = now;
    }

    public async Task SetVisibleAsync(string userId, bool enabled)
    {
        var user = await OwnerAsync(userId);
        user.AvatarHidden = !enabled;
        await db.SaveChangesAsync();
    }

    public async Task RemoveAsync(string userId)
    {
        Clear(await OwnerAsync(userId), clock.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync();
        DeleteFile(env, userId);
    }

    // "Đổi ảnh đại diện" của nhóm (JPG/PNG/WebP ≤ 5 MB, thu về 256×256). Nhóm lưu ảnh trong trình duyệt (localStorage) nên đổi máy là mất;
    // ở đây trình duyệt vẫn thu nhỏ ảnh (js/avatar-upload.js) rồi gửi lên, máy chủ lưu ở App_Data/avatars (ổ dữ liệu, có sao lưu)
    // và chỉ trả lại cho chính chủ. Máy chủ không tự xử lý ảnh: chỉ nhận đúng 3 loại theo chữ ký tệp và giới hạn cỡ ảnh đã thu nhỏ.
    public const int MaxUploadBytes = 300_000;
    public static string? ImageType(ReadOnlySpan<byte> b) =>
        b.Length > 12 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF ? "image/jpeg"
        : b.Length > 12 && b[..8].SequenceEqual((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]) ? "image/png"
        : b.Length > 12 && b[..4].SequenceEqual("RIFF"u8) && b[8..12].SequenceEqual("WEBP"u8) ? "image/webp" : null;

    private static string? FilePath(IWebHostEnvironment? env, string userId) =>
        env == null || !Guid.TryParse(userId, out var id) ? null : Path.Combine(env.ContentRootPath, "App_Data", "avatars", id.ToString("N") + ".img");
    public static void DeleteFile(IWebHostEnvironment? env, string userId)
    {
        if (FilePath(env, userId) is { } path && File.Exists(path)) File.Delete(path);
    }

    public async Task UploadAsync(string userId, IFormFile? file)
    {
        if (file == null || file.Length == 0) throw new BusinessException("Hãy chọn một ảnh JPG, PNG hoặc WebP.");
        if (file.Length > MaxUploadBytes) throw new BusinessException("Ảnh quá lớn. Hãy chọn ảnh JPG, PNG hoặc WebP tối đa 5 MB.");
        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer);
        var bytes = buffer.ToArray();
        if (ImageType(bytes) == null) throw new BusinessException("Chỉ nhận ảnh JPG, PNG hoặc WebP.");
        var user = await OwnerAsync(userId);
        var path = FilePath(env, userId) ?? throw new ForbiddenException();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, bytes);
        user.AvatarUrl = null;
        user.AvatarSource = AvatarSource.Upload;
        user.AvatarHidden = false;
        user.AvatarUpdatedAt = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync();
    }

    public async Task<(byte[] Bytes, string Type)?> ReadAsync(string userId)
    {
        var user = await OwnerAsync(userId);
        if (user.AvatarSource != AvatarSource.Upload || FilePath(env, userId) is not { } path || !File.Exists(path)) return null;
        var bytes = await File.ReadAllBytesAsync(path);
        return ImageType(bytes) is { } type ? (bytes, type) : null;
    }

    private async Task<ApplicationUser> OwnerAsync(string userId) =>
        await db.Users.SingleOrDefaultAsync(u => u.Id == userId && !u.IsGuest) ?? throw new ForbiddenException();
}
