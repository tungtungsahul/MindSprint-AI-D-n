using ClosedXML.Excel;
using MindSprintAI_ASP.NET_MVC.Data.Entities;
using MindSprintAI_ASP.NET_MVC.Services;
using T = MindSprintAI_ASP.NET_MVC.Services.ExcelTemplate;

namespace MindSprintAI_ASP.NET_MVC.Tests;

public class Tang3ExcelTests
{
    private static byte[] Edit(Action<IXLWorksheet> edit, string sheet = "CauHoi")
    {
        using var book = new XLWorkbook(new MemoryStream(T.Create()));
        edit(book.Worksheet(sheet));
        using var output = new MemoryStream();
        book.SaveAs(output);
        return output.ToArray();
    }

    private static List<ImportRow> Read(byte[] file) => T.Read(new MemoryStream(file), "mau.xlsx");

    // Ghi một câu vào dòng row; cột theo chỉ số 0 của T.Headers.
    private static void Put(IXLWorksheet s, int row, params (int Col, string Value)[] cells)
    {
        foreach (var (col, value) in cells) s.Cell(row, col + 1).Value = value;
    }

    [Fact]
    public void MauExcelDocLaiDuNamCauVaHaiThe()
    {
        var rows = Read(T.Create());
        Assert.Equal(7, rows.Count);
        Assert.All(rows, r => Assert.Null(r.Error));
        Assert.Equal(5, rows.Count(r => r.Question != null));
        Assert.Equal(2, rows.Count(r => r.Card != null));
        Assert.All(rows, r => Assert.True(r.Row >= T.FirstDataRow)); // dòng 2 là dòng gợi ý, không phải dữ liệu
        var q = rows.Where(r => r.Question != null).Select(r => r.Question!).ToList();
        Assert.Equal(QuestionType.SingleChoice, q[0].Type); Assert.True(q[0].Options[2].IsCorrect); Assert.Equal(30, q[0].TimeLimitSeconds);
        Assert.Equal([0, 2], q[1].Options.Where(o => o.IsCorrect).Select(o => o.OrderIndex)); // ví dụ ghi bằng chữ A,C
        Assert.True(q[2].AcceptNoDiacritics); Assert.Equal(2, q[2].Blanks[0].Answers.Count);
        Assert.Equal(2, q[3].Blanks.Count); Assert.True(q[3].AcceptAnyBlankOrder); Assert.Equal(BlankScoring.PerBlank, q[3].BlankScoring);
        Assert.Equal(3.14m, q[4].NumberAnswer); Assert.Equal(0.01m, q[4].NumberTolerance);
    }

    [Fact]
    public void MauCoDongGoiYDanhSachLoaiCauVaCotNangCaoToXam()
    {
        using var book = new XLWorkbook(new MemoryStream(T.Create()));
        var s = book.Worksheet("CauHoi");
        Assert.Equal("(bắt buộc)", s.Cell(2, 1).GetString());
        Assert.Contains(s.DataValidations, v => v.Ranges.Any(r => r.RangeAddress.FirstAddress.ColumnNumber == T.Type + 1) && v.Value.Contains("Điền số"));
        Assert.NotEqual(s.Cell(1, T.Prompt + 1).Style.Fill.BackgroundColor, s.Cell(1, T.FirstAdvanced + 1).Style.Fill.BackgroundColor);
        Assert.Equal(2, s.SheetView.SplitRow);
        Assert.Equal("(bắt buộc) Từ, câu hỏi hoặc khái niệm", book.Worksheet("Flashcard").Cell(2, 1).GetString());
    }

    [Theory]
    [InlineData("1,3")] [InlineData("A,C")] [InlineData("a; 3")] [InlineData("1 C")] [InlineData("1.3")] // 1.3: Excel máy Việt Nam đổi "1,3" thành số
    public void DapAnDungNhanCaSoLanChu(string correct)
    {
        var file = Edit(s => Put(s, 10, (T.Prompt, "Chọn hai"), (T.Type, "Chọn nhiều"), (2, "Một"), (3, "Hai"), (4, "Ba"), (T.Correct, correct)));
        var q = Read(file).Single(r => r.Row == 10);
        Assert.Null(q.Error);
        Assert.Equal([0, 2], q.Question!.Options.Where(o => o.IsCorrect).Select(o => o.OrderIndex));
    }

    [Fact]
    public void LoaiTrongLaChonMotDiemTrongLaMot()
    {
        var q = Read(Edit(s => Put(s, 10, (T.Prompt, "Đề"), (2, "Đúng"), (3, "Sai"), (T.Correct, "1")))).Single(r => r.Row == 10).Question!;
        Assert.Equal(QuestionType.SingleChoice, q.Type); Assert.Equal(1, q.Points); Assert.Null(q.TimeLimitSeconds);
    }

