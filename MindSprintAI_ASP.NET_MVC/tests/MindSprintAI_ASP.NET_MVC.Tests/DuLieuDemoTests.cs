using MindSprintAI_ASP.NET_MVC.Services;

namespace MindSprintAI_ASP.NET_MVC.Tests;

// Dữ liệu mẫu tài khoản demo: đi qua service thật, mọi dòng mẫu phải hợp lệ (không dòng nào bị bỏ vì lỗi).
[Collection("db")]
public class DuLieuDemoTests
{
    [Fact]
    public async Task TaoDuBoHocSoTayCongViecLichHoc()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(1);
        var seed = new DemoSeedService(h.Db, new ExcelImportService(h.Db, h.Library, h.Clock), new NotebookService(h.Db, null!, null!, null!, h.Clock),
            new KanbanService(h.Db), new StudyScheduleService(h.Db));
        await seed.SeedIfEmptyAsync(s.Student);
        await seed.SeedIfEmptyAsync(s.Student); // lần hai: đã có bộ học → không thêm trùng

        var set = h.Db.StudySets.Single(x => x.OwnerId == s.Student && x.Title == DemoSeedService.SetTitle);
        Assert.Equal(6, h.Db.Flashcards.Count(f => f.StudySetId == set.Id));
        Assert.Equal(5, h.Db.Questions.Count(q => q.Quiz.StudySetId == set.Id));
        Assert.Equal(1, h.Db.Notebooks.Count(n => n.UserId == s.Student));
        Assert.Equal(3, h.Db.KanbanTasks.Count(t => t.UserId == s.Student));
        Assert.Equal(2, h.Db.StudySlots.Count(t => t.UserId == s.Student));
    }
}
