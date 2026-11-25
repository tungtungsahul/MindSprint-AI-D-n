using System.IO.Compression;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using MindSprintAI_ASP.NET_MVC.Data;
using MindSprintAI_ASP.NET_MVC.Data.Entities;

namespace MindSprintAI_ASP.NET_MVC.Services;

public record ImportRow(string Sheet, int Row, string Content, string? Error, Question? Question = null, Flashcard? Card = null,
    List<string>? Values = null, bool Include = true);
public class ImportEditRow
{
    public string Sheet { get; set; } = "";
    public int Row { get; set; }
    public bool Include { get; set; } = true;
    public List<string> Values { get; set; } = [];
    public string? Error { get; set; }
}
public class ImportPreview
{
    public int QuizId { get; set; }
    public string FileName { get; set; } = "";
    public string? NewSetTitle { get; set; } // QuizId = 0: bản nháp sẽ thành bộ học mới mang tên này khi bấm Lưu
    [Microsoft.AspNetCore.Mvc.ModelBinding.BindNever] public string? Notice { get; set; } // chỉ hiện lần đầu (vd. "Chưa dùng được AI, đã tách thẻ theo quy tắc"); không gửi lại khi kiểm tra/lưu
    public List<ImportEditRow> Rows { get; set; } = [];
}

public static class ExcelTemplate
{
    private static readonly SemaphoreSlim ReadSlots = new(2, 2);
    public const int MaxBytes = 5 * 1024 * 1024, MaxRows = 500;
    // Dòng 1 tiêu đề, dòng 2 gợi ý (bắt buộc/tùy chọn, theo kiểu mẫu Quizizz), dữ liệu từ dòng 3. Người dùng chốt 13/11/2026:
    // phần chính gọn như Quizizz, cài đặt nâng cao dồn về cuối (tô xám), đáp án đúng nhận cả số 1–6 lẫn chữ A–F, chỉ nhận mẫu mới.
    public const int FirstDataRow = 3, Prompt = 0, Type = 1, FirstOption = 2, OptionCount = 6, Correct = 8, Score = 9, Time = 10,
        Explain = 11, Tolerance = 12, CaseSensitive = 13, NoDiacritics = 14, IgnorePunctuation = 15, AnyBlankOrder = 16, PerBlank = 17, KeepOrder = 18,
        FirstAdvanced = Tolerance;
    public static readonly string[] Headers = ["Nội dung câu hỏi", "Loại câu", "Lựa chọn 1", "Lựa chọn 2", "Lựa chọn 3", "Lựa chọn 4", "Lựa chọn 5", "Lựa chọn 6",
        "Đáp án đúng", "Điểm", "Thời gian (giây)", "Giải thích",
        "Dung sai (câu điền số)", "Phân biệt hoa thường", "Chấp nhận không dấu", "Bỏ qua dấu câu", "Đổi thứ tự ô", "Chấm theo ô", "Giữ thứ tự lựa chọn"];
    public static readonly string[] Hints = ["(bắt buộc)", "Chọn một / Chọn nhiều / Điền chữ / Điền số (để trống = Chọn một)",
        "Câu chọn: nội dung lựa chọn.\nCâu điền chữ: mỗi cột là một ô trống; nhiều cách trả lời đúng ghi cách nhau bằng |, ví dụ Hà Nội|Ha Noi.",
        "(bắt buộc với câu chọn, cần ít nhất 2 lựa chọn)", "(tùy chọn)", "(tùy chọn)", "(tùy chọn)", "(tùy chọn)",
        "Câu chọn: số thứ tự 1–6 hoặc chữ A–F; nhiều đáp án cách nhau bằng dấu phẩy, ví dụ 1,3 hoặc A,C.\nCâu điền số: ghi con số đúng, ví dụ 3,14.\nCâu điền chữ: để trống.",
        "(tùy chọn, để trống = 1)", "(tùy chọn, 5–3600; để trống = không giới hạn)", "(tùy chọn) Hiện cho học viên sau khi trả lời",
        "Nâng cao, tùy chọn. Câu điền số: sai lệch cho phép, ví dụ 0,01", "Nâng cao: Có/Không (để trống = Không)", "Nâng cao: Có/Không",
        "Nâng cao: Có/Không", "Nâng cao, câu nhiều ô: Có/Không", "Nâng cao, câu nhiều ô: đúng ô nào được điểm ô đó (Có/Không)", "Nâng cao, câu chọn: Có/Không"];
    public static readonly string[] CardHeaders = ["Mặt trước", "Mặt sau"];
    public static readonly string[] CardHints = ["(bắt buộc) Từ, câu hỏi hoặc khái niệm", "(bắt buộc) Nghĩa, câu trả lời hoặc giải thích"];
    public static readonly string[] TypeNames = ["Chọn một", "Chọn nhiều", "Điền chữ", "Điền số"];

