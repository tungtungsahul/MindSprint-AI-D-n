using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MindSprintAI_ASP.NET_MVC.Data.Entities;
using MindSprintAI_ASP.NET_MVC.Services;

namespace MindSprintAI_ASP.NET_MVC.Tests;

public class Tang4cPureTests
{
    [Theory]
    [InlineData(false, QuestionType.SingleChoice)]
    [InlineData(true, QuestionType.SingleChoice)]
    [InlineData(false, QuestionType.TextInput)]
    [InlineData(true, QuestionType.TextInput)]
    [InlineData(false, QuestionType.NumberInput)]
    [InlineData(true, QuestionType.NumberInput)]
    public async Task CauCoGioChuaMoKhongGuiNoiDungTrongProjectionVaHtml(bool test, QuestionType type)
    {
        var a = Attempt(test, type); var q = a.Questions[0];
        var snap = QuestionSnapshot.FromJson(q.SnapshotJson) with
        { Prompt = "PROMPT_SECRET", Explanation = "EXPLANATION_SECRET", Blanks = [new(["ANSWER_SECRET"])],
            Options = [new(1, 0, "OPTION_A_SECRET", true), new(2, 1, "OPTION_B_SECRET", false)] };
        q.SnapshotJson = snap.ToJson();
        var projected = Assert.Single(AttemptService.ProjectQuestions(a, false));
        Assert.Empty(projected.Prompt); Assert.Empty(projected.Options); Assert.Equal(0, projected.BlankCount);
        Assert.Null(projected.Values); Assert.Null(projected.CorrectAnswers); Assert.Null(projected.Explanation);
        var json = JsonSerializer.Serialize(projected);
        Assert.DoesNotContain("PROMPT_SECRET", json); Assert.DoesNotContain("OPTION_A_SECRET", json);
        Assert.DoesNotContain("OPTION_B_SECRET", json); Assert.DoesNotContain("ANSWER_SECRET", json);
        var html = await Tang4ViewTests.Render("Learn", "Play", a, new()
        { ["Questions"] = new List<MindSprintAI_ASP.NET_MVC.Models.PlayQuestion> { projected } });
        Assert.DoesNotContain("PROMPT_SECRET", html); Assert.DoesNotContain("EXPLANATION_SECRET", html);
        Assert.DoesNotContain("ANSWER_SECRET", html);
        foreach (var option in snap.Options) Assert.DoesNotContain(System.Net.WebUtility.HtmlEncode(option.Text), html);
        Assert.DoesNotContain("answer-input", html); Assert.DoesNotContain("inputmode=", html);
        Assert.Contains("Nội dung hiện khi bạn mở câu", System.Net.WebUtility.HtmlDecode(html));

        QuestionTimerRules.Open(a, q, Now);
        projected = Assert.Single(AttemptService.ProjectQuestions(a, false));
        Assert.Equal(snap.Prompt, projected.Prompt); Assert.NotEmpty(projected.Options);
        Assert.NotNull(q.TimerStartedAt); Assert.Null(projected.CorrectIds); Assert.Null(projected.IsCorrect);
        Assert.Null(projected.Explanation); Assert.Null(projected.CorrectAnswers);
        html = await Tang4ViewTests.Render("Learn", "Play", a, new()
        { ["Questions"] = new List<MindSprintAI_ASP.NET_MVC.Models.PlayQuestion> { projected } });
        Assert.Contains("PROMPT_SECRET", html); Assert.DoesNotContain("EXPLANATION_SECRET", html);
        html = await Tang4ViewTests.Render("Learn", "_PlayQuestionBody",
            new MindSprintAI_ASP.NET_MVC.Models.OpenQuestionResult(projected, 1, 30, false, false));
        Assert.Contains("PROMPT_SECRET", html); Assert.Contains("data-seconds-left=\"30\"", html);
        Assert.DoesNotContain("data-correct=", html); Assert.DoesNotContain("data-is-correct=", html);
        Assert.DoesNotContain("EXPLANATION_SECRET", html); Assert.DoesNotContain("ANSWER_SECRET", html);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CauKhongGioVaTrangReviewVanCoNoiDung(bool test)
    {
        var a = Attempt(test, limit: null);
        Assert.Equal("Câu hỏi", Assert.Single(AttemptService.ProjectQuestions(a, false)).Prompt);
        a.Questions[0].SnapshotJson = (QuestionSnapshot.FromJson(a.Questions[0].SnapshotJson) with { TimeLimitSeconds = 30 }).ToJson();
        a.Status = AttemptStatus.Ended;
        Assert.Equal("Câu hỏi", Assert.Single(AttemptService.ProjectQuestions(a, false)).Prompt);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void CauDaMoTamDungHoacHetGioVanCoNoiDung(bool test, bool timeout)
    {
        var a = Attempt(test); var q = a.Questions[0];
        if (timeout) q.State = AttemptQuestionState.LockedTimeout;
        else q.TimeLeftSeconds = 12;
        var projected = Assert.Single(AttemptService.ProjectQuestions(a, false));
        Assert.Equal("Câu hỏi", projected.Prompt); Assert.NotEmpty(projected.Options); Assert.False(projected.BodyHidden);
    }

    internal static readonly DateTime Now = new(2026, 11, 5, 8, 0, 0, DateTimeKind.Utc);
    internal static Attempt Attempt(bool test = false, QuestionType type = QuestionType.SingleChoice, int? limit = 30)
    {
        var a = new Attempt { Participant = new Participant { Assignment = new Assignment
        {
            Mode = test ? AssignmentMode.Test : AssignmentMode.Practice, MasteryThreshold = test ? null : 100,
            AnswersPerQuestion = 2, ScoreRetries = true
        } } };
        a.Questions.Add(new AttemptQuestion { Id = 1, Attempt = a, SnapshotJson = new QuestionSnapshot(1, 0, type,
            "Câu hỏi", "Giải thích", 1, [new(1, 0, "Đúng", true), new(2, 1, "Sai", false)])
            { TimeLimitSeconds = limit, IsRequired = SurveyRules.IsSurvey(type) }.ToJson() });
        return a;
    }

    [Theory]
    [InlineData(4, false)]
    [InlineData(5, true)]
    [InlineData(3600, true)]
    [InlineData(3601, false)]
    [InlineData(null, true)]
    public void GioRiengChiNhanTu5Den3600HoacTrong(int? limit, bool valid)
    {
        var q = new Question { Type = QuestionType.SurveyText, Prompt = "Ý kiến", Points = 0, MaxLength = 500, TimeLimitSeconds = limit };
        Assert.Equal(valid, QuestionRules.Validate(q) == null);
    }

    [Fact]
    public void SnapshotGiuGioVaBanCuKhongCoGio()
    {
        var q = new Question { TimeLimitSeconds = 25 };
        var json = QuestionSnapshot.From(q).ToJson();
        q.TimeLimitSeconds = 3600;
        Assert.Equal(25, QuestionSnapshot.FromJson(json).TimeLimitSeconds);
        var old = QuestionSnapshot.FromJson(json.Replace("\"TimeLimitSeconds\":25", "\"OldLimit\":25"));
        Assert.Null(old.TimeLimitSeconds);
    }

    [Theory]
    [InlineData(30, false)]
    [InlineData(31, false)]
    [InlineData(32, false)]
    [InlineData(33, true)]
    public void KiemTraKhoaNhungNhanTreMang2Giay(int seconds, bool reject)
    {
        var a = Attempt(test: true); var q = a.Questions[0];
        QuestionTimerRules.Open(a, q, Now);
        AttemptRules.ExpireIfDue(a, a.Participant, Now.AddSeconds(seconds));
        Assert.Equal(AttemptQuestionState.LockedTimeout, q.State);
        Assert.Equal(reject, QuestionTimerRules.RejectAnswer(a, q, Now.AddSeconds(seconds)));
        Assert.Empty(q.Submissions);
        Assert.Equal(0, QuestionTimerRules.Read(a, q, Now.AddSeconds(seconds)).SecondsLeft);
    }

    [Fact]
    public void KiemTraRoiCauHoacNgatKhongDungGio()
    {
        var a = Attempt(test: true); var q = a.Questions[0];
        QuestionTimerRules.Open(a, q, Now);
        QuestionTimerRules.Pause(a, Now.AddSeconds(10));
        Assert.Equal(Now, q.TimerStartedAt);
        Assert.Equal(20, QuestionTimerRules.Read(a, q, Now.AddSeconds(10)).SecondsLeft);
    }

    [Fact]
    public void OnTapTamDungVaMoLaiDungSoGiayKhongCapLaiGio()
    {
        var a = Attempt(); var q = a.Questions[0];
        QuestionTimerRules.Open(a, q, Now);
        QuestionTimerRules.Pause(a, Now.AddSeconds(11));
        Assert.Equal(19, q.TimeLeftSeconds); Assert.Null(q.TimerStartedAt);
        QuestionTimerRules.Open(a, q, Now.AddMinutes(5));
        QuestionTimerRules.Open(a, q, Now.AddMinutes(5).AddSeconds(4));
        Assert.Equal(15, QuestionTimerRules.Read(a, q, Now.AddMinutes(5).AddSeconds(4)).SecondsLeft);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OnTapTimeoutSaiLamLaiDuGioVaTinhVaoGioiHan(bool retries)
    {
        var a = Attempt(); var q = a.Questions[0];
        a.Participant.Assignment.ScoreRetries = retries;
        QuestionTimerRules.Open(a, q, Now);
        QuestionTimerRules.Expire(a, Now.AddSeconds(33));
        QuestionTimerRules.Expire(a, Now.AddSeconds(34));
        var first = Assert.Single(q.Submissions);
        Assert.False(first.IsCorrect); Assert.Equal(0, first.Score); Assert.Equal(0, q.FirstScore);
        Assert.True(first.IsFirstAnswer); Assert.True(first.IsFinal); Assert.Equal(1, q.AnswerCount);
        Assert.True(MasteryRules.CanRetry(a, q)); Assert.Equal(retries ? 0m : null, a.PracticeScore);
        var projected = Assert.Single(AttemptService.ProjectQuestions(a, false));
        Assert.False(projected.IsCorrect); Assert.NotNull(projected.CorrectIds);
        QuestionTimerRules.Open(a, q, Now.AddSeconds(40), retry: true);
        Assert.Equal(30, QuestionTimerRules.Read(a, q, Now.AddSeconds(40)).SecondsLeft);
        QuestionTimerRules.Open(a, q, Now.AddSeconds(45), retry: true);
        Assert.Equal(25, QuestionTimerRules.Read(a, q, Now.AddSeconds(45)).SecondsLeft);
        QuestionTimerRules.Expire(a, Now.AddSeconds(73));
        Assert.Equal(2, q.AnswerCount); Assert.Equal(2, q.Submissions.Count);
        Assert.Single(q.Submissions, s => s.IsFinal); Assert.False(MasteryRules.CanRetry(a, q));
        Assert.True(MasteryRules.Progress(a)!.CanFinish);
    }

    [Fact]
    public void MoCauKhacTamDungCauCuKeCaCauMoiKhongGio()
    {
        var a = Attempt(); var q = a.Questions[0];
        var next = new AttemptQuestion { Id = 2, Attempt = a,
            SnapshotJson = (QuestionSnapshot.FromJson(q.SnapshotJson) with { TimeLimitSeconds = null }).ToJson() };
        a.Questions.Add(next);
        QuestionTimerRules.Open(a, q, Now);
        QuestionTimerRules.Open(a, next, Now.AddSeconds(7));
        Assert.Null(q.TimerStartedAt); Assert.Equal(23, q.TimeLeftSeconds); Assert.Null(next.TimerStartedAt);
        QuestionTimerRules.Open(a, q, Now.AddMinutes(10));
        Assert.Equal(23, QuestionTimerRules.Read(a, q, Now.AddMinutes(10)).SecondsLeft);
    }

    [Fact]
    public void KhaoSatTimeoutKhongBiaPhanHoiVanGiuNhanThieu()
    {
        var a = Attempt(type: QuestionType.SurveyText); var q = a.Questions[0];
        QuestionTimerRules.Open(a, q, Now);
        QuestionTimerRules.Expire(a, Now.AddSeconds(33));
        Assert.Empty(q.Submissions); Assert.Equal(0, q.AnswerCount);
        Assert.Equal(new[] { 1 }, SurveyRules.MissingRequired(a));
        Assert.True(MasteryRules.Progress(a)!.CanFinish);
        AttemptRules.End(a, a.Participant, AttemptEndReason.Submitted, Now.AddSeconds(34));
        Assert.True(a.Participant.CompletionLabels.HasFlag(CompletionLabels.MissingRequiredSurvey));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HanCaLuotThangGioRieng(bool test)
    {
        var a = Attempt(test); var q = a.Questions[0]; a.DeadlineAt = Now.AddSeconds(10);
        QuestionTimerRules.Open(a, q, Now);
        Assert.Equal(10, QuestionTimerRules.Read(a, q, Now).SecondsLeft);
        Assert.True(AttemptRules.ExpireIfDue(a, a.Participant, Now.AddSeconds(100)));
        Assert.Equal(AttemptStatus.Ended, a.Status); Assert.Empty(q.Submissions);
        Assert.Null(q.TimerStartedAt); Assert.Equal(a.DeadlineAt, a.EndedAt);
    }

    [Fact]
    public void CauKhongGioKhongBiAnhHuong()
    {
        var a = Attempt(limit: null); var q = a.Questions[0];
        QuestionTimerRules.Open(a, q, Now);
        QuestionTimerRules.Expire(a, Now.AddDays(2));
        Assert.Null(q.TimerStartedAt); Assert.Null(q.TimeLeftSeconds);
        Assert.Null(QuestionTimerRules.Read(a, q, Now).SecondsLeft); Assert.Empty(q.Submissions);
    }

    [Fact]
    public void OpenQuestionJsonChiCoTrangThaiDongHo()
    {
        var a = Attempt(); var q = a.Questions[0];
        QuestionTimerRules.Open(a, q, Now);
        var json = JsonSerializer.Serialize(QuestionTimerRules.Read(a, q, Now), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(new[] { "secondsLeft", "locked", "expired" }, doc.RootElement.EnumerateObject().Select(p => p.Name));
        Assert.DoesNotContain("Đúng", json); Assert.DoesNotContain("isCorrect", json); Assert.DoesNotContain("explanation", json);
    }

    [Fact]
    public void NopKiemTraChamNhapCauDaKhoaNhungGiuLanXacNhanCuoi()
    {
        var a = Attempt(test: true, type: QuestionType.MultipleChoice); var q = a.Questions[0];
        q.DraftJson = JsonSerializer.Serialize(new ChoiceAnswer([1]));
        QuestionTimerRules.Open(a, q, Now);
        QuestionTimerRules.Expire(a, Now.AddSeconds(33));
        AttemptRules.End(a, a.Participant, AttemptEndReason.Submitted, Now.AddSeconds(35));
        Assert.Equal(1, a.FirstScore); Assert.Single(q.Submissions);
        Assert.Equal(AttemptQuestionState.LockedTimeout, q.State);
    }
}

[Collection("db")]
public class Tang4cTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MoCauMoiNhanNoiDungVaDongHoDaChay(bool test)
    {
        using var h = new Harness(); var (s, a) = await Start(h, test: test); var q = a.Questions[0];
        var snap = QuestionSnapshot.FromJson(q.SnapshotJson);
        var before = (await Service(h).GetPlayQuestionsAsync(a, s.Student)).Single(x => x.Id == q.Id);
        Assert.Empty(before.Prompt); Assert.Empty(before.Options); Assert.Null(before.Explanation);
        Assert.Equal(0, before.BlankCount); Assert.Null(q.TimerStartedAt); Assert.Null(q.TimeLeftSeconds);
        var untimed = a.Questions[1];
        Assert.Equal(QuestionSnapshot.FromJson(untimed.SnapshotJson).Prompt,
            (await Service(h).GetPlayQuestionsAsync(a, s.Student)).Single(x => x.Id == untimed.Id).Prompt);
        var opened = await Service(h).OpenQuestionAsync(a.Id, q.Id, s.Student);
        Assert.Equal(snap.Prompt, opened.Question.Prompt);
        Assert.Equal(30, opened.SecondsLeft); Assert.False(opened.Question.BodyHidden);
        Assert.Null(opened.Question.CorrectIds); Assert.Null(opened.Question.IsCorrect); Assert.Null(opened.Question.Explanation);
        var after = (await Service(h).GetPlayQuestionsAsync(a, s.Student)).Single(x => x.Id == q.Id);
        Assert.Equal(snap.Prompt, after.Prompt); Assert.Equal(snap.Options.Select(x => x.Text).Order(), after.Options.Select(x => x.Text).Order());
        Assert.Null(after.CorrectIds); Assert.Null(after.IsCorrect); Assert.Null(after.Explanation);
        var saved = await h.Db.AttemptQuestions.AsNoTracking().SingleAsync(x => x.Id == q.Id);
        Assert.NotNull(saved.TimerStartedAt); Assert.Equal(30, saved.TimeLeftSeconds);
    }

    private static AttemptService Service(Harness h) => Tang4DbTests.Attempts(h);
    private static LearnConnectionService Connections(Harness h) => new(h.Db, Service(h), h.Clock, h.Connections, new TestLearnHub(), h.Classrooms, h.Notifier);

    private static async Task<(Scenario Scenario, Attempt Attempt)> Start(Harness h, bool test = false,
        QuestionType type = QuestionType.SingleChoice, int? limit = 30, bool mastery = true)
    {
        var s = await Tang4DbTests.Mixed(h, choices: 2, input: false, surveys: false,
            threshold: mastery ? 100 : null, limit: 2, retries: true, test: test);
        s.Assignment.ShuffleQuestions = false;
        var q = s.Quiz.Questions.OrderBy(q => q.OrderIndex).First();
        q.TimeLimitSeconds = limit;
        q.Type = type;
        if (SurveyRules.IsSurvey(type))
        {
            q.IsRequired = true; q.Points = 0;
            foreach (var option in q.Options) option.IsCorrect = false;
            q.MaxLength = type == QuestionType.SurveyText ? 500 : null;
        }
        await h.Db.SaveChangesAsync();
        var a = await Service(h).StartAsync(s.Assignment.Id, s.Student);
        return (s, a);
    }

    private static Task<AnswerResult> Answer(Harness h, Attempt a, AttemptQuestion q, string student, Guid? request = null) =>
        Service(h).AnswerAsync(a.Id, q.Id, request ?? Guid.NewGuid(),
            QuestionSnapshot.FromJson(q.SnapshotJson).Options.Where(o => o.IsCorrect).Select(o => o.Id).ToList(), student);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LuuCauKiemGioRiengTrongServiceKeCaQuizNhap(bool input)
    {
        using var h = new Harness();
        var user = await h.UserAsync("Lan");
        var set = await h.Library.CreateStudySetAsync("Giờ riêng", user);
        var quiz = await h.Library.CreateQuizAsync(set.Id, "Quiz", user);
        foreach (var limit in new int?[] { 4, 5, 3600, 3601, null })
        {
            Task<Question> Save() => input
                ? h.Library.SaveInputQuestionAsync(quiz.Id, null, null,
                    new InputQuestionInput(QuestionType.TextInput, "Điền", null, 1, [new[] { "Đúng" }], TimeLimitSeconds: limit), user)
                : h.Library.SaveChoiceQuestionAsync(quiz.Id, null, null, Harness.Single("Chọn") with { TimeLimitSeconds = limit }, user);
            if (limit is 4 or 3601) await Assert.ThrowsAsync<BusinessException>(Save);
            else Assert.Equal(limit, (await Save()).TimeLimitSeconds);
        }
    }

    [Fact]
    public async Task SuaCauGocKhongDoiSnapshotCuaLuotDangLam()
    {
        using var h = new Harness(); var (s, a) = await Start(h);
        var q = a.Questions[0];
        s.Quiz.Questions.Single(x => x.Id == q.QuestionId).TimeLimitSeconds = 3600;
        await h.Db.SaveChangesAsync();
        Assert.Equal(30, (await Service(h).OpenQuestionAsync(a.Id, q.Id, s.Student)).SecondsLeft);
        Assert.Equal(30, QuestionSnapshot.FromJson(q.SnapshotJson).TimeLimitSeconds);
    }

    [Theory]
    [InlineData(30, false)]
    [InlineData(31, false)]
    [InlineData(32, false)]
    [InlineData(33, true)]
    public async Task KiemTraTraLoiTreMangVaKhoaDuocLuu(int seconds, bool rejected)
    {
        using var h = new Harness(); var (s, a) = await Start(h, test: true); var q = a.Questions[0];
        await Service(h).OpenQuestionAsync(a.Id, q.Id, s.Student);
        h.Clock.UtcNow = h.Clock.UtcNow.AddSeconds(seconds);
        if (rejected)
        {
            var error = await Assert.ThrowsAsync<BusinessException>(() => Answer(h, a, q, s.Student));
            Assert.Equal("Câu này đã hết giờ.", error.Message);
            Assert.Empty(q.Submissions);
        }
        else
        {
            var request = Guid.NewGuid();
            var result = await Answer(h, a, q, s.Student, request);
            Assert.Null(result.IsCorrect); Assert.Null(result.CorrectOptionIds);
            await Answer(h, a, q, s.Student, request);
            Assert.Single(q.Submissions); Assert.Equal(1, q.FirstScore);
        }
        Assert.Equal(AttemptQuestionState.LockedTimeout, (await h.Db.AttemptQuestions.AsNoTracking().SingleAsync(x => x.Id == q.Id)).State);
    }

    [Fact]
    public async Task KiemTraRoiCauVaDisconnectVanChay()
    {
        using var h = new Harness(); var (s, a) = await Start(h, test: true); var q = a.Questions[0];
        var connections = Connections(h);
        await connections.JoinAttemptAsync("c", a.Id, s.Student, h.Device.Id);
        await Service(h).OpenQuestionAsync(a.Id, q.Id, s.Student);
        h.Clock.UtcNow = h.Clock.UtcNow.AddSeconds(10);
        await Service(h).OpenQuestionAsync(a.Id, a.Questions[1].Id, s.Student);
        await connections.DisconnectAsync("c");
        Assert.NotNull(q.TimerStartedAt);
        Assert.Equal(20, (await Service(h).OpenQuestionAsync(a.Id, q.Id, s.Student)).SecondsLeft);
        h.Clock.UtcNow = h.Clock.UtcNow.AddSeconds(23);
        await Service(h).GetStateAsync(a.Id, s.Student);
        Assert.Equal(AttemptQuestionState.LockedTimeout, q.State);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NhapTruocKhoaDuocChamKhiNopHoacDenHanNhapSauKhoaBiChan(bool due)
    {
        using var h = new Harness(); var (s, a) = await Start(h, test: true, type: QuestionType.MultipleChoice); var q = a.Questions[0];
        await Service(h).OpenQuestionAsync(a.Id, q.Id, s.Student);
        var correct = QuestionSnapshot.FromJson(q.SnapshotJson).Options.Where(o => o.IsCorrect).Select(o => o.Id).ToArray();
        await Service(h).SaveDraftAsync(a.Id, q.Id, correct, s.Student);
        h.Clock.UtcNow = h.Clock.UtcNow.AddSeconds(33);
        var error = await Assert.ThrowsAsync<BusinessException>(() => Service(h).SaveDraftAsync(a.Id, q.Id, [], s.Student));
        Assert.Equal("Câu này đã hết giờ.", error.Message);
        if (due) { h.Clock.UtcNow = a.DeadlineAt!.Value; await Service(h).GetForLearnerAsync(a.Id, s.Student); }
        else await Service(h).FinishAsync(a.Id, s.Student);
        Assert.Equal(1, q.FirstScore); Assert.Single(q.Submissions);
    }

    [Fact]
    public async Task XacNhanCuoiThangNhapLuuSauDoKhiCauKhoa()
    {
        using var h = new Harness(); var (s, a) = await Start(h, test: true, type: QuestionType.MultipleChoice); var q = a.Questions[0];
        await Service(h).OpenQuestionAsync(a.Id, q.Id, s.Student);
        await Answer(h, a, q, s.Student);
        await Service(h).SaveDraftAsync(a.Id, q.Id, [], s.Student);
        h.Clock.UtcNow = h.Clock.UtcNow.AddSeconds(33);
        await Service(h).FinishAsync(a.Id, s.Student);
        Assert.Equal(1, q.FirstScore); Assert.Single(q.Submissions);
    }

    [Fact]
    public async Task OnTapTraLoiDungDongHoVaLapRequestKhongThemLan()
    {
        using var h = new Harness(); var (s, a) = await Start(h); var q = a.Questions[0];
        await Service(h).OpenQuestionAsync(a.Id, q.Id, s.Student);
        h.Clock.UtcNow = h.Clock.UtcNow.AddSeconds(7);
        var request = Guid.NewGuid(); await Answer(h, a, q, s.Student, request);
        Assert.Null(q.TimerStartedAt); Assert.Equal(23, q.TimeLeftSeconds);
        h.Clock.UtcNow = h.Clock.UtcNow.AddMinutes(5);
        await Answer(h, a, q, s.Student, request);
        await Service(h).OpenQuestionAsync(a.Id, q.Id, s.Student);
        Assert.Null(q.TimerStartedAt); Assert.Single(q.Submissions); Assert.Equal(1, q.AnswerCount);
    }

    [Fact]
    public async Task OnTapChuyenCauTamDungVaTiepTucDungSoGiay()
    {
        using var h = new Harness(); var (s, a) = await Start(h); var q = a.Questions[0];
        await Service(h).OpenQuestionAsync(a.Id, q.Id, s.Student);
        h.Clock.UtcNow = h.Clock.UtcNow.AddSeconds(7);
        await Service(h).OpenQuestionAsync(a.Id, a.Questions[1].Id, s.Student);
        Assert.Null(q.TimerStartedAt); Assert.Equal(23, q.TimeLeftSeconds);
        h.Clock.UtcNow = h.Clock.UtcNow.AddMinutes(5);
        Assert.Equal(23, (await Service(h).OpenQuestionAsync(a.Id, q.Id, s.Student)).SecondsLeft);
        h.Clock.UtcNow = h.Clock.UtcNow.AddSeconds(3);
        Assert.Equal(20, (await Service(h).OpenQuestionAsync(a.Id, q.Id, s.Student)).SecondsLeft);
    }

    [Theory]
    [InlineData("exit")]
    [InlineData("disconnect")]
    [InlineData("startup")]
    public async Task OnTapThoatNgatVaKhoiDongLaiTamDung(string action)
    {
        using var h = new Harness(); var (s, a) = await Start(h); var q = a.Questions[0];
        var connections = Connections(h);
        await connections.JoinAttemptAsync("c", a.Id, s.Student, h.Device.Id);
        await Service(h).OpenQuestionAsync(a.Id, q.Id, s.Student);
        h.Clock.UtcNow = h.Clock.UtcNow.AddSeconds(9);
        if (action == "exit") await Service(h).PauseAsync(a.Id, s.Student);
        else if (action == "disconnect") await connections.DisconnectAsync("c");
        else await LearnConnectionService.ResetDisconnectedAsync(h.Db, h.Clock.UtcNow);
        var saved = await h.Db.AttemptQuestions.AsNoTracking().SingleAsync(x => x.Id == q.Id);
        Assert.Null(saved.TimerStartedAt); Assert.Equal(21, saved.TimeLeftSeconds);
        h.Clock.UtcNow = h.Clock.UtcNow.AddMinutes(5);
        Assert.Equal(21, (await Service(h).OpenQuestionAsync(a.Id, q.Id, s.Student)).SecondsLeft);
    }

    [Fact]
    public async Task OnTapTimeoutVaoCauSaiLamLaiDuGioTinhGioiHanVaDiemSauLuyen()
    {
        using var h = new Harness(); var (s, a) = await Start(h); var q = a.Questions[0];
        await Service(h).OpenQuestionAsync(a.Id, q.Id, s.Student);
        h.Clock.UtcNow = h.Clock.UtcNow.AddSeconds(33);
        await Service(h).GetForLearnerAsync(a.Id, s.Student);
        var first = Assert.Single(q.Submissions);
        Assert.False(first.IsCorrect); Assert.Equal(0, first.Score); Assert.Equal(0, q.FirstScore);
        Assert.True(MasteryRules.CanRetry(a, q)); Assert.Equal(1, q.AnswerCount);
        var play = (await Service(h).GetPlayQuestionsAsync(a, s.Student)).Single(x => x.Id == q.Id);
        Assert.False(play.IsCorrect); Assert.True(play.CanRetry);
        Assert.Equal(30, (await Service(h).OpenQuestionAsync(a.Id, q.Id, s.Student, retry: true)).SecondsLeft);
        await Answer(h, a, q, s.Student);
        Assert.Equal(2, q.AnswerCount); Assert.Equal(0, q.FirstScore); Assert.Equal(1, q.LatestScore);
        Assert.Equal(0, a.FirstScore); Assert.Equal(1, a.PracticeScore); Assert.True(q.IsMastered);
        await Assert.ThrowsAsync<BusinessException>(() => Service(h).OpenQuestionAsync(a.Id, q.Id, s.Student, retry: true));
        Assert.Single(q.Submissions, x => x.IsFinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task KhaoSatBatBuocTimeoutKhongChanNopVaKhongBiaPhanHoi(bool test)
    {
        using var h = new Harness(); var (s, a) = await Start(h, test, QuestionType.SurveySingle, mastery: false); var q = a.Questions[0];
        await Service(h).OpenQuestionAsync(a.Id, q.Id, s.Student);
        h.Clock.UtcNow = h.Clock.UtcNow.AddSeconds(33);
        await Service(h).FinishAsync(a.Id, s.Student);
        Assert.Equal(AttemptQuestionState.LockedTimeout, q.State); Assert.Empty(q.Submissions);
        Assert.True(a.Participant.CompletionLabels.HasFlag(CompletionLabels.MissingRequiredSurvey));
    }

    [Fact]
    public async Task GioRiengKhongVuotHanCaLuot()
    {
        using var h = new Harness(); var (s, a) = await Start(h); var q = a.Questions[0];
        a.DeadlineAt = h.Clock.UtcNow.AddSeconds(5); await h.Db.SaveChangesAsync();
        Assert.Equal(5, (await Service(h).OpenQuestionAsync(a.Id, q.Id, s.Student)).SecondsLeft);
        h.Clock.UtcNow = h.Clock.UtcNow.AddSeconds(5);
        await Assert.ThrowsAsync<BusinessException>(() => Answer(h, a, q, s.Student));
        Assert.Equal(AttemptStatus.Ended, a.Status); Assert.Empty(q.Submissions);
    }

    [Fact]
    public async Task CauKhongGioVanTraLoiDuocKhongCanOpen()
    {
        using var h = new Harness(); var (s, a) = await Start(h, limit: null); var q = a.Questions[0];
        h.Clock.UtcNow = h.Clock.UtcNow.AddMinutes(5);
        await Answer(h, a, q, s.Student);
        Assert.Null(q.TimerStartedAt); Assert.Null(q.TimeLeftSeconds); Assert.Equal(1, q.FirstScore);
    }

    [Fact]
    public async Task OpenTraNoiDungDaLocVaChanSaiThietBiNguoiDungCauHoi()
    {
        using var h = new Harness(); var (s, a) = await Start(h); var q = a.Questions[0];
        h.Device.Id = "máy khác";
        await Assert.ThrowsAsync<BusinessException>(() => Service(h).OpenQuestionAsync(a.Id, q.Id, s.Student));
        h.Device.Id = "test-browser";
        await Assert.ThrowsAsync<ForbiddenException>(() => Service(h).OpenQuestionAsync(a.Id, q.Id, s.Lecturer));
        await Assert.ThrowsAsync<BusinessException>(() => Service(h).OpenQuestionAsync(a.Id, -1, s.Student));
        var result = await Service(h).OpenQuestionAsync(a.Id, q.Id, s.Student);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.NotEmpty(result.Question.Prompt); Assert.NotEmpty(result.Question.Options);
        Assert.False(json.RootElement.GetProperty("question").TryGetProperty("correctIds", out _));
        Assert.False(json.RootElement.GetProperty("question").TryGetProperty("isCorrect", out _));
        Assert.False(json.RootElement.GetProperty("question").TryGetProperty("explanation", out _));
        Assert.Null((await h.Db.AttemptQuestions.AsNoTracking().SingleAsync(x => x.Id == a.Questions[1].Id)).TimerStartedAt);
    }

    [Fact]
    public async Task TraLoiVaExpiryDongThoiChiGhiMotLanDuoiKhoaHang()
    {
        using var h = new Harness(); var (s, a) = await Start(h); var q = a.Questions[0];
        await Service(h).OpenQuestionAsync(a.Id, q.Id, s.Student);
        h.Clock.UtcNow = h.Clock.UtcNow.AddSeconds(33);
        using var other = DatabaseFixture.NewContext();
        var second = new AttemptService(other, h.Clock, [new ChoiceGrader()], h.Device, h.Notifier, h.Connections);
        var answer = Assert.ThrowsAsync<BusinessException>(() => Answer(h, a, q, s.Student));
        var expiry = second.GetForLearnerAsync(a.Id, s.Student);
        await Task.WhenAll(answer, expiry);
        var submissions = await other.AnswerSubmissions.AsNoTracking().Where(x => x.AttemptQuestionId == q.Id).ToListAsync();
        Assert.Single(submissions); Assert.False(submissions[0].IsCorrect); Assert.Equal(0, submissions[0].Score);
        Assert.Equal(1, (await other.AttemptQuestions.AsNoTracking().SingleAsync(x => x.Id == q.Id)).AnswerCount);
    }
}
