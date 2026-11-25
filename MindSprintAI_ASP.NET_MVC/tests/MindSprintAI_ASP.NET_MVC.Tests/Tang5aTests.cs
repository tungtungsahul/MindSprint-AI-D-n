using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using MindSprintAI_ASP.NET_MVC.Data.Entities;
using MindSprintAI_ASP.NET_MVC.Models;
using MindSprintAI_ASP.NET_MVC.Services;

namespace MindSprintAI_ASP.NET_MVC.Tests;

public class Tang5aPureTests
{
    [Fact]
    public void DoiDapAnMoiXetDiemLanDauTheoLanTraLoiDau()
    {
        // (25A, 28A) lần đầu sai cả hai khóa, lần luyện đúng khóa mới: chỉ điểm gần nhất được cộng.
        var a = Tang4RuleTests.Build(0, 80);
        a.Participant.Assignment.Mode = AssignmentMode.Practice;
        var q = Tang4RuleTests.Add(a, QuestionType.MultipleChoice);
        q.QuestionId = 1; q.Attempt = a; q.AnswerCount = 2;
        q.Submissions.Add(new() { Id = 1, IsFirstAnswer = true, IsFinal = false, IsCorrect = false, Score = 0,
            PayloadJson = "{\"Selected\":[1,2]}", SubmittedAt = DateTime.UtcNow });
        q.Submissions.Add(new() { Id = 2, IsFirstAnswer = false, IsFinal = true, IsCorrect = false, Score = 0,
            PayloadJson = "{\"Selected\":[2]}", SubmittedAt = DateTime.UtcNow.AddSeconds(5) });
        var score = RegradeRules.Effective(q, [new Regrade { Id = 1, QuestionId = 1, Method = RegradeMethod.NewCorrect,
            NewCorrectJson = "[2]", Reason = "Đáp án B mới đúng", At = DateTime.UtcNow }]);
        Assert.Equal(0, score.First);
        Assert.Equal(1, score.Latest);
    }

    [Fact]
    public async Task TrinhSoanMoSuaCauCuVaHienPhamViKieuSuaCongGio()
    {
        var set = new StudySet { Id = 1, Title = "Bộ học" };
        var quiz = new Quiz { Id = 1, StudySet = set, Status = QuizStatus.Ready, Title = "Quiz" };
        var q = new Question { Id = 1, QuizId = 1, Type = QuestionType.SingleChoice, Prompt = "Câu hỏi",
            Options = [new() { Id = 1, Text = "A", IsCorrect = true }, new() { Id = 2, Text = "B", OrderIndex = 1 }] };
        quiz.Questions.Add(q);
        var html = System.Net.WebUtility.HtmlDecode(await Tang4ViewTests.Render("StudySets", "Quiz", new QuizEditorModel
        { Quiz = quiz, Form = QuestionForm.From(q), WasAssigned = true, HasAttempts = true, Usage = [new(1, "Quiz", "Lớp A", true)] }));
        Assert.Contains("Quiz này đang dùng ở 1 bài giao", html); Assert.Contains("Lớp A", html);
        Assert.Contains("name=\"EditKind\"", html); Assert.Contains("Sửa nhỏ (không đổi nghĩa/đáp án)", html);
        Assert.Contains("ExtraSessionMinutes", html); Assert.Contains("Lưu câu hỏi", html);
        Assert.DoesNotContain("Chức năng sửa đề khi đã có người làm sẽ có ở bản sau", html);
        Assert.DoesNotContain("action=\"/StudySets/DeleteQuestion", html);
    }
    [Theory]
    [InlineData(QuestionEditKind.Minor)]
    [InlineData(QuestionEditKind.Content)]
    public void SuaDeVanGiuLuaChonGioRiengCuaLuotCu(QuestionEditKind kind)
    {
        var a = MakeAttempt(1); var q = a.Questions[0];
        a.Participant.Assignment.IgnoreQuestionTimers = true;
        var snapshot = QuestionSnapshot.FromJson(q.SnapshotJson) with { TimeLimitSeconds = 30, IgnoreQuestionTimers = false };
        q.SnapshotJson = snapshot.ToJson();
        QuestionEditRules.Apply(a, q, snapshot with { Prompt = "Đề sửa" }, 3, kind);
        Assert.Equal(30, QuestionSnapshot.FromJson(q.SnapshotJson).TimeLimitSeconds);
    }
    [Fact]
    public void SuaNhoSoBangNhauKhacSoChuSoLeVanHopLe()
    {
        var first = new Question { Type = QuestionType.NumberInput, Points = 1.0000m, NumberAnswer = 3.000m, NumberTolerance = .1000m };
        var second = new Question { Type = QuestionType.NumberInput, Points = 1, NumberAnswer = 3, NumberTolerance = .1m };
        Assert.True(QuestionEditRules.IsMinor(first, second));
    }

    [Fact]
    public void DatTinhTheoDiemDaLamTronSauChamLai()
    {
        var a = new Assignment { PassMark = 5, ScoreScale = ScoreScale.Ten };
        Assert.True(GradebookService.Passed(new(4.999m, 10), a));
        Assert.False(GradebookService.Passed(new(4.99m, 10), a));
        Assert.Null(GradebookService.Passed(null, a));
    }

    [Theory]
    [InlineData("[1]", 2)]
    [InlineData("[2,3]", 2)]
    [InlineData("[2]", 0)]
    [InlineData("[1,2,3]", 0)]
    public void DoiKhoaChonNhieuChiNhanDungCaBoCuHoacMoi(string payload, decimal expected)
    {
        var a = MakeAttempt(); var q = a.Questions[0];
        q.SnapshotJson = (QuestionSnapshot.FromJson(q.SnapshotJson) with { Type = QuestionType.MultipleChoice,
            Options = [new(1, 0, "Cũ", true), new(2, 1, "Mới A", false), new(3, 2, "Mới B", false)] }).ToJson();
        q.Submissions.Add(new() { IsFinal = true, IsFirstAnswer = true, PayloadJson = "{\"Selected\":" + payload + "}", IsCorrect = payload == "[1]", Score = payload == "[1]" ? 2 : 0 });
        q.AnswerCount = 1;
        a.Participant.Assignment.Regrades.Add(new() { QuestionId = 1, Method = RegradeMethod.NewCorrect, NewCorrectJson = "[2,3]" });
        RegradeRules.Recompute(a); Assert.Equal(expected, a.FirstScore);
    }
    [Fact]
    public void SuaNhoChiDoiChuKhongDoiKetQua()
    {
        var a = MakeAttempt(1); var q = a.Questions[0];
        RegradeRules.Recompute(a);
        var old = q.Submissions[0];
        var snap = QuestionSnapshot.FromJson(q.SnapshotJson) with { Prompt = "Đề sửa chữ", Explanation = "Giải thích sửa" };
        QuestionEditRules.Apply(a, q, snap, 3, QuestionEditKind.Minor);
        Assert.Equal(2, a.FirstScore); Assert.Equal(1, q.AnswerCount); Assert.True(old.IsFinal);
        Assert.Equal("Đề sửa chữ", QuestionSnapshot.FromJson(q.SnapshotJson).Prompt); Assert.Equal(3, q.QuestionVersion);
    }

