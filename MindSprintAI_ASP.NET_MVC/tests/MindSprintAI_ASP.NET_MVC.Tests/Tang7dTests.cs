using System.Text.Json;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using MindSprintAI_ASP.NET_MVC.Controllers;
using MindSprintAI_ASP.NET_MVC.Data;
using MindSprintAI_ASP.NET_MVC.Data.Entities;
using MindSprintAI_ASP.NET_MVC.Models;
using MindSprintAI_ASP.NET_MVC.Services;

namespace MindSprintAI_ASP.NET_MVC.Tests;

public class Tang7dPureTests
{
    [Theory]
    [InlineData(null, "Chờ giảng viên chấm")]
    [InlineData(0, "Sai")]
    public async Task CauDienChuaChamKhongHienSai(int? score, string label)
    {
        var at = Attempt(score); at.Questions[0].SnapshotJson = (QuestionSnapshot.FromJson(at.Questions[0].SnapshotJson) with { Type = QuestionType.NumberInput }).ToJson();
        var html = WebUtility.HtmlDecode(await Tang4ViewTests.Render("Assignments", "Attempt", at, new() { ["Items"] = at.Questions.Select(q => (q, QuestionSnapshot.FromJson(q.SnapshotJson))).ToList(),
            ["InputReviews"] = new Dictionary<long, InputReview> { [1] = new(["Hà Nội"], ["Hà Nội"], [false]) } }));
        Assert.Contains($">{label}</span>", html);
        Assert.Equal(score == null, !html.Contains(">Sai</span>"));
        // Ô trả lời chưa chấm không tô đỏ như câu sai.
        var cell = System.Text.RegularExpressions.Regex.Match(html, "<p class=\"([^\"]*)\">\\s*Ô 1");
        Assert.True(cell.Success);
        Assert.Equal(score != null, cell.Groups[1].Value.Contains("text-danger"));
    }

    internal static Attempt Attempt(decimal? finalScore = 1m, bool blank = false)
    {
        var at = Tang4RuleTests.Build(1); at.Participant.Assignment.MasteryThreshold = null;
        at.Status = AttemptStatus.Ended; at.MaxScore = 1; at.FirstScore = 0.75m;
        at.Participant.Assignment.CriticalQuestionsEnabled = true;
        at.Participant.Assignment.CriticalQuestionIdsJson = "[1]";
        var q = at.Questions[0]; q.FirstScore = 0.75m;
        if (!blank) { q.AnswerCount = 1; q.Submissions.Add(new() { IsFinal = true, IsFirstAnswer = true, Score = finalScore, IsCorrect = finalScore == 1, SubmittedAt = DateTime.UtcNow }); }
        return at;
    }

    [Theory]
    [InlineData(1, 2, 2)]
    [InlineData(2, 3, 4)]
    [InlineData(3, 4, 8)]
    [InlineData(4, 5, 16)]
    [InlineData(5, 5, 16)]
    public void NhoLenHopVaDungKhoangNgay(int box, int next, int days)
    {
        var today = new DateOnly(2026, 11, 8);
        var state = Leitner.Review(box, today, FlashcardRating.Remembered, today);
        Assert.True(state.WasDue); Assert.Equal(next, state.Box); Assert.Equal(today.AddDays(days), state.DueDate);
        var forgotten = Leitner.Review(box, today, FlashcardRating.NotRemembered, today);
        Assert.Equal(1, forgotten.Box); Assert.Equal(today.AddDays(1), forgotten.DueDate);
    }

    [Theory]
    [InlineData(FlashcardRating.Remembered)]
    [InlineData(FlashcardRating.NotRemembered)]
    public void ChuaDenHanKhongDoiHopHan(FlashcardRating rating)
    {
        var today = new DateOnly(2026, 11, 8);
        var state = Leitner.Review(3, today.AddDays(1), rating, today);
        Assert.False(state.WasDue); Assert.Equal(3, state.Box); Assert.Equal(today.AddDays(1), state.DueDate);
        Assert.True(Leitner.IsDue(null, null, today));
        Assert.Equal(2, Leitner.Review(null, null, FlashcardRating.Remembered, today).Box);
    }

    [Fact]
    public void MocNgayVietNam()
    {
        var before = new DateTime(2026, 11, 8, 16, 59, 59, DateTimeKind.Utc);
        var midnight = before.AddSeconds(1);
        Assert.Equal(new DateOnly(2026, 11, 8), VnTime.Date(before));
        Assert.Equal(new DateOnly(2026, 11, 9), VnTime.Date(midnight));
        Assert.False(Leitner.IsDue(2, VnTime.Date(midnight), VnTime.Date(before)));
        Assert.True(Leitner.IsDue(2, VnTime.Date(midnight), VnTime.Date(midnight)));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(0.5, true)]
    [InlineData(1, false)]
    public void CauDiemLietPhaiDuDiemVaKhongDoiConSo(double score, bool failed)
    {
        var at = Attempt((decimal)score); var before = at.FirstScore;
        CriticalScoreRules.Recompute(at);
        Assert.Equal(failed ? CriticalLabel.Failed : CriticalLabel.None, at.CriticalLabel);
        Assert.Equal(failed, at.FailedCriticalQuestion); Assert.Equal(before, at.FirstScore); Assert.Equal(1, at.MaxScore);
    }

