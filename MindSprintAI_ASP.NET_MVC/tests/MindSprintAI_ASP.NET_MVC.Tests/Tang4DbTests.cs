using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MindSprintAI_ASP.NET_MVC.Data.Entities;
using MindSprintAI_ASP.NET_MVC.Services;

namespace MindSprintAI_ASP.NET_MVC.Tests;

[Collection("db")]
public class Tang4DbTests
{
    internal static AttemptService Attempts(Harness h) => new(h.Db, h.Clock,
        [new ChoiceGrader(), new TextInputGrader(), new NumberInputGrader(), new SurveyGrader()], h.Device, h.Notifier, h.Connections);
    internal static SurveyService Surveys(Harness h) => new(h.Db, h.Assignments, h.Clock);

    internal static async Task<Scenario> Mixed(Harness h, int choices = 2, bool input = true, bool surveys = true,
        int? threshold = 80, int? limit = null, bool retries = false, bool test = false)
    {
        var lecturer = await h.UserAsync("Lan");
        var student = await h.UserAsync("Minh");
        var set = await h.Library.CreateStudySetAsync("Bộ tầng 4", lecturer);
        var quiz = await h.Library.CreateQuizAsync(set.Id, "Quiz tầng 4", lecturer);
        for (var i = 0; i < choices; i++)
            await h.Library.SaveChoiceQuestionAsync(quiz.Id, null, null, Harness.Single($"Chọn {i + 1}"), lecturer);
        if (input) await h.Library.SaveInputQuestionAsync(quiz.Id, null, null,
            new InputQuestionInput(QuestionType.TextInput, "Điền chữ", "GIẢI_THÍCH_BÍ_MẬT", 2, [new[] { "Đúng" }]), lecturer);
        if (surveys)
        {
            foreach (var type in new[] { QuestionType.SurveySingle, QuestionType.SurveyMultiple, QuestionType.SurveyText })
                await h.Library.SaveChoiceQuestionAsync(quiz.Id, null, null, new ChoiceQuestionInput(type, Display(type), null,
                    999, true, type == QuestionType.SurveyText ? [] : [new("A", true), new("B", true)], IsRequired: true), lecturer);
        }
        Assert.Empty(await h.Library.MarkReadyAsync(quiz.Id, lecturer));
        var room = await h.Classrooms.CreateAsync("Lớp tầng 4", ClassJoinPolicy.Immediate, lecturer);
        var code = (await h.Codes.GetActiveAsync(JoinCodeTarget.Classroom, room.Id))!.Code;
        await h.Classrooms.JoinByCodeAsync(code, student);
        h.Clock.UtcNow = h.Clock.UtcNow.AddMinutes(1);
        var assignment = test
            ? await h.Assignments.CreateTestAsync(room.Id, quiz.Id, new TestSettings(null, 45), lecturer)
            : await h.Assignments.CreatePracticeAsync(room.Id, quiz.Id,
                new PracticeSettings(null, h.Clock.UtcNow.AddHours(2), 5, MasteryThreshold: threshold,
                    AnswersPerQuestion: limit, ScoreRetries: retries), lecturer);
        if (test) await h.Assignments.StartTestAsync(assignment.Id, lecturer);
        return new(lecturer, student, quiz, room, assignment, code);
    }

    private static string Display(QuestionType type) => Models.DisplayText.Of(type);
    internal static QuestionSnapshot Snap(AttemptQuestion q) => QuestionSnapshot.FromJson(q.SnapshotJson);
    internal static async Task Answer(Harness h, Attempt a, AttemptQuestion q, string user, bool correct = true, Guid? request = null)
    {
        var snap = Snap(q);
        var selected = SurveyRules.IsChoice(snap.Type)
            ? new[] { snap.Options.First(o => SurveyRules.IsSurvey(snap.Type) || o.IsCorrect == correct).Id } : [];
        await Attempts(h).AnswerAsync(a.Id, q.Id, request ?? Guid.NewGuid(), selected, user,
            snap.Type == QuestionType.TextInput ? [correct ? "Đúng" : "Sai"] : null,
            snap.Type == QuestionType.SurveyText ? "Ý kiến" : null);
    }