    public static byte[] Create()
    {
        using var book = new XLWorkbook();
        var sheet = book.AddWorksheet("CauHoi");
        Layout(sheet, Headers, Hints);
        string[][] examples =
        [
            ["Hành tinh nào lớn nhất trong Hệ Mặt Trời?", "Chọn một", "Trái Đất", "Sao Hỏa", "Sao Mộc", "Sao Kim", "", "", "3", "1", "30",
                "Sao Mộc là hành tinh khí khổng lồ, khối lượng gấp khoảng 318 lần Trái Đất."],
            ["Những số nào là số chẵn?", "Chọn nhiều", "2", "3", "4", "7", "", "", "A,C", "1", "", "Số chẵn chia hết cho 2."],
            ["Thủ đô của Việt Nam là thành phố nào?", "Điền chữ", "Hà Nội|Thủ đô Hà Nội", "", "", "", "", "", "", "1", "", "", "", "", "Có"],
            ["Kể tên hai thành phố đông dân nhất Việt Nam.", "Điền chữ", "Hà Nội", "TP. Hồ Chí Minh|Thành phố Hồ Chí Minh|Sài Gòn", "", "", "", "", "", "2", "60", "",
                "", "", "Có", "", "Có", "Có"],
            ["Số π làm tròn đến hai chữ số thập phân là bao nhiêu?", "Điền số", "", "", "", "", "", "", "3,14", "1", "", "", "0,01"],
        ];
        for (var r = 0; r < examples.Length; r++)
            for (var c = 0; c < examples[r].Length; c++)
                if (examples[r][c].Length > 0) sheet.Cell(FirstDataRow + r, c + 1).Value = examples[r][c];
        var lastRow = FirstDataRow + MaxRows - 1;
        sheet.Range(FirstDataRow, Type + 1, lastRow, Type + 1).CreateDataValidation().List("\"" + string.Join(",", TypeNames) + "\"");
        sheet.Range(FirstDataRow, CaseSensitive + 1, lastRow, KeepOrder + 1).CreateDataValidation().List("\"Có,Không\"");
        // Ô văn bản: gõ 1,3 không bị Excel đổi thành số 1,3 (một phẩy ba); 007 không mất số 0.
        sheet.Range(FirstDataRow, FirstOption + 1, lastRow, Correct + 1).Style.NumberFormat.Format = "@";
        Width(sheet, 42, 15, 18, 18, 18, 18, 18, 18, 22, 8, 14, 42, 16, 14, 14, 14, 14, 14, 14);

        var cards = book.AddWorksheet("Flashcard");
        Layout(cards, CardHeaders, CardHints);
        cards.Cell(FirstDataRow, 1).Value = "Thủ đô của Việt Nam"; cards.Cell(FirstDataRow, 2).Value = "Hà Nội";
        cards.Cell(FirstDataRow + 1, 1).Value = "Photosynthesis"; cards.Cell(FirstDataRow + 1, 2).Value = "Quang hợp";
        Width(cards, 45, 45);
        using var output = new MemoryStream();
        book.SaveAs(output);
        return output.ToArray();
    }