    [Fact]
    public void BoTrongLaLiet()
    {
        var at = Attempt(blank: true); at.Questions[0].LatestScore = 1;
        CriticalScoreRules.Recompute(at); Assert.Equal(CriticalLabel.Failed, at.CriticalLabel);
    }

    [Theory]
    [InlineData(5, true)]
    [InlineData(5.01, false)]
    [InlineData(4, true)]
    public void NguongSoSanhDiemHienThiVaBangCungLiet(double displayed, bool failed)
    {
        var at = Attempt(); at.Participant.Assignment.CriticalThreshold = 5;
        at.FirstScore = (decimal)displayed / 10;
        CriticalScoreRules.Recompute(at); Assert.Equal(failed ? CriticalLabel.Failed : CriticalLabel.None, at.CriticalLabel);
    }

    [Fact]
    public void HaiCongTacDocLapVaCauCuoiSauLuyen()
    {
        var at = Attempt(0); var a = at.Participant.Assignment;
        a.CriticalThreshold = 5; a.MasteryThreshold = 80; a.ScoreRetries = true;
        at.PracticeScore = 1; at.Questions[0].Submissions[0].IsFinal = false;
        at.Questions[0].Submissions.Add(new() { IsFinal = true, Score = 1, IsCorrect = true, SubmittedAt = DateTime.UtcNow.AddMinutes(1) });
        CriticalScoreRules.Recompute(at); Assert.Equal(CriticalLabel.None, at.CriticalLabel);
        at.PracticeScore = 0.5m; CriticalScoreRules.Recompute(at); Assert.Equal(CriticalLabel.Failed, at.CriticalLabel);
        a.CriticalThreshold = null; at.Questions[0].Submissions[^1].Score = 0.5m;
        CriticalScoreRules.Recompute(at); Assert.True(at.FailedCriticalQuestion);
    }

    [Fact]
    public void ChoChamVaChamXongXetLai()
    {
        var at = Attempt(null);
        CriticalScoreRules.Recompute(at); Assert.Equal(CriticalLabel.Pending, at.CriticalLabel);
        at.Participant.Assignment.PassMark = 5;
        Assert.Null(GradebookService.Passed(CriticalSelectionRules.FromAttempt(at), at.Participant.Assignment));
        at.Questions[0].Submissions[0].Score = 0;
        CriticalScoreRules.Recompute(at); Assert.Equal(CriticalLabel.Failed, at.CriticalLabel);
        at.Questions[0].Submissions[0].Score = 1;
        CriticalScoreRules.Recompute(at); Assert.Equal(CriticalLabel.None, at.CriticalLabel);
    }

    [Theory]
    [InlineData(ScoreSelection.Highest, CriticalLabel.None, 1)]
    [InlineData(ScoreSelection.First, CriticalLabel.Failed, 0.5)]
    [InlineData(ScoreSelection.Latest, CriticalLabel.Failed, 0.25)]
    [InlineData(ScoreSelection.Average, CriticalLabel.None, 0.5833333333333333)]
    public void E6NhanTheoLuotDuocChon(ScoreSelection mode, CriticalLabel expected, double score)
    {
        var first = Attempt(0); first.AttemptNumber = 1; first.FirstScore = 0.5m; CriticalScoreRules.Recompute(first);
        var best = Attempt(1); best.AttemptNumber = 2; best.FirstScore = 1; CriticalScoreRules.Recompute(best);
        var last = Attempt(0); last.AttemptNumber = 3; last.FirstScore = 0.25m; CriticalScoreRules.Recompute(last);
        var a = first.Participant.Assignment; a.ScoreSelection = mode; a.PassMark = 2;
        var selected = GradebookService.SelectForAssignment([first, best, last], a)!;
        Assert.Equal(expected, selected.CriticalLabel); Assert.Equal(score, (double)selected.Raw, 10);
        Assert.Equal(expected != CriticalLabel.Failed, GradebookService.Passed(selected, a));
        last.ExcludeFromGradebook = first.ExcludeFromGradebook = true;
        Assert.Equal(CriticalLabel.None, GradebookService.SelectForAssignment([first, best, last], a)!.CriticalLabel);
    }

