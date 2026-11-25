using System.Text.Json;
using MindSprintAI_ASP.NET_MVC.Data.Entities;
using MindSprintAI_ASP.NET_MVC.Models;
using MindSprintAI_ASP.NET_MVC.Services;

namespace MindSprintAI_ASP.NET_MVC.Tests;

public class Tang3AttemptTests
{
    internal static Attempt Build(QuestionType type = QuestionType.TextInput)
    {
        var q = Tang3GradingTests.TextQuestion(["DAP_AN_CHU_BI_MAT"], ["Ô hai"]) with
        { Type = type, NumberAnswer = 9327.314159m, NumberTolerance = .00123m, BlankScoring = BlankScoring.PerBlank };
        var a = new Assignment { Mode = AssignmentMode.Test };
        var p = new Participant { Assignment = a };
        var attempt = new Attempt { Participant = p, DeadlineAt = DateTime.UtcNow.AddMinutes(2), Questions = [] };
        p.Attempts.Add(attempt);
        attempt.Questions.Add(new AttemptQuestion { Id = 1, Attempt = attempt, SnapshotJson = q.ToJson() });
        return attempt;
    }

    [Theory]
    [InlineData(QuestionType.TextInput), InlineData(QuestionType.NumberInput)]
    public void JsonKhongLoDapAnKhiDangLamTruocCongBoVaDuocDuyetLamLai(QuestionType type)
    {
        var attempt = Build(type);
        foreach (var ended in new[] { false, true })
        foreach (var published in new[] { false, true })
        foreach (var retake in new[] { false, true })
        {
            attempt.Status = ended ? AttemptStatus.Ended : AttemptStatus.InProgress;
            attempt.Participant.Assignment.AnswersPublishedAt = published ? DateTime.UtcNow : null;
            var json = JsonSerializer.Serialize(AttemptService.ProjectQuestions(attempt, retake));
            if (ended && published && !retake) continue;
            Assert.DoesNotContain("DAP_AN_CHU_BI_MAT", json);
            Assert.DoesNotContain("9327", json);
            Assert.DoesNotContain("0.00123", json);
            Assert.DoesNotContain("IsCorrect", json);
            Assert.DoesNotContain("CorrectAnswers", json);
            Assert.DoesNotContain("Explanation", json);
        }
    }

    [Fact]
    public void NhapsThieuOKhongChamConHopLeChamKhiHetGio()
    {
        var attempt = Build();
        attempt.Questions[0].DraftJson = JsonSerializer.Serialize(new TextAnswer(["DAP_AN_CHU_BI_MAT", " "]));
        AttemptRules.End(attempt, attempt.Participant, AttemptEndReason.DueDate, DateTime.UtcNow);
        Assert.Equal(0, attempt.FirstScore);
        Assert.Empty(attempt.Questions[0].Submissions);

        attempt = Build();
        attempt.Questions[0].DraftJson = JsonSerializer.Serialize(new TextAnswer(["DAP_AN_CHU_BI_MAT", "Ô hai"]));
        AttemptRules.End(attempt, attempt.Participant, AttemptEndReason.DueDate, DateTime.UtcNow);
        Assert.Equal(2, attempt.FirstScore);
        Assert.Single(attempt.Questions[0].Submissions);
    }

    [Fact]
    public void DaXacNhanKhongChamLaiNhapDo()
    {
        var attempt = Build(QuestionType.NumberInput);
        attempt.Questions[0].AnswerCount = 1;
        attempt.Questions[0].FirstScore = 2;
        attempt.Questions[0].DraftJson = JsonSerializer.Serialize(new NumberAnswer("x"));
        AttemptRules.End(attempt, attempt.Participant, AttemptEndReason.DueDate, DateTime.UtcNow);
        Assert.Equal(2, attempt.FirstScore);
    }

    [Theory]
    [InlineData("0", 0), InlineData("9327.314159", 2), InlineData("5/2", 0), InlineData("", 0)]
    public void NhapSoKhiHetGio(string value, int score)
    {
        var attempt = Build(QuestionType.NumberInput);
        attempt.Questions[0].DraftJson = JsonSerializer.Serialize(new NumberAnswer(value));
        AttemptRules.End(attempt, attempt.Participant, AttemptEndReason.DueDate, DateTime.UtcNow);
        Assert.Equal(score, attempt.FirstScore);
        Assert.Equal(value is "" or "5/2" ? 0 : 1, attempt.Questions[0].Submissions.Count);
    }

    [Fact]
    public void OnTapChuaTraLoiKhongLoDapAnKeCaHetHan()
    {
        var attempt = Build();
        attempt.Participant.Assignment.Mode = AssignmentMode.Practice;
        attempt.Status = AttemptStatus.Ended;
        Assert.Null(AttemptService.ProjectQuestions(attempt, false)[0].CorrectAnswers);
    }
}