    private static void Layout(IXLWorksheet sheet, string[] headers, string[] hints)
    {
        for (var i = 0; i < headers.Length; i++)
        {
            var advanced = headers == Headers && i >= FirstAdvanced;
            var head = sheet.Cell(1, i + 1);
            head.Value = headers[i];
            head.Style.Font.Bold = true;
            head.Style.Font.FontColor = advanced ? XLColor.FromHtml("#595959") : XLColor.FromHtml("#09090B");
            head.Style.Fill.BackgroundColor = advanced ? XLColor.FromHtml("#E7E6E6") : XLColor.FromHtml("#D2E823");
            var hint = sheet.Cell(2, i + 1);
            hint.Value = hints[i];
            hint.Style.Font.Italic = true; hint.Style.Font.FontSize = 9;
            hint.Style.Font.FontColor = advanced ? XLColor.FromHtml("#7F7F7F") : XLColor.FromHtml("#3F3D38");
            hint.Style.Fill.BackgroundColor = advanced ? XLColor.FromHtml("#F2F2F2") : XLColor.FromHtml("#F3F7DC");
        }
        var top = sheet.Range(1, 1, 2, headers.Length);
        top.Style.Alignment.WrapText = true; top.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
        top.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
        sheet.Row(2).Height = 75;
        sheet.Range(FirstDataRow, 1, FirstDataRow + MaxRows - 1, headers.Length).Style.Alignment.WrapText = true;
        sheet.SheetView.Freeze(2, 1); // giữ tiêu đề + gợi ý và cột nội dung khi cuộn
    }

    private static void Width(IXLWorksheet sheet, params double[] widths)
    {
        for (var i = 0; i < widths.Length; i++) sheet.Column(i + 1).Width = widths[i];
    }

    private static bool IsHintRow(IXLWorksheet sheet, string[] hints) =>
        Cell(sheet.Cell(2, 1)) == hints[0];

    public static List<ImportRow> Read(Stream input, string fileName)
    {
        if (!ReadSlots.Wait(TimeSpan.FromSeconds(3)))
            throw new BusinessException("MindSprint AI đang bận đọc nhiều file Excel cùng lúc. Hãy thử lại sau ít phút.");
        try { return ReadCore(input, fileName); }
        finally { ReadSlots.Release(); }
    }