    [Fact]
    public void E6TrungBinhChiXetNguongTrungBinhKhongLayNhanNguongCuaLuot()
    {
        var low = Attempt(); low.FirstScore = 0.2m; low.AttemptNumber = 1;
        var high = Attempt(); high.FirstScore = 1; high.AttemptNumber = 2;
        var a = low.Participant.Assignment; a.CriticalThreshold = 5; a.ScoreSelection = ScoreSelection.Average;
        CriticalScoreRules.Recompute(low); Assert.Equal(CriticalLabel.Failed, low.CriticalLabel);
        Assert.Equal(CriticalLabel.None, GradebookService.SelectForAssignment([low, high], a)!.CriticalLabel);
        a.CriticalThreshold = 6;
        Assert.Equal(CriticalLabel.Failed, GradebookService.SelectForAssignment([low, high], a)!.CriticalLabel);
        high.Status = AttemptStatus.Cancelled;
        Assert.Equal(0.2m, GradebookService.SelectForAssignment([low, high], a)!.Raw);
    }

    [Fact]
    public void MigrationMacDinhVaModelKhopChiThem()
    {
        using var db = new AppDbContext(AppDbContextFactory.BuildOptions(DatabaseFixture.ConnectionString));
        var snapshot = db.GetService<IMigrationsAssembly>().ModelSnapshot!.Model;
        var initialized = db.GetService<Microsoft.EntityFrameworkCore.Infrastructure.IModelRuntimeInitializer>().Initialize(snapshot, designTime: true);
        Assert.False(db.GetService<IMigrationsModelDiffer>().HasDifferences(initialized.GetRelationalModel(), db.GetService<Microsoft.EntityFrameworkCore.Metadata.IDesignTimeModel>().Model.GetRelationalModel()));
        Assert.Equal(FlashcardMode.Traditional, new Assignment().FlashcardMode);
        Assert.Equal(FlashcardMode.Traditional, new StudySet().FlashcardMode);
        Assert.Equal(FlashcardRating.NotRemembered, FlashcardService.TraditionalRating(new() { LastRating = FlashcardRating.NotRemembered }));
    }

    [Fact]
    public void DueCardsDichSangSql()
    {
        using var db = new AppDbContext(AppDbContextFactory.BuildOptions(DatabaseFixture.ConnectionString));
        var query = new SrsDueService(db, TimeProvider.System).PersonalCards();
        Assert.Contains("SrsDueDate", query.ToQueryString());
        Assert.Contains("SELECT", db.Users.Where(u => query.Any(c => c.UserId == u.Id)).ToQueryString());
    }

    [Fact]
    public void PresetGiuCheDoNguongKhongGiuQuizVaCau()
    {
        var a = new Assignment { FlashcardMode = FlashcardMode.LearnerChoice, CriticalThreshold = 4.5m,
            CriticalQuestionsEnabled = true, QuizId = 7, CriticalQuestionIdsJson = "[12,13]" };
        var preset = SettingPresetRules.Assignment(JsonSerializer.Serialize(AssignmentPreset.From(a)), AssignmentMode.Practice);
        var input = preset.ToInput(1, AssignmentMode.Practice);
        Assert.Equal(FlashcardMode.LearnerChoice, input.FlashcardMode); Assert.True(input.ThresholdEnabled);
        Assert.Equal(4.5m, input.CriticalThreshold); Assert.True(input.CriticalQuestionsEnabled);
        Assert.Equal(0, input.QuizId); Assert.Empty(input.CriticalQuestionIds);
    }

    private static void FormMatches(string html, Type controller, string action)
    {
        var method = controller.GetMethods().Single(m => m.Name == action && m.IsDefined(typeof(HttpPostAttribute), true));
        var fields = method.GetParameters().Select(p => p.Name!).ToList();
        var form = Regex.Matches(html, "<form\\b[^>]*>[\\s\\S]*?</form>", RegexOptions.IgnoreCase).Select(m => m.Value)
            .FirstOrDefault(f => fields.All(name => f.Contains($"name=\"{name}\"", StringComparison.OrdinalIgnoreCase)));
        Assert.NotNull(form); Assert.Contains("__RequestVerificationToken", form);
    }

