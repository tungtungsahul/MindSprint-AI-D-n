using Microsoft.EntityFrameworkCore;
using MindSprintAI_ASP.NET_MVC.Data.Entities;
using MindSprintAI_ASP.NET_MVC.Services;

namespace MindSprintAI_ASP.NET_MVC.Tests;

// Sổ tay AI chuyển từ MindSprint-AI-D-n (NotebooksController). Phần gọi Gemini dùng lại GeminiClient.AskAsync đã có test riêng.
[Collection("db")]
public class SoTayAiTests
{
    private static NotebookService Service(Harness h) => new(h.Db, null!, null!, null!, h.Clock);

    [Fact]
    public async Task NguonGhiChuVaGioiHanNhom()
    {
        using var h = new Harness();
        var u = await h.UserAsync("Minh");
        var service = Service(h);
        var nb = await service.CreateAsync("  ", u);
        Assert.Equal("Sổ tay mới", nb.Title);
        await service.AddSourceAsync(nb.Id, null, "Chương 1.Bài mở đầu", NotebookSourceKind.Text, null, u);
        await service.AddSourceAsync(nb.Id, "Rất dài", new string('a', NotebookService.MaxCharsPerSource + 10), NotebookSourceKind.File, "dai.txt", u);
        await Assert.ThrowsAsync<BusinessException>(() => service.AddSourceAsync(nb.Id, "Trống", "   ", NotebookSourceKind.Text, null, u));
        var loaded = await service.GetAsync(nb.Id, u);
        Assert.Equal(("Văn bản dán", "Chương 1. Bài mở đầu"), (loaded.Sources[0].Title, loaded.Sources[0].Content));
        Assert.Equal(NotebookService.MaxCharsPerSource, loaded.Sources[1].Content.Length); // cắt ở 200.000 ký tự như nhóm
        Assert.StartsWith("Văn bản dán\n\nChương 1.", NotebookService.Combined(loaded));
        for (var i = 2; i < NotebookService.MaxSources; i++) await service.AddSourceAsync(nb.Id, null, "x", NotebookSourceKind.Text, null, u);
        Assert.Equal($"Mỗi sổ tay có tối đa {NotebookService.MaxSources} nguồn.",
            (await Assert.ThrowsAsync<BusinessException>(() => service.AddSourceAsync(nb.Id, null, "y", NotebookSourceKind.Text, null, u))).Message);
        Assert.False((await new StudyStatsService(h.Db, h.Clock).TodayAsync(u)).Noted);
        await service.AddNoteAsync(nb.Id, null, "Ý chính", u);
        Assert.Equal("Ghi chú", (await service.GetAsync(nb.Id, u)).Notes.Single().Title);
        // FR-13 nhóm: ghi chú là code giữ nguyên thụt dòng (kể cả dòng đầu), chỉ bỏ dòng trống đầu và khoảng trắng cuối.
        await service.AddNoteAsync(nb.Id, "Code", "\n    for i in range(3):\n        print(i)  \n", u);
        Assert.Equal("    for i in range(3):\n        print(i)", (await service.GetAsync(nb.Id, u)).Notes.Single(n => n.Title == "Code").Content);
        Assert.True((await new StudyStatsService(h.Db, h.Clock).TodayAsync(u)).Noted); // mục tiêu ngày "Ghi chú điều muốn tìm hiểu" tự tích
        Assert.Throws<BusinessException>(() => NotebookService.Combined(new Notebook()));
        // Gia sư OCR: chỉ ảnh PNG/JPG/WEBP ≤ 10 MB; sai loại bị chặn trước khi gửi gì sang Gemini.
        var gif = new Microsoft.AspNetCore.Http.FormFile(new MemoryStream([1, 2, 3]), 0, 3, "file", "de.gif");
        Assert.Equal("Ảnh phải là PNG, JPG hoặc WEBP.", (await Assert.ThrowsAsync<BusinessException>(() => service.AddImageSourceAsync(nb.Id, null, gif, u))).Message);
    }

    [Fact]
    public async Task BoHocTuSoTayChiTaoKhiBamLuu()
    {
        // Người dùng chọn 15/11: bản nháp AI (QuizId = 0) chưa tạo gì; Lưu thì tạo bộ + quiz + câu cùng một giao dịch.
        using var h = new Harness();
        var u = await h.UserAsync("Minh");
        var import = new ExcelImportService(h.Db, h.Library, h.Clock);
        var card = new ImportEditRow { Sheet = "Flashcard", Row = ExcelTemplate.FirstDataRow, Values = ["Điện Biên Phủ", "7/5/1954"] };
        var draft = ExcelImportService.ToPreview(0, "Thẻ ghi nhớ tạo bằng AI", ExcelTemplate.ValidateRows([card]), "Lịch sử Việt Nam");
        Assert.Equal(0, await h.Db.StudySets.CountAsync(s => s.OwnerId == u));
        // Lưu bị từ chối (không chọn dòng nào) → giao dịch hoàn tác, không còn bộ học rỗng, QuizId vẫn là 0.
        var none = ExcelImportService.ToPreview(0, draft.FileName, ExcelTemplate.ValidateRows([new() { Sheet = card.Sheet, Row = card.Row, Values = card.Values, Include = false }]), draft.NewSetTitle);
        await Assert.ThrowsAsync<BusinessException>(() => import.SaveAsync(none, u));
        h.Db.ChangeTracker.Clear();
        Assert.Equal((0, 0), (await h.Db.StudySets.CountAsync(s => s.OwnerId == u), none.QuizId));
        Assert.Equal(1, await import.SaveAsync(draft, u));
        var quiz = await h.Db.Quizzes.Include(q => q.StudySet).SingleAsync(q => q.Id == draft.QuizId);
        Assert.Equal(("Lịch sử Việt Nam", u), (quiz.StudySet.Title, quiz.StudySet.OwnerId));
        Assert.Equal("Điện Biên Phủ", (await h.Db.Flashcards.SingleAsync(c => c.StudySetId == quiz.StudySetId)).Front);
    }

    [Fact]
    public async Task NguoiKhacKhongXemSuaXoaDuocVaXoaSoXoaHet()
    {
        using var h = new Harness();
        var minh = await h.UserAsync("Minh"); var lan = await h.UserAsync("Lan");
        var service = Service(h);
        var nb = await service.CreateAsync("Riêng của Minh", minh);
        await service.AddSourceAsync(nb.Id, null, "Bí mật", NotebookSourceKind.Text, null, minh);
        foreach (var act in new Func<Task>[] {
            () => service.GetAsync(nb.Id, lan), () => service.AddSourceAsync(nb.Id, null, "chèn", NotebookSourceKind.Text, null, lan),
            () => service.AddNoteAsync(nb.Id, null, "chèn", lan), () => service.DeleteAsync(nb.Id, lan) })
            await Assert.ThrowsAsync<BusinessException>(act);
        Assert.Empty(await service.ListAsync(lan));
        Assert.Equal(1, Assert.Single(await service.ListAsync(minh)).Sources);
        await service.DeleteAsync(nb.Id, minh);
        Assert.Equal(0, await h.Db.NotebookSources.CountAsync(s => s.NotebookId == nb.Id));
    }
}