    [Fact]
    public void ResetGiuLichSuGiamLamChuGiuMocVaCapDuGio()
    {
        var a = MakeAttempt(1); var q = a.Questions[0];
        a.MasteredCount = 1; q.IsMastered = true; a.MasteryReachedAt = Tang4cPureTests.Now;
        a.Participant.MasteryReached = true;
        q.DraftJson = "nháp"; q.DraftSavedAt = Tang4cPureTests.Now; q.TimerStartedAt = Tang4cPureTests.Now.AddMinutes(-1); q.TimeLeftSeconds = 1;
        var snapshot = QuestionSnapshot.FromJson(q.SnapshotJson) with { Points = 5, TimeLimitSeconds = 30 };
        QuestionEditRules.Apply(a, q, snapshot, 2, QuestionEditKind.Content);
        Assert.Equal(AttemptQuestionState.ResetByEdit, q.State); Assert.Equal(0, q.AnswerCount);
        Assert.Null(q.FirstScore); Assert.Null(q.LatestScore); Assert.Null(q.DraftJson); Assert.Null(q.DraftSavedAt);
        Assert.False(q.IsMastered); Assert.Equal(0, a.MasteredCount);
        Assert.Equal(Tang4cPureTests.Now, a.MasteryReachedAt); Assert.True(a.Participant.MasteryReached);
        Assert.All(q.Submissions, s => { Assert.False(s.IsFinal); Assert.False(s.IsFirstAnswer); });
        Assert.Equal(0, a.FirstScore); Assert.Equal(0, a.PracticeScore); Assert.Equal(7, a.MaxScore);
        Assert.Null(q.TimerStartedAt); Assert.Null(q.TimeLeftSeconds);
        a.Participant.Assignment.Mode = AssignmentMode.Test;
        QuestionTimerRules.Open(a, q, Tang4cPureTests.Now);
        Assert.Equal(30, QuestionTimerRules.Read(a, q, Tang4cPureTests.Now).SecondsLeft);
    }

    [Theory]
    [InlineData(QuestionEditKind.Minor)]
    [InlineData(QuestionEditKind.Content)]
    public void LuotDaKetThucGiuDeVaDiem(QuestionEditKind kind)
    {
        var a = MakeAttempt(1); RegradeRules.Recompute(a); a.Status = AttemptStatus.Ended;
        var q = a.Questions[0]; var before = q.SnapshotJson;
        QuestionEditRules.Apply(a, q, QuestionSnapshot.FromJson(before) with { Prompt = "Đề mới", Points = 5 }, 3, kind);
        Assert.Equal(before, q.SnapshotJson); Assert.Equal(2, a.FirstScore); Assert.True(q.Submissions[0].IsFinal);
    }

    [Theory]
    [InlineData("type")][InlineData("points")][InlineData("key")][InlineData("options")]
    [InlineData("blanks")][InlineData("case")][InlineData("diacritics")][InlineData("punctuation")]
    [InlineData("blankOrder")][InlineData("scoring")][InlineData("number")][InlineData("tolerance")]
    [InlineData("timer")][InlineData("optionOrder")][InlineData("required")]
    public void SuaNhoTuChoiDoiQuyTac(string field)
    {
        Question Build() => new() { Type = QuestionType.SingleChoice, Prompt = "Câu", Points = 1,
            Options = [new() { Text = "Đúng", IsCorrect = true }, new() { Text = "Sai", OrderIndex = 1 }] };
        var old = Build(); var updated = Build(); updated.Prompt = "Chữ khác"; updated.Explanation = "Giải thích";
        Assert.True(QuestionEditRules.IsMinor(old, updated));
        switch (field)
        {
            case "type": updated.Type = QuestionType.MultipleChoice; break;
            case "points": updated.Points = 2; break;
            case "key": updated.Options[0].IsCorrect = false; break;
            case "options": updated.Options.Add(new() { Text = "Thêm", OrderIndex = 2 }); break;
            case "blanks": updated.Blanks.Add(new() { Answers = [new() { Text = "Đáp án" }] }); break;
            case "case": updated.CaseSensitive = true; break;
            case "diacritics": updated.AcceptNoDiacritics = true; break;
            case "punctuation": updated.IgnorePunctuation = true; break;
            case "blankOrder": updated.AcceptAnyBlankOrder = true; break;
            case "scoring": updated.BlankScoring = BlankScoring.PerBlank; break;
            case "number": updated.NumberAnswer = 3; break;
            case "tolerance": updated.NumberTolerance = .1m; break;
            case "timer": updated.TimeLimitSeconds = 30; break;
            case "optionOrder": updated.KeepOptionOrder = true; break;
            case "required": updated.IsRequired = true; break;
        }
        Assert.False(QuestionEditRules.IsMinor(old, updated));
    }

    [Fact]
    public void ChamLaiApTheoThuTuKhongDoiLamChuVaKhongHoiSinhCauHuy()
    {
        var a = MakeAttempt(2); var q = a.Questions[0];
        a.MasteredCount = 1; q.IsMastered = true; a.MasteryReachedAt = Tang4cPureTests.Now;
        a.Participant.Assignment.Regrades = [new() { Id = 2, QuestionId = 1, Method = RegradeMethod.Cancel },
            new() { Id = 1, QuestionId = 1, Method = RegradeMethod.FullCredit },
            new() { Id = 3, QuestionId = 1, Method = RegradeMethod.FullCredit }];
        RegradeRules.Recompute(a);
        Assert.Equal(0, a.FirstScore); Assert.Equal(2, a.MaxScore); Assert.Equal(1, a.MasteredCount); Assert.True(q.IsMastered);
        Assert.Equal(Tang4cPureTests.Now, a.MasteryReachedAt);
    }

