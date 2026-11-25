using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using MindSprintAI_ASP.NET_MVC.Data.Entities;
using MindSprintAI_ASP.NET_MVC.Services;

namespace MindSprintAI_ASP.NET_MVC.Tests;

// Rà soát sâu lần 4 (15/11/2026): phần nhóm còn thiếu — model Gemini dự phòng, kiểm kết quả AI (NFR-14), ví dụ minh họa của thẻ,
// danh mục bộ học, ảnh đại diện tự tải lên, gợi ý điểm yếu.
public class RaSoatLan4PureTests
{
    // Trả 429 cho model đầu tiên, 200 cho model sau; ghi lại thứ tự model đã gọi.
    private sealed class QuotaThenOk : HttpMessageHandler
    {
        public List<string> Models { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var model = request.RequestUri!.AbsolutePath.Split('/')[^1].Split(':')[0];
            Models.Add(model);
            if (Models.Count == 1) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests));
            var envelope = JsonSerializer.Serialize(new { candidates = new[] { new { content = new { parts = new[] { new { text = "{\"ok\":true}" } } } } } });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(envelope, Encoding.UTF8, "application/json") });
        }
    }

    [Fact]
    public async Task Gemini_HetLuotModelChinh_ThuModelDuPhong()
    {
        var handler = new QuotaThenOk();
        var client = new GeminiClient(new HttpClient(handler), new ConfigurationBuilder().Build());
        var result = await client.PromptJsonAsync("AIza_Test_Key_Only_1234567890_ABCD", "x", new { type = "OBJECT" });
        Assert.True(result.GetProperty("ok").GetBoolean());
        Assert.Equal([GeminiClient.DefaultModel, GeminiClient.DefaultFallbackModels[0]], handler.Models);
    }

    [Fact]
    public async Task Gemini_MoiModelDeuHetLuot_BaoHetLuot()
    {
        var handler = new FakeGeminiHandler { Status = HttpStatusCode.TooManyRequests, ErrorBody = "{}" };
        var client = new GeminiClient(new HttpClient(handler), new ConfigurationBuilder().Build());
        var error = await Assert.ThrowsAsync<BusinessException>(() => client.PromptJsonAsync("AIza_Test_Key_Only_1234567890_ABCD", "x", new { type = "OBJECT" }));
        Assert.Contains("hết lượt", error.Message);
        Assert.Contains(GeminiClient.DefaultFallbackModels[^1], handler.Uri!.AbsolutePath);
    }

    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement;
    private static string Tree(int depth, int children) => depth == 0 ? "{\"title\":\"Lá\"}"
        : "{\"title\":\"Nút\",\"children\":[" + string.Join(",", Enumerable.Repeat(Tree(depth - 1, children), children)) + "]}";

    [Fact]
    public void KetQuaAi_SoDoTuDuyVuotKhuon_BaoLoi()
    {
        NotebookAiService.CheckOutput(Json(Tree(3, 3)), "mindmap"); // 4 tầng (gốc + 3), 40 nút
        Assert.Throws<BusinessException>(() => NotebookAiService.CheckOutput(Json(Tree(4, 1)), "mindmap"));   // quá 3 cấp
        Assert.Throws<BusinessException>(() => NotebookAiService.CheckOutput(Json(Tree(1, 13)), "mindmap"));  // nút quá 12 con
        Assert.Throws<BusinessException>(() => NotebookAiService.CheckOutput(Json(Tree(2, 11)), "mindmap"));  // 1 + 11 + 121 > 100 nút
        Assert.Throws<BusinessException>(() => NotebookAiService.CheckOutput(Json("{\"title\":\"" + new string('a', 101) + "\"}"), "mindmap"));
        Assert.Throws<BusinessException>(() => NotebookAiService.CheckOutput(Json("{\"children\":[]}"), "mindmap"));
        Assert.Throws<BusinessException>(() => NotebookAiService.CheckOutput(Json("{\"text\":\"" + new string('a', 200_001) + "\"}"), "tutor"));
        NotebookAiService.CheckOutput(Json("{\"text\":\"Bình thường\"}"), "summary");
    }

    [Fact]
    public void AnhDaiDien_ChiNhanJpgPngWebp()
    {
        Assert.Equal("image/jpeg", AvatarService.ImageType([0xFF, 0xD8, 0xFF, 0xE0, 0, 0, 0, 0, 0, 0, 0, 0, 0]));
        Assert.Equal("image/png", AvatarService.ImageType([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0, 0]));
        Assert.Equal("image/webp", AvatarService.ImageType([.. "RIFF"u8, 0, 0, 0, 0, .. "WEBP"u8, 0]));
        Assert.Null(AvatarService.ImageType("<svg onload=alert(1)></svg>"u8));
        Assert.Null(AvatarService.ImageType("GIF89a......."u8));
    }

    [Fact]
    public void DanhMuc_GonKhoangTrang_GioiHanDoDai()
    {
        Assert.Equal("Lập trình", StudyLibraryService.CleanCategory("  Lập   trình "));
        Assert.Null(StudyLibraryService.CleanCategory("   "));
        Assert.Throws<BusinessException>(() => StudyLibraryService.CleanCategory(new string('a', StudyLibraryService.MaxCategory + 1)));
    }

    private static byte[] Zip(params (string Name, string Xml)[] parts)
    {
        using var buffer = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(buffer, System.IO.Compression.ZipArchiveMode.Create))
            foreach (var (name, xml) in parts)
            {
                using var writer = new StreamWriter(zip.CreateEntry(name).Open());
                writer.Write(xml);
            }
        return buffer.ToArray();
    }

    [Fact]
    public void DocWordPowerPointExcelRss()
    {
        const string w = "xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"";
        var docx = Zip(("word/document.xml", $"<w:document {w}><w:body><w:p><w:r><w:t>Ti thể</w:t></w:r><w:r><w:t xml:space=\"preserve\"> tạo năng lượng</w:t></w:r></w:p><w:p><w:r><w:t>Đoạn hai</w:t></w:r></w:p></w:body></w:document>"));
        Assert.Equal("Ti thể tạo năng lượng\nĐoạn hai", OfficeText.Extract(docx, ".docx"));
        const string a = "xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\"";
        var pptx = Zip(("ppt/slides/slide10.xml", $"<p:sld xmlns:p=\"x\" {a}><a:p><a:r><a:t>Trang 10</a:t></a:r></a:p></p:sld>"),
            ("ppt/slides/slide2.xml", $"<p:sld xmlns:p=\"x\" {a}><a:p><a:r><a:t>Trang 2</a:t></a:r></a:p></p:sld>"));
        Assert.Equal("Trang 2\n\nTrang 10", OfficeText.Extract(pptx, ".pptx"));
        using (var book = new ClosedXML.Excel.XLWorkbook())
        {
            var sheet = book.AddWorksheet("Từ vựng");
            sheet.Cell(1, 1).Value = "Apple"; sheet.Cell(1, 2).Value = "Quả táo";
            using var buffer = new MemoryStream();
            book.SaveAs(buffer);
            Assert.Equal("Từ vựng\nApple | Quả táo", OfficeText.Extract(buffer.ToArray(), ".xlsx").ReplaceLineEndings("\n"));
        }
        Assert.Throws<BusinessException>(() => OfficeText.Extract("không phải zip"u8.ToArray(), ".docx"));
        Assert.Equal(".docx", NotebookUrlReader.OfficeType(new Uri("https://a.vn/tai-lieu"), "application/vnd.openxmlformats-officedocument.wordprocessingml.document"));
        var rss = NotebookUrlReader.Extract(new Uri("https://a.vn/rss"), Encoding.UTF8.GetBytes(
            "<?xml version=\"1.0\"?><rss><channel><item><title>Bài một về quang hợp</title><description>&lt;p&gt;Lục lạp hấp thụ ánh sáng mặt trời để tạo chất hữu cơ cho cây xanh phát triển&lt;/p&gt;</description></item></channel></rss>"),
            "application/rss+xml", false, null);
        Assert.Contains("Bài một về quang hợp\nLục lạp hấp thụ", rss.Content);
        Assert.DoesNotContain("<p>", rss.Content);
    }

    [Fact]
    public void DiemYeu_CoGoiYTheoLoai()
    {
        Assert.Contains("Chưa nhớ", new WeakArea(WeakKind.Cards, "Bộ A", 3, 4, 1, null).Suggestion);
        Assert.Contains("câu sai", new WeakArea(WeakKind.Quiz, "Quiz B", 3, 4, null, 2).Suggestion);
    }
}

