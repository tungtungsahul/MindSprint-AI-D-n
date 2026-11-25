using System.Reflection;
using Microsoft.EntityFrameworkCore;
using MindSprintAI_ASP.NET_MVC.Data;
using MindSprintAI_ASP.NET_MVC.Data.Entities;
using MindSprintAI_ASP.NET_MVC.Services;

namespace MindSprintAI_ASP.NET_MVC.Tests;

// Đo CPU 14/11/2026: AttemptLock.LoadAsync bỏ ReloadAsync từng dòng, làm mới theo nhóm. Kết quả phải giống ReloadAsync.
[Collection("db")]
public class LamMoiTheoNhomTests
{
    private static Task<Attempt> Load(AppDbContext db, long id) =>
        (Task<Attempt>)typeof(AttemptService).Assembly.GetType("MindSprintAI_ASP.NET_MVC.Services.AttemptLock")!
            .GetMethod("LoadAsync", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [db, id])!;

    [Fact]
    public async Task GiongReload_BoThayDoiChuaLuu_DocGiaTriMoi_GiuDongMoiThem_BoDongDaXoa()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(2);
        var a = await h.Attempts.StartAsync(s.Assignment.Id, s.Student);
        var now = h.Clock.UtcNow;
        await using (var seed = DatabaseFixture.NewContext())
        {
            seed.ScreenLeaves.Add(new ScreenLeave { AttemptId = a.Id, Kind = ScreenLeaveKind.Hidden, StartedAt = now });
            await seed.SaveChangesAsync();
        }
        await using var db = DatabaseFixture.NewContext(); await using var tx = await db.Database.BeginTransactionAsync();
        var tracked = await db.Attempts.Include(x => x.Questions).Include(x => x.ScreenLeaves).SingleAsync(x => x.Id == a.Id);
        var question = tracked.Questions.First();
        var oldCount = question.AnswerCount;
        var gone = tracked.ScreenLeaves.Single();
        question.AnswerCount = 99;                                    // thay đổi chưa lưu → phải bị bỏ
        var added = new ScreenLeave { AttemptId = a.Id, Kind = ScreenLeaveKind.OtherWindow, StartedAt = now };
        db.ScreenLeaves.Add(added);                                   // dòng mới chưa lưu → phải giữ (Added)
        var newDeadline = now.AddHours(5);
        await using (var other = DatabaseFixture.NewContext())        // người khác đổi hạn và xóa dòng rời màn hình
        {
            await other.Attempts.Where(x => x.Id == a.Id).ExecuteUpdateAsync(u => u.SetProperty(x => x.DeadlineAt, newDeadline));
            await other.ScreenLeaves.Where(x => x.Id == gone.Id).ExecuteDeleteAsync();
        }

        var fresh = await Load(db, a.Id);

        Assert.Same(tracked, fresh);
        Assert.Equal(newDeadline, fresh.DeadlineAt);
        Assert.Equal(EntityState.Unchanged, db.Entry(fresh).State);
        Assert.Equal(oldCount, question.AnswerCount);
        Assert.Equal(EntityState.Unchanged, db.Entry(question).State);
        Assert.Equal(EntityState.Detached, db.Entry(gone).State);
        Assert.Equal(EntityState.Added, db.Entry(added).State);
    }
}