    [Theory]
    [InlineData(false), InlineData(true)]
    public async Task LamLaiGiuDiemLanDauVaChiTinhDiemSauLuyenKhiBat(bool retries)
    {
        using var h = new Harness();
        var s = await Mixed(h, choices: 7, retries: retries);
        var a = await Attempts(h).StartAsync(s.Assignment.Id, s.Student);
        Assert.Equal(9, a.MaxScore);
        Assert.Equal(7, MasteryRules.Denominator(a));
        var choice = a.Questions.Where(q => MasteryRules.Eligible(Snap(q).Type)).ToList();
        await Answer(h, a, choice[0], s.Student, false);
        await Answer(h, a, choice[0], s.Student);
        Assert.Equal(0, a.FirstScore);
        Assert.Equal(retries ? 1m : null, a.PracticeScore);
        Assert.Equal(1, a.MasteredCount);
        Assert.Equal(2, choice[0].AnswerCount);
        Assert.Equal(2, choice[0].Submissions.Count);
        Assert.Single(choice[0].Submissions, x => x.IsFinal);
        foreach (var q in choice.Skip(1).Take(4)) await Answer(h, a, q, s.Student);
        Assert.Null(a.MasteryReachedAt); // 5/7 chưa đủ 80%.
        await Answer(h, a, choice[5], s.Student);
        var reachedAt = a.MasteryReachedAt;
        Assert.NotNull(reachedAt); // 6/7 mới đạt.
        await Assert.ThrowsAsync<BusinessException>(() => Attempts(h).FinishAsync(a.Id, s.Student));
        await Answer(h, a, choice[6], s.Student, false);
        Assert.Equal(reachedAt, a.MasteryReachedAt);
        Assert.True(a.Participant.MasteryReached);
        var input = a.Questions.Single(q => Snap(q).Type == QuestionType.TextInput);
        await Answer(h, a, input, s.Student);
        Assert.Equal(7, a.FirstScore); // (24A) câu lần đầu sau mốc vẫn vào điểm.
        foreach (var q in a.Questions.Where(q => SurveyRules.IsSurvey(Snap(q).Type)))
        {
            await Answer(h, a, q, s.Student);
            Assert.Null(q.Submissions.Single(x => x.IsFinal).IsCorrect);
            Assert.Equal(0, q.LatestScore);
        }
        Assert.Equal(retries ? 8m : null, a.PracticeScore);
        await Attempts(h).FinishAsync(a.Id, s.Student);
        Assert.Equal(AttemptEndReason.FinishedAfterMastery, a.EndReason);
        Assert.Equal(CompletionStatus.Completed, a.Participant.CompletionStatus);
        var report = await h.Gradebook.GetReportAsync(s.Assignment.Id, s.Lecturer);
        Assert.Equal(7, report.Rows.Single().Score!.Raw);
        Assert.Equal(retries ? 8m : null, report.Rows.Single().Score!.Practice);
        Assert.Equal("Đạt mốc 80%", report.Rows.Single().Mastery);
        var next = await Attempts(h).StartAsync(s.Assignment.Id, s.Student);
        Assert.Equal(0, next.MasteredCount);
        Assert.Null(next.MasteryReachedAt);
        Assert.All(next.Questions, q => { Assert.False(q.IsMastered); Assert.Equal(0, q.AnswerCount); });
    }

