using System.IO.Compression;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using MindSprintAI_ASP.NET_MVC.Data.Entities;
using MindSprintAI_ASP.NET_MVC.Services;

namespace MindSprintAI_ASP.NET_MVC.Tests;

// Bài làm theo đề tài liệu (16/11/2026): GV tải đề PDF/Word, học viên viết bài + đính kèm tệp trong giờ, GV chấm tay thang 10.
public class BaiLamTheoDePureTests
{
    [Fact]
    public void NhanLoaiTepTheoChuKy_KhongTheoDuoi()
    {
        Assert.Equal(".pdf", PaperFileStore.Detect(BaiLamTheoDeDbTests.Pdf)?.Ext);
        Assert.Equal(".png", PaperFileStore.Detect([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0])?.Ext);
        Assert.Equal(".jpg", PaperFileStore.Detect([0xFF, 0xD8, 0xFF, 0xE0])?.Ext);
        Assert.Equal(".docx", PaperFileStore.Detect(BaiLamTheoDeDbTests.Docx())?.Ext);
        Assert.Null(PaperFileStore.Detect(Encoding.UTF8.GetBytes("<html><script>alert(1)</script>")));
        Assert.Null(PaperFileStore.Detect(BaiLamTheoDeDbTests.Zip("x.txt")));   // zip thường không phải Word
        Assert.Null(PaperFileStore.Detect([]));
    }
}

[Collection("db")]
public class BaiLamTheoDeDbTests : IDisposable
{
    // Mỗi test một thư mục tạm cho tệp đề/bài nộp; xUnit gọi Dispose sau từng test (kể cả khi test hỏng) để không để lại rác.
    private readonly List<string> roots = [];
    public void Dispose() { foreach (var root in roots) try { Directory.Delete(root, true); } catch (IOException) { } }

    public static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF");

