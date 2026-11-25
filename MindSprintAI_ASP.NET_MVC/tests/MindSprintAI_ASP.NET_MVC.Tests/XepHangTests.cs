using Microsoft.AspNetCore.Mvc;
using MindSprintAI_ASP.NET_MVC.Controllers;
using MindSprintAI_ASP.NET_MVC.Data.Entities;
using MindSprintAI_ASP.NET_MVC.Services;

namespace MindSprintAI_ASP.NET_MVC.Tests;

// Xếp hạng (người dùng chốt 17/11/2026): bài = điểm rồi thời gian; ôn tập hiện ngay, kiểm tra sau công bố;
// lớp = trung bình % các bài đã giao (chưa làm = 0); học viên thấy top 10 + hạng của mình; GV tắt được theo lớp.
public class XepHangPureTests
{
    [Fact]
    public void HocVienThayTop10VaHangCuaMinh_GiangVienThayCaBang()
    {
        var rows = Enumerable.Range(1, 12).Select(i => new RankRow(i, "u" + i, "HV " + i, 100 - i, null, i == 12)).ToList();
        var learner = RankingService.Cut(rows, lecturer: false);
        Assert.Equal(10, learner.Rows.Count);
        Assert.Equal(12, learner.Mine?.Rank);
        Assert.Equal(12, learner.Count);
        var lecturer = RankingService.Cut(rows, lecturer: true);
        Assert.Equal(12, lecturer.Rows.Count);
        Assert.Null(lecturer.Mine);
        // Mình đã trong top 10 thì không lặp lại dòng của mình.
        Assert.Null(RankingService.Cut(rows.Select(r => r with { Me = r.Rank == 3 }).ToList(), false).Mine);
    }
}

[Collection("db")]
public class XepHangDbTests
{
    // Làm một lượt: đúng `correct` câu đầu, mất `minutes` phút, rồi nộp.
    private static async Task PlayAsync(Harness h, int assignmentId, string user, int correct, int minutes)
    {
        var at = await h.Attempts.StartAsync(assignmentId, user);
        var i = 0;
        foreach (var q in at.Questions.OrderBy(q => q.Id))
        {
            var options = QuestionSnapshot.FromJson(q.SnapshotJson).Options;
            var pick = (i++ < correct ? options.First(o => o.IsCorrect) : options.First(o => !o.IsCorrect)).Id;
            await h.Attempts.AnswerAsync(at.Id, q.Id, Guid.NewGuid(), [pick], user, questionVersion: q.QuestionVersion);
        }
        h.Clock.UtcNow = h.Clock.UtcNow.AddMinutes(minutes);
        await h.Attempts.FinishAsync(at.Id, user);
        h.Db.ChangeTracker.Clear();
    }

    private static async Task<string> StudentAsync(Harness h, Scenario s, string name)
    {
        var u = await h.UserAsync(name);
        await h.Classrooms.JoinByCodeAsync(s.ClassCode, u);
        return u;
    }

    private static RankingService Ranking(Harness h) => new(h.Db, h.Classrooms);

    [Fact]
    public async Task XepHangBai_DiemTruocRoiThoiGianRoiLucNop_MoiNguoiMotHang()
    {
        using var h = new Harness();
        var s = await h.ScenarioAsync(3);
        var b = await StudentAsync(h, s, "Bình");
        var c = await StudentAsync(h, s, "Chi");
        var d = await StudentAsync(h, s, "Dũng");
        await StudentAsync(h, s, "Én (chưa làm)");
        await PlayAsync(h, s.Assignment.Id, s.Student, 3, 5);   // 100%, 5 phút
        await PlayAsync(h, s.Assignment.Id, b, 3, 3);           // 100%, 3 phút → hạng 1
        await PlayAsync(h, s.Assignment.Id, d, 3, 3);           // 100%, 3 phút như Bình nhưng nộp sau → hạng 2 (18/11: không đồng hạng)
        await PlayAsync(h, s.Assignment.Id, c, 2, 1);           // 66,7% dù nhanh nhất → cuối

        var board = await Ranking(h).ForAssignmentAsync(s.Assignment.Id, s.Lecturer);
        Assert.Null(board.Hidden);
        Assert.Equal([1, 2, 3, 4], board.Rows.Select(r => r.Rank));
        Assert.Equal(["Bình", "Dũng"], board.Rows.Take(2).Select(r => r.Name));
        Assert.Equal(("Minh", 100m), (board.Rows[2].Name, board.Rows[2].Percent));
        Assert.Equal(("Chi", 66.7m), (board.Rows[3].Name, board.Rows[3].Percent));
        Assert.Equal(TimeSpan.FromMinutes(5), board.Rows[2].Time);

        // Ôn tập: học viên thấy ngay, có đánh dấu dòng của mình; người chưa làm không có trong bảng.
        var mine = await Ranking(h).ForAssignmentAsync(s.Assignment.Id, s.Student);
        Assert.Equal(4, mine.Count);
        Assert.True(mine.Rows.Single(r => r.Me).Name == "Minh");
        // Người ngoài lớp không xem được.
        var outsider = await h.UserAsync("Người ngoài");
        await Assert.ThrowsAsync<ForbiddenException>(() => Ranking(h).ForAssignmentAsync(s.Assignment.Id, outsider));
    }