    [Fact]
    public async Task FormMoiKhopThamSoVaKhongLoNhanKhiChuaCongBo()
    {
        var study = await Tang4ViewTests.Render("Flashcards", "Study", new CardSession(1, 2, "Bộ <script>",
            [new(1, "Trước", "Sau", false, false, null)], 0, 0, 1, null, true, false, Mode: FlashcardMode.SpacedRepetition, CanChooseMode: true, DueCount: 1));
        FormMatches(study, typeof(FlashcardsController), "ChangeMode");
        Assert.Contains("tabindex=\"0\"", study); Assert.Contains("Ôn thẻ đến hạn hôm nay", WebUtility.HtmlDecode(study)); Assert.DoesNotContain("Bộ <script>", study);
        var a = new Assignment { Id = 2, QuizId = 1, Mode = AssignmentMode.Practice, Form = AssignmentForm.Combined };
        var details = await Tang4ViewTests.Render("Assignments", "Details", new AssignmentDetailsModel { Assignment = a,
            Code = "12345678", Rows = [], OpenAttempts = 0, RegradeQuestions = [new(1, 1, "Đề <script>", true, [])] });
        FormMatches(details, typeof(AssignmentsController), "ChangeFlashcardMode"); FormMatches(details, typeof(AssignmentsController), "ChangeCriticalSettings");
        var at = Attempt(null); at.Questions[0].SnapshotJson = (QuestionSnapshot.FromJson(at.Questions[0].SnapshotJson) with { Type = QuestionType.TextInput }).ToJson();
        var attempt = await Tang4ViewTests.Render("Assignments", "Attempt", at, new() { ["Items"] = at.Questions.Select(q => (q, QuestionSnapshot.FromJson(q.SnapshotJson))).ToList(),
            ["InputReviews"] = new Dictionary<long, InputReview> { [1] = new([""], [], null) } });
        FormMatches(attempt, typeof(AssignmentsController), "ManualGrade");
        at.Participant.Assignment.Mode = AssignmentMode.Test; at.CriticalLabel = CriticalLabel.Failed; at.CriticalReasons = "LY_DO_BI_MAT";
        var hidden = await Tang4ViewTests.Render("Learn", "Review", at, new() { ["Questions"] = new List<PlayQuestion>(), ["ShowScores"] = false });
        Assert.DoesNotContain("LY_DO_BI_MAT", hidden); Assert.DoesNotContain("Liệt", WebUtility.HtmlDecode(hidden));
        var create = await Tang4ViewTests.Render("Assignments", "Create", new AssignmentCreateModel { Classroom = new() { Id = 1 },
            Form = new() { ClassroomId = 1 }, StudySets = [new() { Id = 1 }], ReadyQuizzes = [new() { Id = 1 }],
            CriticalQuestions = new() { [1] = [new(1, 1, "Đề")] } });
        var createForm = Regex.Matches(create, "<form\\b[^>]*>[\\s\\S]*?</form>", RegexOptions.IgnoreCase).Select(m => m.Value).First(f => f.Contains("id=\"assignment-form\""));
        var names = Regex.Matches(createForm, "name=\"([^\"]+)\"").Select(m => m.Groups[1].Value).Where(n => n != "__RequestVerificationToken").Distinct().ToList();
        Assert.All(names, name => Assert.Contains(typeof(AssignmentInput).GetProperties(), p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)));
        Assert.Contains("FlashcardMode", names); Assert.Contains("ThresholdEnabled", names); Assert.Contains("CriticalQuestionIds", names);
    }
}

[Collection("db")]
public class Tang7dTests
{
    private static FlashcardService Cards(Harness h) => new(h.Db, h.Library, h.Clock, h.Notifier);

    private static async Task<Scenario> WithCards(Harness h, FlashcardMode mode = FlashcardMode.LearnerChoice)
    {
        var s = await h.ScenarioAsync();
        await Cards(h).SaveAsync(s.Quiz.StudySetId, null, "Trước 1", "Sau 1", s.Lecturer);
        await Cards(h).SaveAsync(s.Quiz.StudySetId, null, "Trước 2", "Sau 2", s.Lecturer);
        h.Clock.UtcNow = h.Clock.UtcNow.AddMinutes(1);
        var a = await h.Assignments.CreatePracticeAsync(s.Room.Id, s.Quiz.Id, new(null, null, null,
            Form: AssignmentForm.Combined, FlashcardMode: mode), s.Lecturer);
        return s with { Assignment = a };
    }

    [Theory]
    [InlineData(FlashcardMode.Traditional, FlashcardMode.SpacedRepetition)]
    [InlineData(FlashcardMode.SpacedRepetition, FlashcardMode.Traditional)]
    public async Task GvCoDinhCheDoThiHocVienKhongVuotDuoc(FlashcardMode chosen, FlashcardMode forbidden)
    {
        using var h = new Harness(); var s = await WithCards(h, chosen); var f = Cards(h);
        var session = await f.GetSessionAsync(s.Quiz.StudySetId, s.Assignment.Id, s.Student);
        Assert.Equal(chosen, session.Mode); Assert.False(session.CanChooseMode);
        await Assert.ThrowsAsync<BusinessException>(() => f.RateAsync(session.StudySetId, s.Assignment.Id, session.Cards[0].Id, FlashcardRating.Remembered, s.Student, forbidden));
        await Assert.ThrowsAsync<BusinessException>(() => f.ChangeModeAsync(session.StudySetId, s.Assignment.Id, forbidden, s.Student));
        Assert.False(await h.Db.FlashcardReviews.AnyAsync(r => r.StudySetId == session.StudySetId));
    }