    private static List<ImportRow> ReadCore(Stream input, string fileName)
    {
        if (!string.Equals(Path.GetExtension(fileName), ".xlsx", StringComparison.OrdinalIgnoreCase))
            throw new BusinessException("Chỉ nhận file Excel có đuôi .xlsx.");
        using var buffer = new MemoryStream();
        var bytes = new byte[8192];
        int read;
        while ((read = input.Read(bytes)) > 0)
        {
            if (buffer.Length + read > MaxBytes) throw new BusinessException("File tối đa 5 MB.");
            buffer.Write(bytes, 0, read);
        }
        try
        {
            buffer.Position = 0;
            using (var zip = new ZipArchive(buffer, ZipArchiveMode.Read, true))
            {
                if (zip.Entries.Count > 2000 || zip.Entries.Sum(e => e.Length) > 5 * 1024 * 1024)
                    throw new BusinessException("File Excel quá nặng so với giới hạn cho phép. Hãy dùng file mẫu và bớt dữ liệu.");
                if (zip.Entries.Any(e => e.FullName.Contains("vbaProject", StringComparison.OrdinalIgnoreCase)))
                    throw new BusinessException("Không nhận file có macro (đoạn lệnh tự chạy). Hãy lưu lại thành file .xlsx thường rồi tải lên.");
            }
            buffer.Position = 0;
            using var book = new XLWorkbook(buffer);
            var rows = new List<ImportRow>();
            foreach (var sheetName in new[] { "CauHoi", "Flashcard" })
            {
                if (!book.TryGetWorksheet(sheetName, out var sheet)) throw new BusinessException($"Thiếu sheet Excel {sheetName}{(sheetName == "CauHoi" ? " (câu hỏi)" : " (thẻ ghi nhớ)")}. Hãy dùng file mẫu và giữ nguyên tên các sheet.");
                var headers = sheetName == "CauHoi" ? Headers : CardHeaders;
                for (var col = 0; col < headers.Length; col++)
                    if (Cell(sheet.Cell(1, col + 1)) != headers[col]) throw new BusinessException($"Sheet {sheetName}: cột {col + 1} phải có tiêu đề '{headers[col]}'. Hãy tải file mẫu mới nhất và giữ nguyên hàng tiêu đề.");
                // Dòng 2 là dòng gợi ý của file mẫu; ai lỡ xóa dòng gợi ý thì dữ liệu bắt đầu từ dòng 2.
                var firstRow = IsHintRow(sheet, sheetName == "CauHoi" ? Hints : CardHints) ? FirstDataRow : 2;
                var lastRow = sheet.LastRowUsed(XLCellsUsedOptions.Contents)?.RowNumber() ?? 1;
                if (lastRow - firstRow + 1 > MaxRows) throw new BusinessException($"Sheet {sheetName}: tối đa 500 dòng dữ liệu. Hãy chia thành nhiều file.");
                if ((sheet.LastColumnUsed(XLCellsUsedOptions.Contents)?.ColumnNumber() ?? 0) > headers.Length)
                    throw new BusinessException($"Sheet {sheetName}: có cột nằm ngoài file mẫu. Hãy xóa các cột thừa.");
                for (var row = firstRow; row <= lastRow; row++)
                {
                    var values = new List<string>();
                    string? cellError = null;
                    for (var col = 1; col <= headers.Length; col++)
                    {
                        try { values.Add(Cell(sheet.Cell(row, col))); }
                        catch (BusinessException e) { values.Add(""); cellError ??= e.Message; }
                    }
                    if (values.All(string.IsNullOrWhiteSpace) && cellError == null) continue;
                    var parsed = ValidateRow(new ImportEditRow { Sheet = sheetName, Row = row, Values = values });
                    rows.Add(cellError == null ? parsed : parsed with { Error = $"Dòng {row}: {cellError}" });
                }
            }
            return rows;
        }
        catch (BusinessException) { throw; }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            throw new BusinessException("Không đọc được file Excel. File có thể bị hỏng hoặc không đúng mẫu; hãy tải lại file mẫu rồi nhập dữ liệu vào đó.");
        }
    }

    public static List<ImportRow> ValidateRows(IReadOnlyList<ImportEditRow> rows)
    {
        if (rows.Count > MaxRows * 2 || rows.GroupBy(r => r.Sheet).Any(g => g.Count() > MaxRows))
            throw new BusinessException("Tối đa 500 dòng mỗi sheet Excel.");
        if (rows.Select(r => (r.Sheet, r.Row)).Distinct().Count() != rows.Count)
            throw new BusinessException("Có dòng xem trước bị trùng.");
        return rows.Select(ValidateRow).ToList();
    }

    private static ImportRow ValidateRow(ImportEditRow row)
    {
        var content = row.Values.ElementAtOrDefault(0) ?? "";
        try
        {
            if (row.Sheet is not ("CauHoi" or "Flashcard") || row.Row is < 2 or > MaxRows + FirstDataRow - 1)
                throw new BusinessException("Sheet hoặc số dòng không hợp lệ. Hãy tải lại file lên.");
            if (row.Values.Count != (row.Sheet == "CauHoi" ? Headers.Length : 2) || row.Values.Any(v => v?.Length > 32767))
                throw new BusinessException("Số cột hoặc độ dài ô không hợp lệ.");
            var values = row.Values.Select(v => (v ?? "").Trim()).ToArray();
            if (row.Sheet == "CauHoi")
                return new(row.Sheet, row.Row, content, null, ParseQuestion(values), Values: row.Values, Include: row.Include);
            if (string.IsNullOrWhiteSpace(values[0]) || string.IsNullOrWhiteSpace(values[1])) throw new BusinessException("Nhập đủ mặt trước và mặt sau.");
            return new(row.Sheet, row.Row, content, null, Card: new Flashcard { Front = values[0], Back = values[1] }, Values: row.Values, Include: row.Include);
        }
        catch (BusinessException e) { return new(row.Sheet, row.Row, content, $"Dòng {row.Row}: {e.Message}", Values: row.Values, Include: row.Include); }
    }

    private static string Cell(IXLCell cell)
    {
        // (79b) CachedValue không tính công thức; công thức không được nhập vào nội dung học.
        if (cell.HasFormula) throw new BusinessException("Ô này đang dùng công thức. Hãy sao chép ô rồi dán lại dạng giá trị (Dán đặc biệt → Giá trị) trước khi tải lên.");
        return cell.CachedValue.ToString(System.Globalization.CultureInfo.InvariantCulture).Trim();
    }

    private static Question ParseQuestion(string[] v)
    {
        var type = v[Type] switch
        {
            "" or "Chọn một" => QuestionType.SingleChoice, "Chọn nhiều" => QuestionType.MultipleChoice,
            "Điền chữ" => QuestionType.TextInput, "Điền số" => QuestionType.NumberInput,
            _ => throw new BusinessException("Loại câu chỉ nhận Chọn một, Chọn nhiều, Điền chữ hoặc Điền số.")
        };
        var points = v[Score].Length == 0 ? 1 : NumberParser.Parse(v[Score]);
        int? time = null;
        if (v[Time].Length > 0)
            time = int.TryParse(v[Time], out var seconds) ? seconds : throw new BusinessException("Thời gian ghi số giây nguyên, ví dụ 30.");
        bool Flag(int index) => v[index] switch { "" or "Không" => false, "Có" => true, _ => throw new BusinessException($"Cột {Headers[index]} chỉ nhận Có/Không.") };
        var options = Enumerable.Range(FirstOption, OptionCount).Select(i => v[i]).ToArray();
        Question q;
        if (type == QuestionType.TextInput)
        {
            if (v[Correct].Length > 0) throw new BusinessException("Câu điền chữ ghi đáp án vào các cột Lựa chọn (mỗi cột là một ô trống) và để trống cột Đáp án đúng.");
            if (options[5].Length > 0) throw new BusinessException("Câu điền chữ có tối đa 5 ô (Lựa chọn 1–5).");
            var last = Array.FindLastIndex(options, o => o.Length > 0);
            var blanks = options.Take(last + 1).Select(o => (IReadOnlyList<string>)o.Split('|').ToList()).ToList();
            q = StudyLibraryService.BuildInputQuestion(new InputQuestionInput(type, v[Prompt], v[Explain], points, blanks,
                Flag(CaseSensitive), Flag(NoDiacritics), Flag(IgnorePunctuation), Flag(AnyBlankOrder),
                Flag(PerBlank) ? BlankScoring.PerBlank : BlankScoring.AllOrNothing, TimeLimitSeconds: time));
        }
        else if (type == QuestionType.NumberInput)
        {
            if (options.Any(o => o.Length > 0)) throw new BusinessException("Câu điền số ghi con số đúng vào cột Đáp án đúng và để trống các cột Lựa chọn.");
            if (v[Correct].Length == 0) throw new BusinessException("Câu điền số cần con số đúng ở cột Đáp án đúng, ví dụ 3,14.");
            q = StudyLibraryService.BuildInputQuestion(new InputQuestionInput(type, v[Prompt], v[Explain], points, [],
                NumberAnswer: v[Correct], NumberTolerance: v[Tolerance], TimeLimitSeconds: time));
        }
        else
        {
            var correct = CorrectIndexes(v[Correct], options);
            q = new Question { Type = type, Prompt = v[Prompt], Points = points, Explanation = v[Explain], KeepOptionOrder = Flag(KeepOrder),
                TimeLimitSeconds = time,
                Options = Enumerable.Range(0, OptionCount).Where(i => options[i].Length > 0).Select(i => new QuestionOption
                { OrderIndex = i, Text = options[i], IsCorrect = correct.Contains(i) }).ToList() };
        }
        if (QuestionRules.Validate(q) is { } error) throw new BusinessException(error);
        return q;
    }

    // Người dùng chốt 13/11/2026: nhận cả số (1–6) lẫn chữ (A–F), trộn được, cách nhau bằng dấu phẩy/chấm phẩy/khoảng trắng.
    internal static HashSet<int> CorrectIndexes(string text, string[] options)
    {
        const string Help = "Đáp án đúng ghi số thứ tự (1–6) hoặc chữ (A–F) của lựa chọn có nội dung, nhiều đáp án cách nhau bằng dấu phẩy, ví dụ 1,3 hoặc A,C.";
        // Excel máy Việt Nam coi 1,3 là số thập phân (1.3): tách cả dấu chấm.
        var tokens = text.ToUpperInvariant().Split([',', ';', ' ', '.'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) throw new BusinessException("Chưa có đáp án đúng. " + Help);
        var result = new HashSet<int>();
        foreach (var t in tokens)
        {
            var i = t.Length == 1 && t[0] is >= 'A' and <= 'F' ? t[0] - 'A' : int.TryParse(t, out var n) && n is >= 1 and <= OptionCount ? n - 1 : -1;
            if (i < 0 || options[i].Length == 0) throw new BusinessException(Help);
            result.Add(i);
        }
        return result;
    }
}

