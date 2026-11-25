using System.Diagnostics;
using System.IO.Compression;

namespace MindSprintAI_ASP.NET_MVC.Services;

// Tệp của bài làm theo đề: đề GV (luôn lưu PDF) và tệp học viên đính kèm. Nằm ngoài wwwroot (App_Data/assignment-files),
// chỉ phát qua controller đã kiểm quyền. Loại tệp xét theo CHỮ KÝ nội dung, không tin đuôi tệp hay Content-Type trình duyệt gửi.
public class PaperFileStore(IWebHostEnvironment env, IConfiguration config, ILogger<PaperFileStore> logger)
{
    public const long MaxBytes = 20L * 1024 * 1024;
    public static readonly string[] PaperTypes = [".pdf", ".docx"];
    public static readonly string[] AnswerTypes = [".pdf", ".docx", ".jpg", ".jpeg", ".png", ".webp"];
    private static readonly SemaphoreSlim ConvertGate = new(1, 1);   // LibreOffice: mỗi lần đổi một tệp

    private string Root => Path.Combine(env.ContentRootPath, "App_Data", "assignment-files");
    private string PaperPath(int assignmentId, string stored) => Path.Combine(Root, assignmentId.ToString(), stored);
    private string AnswerPath(int assignmentId, long attemptId, string stored) =>
        Path.Combine(Root, assignmentId.ToString(), "attempts", attemptId.ToString(), stored);

    // Loại thật của tệp theo vài byte đầu (DOCX = gói zip có word/document.xml). null = không nhận.
    public static (string Ext, string ContentType)? Detect(byte[] b)
    {
        if (b.Length >= 5 && b[0] == 0x25 && b[1] == 0x50 && b[2] == 0x44 && b[3] == 0x46 && b[4] == 0x2D) return (".pdf", "application/pdf");
        if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return (".jpg", "image/jpeg");
        if (b.Length >= 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47 && b[4] == 0x0D && b[5] == 0x0A && b[6] == 0x1A && b[7] == 0x0A) return (".png", "image/png");
        if (b.Length >= 12 && b[0] == 0x52 && b[1] == 0x49 && b[2] == 0x46 && b[3] == 0x46 && b[8] == 0x57 && b[9] == 0x45 && b[10] == 0x42 && b[11] == 0x50) return (".webp", "image/webp");
        if (b.Length >= 4 && b[0] == 0x50 && b[1] == 0x4B && b[2] == 0x03 && b[3] == 0x04)
        {
            try
            {
                using var zip = new ZipArchive(new MemoryStream(b), ZipArchiveMode.Read);
                if (zip.GetEntry("word/document.xml") != null)
                    return (".docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document");
            }
            catch (InvalidDataException) { }
        }
        return null;
    }