    [Fact]
    public async Task LuuCheDoTheoNguoiThamGiaVaBoCaNhan()
    {
        using var h = new Harness(); var s = await WithCards(h); var f = Cards(h);
        await f.ChangeModeAsync(s.Quiz.StudySetId, s.Assignment.Id, FlashcardMode.SpacedRepetition, s.Student);
        h.Db.ChangeTracker.Clear();
        Assert.Equal(FlashcardMode.SpacedRepetition, (await f.GetSessionAsync(s.Quiz.StudySetId, s.Assignment.Id, s.Student)).Mode);
        var other = await h.UserAsync("khac"); await h.Classrooms.JoinByCodeAsync(s.ClassCode, other);
        Assert.Equal(FlashcardMode.Traditional, (await f.GetSessionAsync(s.Quiz.StudySetId, s.Assignment.Id, other)).Mode);
        Assert.Equal(FlashcardMode.Traditional, (await f.GetSessionAsync(s.Quiz.StudySetId, null, s.Lecturer)).Mode);
        await f.ChangeModeAsync(s.Quiz.StudySetId, null, FlashcardMode.SpacedRepetition, s.Lecturer); h.Db.ChangeTracker.Clear();
        Assert.Equal(FlashcardMode.SpacedRepetition, (await f.GetSessionAsync(s.Quiz.StudySetId, null, s.Lecturer)).Mode);
        Assert.Equal(FlashcardMode.LearnerChoice, (await new UserSettingPresetService(h.Db, h.Clock).AssignmentAsync(AssignmentMode.Practice, s.Lecturer)).FlashcardMode);
    }

