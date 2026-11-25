using System.Text.Json;
using MindSprintAI_ASP.NET_MVC.Services;

namespace MindSprintAI_ASP.NET_MVC.Tests;

// "Tạo bằng AI" của nhóm → bản nháp vào trang xem trước nhập Excel; mọi dòng AI đi qua cùng bộ kiểm tra như dòng Excel.
public class AiTaoCauHoiTests
{
    private static JsonElement Json(string s) => JsonDocument.Parse(s).RootElement;

    [Fact]
    public void CauHoiAiThanhDongExcelHopLeVaDongSaiBaoLoi()
    {
        var items = Json("""
            [ { "question": "Thủ đô Việt Nam?", "options": ["Huế", "Hà Nội", "Đà Nẵng", "Cần Thơ"], "correct": [2], "explanation": "Hà Nội là thủ đô." },
              { "question": "Số chẵn?", "options": ["1", "2", "3", "4"], "correct": [2, 4] },
              { "question": "Thiếu đáp án", "options": ["A", "B"], "correct": [] },
              { "question": 5, "options": "sai kiểu" } ]
            """);
        var rows = ExcelTemplate.ValidateRows(AiGenerateService.ToRows(items, cards: false, max: 10));
        Assert.Equal(4, rows.Count);
        Assert.Null(rows[0].Error);
        Assert.Equal("Chọn một", rows[0].Values![ExcelTemplate.Type]);
        Assert.Null(rows[1].Error);
        Assert.Equal(("Chọn nhiều", "2,4"), (rows[1].Values![ExcelTemplate.Type], rows[1].Values![ExcelTemplate.Correct]));
        Assert.NotNull(rows[2].Error); // AI quên đáp án → dòng báo lỗi, không lưu được tới khi sửa
        Assert.NotNull(rows[3].Error);
    }

    [Fact]
    public void TheAiVaGioiHanSoLuong()
    {
        var items = Json("""[ { "front": "API", "back": "Giao diện lập trình ứng dụng" }, { "front": "", "back": "Thiếu mặt trước" }, { "front": "A", "back": "B" } ]""");
        var rows = ExcelTemplate.ValidateRows(AiGenerateService.ToRows(items, cards: true, max: 2));
        Assert.Equal(2, rows.Count); // AI trả thừa thì cắt đúng số đã xin
        Assert.Null(rows[0].Error);
        Assert.NotNull(rows[1].Error);
        Assert.Throws<BusinessException>(() => AiGenerateService.ToRows(Json("[]"), cards: true, max: 5));
    }

    [Fact]
    public void DonChuTrichTuPdf()
    {
        Assert.Equal("Câu một. Câu hai", AiGenerateService.Clean("Câu   một.Câu \t hai"));
        Assert.Equal("", AiGenerateService.Clean("   "));
    }
}