    [Fact]
    public async Task ChuaDatConLuotBiChanHetLuotDuocKetThucVaKhongVuotGioiHan()
    {
        using var h = new Harness();
        var s = await Mixed(h, choices: 2, input: false, surveys: false, threshold: 100, limit: 2);
        var a = await Attempts(h).StartAsync(s.Assignment.Id, s.Student);
        await Assert.ThrowsAsync<BusinessException>(() => Attempts(h).FinishAsync(a.Id, s.Student));
        foreach (var q in a.Questions) await Answer(h, a, q, s.Student, false);
        await Assert.ThrowsAsync<BusinessException>(() => Attempts(h).FinishAsync(a.Id, s.Student));
        foreach (var q in a.Questions) await Answer(h, a, q, s.Student, false);
        await Assert.ThrowsAsync<BusinessException>(() => Answer(h, a, a.Questions[0], s.Student));
        await Attempts(h).FinishAsync(a.Id, s.Student);
        Assert.Equal(AttemptEndReason.Submitted, a.EndReason);
        Assert.Null(a.MasteryReachedAt);
        Assert.Contains("Chưa đạt mốc (0/2, mốc 100%)", (await Attempts(h).GetAssignmentPageAsync(s.Assignment.Id, s.Student)).Mastery);
    }

    [Fact]
    public async Task CauDienVaKhaoSatKhongDuocLamLaiOnTap()
    {
        using var h = new Harness();
        var s = await Mixed(h);
        var a = await Attempts(h).StartAsync(s.Assignment.Id, s.Student);
        foreach (var q in a.Questions.Where(q => !MasteryRules.Eligible(Snap(q).Type)))
        {
            await Answer(h, a, q, s.Student, false);
            await Assert.ThrowsAsync<BusinessException>(() => Answer(h, a, q, s.Student));
        }
        Assert.Equal(0, a.MasteredCount);
    }

    [Theory]
    [InlineData(false), InlineData(true)]
    public async Task DenHanHoacDongGiuMucLamChuVaNhanThieuKhaoSat(bool close)
    {
        using var h = new Harness();
        var s = await Mixed(h);
        var a = await Attempts(h).StartAsync(s.Assignment.Id, s.Student);
        foreach (var q in a.Questions.Where(q => MasteryRules.Eligible(Snap(q).Type))) await Answer(h, a, q, s.Student);
        var at = a.MasteryReachedAt;
        if (close) await h.Assignments.CloseNowAsync(s.Assignment.Id, s.Lecturer);
        else h.Clock.UtcNow = a.DeadlineAt!.Value;
        a = await Attempts(h).GetForLearnerAsync(a.Id, s.Student);
        Assert.Equal(AttemptStatus.Ended, a.Status);
        Assert.Equal(2, a.MasteredCount);
        Assert.Equal(at, a.MasteryReachedAt);
        Assert.True(a.Participant.MasteryReached);
        Assert.Equal(CompletionStatus.Completed, a.Participant.CompletionStatus);
        Assert.True(a.Participant.CompletionLabels.HasFlag(CompletionLabels.MissingRequiredSurvey));
        await Assert.ThrowsAsync<BusinessException>(() => Answer(h, a, a.Questions[^1], s.Student));
    }

    [Fact]
    public async Task SettingChiTruocLuotDauVaPhaiCoCauLuaChon()
    {
        using var h = new Harness();
        var s = await Mixed(h);
        await h.Assignments.ChangeMasteryAsync(s.Assignment.Id, new(90, 1, true), s.Lecturer);
        Assert.Equal(90, s.Assignment.MasteryThreshold);
        Assert.True(s.Assignment.ScoreRetries);
        await Assert.ThrowsAsync<BusinessException>(() => h.Assignments.ChangeMasteryAsync(s.Assignment.Id, new(85), s.Lecturer));
        await Assert.ThrowsAsync<BusinessException>(() => h.Assignments.ChangeMasteryAsync(s.Assignment.Id, new(80, 0), s.Lecturer));
        await Assert.ThrowsAsync<ForbiddenException>(() => h.Assignments.ChangeMasteryAsync(s.Assignment.Id, new(80), s.Student));
        await Attempts(h).StartAsync(s.Assignment.Id, s.Student);
        await Assert.ThrowsAsync<BusinessException>(() => h.Assignments.ChangeMasteryAsync(s.Assignment.Id, new(null), s.Lecturer));
        var noChoice = await Mixed(h, choices: 0, threshold: null);
        await Assert.ThrowsAsync<BusinessException>(() => h.Assignments.CreatePracticeAsync(noChoice.Room.Id, noChoice.Quiz.Id,
            new(null, null, 1, MasteryThreshold: 80), noChoice.Lecturer));
        await Assert.ThrowsAsync<BusinessException>(() => h.Assignments.ChangeMasteryAsync(noChoice.Assignment.Id, new(80), noChoice.Lecturer));
        var test = await Mixed(h, test: true);
        await Assert.ThrowsAsync<BusinessException>(() => h.Assignments.ChangeMasteryAsync(test.Assignment.Id, new(80), test.Lecturer));
    }

