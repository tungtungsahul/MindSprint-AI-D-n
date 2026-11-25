using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MindSprintAI_ASP.NET_MVC.Data;
using MindSprintAI_ASP.NET_MVC.Data.Entities;

namespace MindSprintAI_ASP.NET_MVC.Services;

// Dữ liệu mẫu cho tài khoản demo (chỉ Development + chính máy, xem AccountDemo.cs): bấm "Dùng thử tài khoản demo" lần đầu là có sẵn
// một bộ học (thẻ + quiz), một sổ tay có nguồn và ghi chú, công việc và lịch học để trình diễn. Đi qua đúng các service thật
// nên dữ liệu qua cùng kiểm tra như người dùng tự nhập. Nội dung tự viết, không lấy từ nguồn có bản quyền.
public sealed class DemoSeedService(AppDbContext db, ExcelImportService excel, NotebookService notebooks, KanbanService kanban, StudyScheduleService schedule)
{
    public const string SetTitle = "Demo — Sinh học tế bào";

    private static readonly object[] Cards =
    [
        new { front = "Tế bào", back = "Đơn vị cấu tạo và chức năng cơ bản của mọi cơ thể sống." },
        new { front = "Ti thể", back = "Bào quan tạo năng lượng (ATP) cho tế bào qua hô hấp tế bào." },
        new { front = "Lục lạp", back = "Bào quan ở tế bào thực vật, nơi diễn ra quang hợp." },
        new { front = "Ribôxôm", back = "Nơi tổng hợp prôtêin." },
        new { front = "Màng sinh chất", back = "Lớp màng bao quanh tế bào, kiểm soát chất đi vào và đi ra." },
        new { front = "Nhân tế bào", back = "Chứa vật chất di truyền (ADN), điều khiển hoạt động của tế bào." },
    ];

    private static readonly object[] Questions =
    [
        new { question = "Bào quan nào tạo ra phần lớn năng lượng cho tế bào?", options = new[] { "Ti thể", "Lục lạp", "Ribôxôm", "Không bào" }, correct = new[] { 1 }, explanation = "Ti thể thực hiện hô hấp tế bào, tạo ATP." },
        new { question = "Quang hợp diễn ra ở bào quan nào?", options = new[] { "Nhân", "Lục lạp", "Ti thể", "Bộ máy Gôngi" }, correct = new[] { 2 }, explanation = "Lục lạp chứa diệp lục hấp thụ ánh sáng." },
        new { question = "Prôtêin được tổng hợp ở đâu?", options = new[] { "Ribôxôm", "Màng sinh chất", "Không bào", "Lục lạp" }, correct = new[] { 1 }, explanation = "" },
        new { question = "Những bào quan nào có ở tế bào thực vật?", options = new[] { "Lục lạp", "Thành tế bào", "Ti thể", "Trung thể" }, correct = new[] { 1, 2, 3 }, explanation = "Trung thể chủ yếu có ở tế bào động vật." },
        new { question = "Vật chất di truyền ADN nằm chủ yếu ở đâu?", options = new[] { "Màng sinh chất", "Nhân tế bào", "Ribôxôm", "Tế bào chất" }, correct = new[] { 2 }, explanation = "" },
    ];

    public async Task SeedIfEmptyAsync(string userId)
    {
        if (!await db.StudySets.AnyAsync(x => x.OwnerId == userId)) await SeedAsync(userId);
    }

    public async Task SeedAsync(string userId)
    {
        var rows = AiGenerateService.ToRows(JsonSerializer.SerializeToElement(Cards), cards: true, Cards.Length)
            .Concat(AiGenerateService.ToRows(JsonSerializer.SerializeToElement(Questions), cards: false, Questions.Length)).ToList();
        await excel.SaveAsync(ExcelImportService.ToPreview(0, "Dữ liệu mẫu", ExcelTemplate.ValidateRows(rows), SetTitle), userId);

        var notebook = await notebooks.CreateAsync("Demo — Sinh học", userId);
        await notebooks.AddSourceAsync(notebook.Id, "Bài 1: Cấu tạo tế bào",
            "Tế bào là đơn vị cơ bản của sự sống. Tế bào nhân thực gồm màng sinh chất, tế bào chất và nhân. Trong tế bào chất có nhiều bào quan: "
            + "ti thể tạo năng lượng, ribôxôm tổng hợp prôtêin, lục lạp (ở thực vật) thực hiện quang hợp. Nhân chứa ADN và điều khiển mọi hoạt động của tế bào.",
            NotebookSourceKind.Text, null, userId);
        await notebooks.AddNoteAsync(notebook.Id, "Cần nhớ", "Ti thể = năng lượng · Lục lạp = quang hợp · Ribôxôm = prôtêin", userId);

        await kanban.CreateAsync(userId, new("Ôn bộ thẻ Sinh học tế bào", "Ôn ít nhất 1 lượt", null, KanbanPriority.High));
        await kanban.CreateAsync(userId, new("Làm quiz demo", null, null, KanbanPriority.Medium, KanbanStatus.Doing));
        await kanban.CreateAsync(userId, new("Đọc lại Bài 1 trong Sổ tay AI", null, null, KanbanPriority.Low, KanbanStatus.Done));

        await schedule.CreateAsync(userId, new(DayOfWeek.Monday, new(19, 0), new(20, 0), StudyTopic.General, "Sinh học"));
        await schedule.CreateAsync(userId, new(DayOfWeek.Thursday, new(20, 0), new(20, 30), StudyTopic.English, "Từ vựng"));
    }
}
