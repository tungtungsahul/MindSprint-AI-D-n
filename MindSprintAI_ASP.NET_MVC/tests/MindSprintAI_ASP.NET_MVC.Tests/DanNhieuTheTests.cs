using MindSprintAI_ASP.NET_MVC.Services;

namespace MindSprintAI_ASP.NET_MVC.Tests;

// Dán nhiều thẻ một lúc — "Nhập hàng loạt flashcards" của MindSprint-AI-D-n (parseBulkInput, flashcards_bulk.py).
[Collection("db")]
public class DanNhieuTheTests
{
    [Fact]
    public void TachDong_HaiCham_GachDung_DanhSach()
    {
        var rows = PasteCards.Rows("""
            Giờ là gì? : 12:30 là giờ trưa
            Lục lạp | Nơi quang hợp | bỏ cột thứ ba
            dòng không có dấu tách

            """);
        Assert.Equal([["Giờ là gì?", "12:30 là giờ trưa"], ["Lục lạp", "Nơi quang hợp"]], rows.Select(r => r.Values));
        Assert.All(rows, r => Assert.Equal("Flashcard", r.Sheet));
        Assert.Equal([ExcelTemplate.FirstDataRow, ExcelTemplate.FirstDataRow + 1], rows.Select(r => r.Row));

        Assert.Equal([["A", "B"], ["C", "D"]], PasteCards.Rows("""[{"front":"A","back":"B"},{"question":"C","answer":"D"},{"front":"thiếu mặt sau"}]""").Select(r => r.Values));
        Assert.Equal([["E", "F"]], PasteCards.Rows("""{"cards":[{"front":"E","back":"F"}]}""").Select(r => r.Values));
        Assert.Equal([["[x", "y"]], PasteCards.Rows("[x : y").Select(r => r.Values)); // dán sai cú pháp danh sách → đọc như dòng chữ
        Assert.Throws<BusinessException>(() => PasteCards.Rows("không có thẻ nào"));
        Assert.Throws<BusinessException>(() => PasteCards.Rows(string.Join('\n', Enumerable.Range(0, ExcelTemplate.MaxRows + 1).Select(i => $"Q{i}: A{i}"))));
    }

    [Fact]
    public async Task TrangNhap_DanNhieuThe_QuaXemTruocRoiLuu()
    {
        // Đúng đường đi của ImportController.PastePreview → trang xem trước → Save.
        using var h = new Harness(); var s = await h.ScenarioAsync(1);
        var quiz = await h.Library.CreateQuizAsync(s.Quiz.StudySetId, "Quiz chưa giao", s.Lecturer);
        var excel = new ExcelImportService(h.Db, h.Library, h.Clock);
        var preview = ExcelImportService.ToPreview(quiz.Id, "Thẻ dán từ văn bản", ExcelTemplate.ValidateRows(PasteCards.Rows("Ti thể : Năng lượng\nLục lạp | Quang hợp")));
        Assert.All(preview.Rows, r => Assert.Null(r.Error));
        var before = h.Db.Flashcards.Count(c => c.StudySetId == s.Quiz.StudySetId);
        Assert.Equal(2, await excel.SaveAsync(await excel.RecheckAsync(preview, s.Lecturer), s.Lecturer));
        Assert.Equal(before + 2, h.Db.Flashcards.Count(c => c.StudySetId == s.Quiz.StudySetId));
    }

    [Fact]
    public async Task ThemCaLoat_CoDongSaiThiKhongThemGi()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(1);
        var service = new FlashcardService(h.Db, h.Library, h.Clock, h.Notifier);
        var setId = s.Quiz.StudySetId;
        var before = h.Db.Flashcards.Count(c => c.StudySetId == setId);
        Assert.Equal(2, await service.AddManyAsync(setId, "Ti thể : Năng lượng\nLục lạp | Quang hợp", s.Lecturer));
        var added = h.Db.Flashcards.Where(c => c.StudySetId == setId).OrderBy(c => c.OrderIndex).ToList().TakeLast(2).Select(c => (c.Front, c.Back));
        Assert.Equal([("Ti thể", "Năng lượng"), ("Lục lạp", "Quang hợp")], added);
        var tooLong = new string('x', 33_000); // vượt giới hạn ô 32.767 ký tự của bộ kiểm tra nhập Excel
        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.AddManyAsync(setId, $"Đúng : Đúng\nQuá dài : {tooLong}", s.Lecturer));
        Assert.StartsWith("Có 1 dòng chưa đúng, chưa thêm thẻ nào. Dòng 2: Số cột hoặc độ dài ô", ex.Message);
        Assert.Equal(before + 2, h.Db.Flashcards.Count(c => c.StudySetId == setId));
        await Assert.ThrowsAnyAsync<Exception>(() => service.AddManyAsync(setId, "A : B", s.Student)); // không phải chủ bộ học
    }
}