    [Theory]
    [InlineData(false)][InlineData(true)]
    public void TraLoiSauChamLaiVaKetThucCungDungMotPhepTinh(bool test)
    {
        var a = MakeAttempt(); a.Participant.Assignment.Mode = test ? AssignmentMode.Test : AssignmentMode.Practice;
        a.Participant.Assignment.Regrades.Add(new() { QuestionId = 1, Method = RegradeMethod.NewCorrect, NewCorrectJson = "[2]" });
        var q = a.Questions[0];
        q.Submissions.Add(new() { IsFirstAnswer = true, IsFinal = true, IsCorrect = false, Score = 0,
            PayloadJson = JsonSerializer.Serialize(new ChoiceAnswer([2])) });
        q.AnswerCount = 1; q.FirstScore = q.LatestScore = 0;
        RegradeRules.Recompute(a);
        Assert.Equal(2, a.FirstScore);
        AttemptRules.End(a, a.Participant, AttemptEndReason.Submitted, Tang4cPureTests.Now);
        Assert.Equal(2, a.FirstScore);
    }

    [Fact]
    public void NhapDuocChamKhiHetHanCungNhanQuyTacChamLai()
    {
        var a = MakeAttempt(); a.Participant.Assignment.Mode = AssignmentMode.Test;
        a.Questions[0].SnapshotJson = (QuestionSnapshot.FromJson(a.Questions[0].SnapshotJson) with { Type = QuestionType.MultipleChoice }).ToJson();
        a.Questions[0].DraftJson = "{\"Selected\":[2]}";
        a.Participant.Assignment.Regrades.Add(new() { QuestionId = 1, Method = RegradeMethod.NewCorrect, NewCorrectJson = "[2]" });
        AttemptRules.End(a, a.Participant, AttemptEndReason.DueDate, Tang4cPureTests.Now);
        Assert.Equal(2, a.FirstScore);
    }

    [Theory]
    [InlineData(false, false, true, false)]
    [InlineData(false, false, false, true)]
    [InlineData(true, false, true, true)]
    [InlineData(true, true, true, false)]
    [InlineData(true, true, true, true)]
    [InlineData(false, false, false, false)]
    public async Task ReviewVaJsonChiLoDapAnKhiDungQuyen(bool test, bool retake, bool published, bool answered)
    {
        var a = MakeAttempt(answered ? 2 : 0); a.Status = AttemptStatus.Ended;
        a.Participant.Assignment.Mode = test ? AssignmentMode.Test : AssignmentMode.Practice;
        a.Participant.Assignment.AnswersPublishedAt = published ? Tang4cPureTests.Now : null;
        a.Participant.Assignment.Regrades.Add(new() { QuestionId = 1, Method = RegradeMethod.NewCorrect,
            NewCorrectJson = "[2]", Reason = "Sửa khóa chấm" });
        var list = AttemptService.ProjectQuestions(a, retake);
        var q = list[0]; var reveal = test ? published && !retake : answered;
        var snap = QuestionSnapshot.FromJson(a.Questions[0].SnapshotJson) with
        { Options = [new(9, 0, "CURRENT_ANSWER_SECRET", true)] };
        list[0] = q with { ReviewNotices = RegradeRules.ReviewNotes(a, a.Questions[0], q.CorrectIds != null, snap, true) };
        var html = System.Net.WebUtility.HtmlDecode(await Tang4ViewTests.Render("Learn", "Review", a, new()
        { ["Questions"] = list, ["ShowScores"] = false }));
        var json = JsonSerializer.Serialize(list[0]);
        Assert.Contains("Câu 1 đã được chấm lại: Sửa khóa chấm", html);
        Assert.Equal(reveal, html.Contains("CURRENT_ANSWER_SECRET"));
        Assert.Equal(reveal, json.Contains("CURRENT_ANSWER_SECRET"));
        Assert.Equal(reveal, html.Contains("EXPLANATION_SECRET"));
        if (!reveal) { Assert.Null(q.CorrectIds); Assert.Contains("Câu 1 đã được sửa", html); }
    }

    [Fact]
    public async Task PartialCauChuaMoKhongLoDapAnSauChamLai()
    {
        var a = MakeAttempt(); var aq = a.Questions[0];
        aq.SnapshotJson = (QuestionSnapshot.FromJson(aq.SnapshotJson) with { TimeLimitSeconds = 30 }).ToJson();
        a.Participant.Assignment.Regrades.Add(new() { QuestionId = 1, Method = RegradeMethod.FullCredit, Reason = "Chấp nhận" });
        var q = AttemptService.ProjectQuestions(a, false)[0];
        var html = await Tang4ViewTests.Render("Learn", "_PlayQuestionBody", new OpenQuestionResult(q, 2, 30, false, false));
        Assert.DoesNotContain("OLD_SECRET", html); Assert.DoesNotContain("EXPLANATION_SECRET", html);
        Assert.DoesNotContain("data-correct=", html); Assert.Null(q.ReviewNotices);
    }

    [Fact]
    public async Task GiaoDienChamLaiCoAntiforgeryVaMaYeuCauKhongChenJs()
    {
        var html = await Tang4ViewTests.Render("Assignments", "_RegradeForm", new RegradeFormModel(1,
            new RegradeQuestion(1, 1, "Câu", true, [new(1, 0, "<script>alert(1)</script>", true)])));
        Assert.Contains("__RequestVerificationToken", html); Assert.Contains("clientRequestId", html);
        Assert.DoesNotContain("<script>alert", html); Assert.Contains("&lt;script&gt;", html);
    }

    [Fact]
    public void ModelKhopSnapshot()
    {
        // MindSprint gộp migration Tang5a vào KhoiTao: chỉ còn kiểm model khớp snapshot.
        using var db = DatabaseFixture.NewContext();
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Theory]
    [InlineData(RegradeMethod.NewCorrect, 2, 2, 0, 4)]
    [InlineData(RegradeMethod.FullCredit, 2, 2, 2, 4)]
    [InlineData(RegradeMethod.Cancel, 0, 0, 0, 2)]
    public void BaCachChamLaiTinhDungCaNguoiBoTrong(RegradeMethod method, decimal oldKey, decimal newKey, decimal blank, decimal max)
    {
        foreach (var (selected, expected) in new[] { (1, oldKey), (2, newKey), (0, blank) })
        {
            var a = MakeAttempt(selected);
            a.Participant.Assignment.Regrades.Add(new Regrade { QuestionId = 1, Method = method, NewCorrectJson = "[2]" });
            RegradeRules.Recompute(a);
            Assert.Equal(expected, a.FirstScore);
            Assert.Equal(expected, a.PracticeScore);
            Assert.Equal(max, a.MaxScore);
        }
    }