    [Theory]
    [InlineData(false), InlineData(true)]
    public async Task KhaoSatBatBuocChanNop500KyTuVaKiemTraDuocDoi(bool test)
    {
        using var h = new Harness();
        var s = await Mixed(h, choices: 0, input: false, threshold: null, test: test);
        var a = await Attempts(h).StartAsync(s.Assignment.Id, s.Student);
        Assert.Equal(0, a.MaxScore);
        Assert.Equal(0, MasteryRules.Denominator(a));
        Assert.True((await Attempts(h).GetAssignmentPageAsync(s.Assignment.Id, s.Student)).Ungraded);
        var text = a.Questions.Single(q => Snap(q).Type == QuestionType.SurveyText);
        await Assert.ThrowsAsync<BusinessException>(() => Attempts(h).AnswerAsync(a.Id, text.Id, Guid.NewGuid(), [], s.Student, value: new string('a', 501)));
        var error = await Assert.ThrowsAsync<BusinessException>(() => Attempts(h).FinishAsync(a.Id, s.Student));
        Assert.Contains("Còn câu khảo sát bắt buộc chưa trả lời: câu", error.Message);
        foreach (var q in a.Questions) await Answer(h, a, q, s.Student);
        if (test)
        {
            await Attempts(h).AnswerAsync(a.Id, text.Id, Guid.NewGuid(), [], s.Student, value: new string('b', 500));
            Assert.Equal(2, text.AnswerCount);
            Assert.Single(text.Submissions, x => x.IsFinal);
        }
        await Attempts(h).FinishAsync(a.Id, s.Student);
        Assert.Equal(0, a.FirstScore);
        Assert.Equal("Không chấm điểm", ScoreFormatter.Format(a.FirstScore, a.MaxScore, s.Assignment.ScoreScale, s.Assignment.Rounding));
        Assert.All(a.Questions.SelectMany(q => q.Submissions), x => Assert.Null(x.IsCorrect));
        Assert.False(a.Participant.CompletionLabels.HasFlag(CompletionLabels.MissingRequiredSurvey));
    }

    [Fact]
    public async Task KiemTraThieuKhaoSatBatBuocVanChoCongBoDiemNhuNguoiKhac()
    {
        // (54B, B5b) nhãn thiếu khảo sát không được biến bài chờ công bố thành "Hoàn thành".
        using var h = new Harness();
        var s = await Mixed(h, choices: 1, input: false, threshold: null, test: true);
        var a = await Attempts(h).StartAsync(s.Assignment.Id, s.Student);
        await Answer(h, a, a.Questions.Single(q => Snap(q).Type == QuestionType.SingleChoice), s.Student);
        h.Clock.UtcNow = a.DeadlineAt!.Value;
        a = await Attempts(h).GetForLearnerAsync(a.Id, s.Student);
        Assert.True(a.Participant.CompletionLabels.HasFlag(CompletionLabels.MissingRequiredSurvey));
        Assert.Equal(CompletionStatus.SubmittedAwaitingScore, a.Participant.CompletionStatus);
        Assert.Equal(CompletionStatus.SubmittedAwaitingScore, (await h.Gradebook.GetReportAsync(s.Assignment.Id, s.Lecturer)).Rows.Single().Status);
    }

