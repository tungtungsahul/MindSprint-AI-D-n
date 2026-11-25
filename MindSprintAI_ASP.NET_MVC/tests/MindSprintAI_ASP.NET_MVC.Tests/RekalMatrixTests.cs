using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using MindSprintAI_ASP.NET_MVC.Data;
using MindSprintAI_ASP.NET_MVC.Data.Entities;
using MindSprintAI_ASP.NET_MVC.Services;

namespace MindSprintAI_ASP.NET_MVC.Tests;

[Collection("db")]
public class RekalMatrixTests
{
    private static GradebookRow Row(string id, string name) => new(id, name, CompletionStatus.Completed, CompletionLabels.None, 1, null, null);

    [Theory]
    [InlineData(ScoreSelection.First, 0)]
    [InlineData(ScoreSelection.Latest, 100)]
    [InlineData(ScoreSelection.Highest, 100)]
    [InlineData(ScoreSelection.Average, 50)]
    public async Task MaTranLayDungLuotDongGopVaoDiem(ScoreSelection selection, int expected)
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(1);
        s.Assignment.ScoreSelection = selection;
        var p = new Participant { Assignment = s.Assignment, UserId = s.Student };
        for (var i = 1; i <= 2; i++)
        {
            var a = new Attempt { Participant = p, AttemptNumber = i, Status = AttemptStatus.Ended, FirstScore = i - 1, MaxScore = 1 };
            a.Questions.Add(new() { QuestionId = s.Quiz.Questions[0].Id, SnapshotJson = QuestionSnapshot.From(s.Quiz.Questions[0]).ToJson(), AnswerCount = 1,
                Submissions = [new() { ClientRequestId = Guid.NewGuid(), IsFirstAnswer = true, IsFinal = true, Score = i - 1, IsCorrect = i == 2 }] });
            h.Db.Attempts.Add(a);
        }
        await h.Db.SaveChangesAsync();
        var result = await h.Gradebook.GetMatrixAsync(s.Assignment.Id, s.Lecturer, [Row(s.Student, "Minh")]);
        Assert.Equal((decimal)expected, result.Columns[0].CorrectPercent);
        Assert.Equal(selection == ScoreSelection.Average ? 2 : 1, result.Rows[0].Cells[0].Count);
    }

    [Fact]
    public async Task CanhBaoTuBangTheoDoiGomRoiManHinhVaChoChuyenMay()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(1);
        var p = new Participant { Assignment = s.Assignment, UserId = s.Student };
        var attempt = new Attempt { Participant = p, AttemptNumber = 1, MonitorScreen = true, DeadlineAt = h.Clock.UtcNow.AddHours(1), LastSeenAt = h.Clock.UtcNow };
        attempt.DeviceChanges.Add(new() { RequestingDeviceId = "may-khac", CreatedAt = h.Clock.UtcNow });
        attempt.ScreenLeaves.Add(new() { Kind = ScreenLeaveKind.Hidden, Counted = true, StartedAt = h.Clock.UtcNow.AddSeconds(-10), EndedAt = h.Clock.UtcNow });
        h.Db.Attempts.Add(attempt); await h.Db.SaveChangesAsync();
        var (_, rows) = await h.Gradebook.GetReportAsync(s.Assignment.Id, s.Lecturer);
        var row = Assert.Single(rows, GradebookService.NeedsAttention);
        Assert.Equal(1, row.Screen!.Count); Assert.NotNull(row.DeviceChange);
        Assert.Equal(h.Clock.UtcNow, row.DeviceChange.RequestedAt);
    }

    [Fact]
    public async Task DungSaiChuaLamChoChamVaTiLeCaLop()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(4);
        var p = new Participant { Assignment = s.Assignment, UserId = s.Student };
        var attempt = new Attempt { Participant = p, Status = AttemptStatus.Ended, AttemptNumber = 1, MaxScore = 4, FirstScore = 1 };
        for (var i = 0; i < 4; i++)
        {
            var snapshot = QuestionSnapshot.From(s.Quiz.Questions[i]);
            if (i == 3) snapshot = snapshot with { Type = QuestionType.TextInput };
            var q = new AttemptQuestion { QuestionId = snapshot.QuestionId, SnapshotJson = snapshot.ToJson(), AnswerCount = i == 2 ? 0 : 1 };
            if (i != 2) q.Submissions.Add(new() { ClientRequestId = Guid.NewGuid(), IsFirstAnswer = true, IsFinal = true,
                Score = i == 3 ? null : i == 0 ? 1 : 0, IsCorrect = i == 3 ? null : i == 0 });
            attempt.Questions.Add(q);
        }
        h.Db.Attempts.Add(attempt); await h.Db.SaveChangesAsync();
        var result = await h.Gradebook.GetMatrixAsync(s.Assignment.Id, s.Lecturer, [Row(s.Student, "Minh"), Row("not-started", "An")]);
        Assert.Equal(new[] { MatrixState.Correct, MatrixState.Incorrect, MatrixState.Unanswered, MatrixState.Pending }, result.Rows[0].Cells.Select(c => c[0].State));
        Assert.Equal(50m, result.Columns[0].CorrectPercent);
        Assert.All(result.Columns.Skip(1), c => Assert.Equal(0m, c.CorrectPercent));
        await Assert.ThrowsAsync<ForbiddenException>(() => h.Gradebook.GetMatrixAsync(s.Assignment.Id, s.Student, []));
    }

    [Fact]
    public async Task LamChuTheoLanDauVaCoTotNhatKhongTheoDiemChamLai()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(1);
        s.Assignment.MasteryThreshold = 100;
        var p = new Participant { Assignment = s.Assignment, UserId = s.Student, MasteryReached = true };
        var a = new Attempt { Participant = p, AttemptNumber = 1, MasteredCount = 1, Status = AttemptStatus.Ended, MasteryReachedAt = h.Clock.UtcNow };
        a.Questions.Add(new() { QuestionId = s.Quiz.Questions[0].Id, SnapshotJson = QuestionSnapshot.From(s.Quiz.Questions[0]).ToJson(), IsMastered = true, AnswerCount = 2,
            Submissions = [new() { ClientRequestId = Guid.NewGuid(), IsFirstAnswer = true, IsCorrect = false, Score = 0 }, new() { ClientRequestId = Guid.NewGuid(), IsFinal = true, IsCorrect = true, Score = 1 }] });
        h.Db.Attempts.Add(a); await h.Db.SaveChangesAsync();
        var result = await h.Gradebook.GetMatrixAsync(s.Assignment.Id, s.Lecturer, [Row(s.Student, "Minh")]);
        Assert.Equal(new MasteryOverview(0, 100, 1, 1), result.Mastery);
        Assert.Equal(MatrixState.Incorrect, result.Rows[0].Cells[0][0].State);
    }

    [Fact]
    public async Task SoTruyVanCoDinhKhiTangSoHocVien()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(1);
        var counter = new ReadCounter();
        var options = new DbContextOptionsBuilder<AppDbContext>(AppDbContextFactory.BuildOptions(DatabaseFixture.ConnectionString)).AddInterceptors(counter).Options;
        await using var db = new AppDbContext(options);
        var service = new GradebookService(db, new(db, h.Classrooms, h.Codes, h.Clock, h.Notifier), h.Clock);
        await service.GetMatrixAsync(s.Assignment.Id, s.Lecturer, [Row(s.Student, "Minh")]);
        var small = counter.Count; counter.Count = 0;
        await service.GetMatrixAsync(s.Assignment.Id, s.Lecturer, Enumerable.Range(0, 50).Select(i => Row($"u-{i}", "Tên")).ToList());
        Assert.Equal(small, counter.Count);
        Assert.InRange(small, 1, 7);
    }

    private sealed class ReadCounter : DbCommandInterceptor
    {
        public int Count;
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { Count++; return ValueTask.FromResult(result); }
    }
}

