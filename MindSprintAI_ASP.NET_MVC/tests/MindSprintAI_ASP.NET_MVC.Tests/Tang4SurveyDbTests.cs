using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MindSprintAI_ASP.NET_MVC.Data.Entities;
using MindSprintAI_ASP.NET_MVC.Services;

namespace MindSprintAI_ASP.NET_MVC.Tests;

[Collection("db")]
public class Tang4SurveyDbTests
{
    [Fact]
    public async Task QuyenXemDungBaiGiaoCongKhaiYKienVaThuHoi()
    {
        using var h = new Harness();
        var s = await Tang4DbTests.Mixed(h, choices: 0, input: false, threshold: null);
        var service = Tang4DbTests.Surveys(h);
        var learnerIds = new List<string> { s.Student };
        for (var i = 1; i < 5; i++)
        {
            var user = await h.UserAsync($"Tên riêng {i}");
            await h.Classrooms.JoinByCodeAsync(s.ClassCode, user);
            learnerIds.Add(user);
        }
        await Assert.ThrowsAsync<ForbiddenException>(() => service.GetForLearnerAsync(s.Assignment.Id, s.Student));
        var first = await Tang4DbTests.Attempts(h).StartAsync(s.Assignment.Id, s.Student);
        await service.PublishAsync(s.Assignment.Id, true, s.Lecturer);
        await Assert.ThrowsAsync<ForbiddenException>(() => service.GetForLearnerAsync(s.Assignment.Id, s.Student)); // chưa kết thúc
        foreach (var q in first.Questions) await Tang4DbTests.Answer(h, first, q, s.Student);
        await Tang4DbTests.Attempts(h).FinishAsync(first.Id, s.Student);
        var low = await service.GetForLearnerAsync(s.Assignment.Id, s.Student);
        Assert.All(low.Questions, q => { Assert.True(q.Suppressed); Assert.Null(q.Respondents); Assert.Empty(q.Options); });
        foreach (var user in learnerIds.Skip(1))
        {
            var a = await Tang4DbTests.Attempts(h).StartAsync(s.Assignment.Id, user);
            foreach (var q in a.Questions) await Tang4DbTests.Answer(h, a, q, user);
            await Tang4DbTests.Attempts(h).FinishAsync(a.Id, user);
        }
        var teacher = await service.GetForLecturerAsync(s.Assignment.Id, s.Lecturer);
        Assert.All(teacher.Questions, q => Assert.Equal(5, q.Respondents));
        Assert.Contains(teacher.Questions.SelectMany(q => q.Opinions), x => x.DisplayName == "Minh");
        var opinion = teacher.Questions.SelectMany(q => q.Opinions).First();
        Assert.All((await service.GetForLearnerAsync(s.Assignment.Id, s.Student)).Questions, q => Assert.Empty(q.Opinions));
        await service.PublishOpinionAsync(s.Assignment.Id, opinion.SubmissionId, true, s.Lecturer);
        var student = await service.GetForLearnerAsync(s.Assignment.Id, s.Student);
        Assert.Single(student.Questions.SelectMany(q => q.Opinions));
        var json = JsonSerializer.Serialize(student);
        Assert.DoesNotContain("DisplayName", json); Assert.DoesNotContain("SubmissionId", json); Assert.DoesNotContain("Minh", json);
        Assert.All(student.Questions, q => { Assert.False(q.Suppressed); Assert.Equal(5, q.Respondents); });

        var other = await h.UserAsync("Chưa làm");
        await h.Classrooms.JoinByCodeAsync(s.ClassCode, other);
        await Assert.ThrowsAsync<ForbiddenException>(() => service.GetForLearnerAsync(s.Assignment.Id, other));
        // Làm một bài khác trong cùng lớp vẫn không được xem bài này.
        var another = await h.Assignments.CreatePracticeAsync(s.Room.Id, s.Quiz.Id, new(null, null, 1), s.Lecturer);
        var anotherAttempt = await Tang4DbTests.Attempts(h).StartAsync(another.Id, other);
        foreach (var q in anotherAttempt.Questions) await Tang4DbTests.Answer(h, anotherAttempt, q, other);
        await Tang4DbTests.Attempts(h).FinishAsync(anotherAttempt.Id, other);
        await Assert.ThrowsAsync<ForbiddenException>(() => service.GetForLearnerAsync(s.Assignment.Id, other));
        await Assert.ThrowsAsync<ForbiddenException>(() => service.GetForLecturerAsync(s.Assignment.Id, s.Student));
        await Assert.ThrowsAsync<ForbiddenException>(() => service.PublishAsync(s.Assignment.Id, true, s.Student));
        await Assert.ThrowsAsync<ForbiddenException>(() => service.PublishOpinionAsync(s.Assignment.Id, opinion.SubmissionId, true, s.Student));
        var foreign = await service.GetForLecturerAsync(another.Id, s.Lecturer);
        await Assert.ThrowsAsync<ForbiddenException>(() => service.PublishOpinionAsync(s.Assignment.Id,
            foreign.Questions.SelectMany(q => q.Opinions).First().SubmissionId, true, s.Lecturer));
        await service.PublishOpinionAsync(s.Assignment.Id, opinion.SubmissionId, false, s.Lecturer);
        Assert.Empty((await service.GetForLearnerAsync(s.Assignment.Id, s.Student)).Questions.SelectMany(q => q.Opinions));
        await service.PublishAsync(s.Assignment.Id, false, s.Lecturer);
        await Assert.ThrowsAsync<ForbiddenException>(() => service.GetForLearnerAsync(s.Assignment.Id, s.Student));
    }