    [Fact]
    public async Task KhaoSatKiemTraNhapDuocChotKhiHetGioNhungKhongTuDien()
    {
        using var h = new Harness();
        var s = await Mixed(h, choices: 0, input: false, threshold: null, test: true);
        var a = await Attempts(h).StartAsync(s.Assignment.Id, s.Student);
        var text = a.Questions.Single(q => Snap(q).Type == QuestionType.SurveyText);
        await Attempts(h).SaveDraftAsync(a.Id, text.Id, [], s.Student, value: "Ý kiến đang nhập");
        h.Clock.UtcNow = a.DeadlineAt!.Value;
        a = await Attempts(h).GetForLearnerAsync(a.Id, s.Student);
        Assert.Equal(1, text.AnswerCount);
        Assert.Null(text.Submissions.Single().IsCorrect);
        Assert.Contains(a.Questions, q => q.AnswerCount == 0);
        Assert.True(a.Participant.CompletionLabels.HasFlag(CompletionLabels.MissingRequiredSurvey));
        Assert.Equal(CompletionStatus.Completed, a.Participant.CompletionStatus);
        Assert.Equal(CompletionStatus.Completed, (await h.Gradebook.GetReportAsync(s.Assignment.Id, s.Lecturer)).Rows.Single().Status);
    }

    [Fact]
    public async Task IdempotencyKhiLamLaiKhongCongHaiLanVaKhongDoiThuTuLuaChon()
    {
        using var h = new Harness();
        var s = await Mixed(h, choices: 1, input: false, surveys: false, limit: 2);
        var a = await Attempts(h).StartAsync(s.Assignment.Id, s.Student);
        var q = a.Questions.Single();
        var order = q.OptionOrderJson;
        await Answer(h, a, q, s.Student, false);
        var request = Guid.NewGuid();
        await Answer(h, a, q, s.Student, request: request);
        await Answer(h, a, q, s.Student, request: request);
        Assert.Equal(2, q.AnswerCount);
        Assert.Equal(1, a.MasteredCount);
        Assert.Equal(order, q.OptionOrderJson);
        Assert.Equal(0, a.FirstScore);
        Assert.Equal(2, q.Submissions.Count);
        var browser = (await Attempts(h).GetPlayQuestionsAsync(a, s.Student)).Single();
        Assert.Equal(2, browser.AnswerCount);
        Assert.True(browser.IsMastered);
        Assert.False(browser.CanRetry);
    }

    [Fact]
    public async Task HaiYeuCauLamLaiDongThoiKhongVuotGioiHanVaKhongDemLamChuHaiLan()
    {
        using var h = new Harness();
        var s = await Mixed(h, choices: 1, input: false, surveys: false, limit: 2);
        var a = await Attempts(h).StartAsync(s.Assignment.Id, s.Student);
        var q = a.Questions.Single();
        await Answer(h, a, q, s.Student, false);
        using var first = new Harness();
        using var second = new Harness();
        await Attempts(first).GetForLearnerAsync(a.Id, s.Student);
        await Attempts(second).GetForLearnerAsync(a.Id, s.Student);
        async Task<bool> Send(Harness tab)
        {
            try { await Answer(tab, a, q, s.Student); return true; }
            catch (BusinessException) { return false; }
        }
        var results = await Task.WhenAll(Send(first), Send(second));
        Assert.Single(results, x => x);
        using var fresh = new Harness();
        var saved = await Attempts(fresh).GetForLearnerAsync(a.Id, s.Student);
        Assert.Equal(1, saved.MasteredCount);
        Assert.Equal(2, saved.Questions.Single().AnswerCount);
        Assert.Equal(2, saved.Questions.Single().Submissions.Count);
    }
}