public class RekalMatrixPureTests
{
    [Fact]
    public void DungQuyTacLayLuotVaKhongTinhLuotHuyHoacLoai()
    {
        var first = new Attempt { AttemptNumber = 1, Status = AttemptStatus.Ended, FirstScore = 1, MaxScore = 2 };
        var best = new Attempt { AttemptNumber = 2, Status = AttemptStatus.Ended, FirstScore = 2, MaxScore = 2 };
        var latest = new Attempt { AttemptNumber = 3, Status = AttemptStatus.Ended, FirstScore = 0, MaxScore = 2 };
        var excluded = new Attempt { AttemptNumber = 4, Status = AttemptStatus.Ended, ExcludeFromGradebook = true };
        var cancelled = new Attempt { AttemptNumber = 5, Status = AttemptStatus.Cancelled };
        Attempt[] all = [first, best, latest, excluded, cancelled];
        Assert.Same(first, Assert.Single(GradebookService.ScoredAttempts(all, ScoreSelection.First)));
        Assert.Same(best, Assert.Single(GradebookService.ScoredAttempts(all, ScoreSelection.Highest)));
        Assert.Same(latest, Assert.Single(GradebookService.ScoredAttempts(all, ScoreSelection.Latest)));
        Assert.Equal(new[] { first, best, latest }, GradebookService.ScoredAttempts(all, ScoreSelection.Average));
        Assert.Equal(1m, GradebookService.Select(all, ScoreSelection.Average)!.Raw);
    }

    [Fact]
    public void CanhBaoTinhTheoHocVienVaDungNguonBangDiem()
    {
        var row = new GradebookRow("1", "An", CompletionStatus.Completed, CompletionLabels.None, 0, null, null);
        Assert.False(GradebookService.NeedsAttention(row));
        Assert.True(GradebookService.NeedsAttention(row with { DeviceChange = new(1, 2, null, null) }));
        Assert.True(GradebookService.NeedsAttention(row with { Screen = new(2, 12, false, true, null, null) }));
        Assert.True(GradebookService.NeedsAttention(row with { EndText = "Tự nộp vì rời màn hình quá 2 lần" }));
    }
}
