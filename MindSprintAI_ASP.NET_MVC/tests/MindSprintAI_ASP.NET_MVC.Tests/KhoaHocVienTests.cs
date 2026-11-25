using System.Reflection;
using Microsoft.EntityFrameworkCore;
using MindSprintAI_ASP.NET_MVC.Data;
using MindSprintAI_ASP.NET_MVC.Data.Entities;
using MindSprintAI_ASP.NET_MVC.Services;

namespace MindSprintAI_ASP.NET_MVC.Tests;

// Thử tải 13/11/2026: 650 khách cùng làm bài thì mọi thao tác xếp hàng qua MỘT khóa riêng của dòng bài.
// Người dùng duyệt: học viên khóa CHUNG dòng bài + khóa riêng "ngăn kéo" của chính mình; GV khóa ĐỘC QUYỀN, chờ học viên đang thao tác.
[Collection("db")]
public class KhoaHocVienTests
{
    private static readonly Type Lock = typeof(AttemptService).Assembly.GetType("MindSprintAI_ASP.NET_MVC.Services.AttemptLock")!;
    private static Task<Assignment> Learner(AppDbContext db, int id, string learner) =>
        (Task<Assignment>)Lock.GetMethod("LearnerAsync", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [db, id, learner])!;
    private static Task<Assignment> Teacher(AppDbContext db, int id) =>
        (Task<Assignment>)Lock.GetMethod("AssignmentAsync", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [db, id])!;

    private static async Task<bool> StillWaiting(Task task) => await Task.WhenAny(task, Task.Delay(1500)) != task;
    private static async Task Finishes(Task task) => Assert.Same(task, await Task.WhenAny(task, Task.Delay(15000)));