public class ExcelImportService(AppDbContext db, StudyLibraryService library, TimeProvider clock)
{
    public const string RowsHaveErrors = "Còn dòng được chọn bị lỗi. Hãy sửa hoặc bỏ tick Nhập ở dòng đó, rồi bấm Kiểm tra lại.";

    public async Task EnsureCanImportAsync(int quizId, string userId)
    {
        if (quizId == 0) return; // bản nháp cho bộ học MỚI (Sổ tay AI): bộ học + quiz chỉ tạo khi bấm Lưu
        await library.GetOwnedQuizAsync(quizId, userId);
        if (await library.WasAssignedAsync(quizId)) throw new BusinessException("Quiz này đã được giao cho lớp nên không nhập thêm câu từ file Excel được. Hãy nhân bản bộ học rồi nhập vào bản mới.");
    }

    public async Task<ImportPreview> PreviewAsync(int quizId, byte[] file, string name, string userId)
    {
        await EnsureCanImportAsync(quizId, userId);
        return ToPreview(quizId, name, ExcelTemplate.Read(new MemoryStream(file), name));
    }

    public static ImportPreview ToPreview(int quizId, string name, List<ImportRow> rows, string? newSetTitle = null) => new()
    {
        QuizId = quizId, FileName = name, NewSetTitle = quizId == 0 ? newSetTitle : null,
        Rows = rows.Select(r => new ImportEditRow { Sheet = r.Sheet, Row = r.Row, Values = r.Values ?? [], Include = r.Include, Error = r.Error }).ToList()
    };