[Collection("db")]
public class RaSoatLan4DbTests
{
    private sealed class Env(string root) : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = root;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ApplicationName { get; set; } = "test";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = root;
        public string EnvironmentName { get; set; } = "Development";
    }

    private static IFormFile Upload(byte[] bytes) => new FormFile(new MemoryStream(bytes), 0, bytes.Length, "image", "a.jpg");

    [Fact]
    public async Task AnhDaiDien_TaiLen_ChiChuXem_GoThiXoaTep_GoogleKhongDe()
    {
        using var h = new Harness();
        var root = Directory.CreateTempSubdirectory("avatar-").FullName;
        try
        {
            var avatars = new AvatarService(h.Db, h.Clock, new Env(root));
            var user = await h.UserAsync("Ảnh");
            byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
            await Assert.ThrowsAsync<BusinessException>(() => avatars.UploadAsync(user, Upload("<svg></svg>"u8.ToArray())));
            await Assert.ThrowsAsync<BusinessException>(() => avatars.UploadAsync(user, Upload(new byte[AvatarService.MaxUploadBytes + 1])));
            await avatars.UploadAsync(user, Upload(jpeg));
            var saved = await h.Db.Users.SingleAsync(u => u.Id == user);
            Assert.Equal(AvatarSource.Upload, saved.AvatarSource);
            Assert.Equal(jpeg, (await avatars.ReadAsync(user))!.Value.Bytes);
            // Đăng nhập Google không thay ảnh tự tải lên.
            var principal = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
                [new System.Security.Claims.Claim(AvatarService.GooglePictureClaim, "https://lh3.googleusercontent.com/a/x=s96-c")]));
            Assert.False(AvatarService.CaptureGoogle(saved, "Google", principal, h.Clock.UtcNow));
            await avatars.RemoveAsync(user);
            Assert.Null(await avatars.ReadAsync(user));
            Assert.Empty(Directory.GetFiles(Path.Combine(root, "App_Data", "avatars")));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task TheCoViDu_BoHocCoDanhMuc_NhanBanGiuNguyen()
    {
        using var h = new Harness();
        var s = await h.ScenarioAsync(1);
        var cards = new FlashcardService(h.Db, h.Library, h.Clock, h.Notifier);
        var id = await cards.SaveAsync(s.Quiz.StudySetId, null, "API", "Giao diện lập trình", s.Lecturer, "  Gọi API hãng bay  ");
        await Assert.ThrowsAsync<BusinessException>(() => cards.SaveAsync(s.Quiz.StudySetId, id, "API", "x", s.Lecturer, new string('a', FlashcardService.MaxExample + 1)));
        Assert.Equal("Gọi API hãng bay", (await h.Db.Flashcards.SingleAsync(c => c.Id == id)).Example);
        await cards.SaveAsync(s.Quiz.StudySetId, id, "API", "Giao diện lập trình", s.Lecturer, " ");
        Assert.Null((await h.Db.Flashcards.SingleAsync(c => c.Id == id)).Example);

        var set = await h.Library.CreateStudySetAsync("Bộ có danh mục", s.Lecturer, " Tiếng  Anh ");
        Assert.Equal("Tiếng Anh", set.Category);
        await h.Library.RenameStudySetAsync(set.Id, "Bộ đổi tên", s.Lecturer, "Lập trình");
        Assert.Equal("Lập trình", (await h.Db.StudySets.SingleAsync(x => x.Id == set.Id)).Category);
        await h.Library.RenameStudySetAsync(set.Id, "Bộ đổi tên", s.Lecturer, "");
        Assert.Null((await h.Db.StudySets.SingleAsync(x => x.Id == set.Id)).Category);
    }
}