    internal static Attempt MakeAttempt(int selected = 0)
    {
        var a = new Attempt { Participant = new Participant { Assignment = new Assignment
        { MasteryThreshold = 100, ScoreRetries = true } }, MaxScore = 4, PracticeScore = 0 };
        for (var id = 1; id <= 2; id++)
        {
            var q = new AttemptQuestion { Id = id, QuestionId = id, Attempt = a, SnapshotJson = new QuestionSnapshot(id, id - 1,
                QuestionType.SingleChoice, "Câu hỏi", "EXPLANATION_SECRET", 2,
                [new(1, 0, "OLD_SECRET", true), new(2, 1, "NEW_SECRET", false)]).ToJson() };
            a.Questions.Add(q);
            if (id == 1 && selected != 0) q.Submissions.Add(new AnswerSubmission
            { IsFirstAnswer = true, IsFinal = true, Score = selected == 1 ? 2 : 0, IsCorrect = selected == 1,
                PayloadJson = JsonSerializer.Serialize(new ChoiceAnswer([selected])) });
            if (q.Submissions.Count > 0) { q.AnswerCount = 1; q.FirstScore = q.LatestScore = q.Submissions[0].Score; }
        }
        return a;
    }
}

[Collection("db")]
public class Tang5aTests
{
    [Fact]
    public async Task CauChuaGiaoSuaRoiVanXoaDuocKhongVuongKhoaNgoaiRevision()
    {
        using var h = new Harness(); var owner = await h.UserAsync("Chủ nháp");
        var set = await h.Library.CreateStudySetAsync("Bộ nháp", owner);
        var quiz = await h.Library.CreateQuizAsync(set.Id, "Nháp", owner);
        var q = await h.Library.SaveChoiceQuestionAsync(quiz.Id, null, null, Harness.Single(), owner);
        await h.Library.SaveChoiceQuestionAsync(quiz.Id, q.Id, q.Version, Harness.Single("Sửa nháp"), owner);
        Assert.True(await h.Db.QuestionRevisions.AnyAsync(r => r.QuestionId == q.Id));
        await h.Library.DeleteQuestionAsync(quiz.Id, q.Id, owner);
        Assert.False(await h.Db.Questions.AnyAsync(x => x.Id == q.Id));
        Assert.False(await h.Db.QuestionRevisions.AnyAsync(r => r.QuestionId == q.Id));
    }
    private static ChoiceQuestionInput Input(Question q, string? prompt = null, decimal? points = null) => new(q.Type,
        prompt ?? q.Prompt, q.Explanation, points ?? q.Points, q.KeepOptionOrder,
        q.Options.OrderBy(o => o.OrderIndex).Select(o => new OptionInput(o.Text, o.IsCorrect)).ToList(), q.IsRequired, q.TimeLimitSeconds);

    private static int Right(AttemptQuestion q) => QuestionSnapshot.FromJson(q.SnapshotJson).Options.Single(o => o.IsCorrect).Id;
    private static int Wrong(AttemptQuestion q) => QuestionSnapshot.FromJson(q.SnapshotJson).Options.First(o => !o.IsCorrect).Id;
    private static Task<AnswerResult> Answer(Harness h, Attempt a, int index = 0, bool right = true) => h.Attempts.AnswerAsync(a.Id,
        a.Questions[index].Id, Guid.NewGuid(), [right ? Right(a.Questions[index]) : Wrong(a.Questions[index])], a.Participant.UserId!,
        questionVersion: a.Questions[index].QuestionVersion);

