using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using MindSprintAI_ASP.NET_MVC.Data;
using MindSprintAI_ASP.NET_MVC.Data.Entities;
using MindSprintAI_ASP.NET_MVC.Models;
using MindSprintAI_ASP.NET_MVC.Services;

namespace MindSprintAI_ASP.NET_MVC.Tests;

public class Tang2RuleTests
{
    private static readonly DateTime Now = new(2026, 11, 5, 8, 0, 0, DateTimeKind.Utc);

    private static Attempt Attempt(AttemptStatus status = AttemptStatus.InProgress)
    {
        var snap = new QuestionSnapshot(1, 0, QuestionType.MultipleChoice, "Câu hỏi", "Giải thích bí mật", 2,
            [new(11, 0, "A", true), new(12, 1, "B", false)]);
        return new Attempt
        {
            Status = status, DeadlineAt = Now, MaxScore = 2,
            Participant = new Participant { Assignment = new Assignment { Mode = AssignmentMode.Test, TestEndsAt = Now } },
            Questions = [new AttemptQuestion
            {
                Id = 1, SnapshotJson = snap.ToJson(), DisplayOrder = 0, OptionOrderJson = "[12,11]",
                AnswerCount = 1, State = AttemptQuestionState.Answered, FirstScore = 2,
                Submissions = [new AnswerSubmission { IsFinal = true, IsCorrect = true, Score = 2, PayloadJson = "{\"Selected\":[11]}" }]
            }]
        };
    }

    [Theory]
    [InlineData(AttemptStatus.InProgress, false, false, false)]
    [InlineData(AttemptStatus.InProgress, true, false, false)]
    [InlineData(AttemptStatus.Paused, true, false, false)]
    [InlineData(AttemptStatus.Ended, false, false, false)]
    [InlineData(AttemptStatus.Ended, true, true, false)]
    [InlineData(AttemptStatus.Ended, true, false, true)]
    public void B5_B6_68_ChiTraDapAnKhiDuocPhep(AttemptStatus status, bool published, bool retaking, bool reveal)
    {
        var a = Attempt(status);
        if (published) a.Participant.Assignment.AnswersPublishedAt = Now;
        var q = Assert.Single(AttemptService.ProjectQuestions(a, retaking));
        Assert.Equal(new[] { 12, 11 }, q.Options.Select(o => o.Id));
        Assert.Equal(new[] { 11 }, q.Selected);
        var json = JsonSerializer.Serialize(q);
        if (reveal)
        {
            Assert.Equal(new[] { 11 }, q.CorrectIds);
            Assert.True(q.IsCorrect);
            Assert.Equal("Giải thích bí mật", q.Explanation);
        }
        else
        {
            Assert.DoesNotContain("IsCorrect", json);
            Assert.DoesNotContain("CorrectIds", json);
            Assert.DoesNotContain("Explanation", json);
            Assert.Null(q.IsCorrect);
            Assert.Null(q.Explanation);
        }
    }