    public async Task<ImportPreview> RecheckAsync(ImportPreview input, string userId)
    {
        await EnsureCanImportAsync(input.QuizId, userId);
        return ToPreview(input.QuizId, input.FileName, ExcelTemplate.ValidateRows(input.Rows), input.NewSetTitle);
    }

    // Lưu xong, input.QuizId là quiz đã nhận câu (quiz mới nếu bản nháp dành cho bộ học mới) để controller chuyển tới đúng trang.
    public async Task<int> SaveAsync(ImportPreview input, string userId)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        int? newQuizId = null;
        if (input.QuizId == 0)
        {
            // Bộ học mới (Sổ tay AI): chỉ tạo khi lưu, cùng giao dịch với câu hỏi → AI lỗi hay bấm Hủy thì không để lại bộ rỗng.
            if (string.IsNullOrWhiteSpace(input.NewSetTitle)) throw new BusinessException("Thiếu tên bộ học mới. Hãy tạo lại bản nháp.");
            var set = await library.CreateStudySetAsync(input.NewSetTitle, userId);
            newQuizId = (await library.CreateQuizAsync(set.Id, $"Quiz — {set.Title}", userId)).Id;
        }
        var quizId = newQuizId ?? input.QuizId;
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT [Id] FROM [Quizzes] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {quizId}");
        await EnsureCanImportAsync(quizId, userId);
        // (79b) dữ liệu đã sửa là đầu vào mới: không tin cờ hợp lệ/đáp án đã chấm ở trang xem trước.
        var rows = ExcelTemplate.ValidateRows(input.Rows).Where(r => r.Include).ToList();
        if (rows.Any(r => r.Error != null)) throw new BusinessException(RowsHaveErrors);
        if (rows.Count == 0) throw new BusinessException("Hãy chọn ít nhất một dòng để nhập.");
        var quiz = await library.GetOwnedQuizAsync(quizId, userId);
        var order = quiz.Questions.Count;
        var cardOrder = (await db.Flashcards.Where(c => c.StudySetId == quiz.StudySetId).MaxAsync(c => (int?)c.OrderIndex) ?? -1) + 1;
        foreach (var row in rows)
        {
            if (row.Question is { } q) { q.QuizId = quizId; q.OrderIndex = order++; db.Questions.Add(q); }
            if (row.Card is { } card)
            {
                card.StudySetId = quiz.StudySetId; card.OrderIndex = cardOrder++; card.CreatedAt = clock.GetUtcNow().UtcDateTime;
                db.Flashcards.Add(card);
            }
        }
        db.Audit?.Add(AuditAction.ExcelImported, "Quiz", quizId, userId); // (NK3)
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        input.QuizId = quizId; // chỉ đổi sau khi đã lưu chắc chắn
        return rows.Count;
    }
}
