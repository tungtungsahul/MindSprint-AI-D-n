using System.Text.Json;
using MindSprintAI_ASP.NET_MVC.Data.Entities;
using MindSprintAI_ASP.NET_MVC.Services;

namespace MindSprintAI_ASP.NET_MVC.Tests;

public class Tang4SurveyTests
{
    private static QuestionSnapshot Question(QuestionType type) => new(1, 0, type, "Khảo sát", null, 0,
        type == QuestionType.SurveyText ? [] : [new(1, 0, "A", false), new(2, 1, "B", false)]);

    private static Attempt Response(int person, QuestionSnapshot q, string payload, bool published = false, int number = 1)
    {
        var a = new Attempt { ParticipantId = person, Participant = new() { User = new() { DisplayName = "TÊN_RIÊNG_BÍ_MẬT" } },
            Status = AttemptStatus.Ended, AttemptNumber = number, EndedAt = new DateTime(2026, 11, 5).AddMinutes(number) };
        a.Questions.Add(new() { QuestionId = q.QuestionId, SnapshotJson = q.ToJson(), AnswerCount = 1,
            Submissions = [new() { Id = person, IsFinal = true, PayloadJson = payload, PublishedToClass = published }] });
        return a;
    }

    [Theory]
    [InlineData(4), InlineData(5)]
    public void DuoiNamNguoiAnHetSoLieuVaYKienChiLayDaTick(int respondents)
    {
        var q = Question(QuestionType.SurveyText);
        var attempts = Enumerable.Range(1, respondents).Select(i => Response(i, q,
            JsonSerializer.Serialize(new SurveyAnswer(i == 1 ? "Ý_KIẾN_ĐƯỢC_DUYỆT" : "Ý_KIẾN_CHƯA_DUYỆT")), published: i == 1)).ToList();
        var teacher = new LecturerSurveyResults(1, DateTime.UtcNow, SurveyAggregation.Build(attempts, [q]));
        Assert.Equal(respondents, teacher.Questions[0].Respondents);
        var student = SurveyAggregation.ForLearner(teacher);
        var result = student.Questions.Single();
        var json = JsonSerializer.Serialize(student);
        Assert.DoesNotContain("DisplayName", json); Assert.DoesNotContain("SubmissionId", json);
        Assert.DoesNotContain("TÊN_RIÊNG_BÍ_MẬT", json);
        Assert.DoesNotContain("Ý_KIẾN_CHƯA_DUYỆT", string.Join("", result.Opinions));
        Assert.Equal(respondents < 5, result.Suppressed);
        if (respondents < 5) { Assert.Null(result.Respondents); Assert.Empty(result.Options); Assert.Empty(result.Opinions); }
        else { Assert.Equal(5, result.Respondents); Assert.Equal("Ý_KIẾN_ĐƯỢC_DUYỆT", Assert.Single(result.Opinions)); }
    }

    [Fact]
    public void ChiLuotKetThucGanNhatKhongTinhHuyVaChiPhanHoiCuoi()
    {
        var q = Question(QuestionType.SurveyMultiple);
        var first = Response(1, q, "{\"Selected\":[1]}");
        var recent = Response(1, q, "{\"Selected\":[2]}", number: 2);
        recent.Questions[0].Submissions.Add(new() { IsFinal = false, PayloadJson = "{\"Selected\":[1]}" });
        var cancelled = Response(1, q, "{\"Selected\":[1]}", number: 3); cancelled.Status = AttemptStatus.Cancelled;
        var unfinished = Response(2, q, "{\"Selected\":[1]}"); unfinished.Status = AttemptStatus.InProgress;
        var both = Response(3, q, "{\"Selected\":[1,2]}");
        var result = Assert.Single(SurveyAggregation.Build([first, recent, cancelled, unfinished, both], [q]));
        Assert.Equal(2, result.Respondents);
        Assert.Equal(1, result.Options[0].Count); Assert.Equal(50, result.Options[0].Percent);
        Assert.Equal(2, result.Options[1].Count); Assert.Equal(100, result.Options[1].Percent);
    }

    [Fact]
    public void NamNguoiLamNhungChiBonNguoiTraLoiCauDoVanAn()
    {
        var q = Question(QuestionType.SurveySingle);
        var attempts = Enumerable.Range(1, 5).Select(i => Response(i, q, "{\"Selected\":[1]}")).ToList();
        attempts[^1].Questions[0].Submissions.Clear();
        var teacher = new LecturerSurveyResults(1, DateTime.UtcNow, SurveyAggregation.Build(attempts, [q]));
        Assert.Equal(4, teacher.Questions[0].Respondents);
        var result = SurveyAggregation.ForLearner(teacher).Questions[0];
        Assert.True(result.Suppressed); Assert.Empty(result.Options); Assert.Null(result.Respondents);
    }
}