    [Fact]
    public void OnTap_VanHienPhanHoiSauKhiTraLoi()
    {
        var a = Attempt();
        a.Participant.Assignment.Mode = AssignmentMode.Practice;
        Assert.True(Assert.Single(AttemptService.ProjectQuestions(a, false)).IsCorrect);
        Assert.True(AttemptService.CanShowScores(a.Participant));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void B6_DiemChiHienSauCongBo(bool published)
    {
        var a = Attempt();
        if (published) a.Participant.Assignment.ScoresPublishedAt = Now;
        Assert.Equal(published, AttemptService.CanShowScores(a.Participant));
        AttemptRules.End(a, a.Participant, AttemptEndReason.Submitted, Now);
        Assert.Equal(published ? CompletionStatus.Completed : CompletionStatus.SubmittedAwaitingScore, a.Participant.CompletionStatus);
    }

    [Theory]
    [InlineData(AttemptEndReason.DueDate, 2)]
    [InlineData(AttemptEndReason.ClosedByLecturer, 2)]
    [InlineData(AttemptEndReason.Submitted, 0)]
    public void B4_NhapChiChamKhiHetGioHoacGvDong(AttemptEndReason reason, int score)
    {
        var a = Attempt();
        var q = a.Questions.Single();
        q.AnswerCount = 0;
        q.State = AttemptQuestionState.Unanswered;
        q.FirstScore = null;
        q.Submissions.Clear();
        q.DraftJson = "{\"Selected\":[11]}";
        AttemptRules.End(a, a.Participant, reason, Now);
        Assert.Equal(score, a.FirstScore);
        Assert.Equal(score == 0 ? 0 : 1, q.Submissions.Count);
    }

    [Theory]
    [InlineData("{\"Selected\":[]}")]
    [InlineData("{\"Selected\":[999]}")]
    public void B4_NhapKhongHopLe_0Diem(string draft)
    {
        var a = Attempt();
        var q = a.Questions.Single();
        q.AnswerCount = 0;
        q.FirstScore = null;
        q.DraftJson = draft;
        q.Submissions.Clear();
        AttemptRules.End(a, a.Participant, AttemptEndReason.DueDate, Now);
        Assert.Equal(0m, a.FirstScore);
    }

    [Fact]
    public void B2_DeadlineDungGioChungCongThemPhut()
    {
        var a = Attempt();
        a.Participant.ExtraMinutes = 10;
        a.Participant.Assignment.DueAt = Now.AddDays(3);
        Assert.Equal(Now.AddMinutes(10), AttemptRules.DeadlineFor(a.Participant.Assignment, a.Participant));
    }

    [Fact]
    public void C2_BangDiemLayLuotMoiNhat_HuyThiLayLuotCu()
    {
        var a = new Assignment { Mode = AssignmentMode.Test, ScoreSelection = ScoreSelection.Highest };
        var old = new Attempt { AttemptNumber = 1, Status = AttemptStatus.Ended, FirstScore = 10, MaxScore = 10 };
        var retake = new Attempt { AttemptNumber = 2, Kind = AttemptKind.Retake, MaxScore = 10 };
        Assert.Equal(10m, GradebookService.SelectForAssignment([old], a)!.Raw);
        Assert.Null(GradebookService.SelectForAssignment([old, retake], a));
        retake.Status = AttemptStatus.Ended;
        retake.FirstScore = 3;
        Assert.Equal(3m, GradebookService.SelectForAssignment([old, retake], a)!.Raw);
        retake.Status = AttemptStatus.Cancelled;
        Assert.Equal(10m, GradebookService.SelectForAssignment([old, retake], a)!.Raw);
    }

    [Fact]
    public void C3_HuyLuotGiaiPhongTran()
    {
        var a = Attempt(AttemptStatus.Ended);
        a.Participant.Attempts.Add(a);
        Assert.True(RetakeService.CanRequest(a.Participant));
        var retake = new Attempt { Kind = AttemptKind.Retake, Status = AttemptStatus.Ended };
        a.Participant.Attempts.Add(retake);
        Assert.False(RetakeService.CanRequest(a.Participant));
        retake.Status = AttemptStatus.Cancelled;
        Assert.True(RetakeService.CanRequest(a.Participant));
        a.Participant.RetakeRequests.Add(new RetakeRequest());
        Assert.False(RetakeService.CanRequest(a.Participant));
    }

    [Fact]
    public void A_MacDinhDaoTheoCheDo()
    {
        var practice = new PracticeSettings(null, null, null);
        var test = new TestSettings(null, 45);
        Assert.False(practice.ShuffleOptions);
        Assert.False(practice.ShuffleQuestions);
        Assert.True(test.ShuffleOptions);
        Assert.True(test.ShuffleQuestions);
        Assert.Contains("Được làm lại nhưng không làm", DisplayText.Of(CompletionLabels.RetakeNotUsed));
        Assert.Equal("Làm lại", DisplayText.Of(AttemptKind.Retake));
    }

    [Fact]
    // MindSprint gộp lịch sử QuizEdu thành KhoiTao; thay test riêng migration Tang2 bằng luật chung của AGENTS.md:
    // mọi migration sau KhoiTao chỉ thêm, không xóa/đổi tên bảng hay cột.
    public void Migration_SauKhoiTao_ChiThemKhongXoaHayDoiTen()
    {
        using var db = DatabaseFixture.NewContext();
        var migrations = db.GetService<IMigrationsAssembly>();
        var later = migrations.Migrations.Where(m => !m.Key.EndsWith("_KhoiTao")).ToList();
        Assert.Contains(later, m => m.Key.EndsWith("_BatRcsi"));
        foreach (var (id, type) in later)
        {
            var migration = migrations.CreateMigration(type, db.Database.ProviderName!);
            Assert.All(migration.UpOperations, op => Assert.False(
                op is DropTableOperation or DropColumnOperation or RenameTableOperation or RenameColumnOperation,
                $"{id}: {op.GetType().Name} — migration chỉ được thêm bảng/cột (AGENTS.md)."));
        }
    }
}