    [Fact]
    public async Task HaiCheDoGiuRiengNhomHopHoanThanhVaLichSu()
    {
        using var h = new Harness(); var s = await WithCards(h); var f = Cards(h); var set = s.Quiz.StudySetId;
        await f.ChangeModeAsync(set, s.Assignment.Id, FlashcardMode.SpacedRepetition, s.Student);
        var session = await f.GetSessionAsync(set, s.Assignment.Id, s.Student); var id = session.Cards[0].Id;
        await f.RateAsync(set, s.Assignment.Id, id, FlashcardRating.Remembered, s.Student);
        var state = await h.Db.FlashcardProgresses.SingleAsync(p => p.FlashcardId == id);
        Assert.Null(FlashcardService.TraditionalRating(state)); Assert.Equal(2, state.SrsBox);
        var due = state.SrsDueDate;
        var reviewedAt = state.LastReviewedAt; var count = state.ReviewCount; h.Clock.UtcNow = h.Clock.UtcNow.AddMinutes(1);
        await f.RateAsync(set, s.Assignment.Id, id, FlashcardRating.NotRemembered, s.Student);
        Assert.Equal(2, state.SrsBox); Assert.Equal(due, state.SrsDueDate);
        Assert.Equal(reviewedAt, state.LastReviewedAt); Assert.Equal(count, state.ReviewCount);
        await f.ChangeModeAsync(set, s.Assignment.Id, FlashcardMode.Traditional, s.Student);
        await f.RateAsync(set, s.Assignment.Id, id, FlashcardRating.NotRemembered, s.Student);
        Assert.Equal(2, state.SrsBox); Assert.Equal(due, state.SrsDueDate); Assert.Equal(FlashcardRating.NotRemembered, state.LastRating);
        Assert.Single((await f.GetSessionAsync(set, s.Assignment.Id, s.Student, forgotten: true)).Cards);
        Assert.Single(await h.Gradebook.GetForgottenCardsAsync(s.Assignment.Id, s.Lecturer));
        await f.ChangeModeAsync(set, s.Assignment.Id, FlashcardMode.SpacedRepetition, s.Student);
        await f.RateAsync(set, s.Assignment.Id, id, FlashcardRating.Remembered, s.Student);
        Assert.Equal(FlashcardRating.NotRemembered, state.LastRating);
        await f.RateAsync(set, s.Assignment.Id, session.Cards[1].Id, FlashcardRating.Remembered, s.Student);
        Assert.Equal(2, (await f.GetSessionAsync(set, s.Assignment.Id, s.Student)).Studied);
        var history = await f.HistoryAsync(set, s.Assignment.Id, s.Student);
        Assert.Equal(5, history.Reviews.Count);
        var first = history.Reviews.OrderBy(r => r.Id).First(); Assert.Null(first.BeforeRating); Assert.Null(first.AfterRating);
        Assert.Equal(1, first.BeforeBox); Assert.Equal(2, first.AfterBox); Assert.True(first.WasDue);
        var early = history.Reviews.OrderBy(r => r.Id).Skip(1).First(); Assert.False(early.WasDue); Assert.Equal(early.BeforeDueDate, early.AfterDueDate);
        var p = await h.Db.Participants.SingleAsync(p => p.AssignmentId == s.Assignment.Id && p.UserId == s.Student);
        Assert.Equal(5, (await f.HistoryAsync(set, s.Assignment.Id, s.Lecturer, p.Id)).Reviews.Count);
        var another = await h.UserAsync("gvkhac"); await h.Classrooms.CreateAsync("Lớp khác", ClassJoinPolicy.Immediate, another);
        await Assert.ThrowsAsync<ForbiddenException>(() => f.HistoryAsync(set, s.Assignment.Id, another, p.Id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BuoiDenHanChiLayTheDenHanVaChuaOnSrs(bool personal)
    {
        using var h = new Harness(); var s = await WithCards(h); var f = Cards(h); var set = s.Quiz.StudySetId;
        var user = personal ? s.Lecturer : s.Student; int? assignment = personal ? null : s.Assignment.Id;
        await f.ChangeModeAsync(set, assignment, FlashcardMode.SpacedRepetition, user);
        var all = await f.GetSessionAsync(set, assignment, user);
        await f.RateAsync(set, assignment, all.Cards[0].Id, FlashcardRating.Remembered, user);
        var due = await f.GetSessionAsync(set, assignment, user, dueOnly: true);
        Assert.Equal(all.Cards[1].Id, Assert.Single(due.Cards).Id); Assert.Equal(1, due.DueCount);
        Assert.Equal(VnTime.Date(h.Clock.UtcNow).AddDays(2), due.NextDueDate);
        await f.RateAsync(set, assignment, all.Cards[1].Id, FlashcardRating.NotRemembered, user);
        due = await f.GetSessionAsync(set, assignment, user, dueOnly: true);
        Assert.Empty(due.Cards); Assert.Equal(VnTime.Date(h.Clock.UtcNow).AddDays(1), due.NextDueDate);
        h.Clock.UtcNow = h.Clock.UtcNow.AddDays(1);
        Assert.Single((await f.GetSessionAsync(set, assignment, user, dueOnly: true)).Cards);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ChiTheSrsKhongGuiDigestDuBatHayTatEmail(bool enabled)
    {
        using var h = new Harness(); var id = await h.UserAsync("SRS"); var u = (await h.Db.Users.FindAsync(id))!;
        u.EmailConfirmed = true; u.EmailRemindersEnabled = enabled; await h.Db.SaveChangesAsync();
        var set = await h.Library.CreateStudySetAsync("Bộ <script>", id);
        await Cards(h).SaveAsync(set.Id, null, "A", "B", id);
        await Cards(h).ChangeModeAsync(set.Id, null, FlashcardMode.SpacedRepetition, id);
        var f = new Tang7cServices(h); await f.Reminders.RunAsync(); await f.Reminders.RunAsync();
        Assert.DoesNotContain(f.Email.Sent, e => e.To == u.Email);
        Assert.False(await h.Db.ReminderDigests.AnyAsync(n => n.UserId == id));
        h.Clock.UtcNow = h.Clock.UtcNow.AddDays(1); await f.Reminders.RunAsync();
        Assert.DoesNotContain(f.Email.Sent, e => e.To == u.Email);
    }

    [Fact]
    public async Task DoiSettingsTinhLaiVaRegradeTinhLaiCoLog()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(1);
        var at = await h.Attempts.StartAsync(s.Assignment.Id, s.Student); var q = Assert.Single(at.Questions);
        var snapshot = QuestionSnapshot.FromJson(q.SnapshotJson);
        await h.Attempts.AnswerAsync(at.Id, q.Id, Guid.NewGuid(), [snapshot.Options.First(o => !o.IsCorrect).Id], s.Student);
        await h.Attempts.FinishAsync(at.Id, s.Student);
        await h.Assignments.ChangeCriticalSettingsAsync(s.Assignment.Id, new(CriticalQuestionsEnabled: true, CriticalQuestionIds: [q.QuestionId]), s.Lecturer);
        Assert.Equal(CriticalLabel.Failed, at.CriticalLabel); var score = at.FirstScore;
        Assert.Equal(score, GradebookService.SelectForAssignment([at], s.Assignment)!.Raw);
        Assert.True(await h.Db.AssignmentChanges.AnyAsync(c => c.AssignmentId == s.Assignment.Id && c.Field == "CriticalSettings"));
        await h.Regrades.ApplyAsync(s.Assignment.Id, new(q.QuestionId, RegradeMethod.FullCredit, "Tính đúng", Guid.NewGuid(), []), s.Lecturer);
        Assert.Equal(CriticalLabel.None, at.CriticalLabel);
        await h.Assignments.ChangeCriticalSettingsAsync(s.Assignment.Id, new(true, 10), s.Lecturer);
        Assert.Equal(CriticalLabel.Failed, at.CriticalLabel);
        Assert.False(at.FailedCriticalQuestion); Assert.Equal(1, at.FirstScore);
        var preset = await new UserSettingPresetService(h.Db, h.Clock).AssignmentAsync(AssignmentMode.Practice, s.Lecturer);
        Assert.Equal(10, preset.CriticalThreshold); Assert.True(preset.ThresholdEnabled);
    }

    [Fact]
    public async Task TuChoiCauKhaoSatCauQuizKhacVaNguongVuotThang()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(); var other = await h.ScenarioAsync(1);
        var invalid = await h.Db.Questions.FirstAsync(q => q.QuizId == other.Quiz.Id);
        await Assert.ThrowsAsync<BusinessException>(() => h.Assignments.ChangeCriticalSettingsAsync(s.Assignment.Id, new(CriticalQuestionsEnabled: true, CriticalQuestionIds: [invalid.Id]), s.Lecturer));
        var q = await h.Db.Questions.FirstAsync(q => q.QuizId == s.Quiz.Id); q.Type = QuestionType.SurveySingle; await h.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<BusinessException>(() => h.Assignments.ChangeCriticalSettingsAsync(s.Assignment.Id, new(CriticalQuestionsEnabled: true, CriticalQuestionIds: [q.Id]), s.Lecturer));
        await Assert.ThrowsAsync<BusinessException>(() => h.Assignments.ChangeCriticalSettingsAsync(s.Assignment.Id, new(true, 10.01m), s.Lecturer));
        await Assert.ThrowsAsync<ForbiddenException>(() => h.Assignments.ChangeCriticalSettingsAsync(s.Assignment.Id, new(true, 5), s.Student));
    }

    [Theory]
    [InlineData(0, CriticalLabel.Failed)]
    [InlineData(1, CriticalLabel.None)]
    public async Task ChamTayTuChoChamThanhNhanCuoi(double score, CriticalLabel expected)
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(1);
        var question = await h.Db.Questions.SingleAsync(q => q.QuizId == s.Quiz.Id);
        question.Type = QuestionType.TextInput; question.Blanks = [new() { BlankIndex = 0, Answers = [new() { Text = "A" }] }]; await h.Db.SaveChangesAsync();
        await h.Assignments.ChangeCriticalSettingsAsync(s.Assignment.Id, new(CriticalQuestionsEnabled: true, CriticalQuestionIds: [question.Id]), s.Lecturer);
        var at = await h.Attempts.StartAsync(s.Assignment.Id, s.Student); var q = Assert.Single(at.Questions);
        q.Submissions.Add(new() { IsFinal = true, IsFirstAnswer = true, Score = null, ClientRequestId = Guid.NewGuid(), PayloadJson = "{\"Blanks\":[\"A\"]}" });
        q.AnswerCount = 1; await h.Db.SaveChangesAsync();
        await h.Attempts.FinishAsync(at.Id, s.Student); Assert.Equal(CriticalLabel.Pending, at.CriticalLabel);
        // Điểm ngoài phạm vi: câu lỗi nêu đúng con số, câu vẫn chờ chấm.
        var range = await Assert.ThrowsAsync<BusinessException>(() => h.Assignments.ManualGradeAsync(at.Id, q.Id, 5m, s.Lecturer));
        Assert.Equal("Điểm phải từ 0 đến 1.", range.Message);
        await h.Assignments.ManualGradeAsync(at.Id, q.Id, (decimal)score, s.Lecturer); Assert.Equal(expected, at.CriticalLabel);
        // Chấm lần nữa: nói câu đã chấm rồi, không gộp với lỗi phạm vi.
        Assert.Contains("không còn chờ chấm", (await Assert.ThrowsAsync<BusinessException>(() => h.Assignments.ManualGradeAsync(at.Id, q.Id, 0m, s.Lecturer))).Message);
    }

    [Fact]
    public async Task HoanThanhTheOHoacCheDoVaKhachChiXemLichSuPhienMinh()
    {
        using var h = new Harness(); var s = await WithCards(h); var f = Cards(h); var set = s.Quiz.StudySetId;
        var a = await h.Assignments.CreatePracticeAsync(s.Room.Id, s.Quiz.Id, new(null, null, null,
            Form: AssignmentForm.FlashcardOnly, StudySetId: set, FlashcardMode: FlashcardMode.LearnerChoice,
            StrangerPolicy: StrangerPolicy.Allow), s.Lecturer);
        var guestService = new GuestService(h.Db, h.Clock, h.Notifier);
        var guest = await guestService.JoinAsync(a.Id, "Khách SRS", GuestService.NewToken());
        var other = await guestService.JoinAsync(a.Id, "Khách khác", GuestService.NewToken());
        var session = await f.GetSessionAsync(set, a.Id, guest.UserId);
        await f.RateAsync(set, a.Id, session.Cards[0].Id, FlashcardRating.NotRemembered, guest.UserId);
        await f.ChangeModeAsync(set, a.Id, FlashcardMode.SpacedRepetition, guest.UserId);
        await f.RateAsync(set, a.Id, session.Cards[1].Id, FlashcardRating.Remembered, guest.UserId);
        var done = await f.GetSessionAsync(set, a.Id, guest.UserId);
        Assert.Equal(2, done.Studied); Assert.Equal(CompletionStatus.Completed, done.Status);
        Assert.Equal(2, (await f.HistoryAsync(set, a.Id, guest.UserId)).Reviews.Count);
        Assert.Empty((await f.HistoryAsync(set, a.Id, other.UserId)).Reviews);
        var participant = await h.Db.Participants.SingleAsync(p => p.AssignmentId == a.Id && p.UserId == guest.UserId);
        await Assert.ThrowsAsync<ForbiddenException>(() => f.HistoryAsync(set, a.Id, other.UserId, participant.Id));
        await Assert.ThrowsAsync<ForbiddenException>(() => f.GetSessionAsync(set, null, guest.UserId));
    }

    [Fact]
    public async Task LuyenTraLoiLaiDungCauCuoiVaKhongLoNhanKiemTraTruocCongBo()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(1, new(null, null, null, MasteryThreshold: 100, ScoreRetries: true));
        var question = await h.Db.Questions.SingleAsync(q => q.QuizId == s.Quiz.Id);
        await h.Assignments.ChangeCriticalSettingsAsync(s.Assignment.Id, new(CriticalQuestionsEnabled: true, CriticalQuestionIds: [question.Id]), s.Lecturer);
        var at = await h.Attempts.StartAsync(s.Assignment.Id, s.Student); var q = Assert.Single(at.Questions);
        var snapshot = QuestionSnapshot.FromJson(q.SnapshotJson);
        await h.Attempts.AnswerAsync(at.Id, q.Id, Guid.NewGuid(), [snapshot.Options.First(o => !o.IsCorrect).Id], s.Student);
        await h.Attempts.OpenQuestionAsync(at.Id, q.Id, s.Student, retry: true);
        await h.Attempts.AnswerAsync(at.Id, q.Id, Guid.NewGuid(), [snapshot.Options.First(o => o.IsCorrect).Id], s.Student);
        await h.Attempts.FinishAsync(at.Id, s.Student);
        Assert.Equal(0, at.FirstScore); Assert.Equal(1, at.PracticeScore); Assert.Equal(CriticalLabel.None, at.CriticalLabel);
        var test = await h.Assignments.CreateTestAsync(s.Room.Id, s.Quiz.Id, new(null, 1,
            CriticalSettings: new(CriticalQuestionsEnabled: true, CriticalQuestionIds: [question.Id])), s.Lecturer);
        await h.Assignments.StartTestAsync(test.Id, s.Lecturer);
        var testAt = await h.Attempts.StartAsync(test.Id, s.Student);
        await h.Attempts.FinishAsync(testAt.Id, s.Student);
        Assert.Equal(CriticalLabel.Failed, testAt.CriticalLabel);
        var hidden = await h.Attempts.GetAssignmentPageAsync(test.Id, s.Student);
        Assert.Null(hidden.Score); Assert.Empty(hidden.AttemptCriticalTexts);
        var lecturer = await h.Gradebook.GetReportAsync(test.Id, s.Lecturer);
        Assert.Equal(CriticalLabel.Failed, Assert.Single(lecturer.Rows).Score!.CriticalLabel);
        h.Clock.UtcNow = h.Clock.UtcNow.AddMinutes(2);
        await h.Assignments.PublishScoresAsync(test.Id, s.Lecturer);
        var shown = await h.Attempts.GetAssignmentPageAsync(test.Id, s.Student);
        Assert.Equal(CriticalLabel.Failed, shown.Score!.CriticalLabel); Assert.Contains("Liệt", shown.AttemptCriticalTexts[testAt.Id]);
    }