    [Fact]
    public void DongLoiBaoDungLyDoVaChiSo()
    {
        var file = Edit(s =>
        {
            Put(s, 10, (T.Prompt, "Đề"), (T.Type, "Chọn một"), (2, "A"), (3, "B"), (T.Correct, "1,2"));
            Put(s, 11, (T.Prompt, "Đề"), (T.Type, "Điền chữ"), (2, "Một"), (4, "Ba"));
            Put(s, 12, (T.Prompt, "Đề"), (2, "A"), (3, "B"), (T.Correct, "5"));
            Put(s, 13, (T.Prompt, "Đề"), (T.Type, "Điền chữ"), (2, "Một"), (T.Correct, "1"));
            Put(s, 14, (T.Prompt, "Đề"), (T.Type, "Điền số"), (2, "3"));
            Put(s, 15, (T.Prompt, "Đề"), (2, "A"), (3, "B"), (T.Correct, "1"), (T.Time, "3"));
            Put(s, 16, (T.Prompt, "Đề"), (T.Type, "Đúng sai"), (2, "A"), (3, "B"), (T.Correct, "1"));
        });
        var rows = Read(file);
        string Error(int row) => rows.Single(r => r.Row == row).Error!;
        Assert.Equal("Dòng 10: Câu chọn một phải có đúng 1 đáp án đúng.", Error(10));
        Assert.Equal("Dòng 11: Ô 2 chưa có đáp án.", Error(11));
        Assert.Contains("số thứ tự (1–6) hoặc chữ (A–F)", Error(12));
        Assert.Contains("để trống cột Đáp án đúng", Error(13));
        Assert.Contains("để trống các cột Lựa chọn", Error(14));
        Assert.NotNull(Error(15));
        Assert.Contains("Loại câu chỉ nhận", Error(16));
    }

    [Fact]
    public void XoaDongGoiYThiDuLieuBatDauTuDongHai()
    {
        var rows = Read(Edit(s => s.Row(2).Delete()));
        Assert.Equal(5, rows.Count(r => r.Question != null));
        Assert.Equal(2, rows.Min(r => r.Row));
    }

    [Fact]
    public void FileMauCuBaoTaiMauMoi()
    {
        using var book = new XLWorkbook();
        var old = book.AddWorksheet("CauHoi");
        old.Cell(1, 1).Value = "Loại"; old.Cell(1, 2).Value = "Nội dung";
        book.AddWorksheet("Flashcard");
        using var output = new MemoryStream(); book.SaveAs(output);
        Assert.Contains("tải file mẫu mới nhất", Assert.Throws<BusinessException>(() => Read(output.ToArray())).Message);
    }

    [Fact]
    public void KhongTinhCongThuc()
    {
        var rows = Read(Edit(s => s.Cell(T.FirstDataRow, T.Score + 1).FormulaA1 = "1/0"));
        Assert.Contains("Ô này đang dùng công thức", rows[0].Error);
    }

    [Fact]
    public void ChanSaiDuoiHongQuaKichThuocVaQuaSoDong()
    {
        Assert.Throws<BusinessException>(() => T.Read(new MemoryStream(T.Create()), "file.xlsm"));
        Assert.Contains("Không đọc được", Assert.Throws<BusinessException>(() => T.Read(new MemoryStream([1, 2, 3]), "file.xlsx")).Message);
        Assert.Contains("5 MB", Assert.Throws<BusinessException>(() => T.Read(new MemoryStream(new byte[T.MaxBytes + 1]), "file.xlsx")).Message);
        var file = Edit(s => s.Cell(T.FirstDataRow + T.MaxRows, 1).Value = "Quá giới hạn");
        Assert.Contains("500 dòng", Assert.Throws<BusinessException>(() => T.Read(new MemoryStream(file), "file.xlsx")).Message);
    }

    [Fact]
    public void KiemLaiDuLieuDaSuaKhongTinCoHopLeVaGiuDongBoChon()
    {
        var preview = ExcelImportService.ToPreview(1, "mau.xlsx", Read(T.Create()));
        preview.Rows[0].Values[T.Correct] = "1,2";
        preview.Rows[0].Error = null;
        preview.Rows[1].Values[T.Prompt] = "Nội dung sửa";
        preview.Rows[2].Include = false;
        var rows = T.ValidateRows(preview.Rows);
        Assert.Contains("đúng 1 đáp án đúng", rows[0].Error);
        Assert.Equal("Nội dung sửa", rows[1].Question!.Prompt);
        Assert.False(rows[2].Include);
        preview.Rows[0].Values[T.Correct] = "C";
        Assert.Null(T.ValidateRows(preview.Rows)[0].Error);
    }

    [Fact]
    public void TrangXemTruocCungGioiHan500DongMoiSheet()
    {
        var rows = Enumerable.Range(3, 501).Select(i => new ImportEditRow { Row = i, Sheet = "Flashcard", Values = ["Trước", "Sau"] }).ToList();
        Assert.Contains("500 dòng", Assert.Throws<BusinessException>(() => T.ValidateRows(rows)).Message);
    }
}