    [Fact]
    public async Task BaiKiemTra_HocVienChiThaySauCongBo_GiangVienThayLuon()
    {
        using var h = new Harness();
        var s = await h.ScenarioAsync(3);
        var test = await h.Assignments.CreateTestAsync(s.Room.Id, s.Quiz.Id, new(null, 30), s.Lecturer);
        await h.Assignments.StartTestAsync(test.Id, s.Lecturer);
        await PlayAsync(h, test.Id, s.Student, 2, 4);

        var hidden = await Ranking(h).ForAssignmentAsync(test.Id, s.Student);
        Assert.Contains("công bố điểm", hidden.Hidden);
        Assert.Empty(hidden.Rows);
        Assert.Single((await Ranking(h).ForAssignmentAsync(test.Id, s.Lecturer)).Rows);
        // Chưa công bố thì bài kiểm tra chưa vào xếp hạng lớp (chỉ còn bài ôn tập, Minh chưa làm = 0%).
        Assert.Equal(0m, (await Ranking(h).ForClassAsync(s.Room.Id, s.Student)).Rows.Single().Percent);

        h.Clock.UtcNow = h.Clock.UtcNow.AddMinutes(31);
        await h.Assignments.PublishScoresAsync(test.Id, s.Lecturer);
        h.Db.ChangeTracker.Clear();
        var shown = await Ranking(h).ForAssignmentAsync(test.Id, s.Student);
        Assert.Null(shown.Hidden);
        Assert.Equal(66.7m, shown.Rows.Single().Percent);
        Assert.Equal(33.4m, (await Ranking(h).ForClassAsync(s.Room.Id, s.Student)).Rows.Single().Percent);   // (66,7 + 0) / 2
    }

    [Fact]
    public async Task XepHangLop_TrungBinhCacBai_ChuaLamTinh0_TatDuocChoHocVien()
    {
        using var h = new Harness();
        var s = await h.ScenarioAsync(3);
        var b = await StudentAsync(h, s, "Bình");
        var second = await h.Assignments.CreatePracticeAsync(s.Room.Id, s.Quiz.Id, new PracticeSettings(null, h.Clock.UtcNow.AddDays(2), 3), s.Lecturer);
        await PlayAsync(h, s.Assignment.Id, s.Student, 3, 2);   // Minh: 100% + chưa làm bài 2 → 50%
        await PlayAsync(h, s.Assignment.Id, b, 2, 2);           // Bình: 66,7% + 100% → 83,4% (làm tròn từng bài rồi lấy trung bình)
        await PlayAsync(h, second.Id, b, 3, 2);

        var board = await Ranking(h).ForClassAsync(s.Room.Id, s.Lecturer);
        Assert.Equal([("Bình", 83.4m, 2), ("Minh", 50m, 1)], board.Rows.Select(r => (r.Name, r.Percent, r.Done!.Value)));
        Assert.All(board.Rows, r => Assert.Equal(2, r.Total));

        await Ranking(h).SetEnabledAsync(s.Room.Id, false, s.Lecturer);
        h.Db.ChangeTracker.Clear();
        var off = await Ranking(h).ForClassAsync(s.Room.Id, s.Student);
        Assert.Contains("tắt", off.Hidden);
        Assert.Contains("tắt", (await Ranking(h).ForAssignmentAsync(s.Assignment.Id, s.Student)).Hidden);
        Assert.Equal(2, (await Ranking(h).ForClassAsync(s.Room.Id, s.Lecturer)).Rows.Count);   // GV vẫn thấy
        await Assert.ThrowsAnyAsync<Exception>(() => Ranking(h).SetEnabledAsync(s.Room.Id, true, s.Student));
        var outsider = await h.UserAsync("Người ngoài");
        await Assert.ThrowsAsync<ForbiddenException>(() => Ranking(h).ForClassAsync(s.Room.Id, outsider));
    }
}