    public static byte[] Zip(string entry)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
            using (var w = new StreamWriter(zip.CreateEntry(entry).Open())) w.Write("<w:document/>");
        return ms.ToArray();
    }
    public static byte[] Docx() => Zip("word/document.xml");

    private sealed class Env(string root) : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = root;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ApplicationName { get; set; } = "test";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = root;
        public string EnvironmentName { get; set; } = "Development";
    }

    private static IFormFile Upload(byte[] bytes, string name = "de.pdf") => new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", name);

    private sealed record Ctx(Harness H, PaperTestService Papers, Scenario S, Assignment Paper, string Root);

    private async Task<Ctx> SetupAsync(Harness h, int minutes = 30, string? soffice = null)
    {
        var root = Directory.CreateTempSubdirectory("paper-test-").FullName;
        roots.Add(root);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Paper:SofficePath"] = soffice }).Build();
        var store = new PaperFileStore(new Env(root), config, NullLogger<PaperFileStore>.Instance);
        var papers = new PaperTestService(h.Db, h.Assignments, h.Attempts, h.Classrooms, h.Codes, store, h.Clock);
        var s = await h.ScenarioAsync();
        var a = await papers.CreateAsync(s.Room.Id, new PaperSettings("Giữa kỳ", null, minutes), Upload(Pdf), s.Lecturer);
        return new Ctx(h, papers, s, a, root);
    }

    private static async Task<Attempt> StartAsync(Ctx c)
    {
        await c.H.Assignments.StartTestAsync(c.Paper.Id, c.S.Lecturer);
        return await c.H.Attempts.StartAsync(c.Paper.Id, c.S.Student);
    }

    [Fact]
    public async Task TaoBai_LuuDePdf_KhongCoQuiz_VaChiGvDoiDeTruocKhiBatDau()
    {
        using var h = new Harness();
        var c = await SetupAsync(h);
        var a = await h.Db.Assignments.AsNoTracking().SingleAsync(x => x.Id == c.Paper.Id);
        Assert.Equal((AssignmentMode.Test, AssignmentForm.Document, (int?)null, "Giữa kỳ"), (a.Mode, a.Form, a.QuizId, a.PaperTitle));
        Assert.True(File.Exists(Path.Combine(c.Root, "App_Data", "assignment-files", a.Id.ToString(), a.PaperStoredName!)));

        // Đề không phải PDF/Word, và học viên không được tạo bài.
        await Assert.ThrowsAsync<BusinessException>(() => c.Papers.CreateAsync(c.S.Room.Id, new PaperSettings("X", null, 30), Upload([0xFF, 0xD8, 0xFF, 0xE0], "a.jpg"), c.S.Lecturer));
        await Assert.ThrowsAnyAsync<Exception>(() => c.Papers.CreateAsync(c.S.Room.Id, new PaperSettings("X", null, 30), Upload(Pdf), c.S.Student));
        await Assert.ThrowsAsync<BusinessException>(() => c.Papers.CreateAsync(c.S.Room.Id, new PaperSettings("X", null, 181), Upload(Pdf), c.S.Lecturer));

        // Học viên chưa vào bài thì không xem được đề.
        await Assert.ThrowsAsync<ForbiddenException>(() => c.Papers.OpenPaperAsync(a.Id, c.S.Student));
        var old = a.PaperStoredName;
        await c.Papers.ReplacePaperAsync(a.Id, Upload(Pdf, "de-moi.pdf"), c.S.Lecturer);
        h.Db.ChangeTracker.Clear();
        var replaced = await h.Db.Assignments.AsNoTracking().SingleAsync(x => x.Id == a.Id);
        Assert.Equal("de-moi.pdf", replaced.PaperFileName);
        Assert.False(File.Exists(Path.Combine(c.Root, "App_Data", "assignment-files", a.Id.ToString(), old!)));

        await StartAsync(c);
        using (var f = (await c.Papers.OpenPaperAsync(a.Id, c.S.Student)).File) Assert.True(f.Length > 0);
        await Assert.ThrowsAsync<BusinessException>(() => c.Papers.ReplacePaperAsync(a.Id, Upload(Pdf), c.S.Lecturer));
    }

    [Fact]
    public async Task DeWord_MayChuKhongCoLibreOffice_BaoLoiDeHieu()
    {
        if (File.Exists("/usr/bin/soffice") || File.Exists(@"C:\Program Files\LibreOffice\program\soffice.exe")) return;   // máy có LibreOffice: bỏ qua
        using var h = new Harness();
        var c = await SetupAsync(h);
        var ex = await Assert.ThrowsAsync<BusinessException>(() => c.Papers.CreateAsync(c.S.Room.Id, new PaperSettings("Word", null, 30), Upload(Docx(), "de.docx"), c.S.Lecturer));
        Assert.Contains("PDF", ex.Message);
    }

    [Fact]
    public async Task HocVien_VietBai_DinhKemToiDa5Tep_HetGioThiKhongSuaDuoc()
    {
        using var h = new Harness();
        var c = await SetupAsync(h, minutes: 30);
        var attempt = await StartAsync(c);
        await c.Papers.SaveTextAsync(attempt.Id, "Câu 1: MVC gồm Model, View, Controller.", c.S.Student);
        await c.Papers.SaveTextAsync(attempt.Id, "Bản sửa", c.S.Student);
        await Assert.ThrowsAsync<BusinessException>(() => c.Papers.SaveTextAsync(attempt.Id, new string('a', PaperTestService.MaxText + 1), c.S.Student));
        // Lỗi trên server 16/11: trình duyệt đếm xuống dòng là 1 (maxlength) nhưng gửi \r\n. 1000 dòng × 99 ký tự: gửi lên 100.998, đếm đúng 99.999.
        var manyLines = string.Join("\r\n", Enumerable.Repeat(new string('a', 99), 1000));
        Assert.True(manyLines.Length > PaperTestService.MaxText);
        await c.Papers.SaveTextAsync(attempt.Id, manyLines, c.S.Student);
        h.Db.ChangeTracker.Clear();
        Assert.Equal(99_999, (await c.Papers.GetSubmissionAsync(attempt.Id)).Text.Length);
        await c.Papers.SaveTextAsync(attempt.Id, "Bản sửa", c.S.Student);

        // Người khác không ghi vào lượt của học viên.
        await Assert.ThrowsAnyAsync<Exception>(() => c.Papers.SaveTextAsync(attempt.Id, "x", c.S.Lecturer));
        // Tệp sai loại bị từ chối dù đặt đuôi .pdf.
        await Assert.ThrowsAsync<BusinessException>(() => c.Papers.AddFileAsync(attempt.Id, Upload(Encoding.UTF8.GetBytes("<html>"), "bai.pdf"), c.S.Student));

        var ids = new List<long>();
        for (var i = 0; i < PaperTestService.MaxFiles; i++) ids.Add((await c.Papers.AddFileAsync(attempt.Id, Upload(Pdf, $"../bai{i}.pdf"), c.S.Student)).Id);
        await Assert.ThrowsAsync<BusinessException>(() => c.Papers.AddFileAsync(attempt.Id, Upload(Pdf), c.S.Student));
        await c.Papers.DeleteFileAsync(attempt.Id, ids[0], c.S.Student);
        await c.Papers.AddFileAsync(attempt.Id, Upload(Pdf, "bai-moi.pdf"), c.S.Student);

        h.Db.ChangeTracker.Clear();
        var sub = await c.Papers.GetSubmissionAsync(attempt.Id);
        Assert.Equal("Bản sửa", sub.Text);
        Assert.Equal(PaperTestService.MaxFiles, sub.Files.Count);
        Assert.DoesNotContain(sub.Files, f => f.FileName.Contains('/') || f.FileName.Contains(".."));

        // Tệp: chủ bài và GV xem được; người ngoài lớp thì không.
        var outsider = await h.UserAsync("Người ngoài");
        var fileId = sub.Files[0].Id;
        using (var f = (await c.Papers.OpenFileAsync(fileId, c.S.Student)).File) Assert.True(f.Length > 0);
        using (var f = (await c.Papers.OpenFileAsync(fileId, c.S.Lecturer)).File) Assert.True(f.Length > 0);
        await Assert.ThrowsAsync<ForbiddenException>(() => c.Papers.OpenFileAsync(fileId, outsider));

        // Hết giờ: máy chủ không nhận sửa nữa.
        h.Clock.UtcNow = h.Clock.UtcNow.AddMinutes(31);
        await Assert.ThrowsAnyAsync<BusinessException>(() => c.Papers.SaveTextAsync(attempt.Id, "sửa muộn", c.S.Student));
    }

    [Fact]
    public async Task Cham_ChiBaiDaNop_Diem0Den10_HaiChuSoLe_VaoBangDiem_ChanCongBoKhiConBaiChuaCham()
    {
        using var h = new Harness();
        var c = await SetupAsync(h);
        var attempt = await StartAsync(c);
        await c.Papers.SaveTextAsync(attempt.Id, "Bài làm", c.S.Student);
        await Assert.ThrowsAsync<BusinessException>(() => c.Papers.GradeAsync(attempt.Id, 8, null, c.S.Lecturer));   // chưa nộp

        await h.Attempts.FinishAsync(attempt.Id, c.S.Student);
        h.Db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<BusinessException>(() => c.Papers.SaveTextAsync(attempt.Id, "sửa sau khi nộp", c.S.Student));

        // Chưa chấm: bảng điểm chưa có điểm, chưa công bố được.
        Assert.Equal(1, await c.Papers.CountUngradedAsync(c.Paper.Id));
        Assert.Null((await h.Gradebook.GetReportAsync(c.Paper.Id, c.S.Lecturer)).Rows.Single(r => r.UserId == c.S.Student).Score);
        h.Clock.UtcNow = h.Clock.UtcNow.AddMinutes(31);   // hết giờ cả lớp: chỉ còn lý do "chưa chấm" chặn công bố
        var block = await Assert.ThrowsAsync<BusinessException>(() => h.Assignments.PublishScoresAsync(c.Paper.Id, c.S.Lecturer));
        Assert.Contains("chưa chấm", block.Message);

        foreach (var bad in new decimal?[] { null, -1, 10.01m, 7.555m })
            await Assert.ThrowsAsync<BusinessException>(() => c.Papers.GradeAsync(attempt.Id, bad, null, c.S.Lecturer));
        await Assert.ThrowsAnyAsync<Exception>(() => c.Papers.GradeAsync(attempt.Id, 9, null, c.S.Student));   // học viên tự chấm

        await c.Papers.GradeAsync(attempt.Id, 8.75m, "  Bài tốt, thiếu ví dụ.  ", c.S.Lecturer);
        h.Db.ChangeTracker.Clear();
        var sub = await c.Papers.GetSubmissionAsync(attempt.Id);
        Assert.Equal((8.75m, "Bài tốt, thiếu ví dụ."), (sub.Score, sub.Comment));
        Assert.Equal(0, await c.Papers.CountUngradedAsync(c.Paper.Id));
        var row = (await h.Gradebook.GetReportAsync(c.Paper.Id, c.S.Lecturer)).Rows.Single(r => r.UserId == c.S.Student);
        Assert.NotNull(row.Score);

        var (_, rows) = await c.Papers.ListAsync(c.Paper.Id, c.S.Lecturer);
        Assert.Equal(8.75m, rows.Single(r => r.UserId == c.S.Student).Score);

        await h.Assignments.PublishScoresAsync(c.Paper.Id, c.S.Lecturer);
        h.Db.ChangeTracker.Clear();
        Assert.NotNull((await h.Db.Assignments.AsNoTracking().SingleAsync(x => x.Id == c.Paper.Id)).ScoresPublishedAt);
    }
}