    [Fact]
    public async Task GopKhachKhongLamLanNhomTruyenThongVaSrsVaChuyenLichSu()
    {
        using var h = new Harness(); var s = await WithCards(h); var f = Cards(h); var set = s.Quiz.StudySetId;
        var user = (await h.Db.Users.FindAsync(s.Student))!; user.EmailConfirmed = true; await h.Db.SaveChangesAsync();
        await h.Assignments.ChangeStrangerPolicyAsync(s.Assignment.Id, StrangerPolicy.Allow, s.Lecturer);
        var token = GuestService.NewToken(); var guests = new GuestService(h.Db, h.Clock, h.Notifier);
        var guest = await guests.JoinAsync(s.Assignment.Id, "Khách", token);
        var session = await f.GetSessionAsync(set, s.Assignment.Id, s.Student); var id = session.Cards[0].Id;
        await f.RateAsync(set, s.Assignment.Id, id, FlashcardRating.NotRemembered, s.Student);
        await f.ChangeModeAsync(set, s.Assignment.Id, FlashcardMode.SpacedRepetition, guest.UserId);
        h.Clock.UtcNow = h.Clock.UtcNow.AddMinutes(1);
        await f.RateAsync(set, s.Assignment.Id, id, FlashcardRating.Remembered, guest.UserId);
        await guests.MergeAsync(token, s.Student);
        var p = await h.Db.Participants.SingleAsync(p => p.AssignmentId == s.Assignment.Id && p.UserId == s.Student);
        var progress = await h.Db.FlashcardProgresses.SingleAsync(c => c.ParticipantId == p.Id && c.FlashcardId == id);
        Assert.Equal(FlashcardRating.NotRemembered, FlashcardService.TraditionalRating(progress)); Assert.Equal(2, progress.SrsBox);
        Assert.Equal(2, (await f.HistoryAsync(set, s.Assignment.Id, s.Student)).Reviews.Count);
    }
}