    [Fact]
    public async Task ChamLaiChiTrongMotBaiGiaoKhongLanSangBaiCungQuiz()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(2);
        var second = await h.Assignments.CreatePracticeAsync(s.Room.Id, s.Quiz.Id, new PracticeSettings(null, null, 3), s.Lecturer);
        var firstAttempt = await h.Attempts.StartAsync(s.Assignment.Id, s.Student);
        var otherAttempt = await h.Attempts.StartAsync(second.Id, s.Student);
        using var grader = new Harness();
        await grader.Regrades.ApplyAsync(s.Assignment.Id, new(s.Quiz.Questions[0].Id, RegradeMethod.FullCredit, "Chỉ bài này", Guid.NewGuid(), []), s.Lecturer);
        // DbContext học viên đã mở trước lần chấm lại vẫn phải nạp quy tắc mới sau khóa bài.
        await Answer(h, firstAttempt, right: false); await Answer(h, otherAttempt, right: false);
        Assert.Equal(1, firstAttempt.FirstScore); Assert.Equal(0, otherAttempt.FirstScore);
    }

    [Theory]
    [InlineData(RegradeMethod.NewCorrect, 2, 2)]
    [InlineData(RegradeMethod.FullCredit, 2, 2)]
    [InlineData(RegradeMethod.Cancel, 1, 1)]
    public async Task LichSuGiuDungDiemSauLuyenVaLamChuKhongDoi(RegradeMethod method, decimal after, decimal max)
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(2, new PracticeSettings(null, null, 3,
            MasteryThreshold: 100, AnswersPerQuestion: 1, ScoreRetries: true));
        var a = await h.Attempts.StartAsync(s.Assignment.Id, s.Student); await Answer(h, a, right: false); await Answer(h, a, 1);
        var mastery = a.MasteredCount;
        var regrade = await h.Regrades.ApplyAsync(s.Assignment.Id, new(s.Quiz.Questions[0].Id, method,
            "Sửa điểm sau luyện", Guid.NewGuid(), [Wrong(a.Questions[0])]), s.Lecturer);
        var history = await h.Db.ScoreHistories.SingleAsync(x => x.RegradeId == regrade.Id);
        Assert.Equal(1, history.OldPracticeScore); Assert.Equal(after, history.NewPracticeScore);
        Assert.Equal(after, a.PracticeScore); Assert.Equal(after, a.FirstScore); Assert.Equal(max, a.MaxScore);
        Assert.Equal(mastery, a.MasteredCount); Assert.False(a.Questions[0].IsMastered);
    }

    [Theory]
    [InlineData(QuestionType.TextInput)]
    [InlineData(QuestionType.NumberInput)]
    public async Task CauDienSuaDuocResetKhongNhanKhoaChonVaApCachHaiBa(QuestionType type)
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(2); var a = await h.Attempts.StartAsync(s.Assignment.Id, s.Student);
        await Answer(h, a);
        var q = s.Quiz.Questions[0];
        var input = new InputQuestionInput(type, "Đề điền", "Giải thích mới", 3, [["Hà Nội"]], NumberAnswer: "2.123456789", NumberTolerance: ".000000001");
        await h.Library.SaveInputQuestionAsync(s.Quiz.Id, q.Id, q.Version, input, s.Lecturer);
        Assert.Equal(AttemptQuestionState.ResetByEdit, a.Questions[0].State); Assert.Equal(type, QuestionSnapshot.FromJson(a.Questions[0].SnapshotJson).Type);
        await Assert.ThrowsAsync<BusinessException>(() => h.Regrades.ApplyAsync(s.Assignment.Id, new(q.Id, RegradeMethod.NewCorrect, "Lỗi", Guid.NewGuid(), [1]), s.Lecturer));
        await h.Library.SaveInputQuestionAsync(s.Quiz.Id, q.Id, q.Version, input with { Prompt = "Sửa chữ" }, s.Lecturer, QuestionEditKind.Minor);
        await h.Regrades.ApplyAsync(s.Assignment.Id, new(q.Id, RegradeMethod.FullCredit, "Chấp nhận cả bỏ trống", Guid.NewGuid(), []), s.Lecturer);
        Assert.Equal(3, a.FirstScore); Assert.Equal(4, a.MaxScore);
        await h.Regrades.ApplyAsync(s.Assignment.Id, new(q.Id, RegradeMethod.Cancel, "Bỏ câu điền", Guid.NewGuid(), []), s.Lecturer);
        Assert.Equal(0, a.FirstScore); Assert.Equal(1, a.MaxScore);
    }

    [Fact]
    public async Task CauKhaoSatKhongDuocChamLaiVaCauHuyKhongTinhTrongLuotMoi()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(3); var q = s.Quiz.Questions[0];
        await h.Library.SaveChoiceQuestionAsync(s.Quiz.Id, q.Id, q.Version, new(QuestionType.SurveyText, "Ý kiến", null, 0, false, []), s.Lecturer);
        var fake = new RecordingLearnNotifier(); h.Notifier = fake;
        await Assert.ThrowsAsync<BusinessException>(() => h.Regrades.ApplyAsync(s.Assignment.Id, new(q.Id, RegradeMethod.FullCredit, "Không được", Guid.NewGuid(), []), s.Lecturer));
        Assert.Empty(fake.LecturerEvents);
        var graded = s.Quiz.Questions[1];
        await h.Regrades.ApplyAsync(s.Assignment.Id, new(graded.Id, RegradeMethod.Cancel, "Bỏ", Guid.NewGuid(), []), s.Lecturer);
        var a = await h.Attempts.StartAsync(s.Assignment.Id, s.Student);
        Assert.Equal(1, a.MaxScore); await Answer(h, a, 1); Assert.Equal(0, a.FirstScore);
    }

    [Fact]
    public async Task HaiYeuCauChamLaiDongThoiChiTaoMotDong()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(2); await h.Attempts.StartAsync(s.Assignment.Id, s.Student);
        var input = new RegradeInput(s.Quiz.Questions[0].Id, RegradeMethod.FullCredit, "Bấm hai lần", Guid.NewGuid(), []);
        using var first = new Harness(); using var second = new Harness();
        var results = await Task.WhenAll(first.Regrades.ApplyAsync(s.Assignment.Id, input, s.Lecturer), second.Regrades.ApplyAsync(s.Assignment.Id, input, s.Lecturer));
        Assert.Equal(results[0].Id, results[1].Id);
        Assert.Equal(1, await h.Db.Regrades.CountAsync(r => r.AssignmentId == s.Assignment.Id));
        Assert.Equal(1, await h.Db.ScoreHistories.CountAsync(r => r.Regrade.AssignmentId == s.Assignment.Id));
    }

    [Fact]
    public async Task SuaNhoLuuBanCuTangVersionGiuDapAnCaLuotMoVaDaXong()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(2);
        var ended = await h.Attempts.StartAsync(s.Assignment.Id, s.Student); await Answer(h, ended); await h.Attempts.FinishAsync(ended.Id, s.Student);
        var open = await h.Attempts.StartAsync(s.Assignment.Id, s.Student); await Answer(h, open);
        var q = s.Quiz.Questions.OrderBy(q => q.OrderIndex).First(); var version = q.Version;
        var oldSnapshot = ended.Questions[0].SnapshotJson; var submissionId = open.Questions[0].Submissions[0].Id;
        await h.Library.SaveChoiceQuestionAsync(s.Quiz.Id, q.Id, version, Input(q, "Chữ đã sửa"), s.Lecturer, QuestionEditKind.Minor);
        Assert.Equal(version + 1, q.Version);
        var revision = await h.Db.QuestionRevisions.SingleAsync(r => r.QuestionId == q.Id);
        Assert.Equal(version, revision.Version); Assert.Equal(QuestionEditKind.Minor, revision.EditKind);
        Assert.Equal("Câu 1", QuestionSnapshot.FromJson(revision.SnapshotJson).Prompt);
        Assert.Equal(oldSnapshot, ended.Questions[0].SnapshotJson); Assert.Equal(1, ended.FirstScore);
        Assert.Equal("Chữ đã sửa", QuestionSnapshot.FromJson(open.Questions[0].SnapshotJson).Prompt);
        Assert.Equal(submissionId, Assert.Single(open.Questions[0].Submissions, x => x.IsFinal).Id);
        Assert.Equal(1, open.FirstScore); Assert.Equal(1, open.Questions[0].AnswerCount);
    }

    [Fact]
    public async Task SuaNhoDoiKhoaBiTuChoiKhongLuuKhongThongBao()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(); await h.Attempts.StartAsync(s.Assignment.Id, s.Student);
        var fake = new RecordingLearnNotifier(); h.Notifier = fake;
        var q = s.Quiz.Questions[0]; var input = Input(q) with { Options = [new("3", true), new("4", false), new("5", false), new("6", false)] };
        var ex = await Assert.ThrowsAsync<BusinessException>(() => h.Library.SaveChoiceQuestionAsync(s.Quiz.Id, q.Id, q.Version, input,
            s.Lecturer, QuestionEditKind.Minor));
        Assert.Contains("Sửa nhỏ", ex.Message); Assert.Empty(fake.Events); Assert.Empty(fake.LecturerEvents);
        using var read = DatabaseFixture.NewContext(); Assert.False(await read.QuestionRevisions.AnyAsync(r => r.QuestionId == q.Id));
    }

    [Fact]
    public async Task SuaNoiDungResetMoiBaiGiaoGiuLuotDaXongVaHocVienTraLoiLaiLanDau()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(2, new PracticeSettings(null, null, 4, MasteryThreshold: 100, ScoreRetries: true));
        var ended = await h.Attempts.StartAsync(s.Assignment.Id, s.Student);
        await Answer(h, ended); await Answer(h, ended, 1); await h.Attempts.FinishAsync(ended.Id, s.Student);
        var open = await h.Attempts.StartAsync(s.Assignment.Id, s.Student); await Answer(h, open); await Answer(h, open, 1);
        var second = await h.Assignments.CreatePracticeAsync(s.Room.Id, s.Quiz.Id, new PracticeSettings(null, null, 3), s.Lecturer);
        var other = await h.Attempts.StartAsync(second.Id, s.Student);
        var q = s.Quiz.Questions.OrderBy(q => q.OrderIndex).First(); var oldEnded = ended.Questions[0].SnapshotJson;
        var reached = open.MasteryReachedAt;
        var fake = new RecordingLearnNotifier(); h.Notifier = fake;
        fake.OnNotice = async (name, notice) =>
        {
            Assert.Equal("question-edited", name);
            using var read = DatabaseFixture.NewContext();
            Assert.Equal(0, (await read.AttemptQuestions.SingleAsync(x => x.AttemptId == notice.AttemptId && x.QuestionId == q.Id)).AnswerCount);
        };
        fake.OnLecturers = async _ => { using var read = DatabaseFixture.NewContext(); Assert.True(await read.QuestionRevisions.AnyAsync(r => r.QuestionId == q.Id)); };
        await h.Library.SaveChoiceQuestionAsync(s.Quiz.Id, q.Id, q.Version, Input(q, "Đề mới", 3), s.Lecturer);
        var reset = open.Questions.Single(x => x.QuestionId == q.Id);
        Assert.Equal(AttemptQuestionState.ResetByEdit, reset.State); Assert.Equal(0, reset.AnswerCount);
        Assert.Null(reset.FirstScore); Assert.Null(reset.LatestScore); Assert.False(reset.IsMastered);
        Assert.Equal(1, open.MasteredCount); Assert.Equal(reached, open.MasteryReachedAt); Assert.True(open.Participant.MasteryReached);
        Assert.All(reset.Submissions, x => { Assert.False(x.IsFinal); Assert.False(x.IsFirstAnswer); });
        Assert.Equal(1, open.FirstScore); Assert.Equal(1, open.PracticeScore); Assert.Equal(4, open.MaxScore);
        Assert.Equal(oldEnded, ended.Questions[0].SnapshotJson); Assert.Equal(2, ended.FirstScore); Assert.Equal(2, ended.MaxScore);
        Assert.Equal(3, QuestionSnapshot.FromJson(other.Questions.Single(x => x.QuestionId == q.Id).SnapshotJson).Points);
        Assert.Equal(2, fake.Events.Count); Assert.Equal(2, fake.LecturerEvents.Count);
        await Answer(h, open);
        Assert.Equal(1, reset.AnswerCount); Assert.True(reset.Submissions.Single(x => x.IsFinal).IsFirstAnswer); Assert.Equal(4, open.FirstScore);
    }

    [Fact]
    public async Task VersionCuKhongGhiDeVaThemXoaDoiThuTuVanBiChan()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(); await h.Attempts.StartAsync(s.Assignment.Id, s.Student);
        var q = s.Quiz.Questions[0]; var oldVersion = q.Version;
        await h.Library.SaveChoiceQuestionAsync(s.Quiz.Id, q.Id, oldVersion, Input(q, "Bản thắng"), s.Lecturer);
        using var tab = new Harness();
        var ex = await Assert.ThrowsAsync<BusinessException>(() => tab.Library.SaveChoiceQuestionAsync(s.Quiz.Id, q.Id, oldVersion, Input(q, "Bản thua"), s.Lecturer));
        Assert.Equal(QuestionEditRules.Stale, ex.Message);
        foreach (var action in new Func<Task>[] {
            async () => { await h.Library.SaveChoiceQuestionAsync(s.Quiz.Id, null, null, Harness.Single(), s.Lecturer); },
            () => h.Library.DeleteQuestionAsync(s.Quiz.Id, q.Id, s.Lecturer) })
            Assert.Equal(QuestionEditRules.StructureBlocked, (await Assert.ThrowsAsync<BusinessException>(action)).Message);
        // Đổi thứ tự / xóa cả quiz có câu riêng, không nói "Hủy câu" hay "giao bài mới" (D39).
        Assert.Equal(QuestionEditRules.MoveBlocked, (await Assert.ThrowsAsync<BusinessException>(() => h.Library.MoveQuestionAsync(s.Quiz.Id, q.Id, 1, s.Lecturer))).Message);
        Assert.Equal(QuestionEditRules.DeleteQuizBlocked, (await Assert.ThrowsAsync<BusinessException>(() => h.Library.DeleteQuizAsync(s.Quiz.Id, s.Lecturer))).Message);
    }

    [Fact]
    public async Task GioCauDuMoiVaCongPhutChungGhiLichSu63PopupSauCommit()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(2);
        var q = s.Quiz.Questions.OrderBy(q => q.OrderIndex).First();
        await h.Library.SaveChoiceQuestionAsync(s.Quiz.Id, q.Id, q.Version, Input(q) with { TimeLimitSeconds = 30 }, s.Lecturer);
        var test = await h.Assignments.CreateTestAsync(s.Room.Id, s.Quiz.Id, new TestSettings(null, 45, ShuffleQuestions: false), s.Lecturer);
        await h.Assignments.StartTestAsync(test.Id, s.Lecturer);
        var a = await h.Attempts.StartAsync(test.Id, s.Student); a.Participant.ExtraMinutes = 2; await h.Db.SaveChangesAsync();
        await h.Attempts.OpenQuestionAsync(a.Id, a.Questions[0].Id, s.Student); await Answer(h, a);
        h.Clock.UtcNow = h.Clock.UtcNow.AddSeconds(12);
        var oldEnd = test.TestEndsAt; var fake = new RecordingLearnNotifier(); h.Notifier = fake;
        fake.OnNotice = async (name, notice) =>
        {
            using var read = DatabaseFixture.NewContext();
            Assert.Equal(oldEnd!.Value.AddMinutes(5), (await read.Assignments.FindAsync(test.Id))!.TestEndsAt);
            Assert.Contains(name, new[] { "question-edited", "deadline-changed" });
        };
        await h.Library.SaveChoiceQuestionAsync(s.Quiz.Id, q.Id, q.Version, Input(q, "Đổi"), s.Lecturer, QuestionEditKind.Content, 5);
        Assert.Equal(oldEnd!.Value.AddMinutes(5), test.TestEndsAt); Assert.Equal(test.TestEndsAt!.Value.AddMinutes(2), a.DeadlineAt);
        Assert.Equal(AttemptQuestionState.ResetByEdit, a.Questions[0].State);
        await h.Attempts.OpenQuestionAsync(a.Id, a.Questions[0].Id, s.Student);
        Assert.Equal(30, QuestionTimerRules.Read(a, a.Questions[0], h.Clock.UtcNow).SecondsLeft);
        Assert.Contains(fake.Events, x => x.Event == "deadline-changed"); Assert.Contains(fake.Events, x => x.Event == "question-edited");
        Assert.True(await h.Db.AssignmentChanges.AnyAsync(c => c.AssignmentId == test.Id && c.Field == nameof(Assignment.TestEndsAt) && c.Reason == "Cộng giờ khi sửa đề (60, 63)"));
    }

    [Theory]
    [InlineData(RegradeMethod.NewCorrect, 2, 2, 1, 2)]
    [InlineData(RegradeMethod.FullCredit, 2, 2, 2, 2)]
    [InlineData(RegradeMethod.Cancel, 1, 1, 1, 1)]
    public async Task BaCachChamLaiDungDiemVaLichSuToanBoLuot(RegradeMethod mode, decimal oldKey, decimal newKey, decimal blank, decimal max)
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(2);
        var learners = new[] { s.Student, await h.UserAsync("Hoa"), await h.UserAsync("Thư") };
        foreach (var student in learners.Skip(1)) await h.Classrooms.JoinByCodeAsync(s.ClassCode, student);
        var list = new List<Attempt>();
        for (var i = 0; i < 3; i++)
        {
            var a = await h.Attempts.StartAsync(s.Assignment.Id, learners[i]); list.Add(a);
            if (i < 2) await Answer(h, a, right: i == 0);
            await Answer(h, a, 1); if (i < 2) await h.Attempts.FinishAsync(a.Id, learners[i]);
        }
        var q = s.Quiz.Questions.OrderBy(q => q.OrderIndex).First();
        var fake = new RecordingLearnNotifier(); h.Notifier = fake;
        fake.OnLecturers = async _ => { using var read = DatabaseFixture.NewContext(); Assert.Equal(3, await read.ScoreHistories.CountAsync(x => x.Regrade.AssignmentId == s.Assignment.Id)); };
        var regrade = await h.Regrades.ApplyAsync(s.Assignment.Id, new(q.Id, mode, "Khóa ban đầu có lỗi", Guid.NewGuid(), [Wrong(list[0].Questions[0])]), s.Lecturer);
        Assert.Equal(new[] { oldKey, newKey, blank }, list.Select(a => a.FirstScore)); Assert.All(list, a => Assert.Equal(max, a.MaxScore));
        var history = await h.Db.ScoreHistories.Where(x => x.RegradeId == regrade.Id).OrderBy(x => x.AttemptId).ToListAsync();
        Assert.Equal(new[] { 2m, 1m, 1m }, history.Select(x => x.OldFirstScore));
        Assert.Equal(new[] { oldKey, newKey, blank }, history.Select(x => x.NewFirstScore));
        Assert.Single(fake.LecturerEvents); Assert.Empty(fake.Events);
        if (mode == RegradeMethod.FullCredit)
        {
            Assert.True(RegradeRules.Effective(list[2].Questions[0], s.Assignment.Regrades).BlankCredit);
            var (_, rows) = await h.Gradebook.GetReportAsync(s.Assignment.Id, s.Lecturer);
            Assert.True(rows.Single(r => r.UserId == learners[2]).BlankRegradeCredit);
        }
    }

    [Fact]
    public async Task ChamLaiApDungTraLoiSauDoLuotMoiVaGuiLapChiMotDong()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(2); var a = await h.Attempts.StartAsync(s.Assignment.Id, s.Student);
        var q = s.Quiz.Questions[0]; var input = new RegradeInput(q.Id, RegradeMethod.NewCorrect, "Chấp nhận thêm", Guid.NewGuid(), [Wrong(a.Questions[0])]);
        var fake = new RecordingLearnNotifier(); h.Notifier = fake;
        var first = await h.Regrades.ApplyAsync(s.Assignment.Id, input, s.Lecturer);
        using (var retry = new Harness()) Assert.Equal(first.Id, (await retry.Regrades.ApplyAsync(s.Assignment.Id, input, s.Lecturer)).Id);
        Assert.Equal(1, await h.Db.Regrades.CountAsync(r => r.AssignmentId == s.Assignment.Id)); Assert.Single(fake.LecturerEvents);
        await Answer(h, a, right: false); Assert.Equal(1, a.FirstScore); await h.Attempts.FinishAsync(a.Id, s.Student);
        var next = await h.Attempts.StartAsync(s.Assignment.Id, s.Student); await Answer(h, next, right: false);
        Assert.Equal(1, next.FirstScore); Assert.Equal(2, next.MaxScore);
    }

    [Fact]
    public async Task HuyCauCuoiLyDoTrongKhaoSatVaKhoaSaiBiTuChoiKhongThongBao()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(2); await h.Attempts.StartAsync(s.Assignment.Id, s.Student);
        var fake = new RecordingLearnNotifier(); h.Notifier = fake;
        var q = s.Quiz.Questions[0];
        await Assert.ThrowsAsync<BusinessException>(() => h.Regrades.ApplyAsync(s.Assignment.Id, new(q.Id, RegradeMethod.FullCredit, " ", Guid.NewGuid(), []), s.Lecturer));
        await Assert.ThrowsAsync<BusinessException>(() => h.Regrades.ApplyAsync(s.Assignment.Id, new(q.Id, RegradeMethod.NewCorrect, "Lỗi", Guid.NewGuid(), [-1]), s.Lecturer));
        Assert.Empty(fake.LecturerEvents);
        await h.Regrades.ApplyAsync(s.Assignment.Id, new(q.Id, RegradeMethod.Cancel, "Bỏ câu lỗi", Guid.NewGuid(), []), s.Lecturer);
        fake.LecturerEvents.Clear();
        await Assert.ThrowsAsync<BusinessException>(() => h.Regrades.ApplyAsync(s.Assignment.Id,
            new(s.Quiz.Questions[1].Id, RegradeMethod.Cancel, "Bỏ nốt", Guid.NewGuid(), []), s.Lecturer));
        Assert.Empty(fake.LecturerEvents); Assert.Equal(1, await h.Db.Regrades.CountAsync(r => r.AssignmentId == s.Assignment.Id));
    }

    [Fact]
    public async Task GvPhuChamLaiDuocNhungKhongSuaChuBoHocChamDuocNguoiNgoaiLopBi403()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync();
        var assistant = await h.UserAsync("GV phụ"); var other = await h.UserAsync("GV lớp khác");
        h.Db.ClassMembers.Add(new() { ClassroomId = s.Room.Id, UserId = assistant, Role = ClassRole.AssistantLecturer, Status = ClassMemberStatus.Active });
        await h.Classrooms.CreateAsync("Lớp khác", ClassJoinPolicy.Immediate, other); await h.Db.SaveChangesAsync();
        var q = s.Quiz.Questions[0];
        await h.Regrades.ApplyAsync(s.Assignment.Id, new(q.Id, RegradeMethod.FullCredit, "Chấp nhận", Guid.NewGuid(), []), assistant);
        await Assert.ThrowsAsync<ForbiddenException>(() => h.Library.SaveChoiceQuestionAsync(s.Quiz.Id, q.Id, q.Version, Input(q, "Sửa lén"), assistant));
        foreach (var person in new[] { other, s.Student })
        {
            await Assert.ThrowsAsync<ForbiddenException>(() => h.Regrades.ApplyAsync(s.Assignment.Id, new(q.Id, RegradeMethod.FullCredit, "Lén", Guid.NewGuid(), []), person));
            await Assert.ThrowsAsync<ForbiddenException>(() => h.Library.SaveChoiceQuestionAsync(s.Quiz.Id, q.Id, q.Version, Input(q), person));
        }
        var lecturer = await h.Db.ClassMembers.SingleAsync(m => m.ClassroomId == s.Room.Id && m.UserId == s.Lecturer);
        lecturer.Status = ClassMemberStatus.Left; await h.Db.SaveChangesAsync();
        await h.Regrades.ApplyAsync(s.Assignment.Id, new(q.Id, RegradeMethod.FullCredit, "Chủ bộ học", Guid.NewGuid(), []), s.Lecturer);
    }

    [Fact]
    public async Task ReviewKiemTraTruocCongBoKhongCoDapAnMoiSauSuaVaChamLai()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(2);
        var test = await h.Assignments.CreateTestAsync(s.Room.Id, s.Quiz.Id, new TestSettings(null, 45, ShuffleQuestions: false), s.Lecturer);
        await h.Assignments.StartTestAsync(test.Id, s.Lecturer); var a = await h.Attempts.StartAsync(test.Id, s.Student);
        await Answer(h, a); await h.Attempts.FinishAsync(a.Id, s.Student);
        var q = s.Quiz.Questions[0];
        await h.Library.SaveChoiceQuestionAsync(s.Quiz.Id, q.Id, q.Version, Input(q) with { Explanation = "EXPLANATION_SECRET",
            Options = [new("CURRENT_SECRET", true), new("Khác", false)] }, s.Lecturer);
        await h.Regrades.ApplyAsync(test.Id, new(q.Id, RegradeMethod.FullCredit, "Sửa chấm", Guid.NewGuid(), []), s.Lecturer);
        var items = await h.Attempts.GetPlayQuestionsAsync(a, s.Student);
        Assert.Null(items[0].CorrectIds); Assert.Null(items[0].Explanation);
        Assert.Contains("Câu 1 đã được sửa", items[0].ReviewNotices!);
        Assert.DoesNotContain("CURRENT_SECRET", JsonSerializer.Serialize(items));
        Assert.DoesNotContain("EXPLANATION_SECRET", JsonSerializer.Serialize(items));
        await h.Assignments.CloseNowAsync(test.Id, s.Lecturer); await h.Assignments.PublishAnswersAsync(test.Id, true, s.Lecturer);
        items = await h.Attempts.GetPlayQuestionsAsync(a, s.Student);
        Assert.Contains("CURRENT_SECRET", string.Join(" ", items[0].ReviewNotices!));
        var retakes = new RetakeService(h.Db, h.Assignments, h.Attempts, h.Clock);
        var request = await retakes.RequestAsync(test.Id, "Xin lại", s.Student);
        await retakes.DecideAsync(request.Id, true, h.Clock.UtcNow.AddHours(1), s.Lecturer);
        items = await h.Attempts.GetPlayQuestionsAsync(a, s.Student); Assert.Null(items[0].CorrectIds);
        Assert.DoesNotContain("CURRENT_SECRET", JsonSerializer.Serialize(items));
        var again = await retakes.StartAsync(request.Id, s.Student); Assert.Equal(1, again.FirstScore);
        await h.Regrades.ApplyAsync(test.Id, new(q.Id, RegradeMethod.FullCredit, "Áp cả lượt lại", Guid.NewGuid(), []), s.Lecturer);
        Assert.Equal(2, await h.Db.ScoreHistories.CountAsync(x => x.Regrade.AssignmentId == test.Id && x.Regrade.Reason == "Áp cả lượt lại"));
    }

    [Fact]
    public async Task AnswerDongThoiSuaResetKhongConSubmissionCuDuocTinh()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(2);
        var test = await h.Assignments.CreateTestAsync(s.Room.Id, s.Quiz.Id, new TestSettings(null, 45, ShuffleQuestions: false), s.Lecturer);
        await h.Assignments.StartTestAsync(test.Id, s.Lecturer); var a = await h.Attempts.StartAsync(test.Id, s.Student); await Answer(h, a);
        var q = s.Quiz.Questions[0]; var aq = a.Questions[0]; var version = aq.QuestionVersion;
        using var writer = new Harness(); using var learner = new Harness();
        var edit = writer.Library.SaveChoiceQuestionAsync(s.Quiz.Id, q.Id, q.Version, Input(q, "Đề mới"), s.Lecturer);
        async Task AnswerOld()
        {
            try { await learner.Attempts.AnswerAsync(a.Id, aq.Id, Guid.NewGuid(), [Right(aq)], s.Student, questionVersion: version); }
            catch (BusinessException ex) { Assert.Contains("tải lại", ex.Message, StringComparison.OrdinalIgnoreCase); } // thông báo thật: "Hãy tải lại bài…"
        }
        await Task.WhenAll(edit, AnswerOld());
        using var read = DatabaseFixture.NewContext(); var saved = await read.AttemptQuestions.Include(x => x.Submissions).SingleAsync(x => x.Id == aq.Id);
        Assert.Equal(0, saved.AnswerCount); Assert.Equal(AttemptQuestionState.ResetByEdit, saved.State);
        Assert.All(saved.Submissions, x => { Assert.False(x.IsFinal); Assert.False(x.IsFirstAnswer); });
    }
}