    [Fact]
    public async Task HaiHocVienCungBaiKhongPhaiChoNhau()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(1);
        await using var dbA = DatabaseFixture.NewContext(); await using var txA = await dbA.Database.BeginTransactionAsync();
        await Learner(dbA, s.Assignment.Id, "hoc-vien-a");
        await using var dbB = DatabaseFixture.NewContext(); await using var txB = await dbB.Database.BeginTransactionAsync();
        await Finishes(Learner(dbB, s.Assignment.Id, "hoc-vien-b")); // khóa riêng cũ: B phải chờ A xong
    }

    [Fact]
    public async Task HaiTabCuaCungMotNguoiVanXepHang()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(1);
        await using var dbA = DatabaseFixture.NewContext(); var txA = await dbA.Database.BeginTransactionAsync();
        await Learner(dbA, s.Assignment.Id, "cung-mot-nguoi");
        await using var dbB = DatabaseFixture.NewContext(); await using var txB = await dbB.Database.BeginTransactionAsync();
        var second = Learner(dbB, s.Assignment.Id, "cung-mot-nguoi");
        Assert.True(await StillWaiting(second));
        await txA.CommitAsync(); await txA.DisposeAsync();
        await Finishes(second);
    }

    [Fact]
    public async Task GiaoVienChoHocVienDangThaoTacXong()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(1);
        await using var dbA = DatabaseFixture.NewContext(); var txA = await dbA.Database.BeginTransactionAsync();
        await Learner(dbA, s.Assignment.Id, "hoc-vien-a");
        await using var dbT = DatabaseFixture.NewContext(); await using var txT = await dbT.Database.BeginTransactionAsync();
        var teacher = Teacher(dbT, s.Assignment.Id);
        Assert.True(await StillWaiting(teacher)); // GV đóng bài/đổi hạn không chen vào giữa một lần lưu của học viên
        await txA.CommitAsync(); await txA.DisposeAsync();
        await Finishes(teacher);
    }

    [Fact]
    public async Task HocVienMoiChoGiaoVienLamXong()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(1);
        await using var dbT = DatabaseFixture.NewContext(); var txT = await dbT.Database.BeginTransactionAsync();
        await Teacher(dbT, s.Assignment.Id);
        await using var dbA = DatabaseFixture.NewContext(); await using var txA = await dbA.Database.BeginTransactionAsync();
        var learner = Learner(dbA, s.Assignment.Id, "hoc-vien-a");
        Assert.True(await StillWaiting(learner));
        await txT.CommitAsync(); await txT.DisposeAsync();
        await Finishes(learner);
    }

    [Fact]
    public async Task CamNangKhoaChungThanhKhoaRiengTrongCungGiaoDich()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(1);
        await using var db = DatabaseFixture.NewContext(); var tx = await db.Database.BeginTransactionAsync();
        await Learner(db, s.Assignment.Id, "hoc-vien-a");
        await Assert.ThrowsAsync<InvalidOperationException>(() => Teacher(db, s.Assignment.Id));
        await tx.RollbackAsync(); await tx.DisposeAsync(); // thả khóa chung trước khi thử phần sau
        await using var db2 = DatabaseFixture.NewContext(); await using var tx2 = await db2.Database.BeginTransactionAsync();
        await Teacher(db2, s.Assignment.Id); await Learner(db2, s.Assignment.Id, "gv-xem-nhu-hoc-vien"); await Teacher(db2, s.Assignment.Id); // đã độc quyền thì không sao
    }

    [Fact]
    public async Task TrangTheoDoiCuaGiaoVienKhongBatCaLopCho()
    {
        // Tài khoản 2 rà 13/11/2026: trang Live tự làm mới ~mỗi giây; nếu vẫn khóa độc quyền cả bài thì cả lớp đứng hình theo nhịp đó.
        using var h = new Harness(); var s = await h.ScenarioAsync(1);
        await using var dbA = DatabaseFixture.NewContext(); await using var txA = await dbA.Database.BeginTransactionAsync();
        await Learner(dbA, s.Assignment.Id, "hoc-vien-dang-luu");
        await Finishes(h.Gradebook.GetReportAsync(s.Assignment.Id, s.Lecturer)); // không chờ học viên đang lưu bài
    }

    [Fact]
    public async Task GiaoVienXemBaiMotHocVienKhongBatNguoiKhacCho()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(1);
        var attempt = await h.Attempts.StartAsync(s.Assignment.Id, s.Student);
        await using var dbA = DatabaseFixture.NewContext(); await using var txA = await dbA.Database.BeginTransactionAsync();
        await Learner(dbA, s.Assignment.Id, "hoc-vien-khac-dang-luu");
        await Finishes(h.Gradebook.GetAttemptForLecturerAsync(attempt.Id, s.Lecturer));
    }

    [Fact]
    public async Task TienDoTheCaNhanKhoaTheoTungNguoi()
    {
        // Tự ôn thẻ trước đây khóa dòng bộ học → cả lớp tự ôn cùng bộ xếp hàng. Nay khóa theo (bộ học, người).
        using var h = new Harness(); var s = await h.ScenarioAsync(1);
        var cards = Lock.GetMethod("PersonalCardsAsync", BindingFlags.NonPublic | BindingFlags.Static)!;
        Task Cards(AppDbContext db, string user) => (Task)cards.Invoke(null, [db, s.Quiz.StudySetId, user])!;
        await using var dbA = DatabaseFixture.NewContext(); var txA = await dbA.Database.BeginTransactionAsync();
        await Cards(dbA, "nguoi-a");
        await using var dbB = DatabaseFixture.NewContext(); await using var txB = await dbB.Database.BeginTransactionAsync();
        await Finishes(Cards(dbB, "nguoi-b"));
        await using var dbC = DatabaseFixture.NewContext(); await using var txC = await dbC.Database.BeginTransactionAsync();
        var sameUser = Cards(dbC, "nguoi-a");
        Assert.True(await StillWaiting(sameUser)); // hai tab của cùng một người vẫn xếp hàng
        await txA.CommitAsync(); await txA.DisposeAsync();
        await Finishes(sameUser);
    }

    private static AttemptService Service(AppDbContext db, Harness h, string device) =>
        new(db, h.Clock, [new ChoiceGrader()], new TestDevice { Id = device }, new NullLearnNotifier(), new LearnConnectionTracker());

    [Fact]
    public async Task HaiMuoiHocVienCungBatDauVaTraLoiSongSong()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(2);
        var students = new List<string>();
        for (var i = 0; i < 20; i++) { var u = await h.UserAsync($"HV{i}"); await h.Classrooms.JoinByCodeAsync(s.ClassCode, u); students.Add(u); }
        var results = await Task.WhenAll(students.Select(async u =>
        {
            await using var db = DatabaseFixture.NewContext();
            var service = Service(db, h, "may-" + u);
            var attempt = await service.StartAsync(s.Assignment.Id, u);
            var questions = await db.AttemptQuestions.Where(q => q.AttemptId == attempt.Id).OrderBy(q => q.Id).ToListAsync();
            foreach (var q in questions)
            {
                var snap = QuestionSnapshot.FromJson(q.SnapshotJson);
                await service.AnswerAsync(attempt.Id, q.Id, Guid.NewGuid(), [snap.Options.First(o => o.IsCorrect).Id], u);
            }
            await service.FinishAsync(attempt.Id, u);
            return attempt.Id;
        }));
        Assert.Equal(20, results.Distinct().Count());
        Assert.Equal(20, await h.Db.Attempts.CountAsync(a => a.Participant.AssignmentId == s.Assignment.Id && a.Status == AttemptStatus.Ended));
        Assert.Equal(40, await h.Db.AnswerSubmissions.CountAsync(x => x.AttemptQuestion.Attempt.Participant.AssignmentId == s.Assignment.Id));
    }

    [Fact]
    public async Task BangTheoDoiChiNapCauCuaLuotDangMoMaVanDungTienDoVaDiem()
    {
        // Thử tải 14/11/2026: không nạp đề của lượt đã xong (trừ khi có mốc thuộc bài / "cho điểm tối đa").
        using var h = new Harness(); var s = await h.ScenarioAsync(2);
        var other = await h.UserAsync("HV xong"); await h.Classrooms.JoinByCodeAsync(s.ClassCode, other);
        async Task<long> AnswerFirst(string user, bool finish)
        {
            var at = await h.Attempts.StartAsync(s.Assignment.Id, user);
            var q = await h.Db.AttemptQuestions.Where(x => x.AttemptId == at.Id).OrderBy(x => x.Id).FirstAsync();
            await h.Attempts.AnswerAsync(at.Id, q.Id, Guid.NewGuid(), [QuestionSnapshot.FromJson(q.SnapshotJson).Options.First(o => o.IsCorrect).Id], user);
            if (finish) await h.Attempts.FinishAsync(at.Id, user);
            return at.Id;
        }
        await AnswerFirst(s.Student, false); await AnswerFirst(other, true);
        var (a, rows) = await h.Gradebook.GetReportAsync(s.Assignment.Id, s.Lecturer);
        Assert.False(GradebookService.NeedsEndedQuestions(a));
        Assert.Equal("1/2 câu đã trả lời", rows.Single(r => r.UserId == s.Student).Progress);
        Assert.NotNull(rows.Single(r => r.UserId == other).Score);
        a.MasteryThreshold = 80; Assert.True(GradebookService.NeedsEndedQuestions(a));
    }

    [Fact]
    public async Task HaiTabCungBamBatDauChiCoMotLuot()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(1);
        var ids = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
        {
            await using var db = DatabaseFixture.NewContext();
            return (await Service(db, h, "cung-may").StartAsync(s.Assignment.Id, s.Student)).Id;
        }));
        Assert.Equal(ids[0], ids[1]);
        Assert.Equal(1, await h.Db.Attempts.CountAsync(a => a.Participant.AssignmentId == s.Assignment.Id));
    }

    [Fact]
    public async Task MuoiLamKhachCungTenVaoCungLucVanKhongTrungTen()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(1);
        await h.Assignments.ChangeStrangerPolicyAsync(s.Assignment.Id, StrangerPolicy.Allow, s.Lecturer);
        await Task.WhenAll(Enumerable.Range(0, 15).Select(async _ =>
        {
            await using var db = DatabaseFixture.NewContext();
            await new GuestService(db, h.Clock, new NullLearnNotifier()).JoinAsync(s.Assignment.Id, "Hoa", GuestService.NewToken());
        }));
        var names = await h.Db.Participants.Where(p => p.AssignmentId == s.Assignment.Id && p.GuestId != null).Select(p => p.GuestDisplayName).ToListAsync();
        Assert.Equal(15, names.Count);
        Assert.Equal(15, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    // Thử tải P2 14/11/2026: một hàng chung cho cả bài → 213/650 khách không vào được. Nay chỉ tên có thể trùng mới chờ nhau.
    [Fact]
    public async Task KhachKhacTenKhongChoNhau_TenCoTheTrungVanXepHangVaKhongTrung()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(1);
        await h.Assignments.ChangeStrangerPolicyAsync(s.Assignment.Id, StrangerPolicy.Allow, s.Lecturer);
        Task Join(string name) => Task.Run(async () =>
        {
            await using var db = DatabaseFixture.NewContext();
            await new GuestService(db, h.Clock, new NullLearnNotifier()).JoinAsync(s.Assignment.Id, name, GuestService.NewToken());
        });
        await using var holder = DatabaseFixture.NewContext(); var tx = await holder.Database.BeginTransactionAsync();
        await (Task)Lock.GetMethod("GuestNameAsync", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [holder, s.Assignment.Id, "Lan"])!;
        await Finishes(Join("Minh"));                       // tên khác: không chờ người đang giữ hàng "Lan"
        var sameName = Join("lan (2)");                     // cùng tên gốc "lan" (khác hoa thường, có hậu tố): phải chờ
        Assert.True(await StillWaiting(sameName));
        await tx.CommitAsync(); await tx.DisposeAsync();
        await Finishes(sameName);
        await Task.WhenAll(Join("Lan"), Join("LAN"), Join("Lan (2)"));
        var names = await h.Db.Participants.Where(p => p.AssignmentId == s.Assignment.Id && p.GuestId != null).Select(p => p.GuestDisplayName).ToListAsync();
        Assert.Equal(5, names.Count);
        Assert.Equal(5, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal("minh", AttemptLockKey("Minh (12)")); Assert.Equal("hoà", AttemptLockKey(" Hoà "));
    }

    private static string AttemptLockKey(string name) =>
        (string)Lock.GetMethod("GuestNameKey", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [name])!;
}