    [Fact]
    public async Task ChiTinhLuotGanNhatDaKetThucLuotHuyKhongTinh()
    {
        using var h = new Harness();
        var s = await Tang4DbTests.Mixed(h, choices: 0, input: false, threshold: null, test: true);
        var attempts = Tang4DbTests.Attempts(h);
        var first = await attempts.StartAsync(s.Assignment.Id, s.Student);
        foreach (var q in first.Questions) await Tang4DbTests.Answer(h, first, q, s.Student);
        await attempts.FinishAsync(first.Id, s.Student);
        await h.Assignments.CloseNowAsync(s.Assignment.Id, s.Lecturer);
        var retakes = new RetakeService(h.Db, h.Assignments, attempts, h.Clock);
        await retakes.RequestAsync(s.Assignment.Id, "Làm lại", s.Student);
        var request = await h.Db.RetakeRequests.SingleAsync(r => r.ParticipantId == first.ParticipantId);
        await retakes.DecideAsync(request.Id, true, h.Clock.UtcNow.AddHours(1), s.Lecturer);
        var second = await retakes.StartAsync(request.Id, s.Student);
        foreach (var q in second.Questions)
        {
            var snap = Tang4DbTests.Snap(q);
            await attempts.AnswerAsync(second.Id, q.Id, Guid.NewGuid(), SurveyRules.IsChoice(snap.Type) ? [snap.Options.Last().Id] : [],
                s.Student, value: "Ý kiến lượt hai");
        }
        await attempts.FinishAsync(second.Id, s.Student);
        var service = Tang4DbTests.Surveys(h);
        var teacher = await service.GetForLecturerAsync(s.Assignment.Id, s.Lecturer);
        Assert.All(teacher.Questions, q => Assert.Equal(1, q.Respondents));
        Assert.Equal("Ý kiến lượt hai", Assert.Single(teacher.Questions.SelectMany(q => q.Opinions)).Text);
        await retakes.CancelAsync(second.Id, "Hủy lượt hai", s.Lecturer);
        teacher = await service.GetForLecturerAsync(s.Assignment.Id, s.Lecturer);
        Assert.Equal("Ý kiến", Assert.Single(teacher.Questions.SelectMany(q => q.Opinions)).Text);
        await retakes.CancelAsync(first.Id, "Hủy lượt đầu", s.Lecturer);
        Assert.All((await service.GetForLecturerAsync(s.Assignment.Id, s.Lecturer)).Questions, q => Assert.Equal(0, q.Respondents));
    }

    [Fact]
    public async Task SoanKhaoSatBoDapAnDungDiemVaGiuThuTuKhiBatDao()
    {
        using var h = new Harness();
        var s = await Tang4DbTests.Mixed(h, choices: 0, input: false, threshold: null);
        Assert.All(s.Quiz.Questions, q =>
        {
            Assert.Equal(0, q.Points); Assert.True(q.IsRequired); Assert.All(q.Options, o => Assert.False(o.IsCorrect));
            Assert.Equal(q.Type == QuestionType.SurveyText ? 500 : null, q.MaxLength);
        });
        s.Assignment.ShuffleOptions = true;
        await h.Db.SaveChangesAsync();
        var a = await Tang4DbTests.Attempts(h).StartAsync(s.Assignment.Id, s.Student);
        foreach (var q in a.Questions)
        {
            var snap = Tang4DbTests.Snap(q);
            Assert.Equal(snap.Options.Select(o => o.Id), JsonSerializer.Deserialize<int[]>(q.OptionOrderJson!));
            Assert.True(snap.IsRequired);
        }
    }
}