    public static async Task<byte[]> ReadUploadAsync(IFormFile? file)
    {
        if (file == null || file.Length == 0) throw new BusinessException("Hãy chọn một tệp.");
        if (file.Length > MaxBytes) throw new BusinessException("Tệp tối đa 20 MB.");
        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer);
        return buffer.ToArray();
    }

    // Đề GV: PDF giữ nguyên, Word đổi sang PDF để mọi trình duyệt hiện được nguyên trang. Trả về tên tệp đã lưu.
    public async Task<string> SavePaperAsync(int assignmentId, byte[] bytes)
    {
        var type = Detect(bytes);
        if (type?.Ext is not (".pdf" or ".docx")) throw new BusinessException("Đề phải là tệp PDF hoặc Word (.docx).");
        var pdf = type.Value.Ext == ".pdf" ? bytes : await ConvertToPdfAsync(bytes);
        var stored = $"paper-{Guid.NewGuid():N}.pdf";
        var path = PaperPath(assignmentId, stored);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, pdf);
        return stored;
    }

    public FileStream? OpenPaper(int assignmentId, string? stored) =>
        Safe(stored) && File.Exists(PaperPath(assignmentId, stored!)) ? File.OpenRead(PaperPath(assignmentId, stored!)) : null;

    public void DeletePaper(int assignmentId, string? stored)
    {
        if (Safe(stored) && File.Exists(PaperPath(assignmentId, stored!))) File.Delete(PaperPath(assignmentId, stored!));
    }

    // Tệp học viên: PDF, Word, ảnh. Trả về (tên lưu, Content-Type thật).
    public async Task<(string Stored, string ContentType)> SaveAnswerAsync(int assignmentId, long attemptId, byte[] bytes)
    {
        var type = Detect(bytes) ?? throw new BusinessException("Chỉ nhận tệp PDF, Word (.docx) hoặc ảnh (JPG, PNG, WebP).");
        var stored = $"{Guid.NewGuid():N}{type.Ext}";
        var path = AnswerPath(assignmentId, attemptId, stored);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, bytes);
        return (stored, type.ContentType);
    }

    public FileStream? OpenAnswer(int assignmentId, long attemptId, string stored) =>
        Safe(stored) && File.Exists(AnswerPath(assignmentId, attemptId, stored)) ? File.OpenRead(AnswerPath(assignmentId, attemptId, stored)) : null;

    public void DeleteAnswer(int assignmentId, long attemptId, string stored)
    {
        if (Safe(stored) && File.Exists(AnswerPath(assignmentId, attemptId, stored))) File.Delete(AnswerPath(assignmentId, attemptId, stored));
    }

    // Tên lưu do máy chủ sinh (chữ, số, gạch, chấm): chặn mọi kiểu "../" dù tên chỉ đọc từ database.
    private static bool Safe(string? stored) => !string.IsNullOrEmpty(stored) && stored.Length <= 64
        && stored.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '.') && !stored.Contains("..");

    // LibreOffice ở chế độ không giao diện. Mỗi lần dùng một hồ sơ tạm riêng (tài khoản chạy web không có thư mục home ghi được).
    private async Task<byte[]> ConvertToPdfAsync(byte[] docx)
    {
        var soffice = FindSoffice() ?? throw new BusinessException("Hệ thống chưa đổi được Word sang PDF. Hãy lưu đề thành PDF (Word: Tệp → Lưu thành → PDF) rồi tải lên.");
        var work = Directory.CreateTempSubdirectory("paper-");
        await ConvertGate.WaitAsync();
        try
        {
            var input = Path.Combine(work.FullName, "de.docx");
            await File.WriteAllBytesAsync(input, docx);
            var start = new ProcessStartInfo(soffice) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in new[] { "-env:UserInstallation=" + new Uri(Path.Combine(work.FullName, "profile")).AbsoluteUri,
                "--headless", "--norestore", "--nolockcheck", "--convert-to", "pdf", "--outdir", work.FullName, input })
                start.ArgumentList.Add(arg);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Không chạy được LibreOffice.");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                throw new BusinessException("Đổi Word sang PDF quá lâu. Hãy lưu đề thành PDF rồi tải lên.");
            }
            var output = Path.Combine(work.FullName, "de.pdf");
            if (process.ExitCode != 0 || !File.Exists(output))
            {
                logger.LogWarning("LibreOffice đổi Word sang PDF thất bại, mã thoát {Code}", process.ExitCode);
                throw new BusinessException("Không đổi được tệp Word này sang PDF. Hãy mở bằng Word, lưu thành PDF rồi tải lên.");
            }
            return await File.ReadAllBytesAsync(output);
        }
        finally
        {
            ConvertGate.Release();
            try { work.Delete(true); } catch { }
        }
    }

    private string? FindSoffice()
    {
        var configured = config["Paper:SofficePath"];
        var candidates = new[] { configured, "/usr/bin/soffice", "/usr/lib/libreoffice/program/soffice",
            @"C:\Program Files\LibreOffice\program\soffice.exe", @"C:\Program Files (x86)\LibreOffice\program\soffice.exe" };
        return candidates.FirstOrDefault(p => !string.IsNullOrEmpty(p) && File.Exists(p));
    }
}
