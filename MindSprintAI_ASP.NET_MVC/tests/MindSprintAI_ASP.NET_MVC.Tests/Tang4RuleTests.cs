using System.Text.Json;
using MindSprintAI_ASP.NET_MVC.Data.Entities;
using MindSprintAI_ASP.NET_MVC.Services;

namespace MindSprintAI_ASP.NET_MVC.Tests;

// Thuần nghiệp vụ: không khởi tạo fixture SQL.
public class Tang4RuleTests
{
    [Fact]
    public void PhanHoiKiemTraGiuNguyenHopDongKhongLoThongTin() => Assert.Equal("{}", JsonSerializer.Serialize(new AnswerResult()));

    internal static Attempt Build(int choices = 7, int threshold = 80)
    {
        var a = new Attempt { Participant = new Participant { Assignment = new Assignment { MasteryThreshold = threshold } } };
        a.Participant.Attempts.Add(a);
        for (var i = 0; i < choices; i++) Add(a, QuestionType.SingleChoice);
        return a;
    }

    internal static AttemptQuestion Add(Attempt a, QuestionType type, bool required = false)
    {
        var snapshot = new QuestionSnapshot(a.Questions.Count + 1, a.Questions.Count, type, "Đề", "BÍ_MẬT", SurveyRules.IsSurvey(type) ? 0 : 1,
            SurveyRules.IsChoice(type) ? [new(1, 0, "A", true), new(2, 1, "B", false)] : [])
            { IsRequired = required, MaxLength = type == QuestionType.SurveyText ? 500 : null };
        var q = new AttemptQuestion { Id = a.Questions.Count + 1, Attempt = a, DisplayOrder = a.Questions.Count, SnapshotJson = snapshot.ToJson() };
        a.Questions.Add(q);
        return q;
    }

    [Theory]
    [InlineData(7, 80, 6), InlineData(7, 90, 7), InlineData(7, 100, 7), InlineData(10, 80, 8)]
    public void MocLamChuLamTronLen(int total, int threshold, int expected) => Assert.Equal(expected, MasteryRules.Required(total, threshold));

    [Fact]
    public void MauSoChiCauChonVaDungNhieuLanChiDemMotLan()
    {
        var a = Build();
        Add(a, QuestionType.MultipleChoice);
        foreach (var type in new[] { QuestionType.TextInput, QuestionType.NumberInput, QuestionType.SurveySingle, QuestionType.SurveyMultiple, QuestionType.SurveyText }) Add(a, type);
        foreach (var q in a.Questions) { MasteryRules.Record(a, q, true, DateTime.UtcNow); MasteryRules.Record(a, q, true, DateTime.UtcNow); }
        Assert.Equal(8, MasteryRules.Denominator(a));
        Assert.Equal(8, a.MasteredCount);
    }

    [Fact]
    public void DatMocRoiSaiKhongMatNhungVanPhaiTraLoiCauChuaGap()
    {
        var a = Build();
        var now = DateTime.UtcNow;
        foreach (var q in a.Questions.Take(6)) { q.AnswerCount = 1; MasteryRules.Record(a, q, true, now); }
        Assert.False(MasteryRules.Progress(a)!.CanFinish);
        MasteryRules.Record(a, a.Questions[0], false, now.AddMinutes(1));
        Assert.Equal(now, a.MasteryReachedAt);
        Assert.True(a.Participant.MasteryReached);
        a.Questions[^1].AnswerCount = 1;
        Assert.True(MasteryRules.Progress(a)!.CanFinish);
    }

    [Theory]
    [InlineData(500), InlineData(501)]
    public void YKienGioiHan500VaKhongDungSai(int length)
    {
        var a = Build(0);
        var s = QuestionSnapshot.FromJson(Add(a, QuestionType.SurveyText).SnapshotJson);
        var json = JsonSerializer.Serialize(new SurveyAnswer(new string('a', length)));
        if (length > 500) Assert.Throws<BusinessException>(() => new SurveyGrader().Grade(s, json));
        else { var result = new SurveyGrader().Grade(s, json); Assert.Null(result.IsCorrect); Assert.Equal(0, result.Score); }
    }

    [Fact]
    public void ChiCauDangSaiMoiDuocLamLai()
    {
        // (22B) câu đã đúng không bị bắt/cho làm lại; chỉ câu có lần trả lời gần nhất sai.
        var a = Build(2, 100);
        a.Participant.Assignment.Mode = AssignmentMode.Practice;
        a.Status = AttemptStatus.InProgress;
        var right = a.Questions[0]; right.AnswerCount = 1;
        right.Submissions.Add(new() { IsFinal = true, IsCorrect = true });
        var wrong = a.Questions[1]; wrong.AnswerCount = 1;
        wrong.Submissions.Add(new() { IsFinal = true, IsCorrect = false });
        Assert.False(MasteryRules.CanRetry(a, right));
        Assert.True(MasteryRules.CanRetry(a, wrong));
    }

    [Fact]
    public void KhaoSatTuyChonBoTrongKhongChanNopVaKhongGanNhan()
    {
        // (6, B5b) chỉ khảo sát bắt buộc mới chặn nộp/gắn nhãn; khảo sát tùy chọn bỏ trống là hợp lệ.
        var a = Build(0);
        var optional = Add(a, QuestionType.SurveyText);
        var required = Add(a, QuestionType.SurveySingle, required: true);
        Assert.Equal([required.DisplayOrder + 1], SurveyRules.MissingRequired(a));
        required.AnswerCount = 1;
        Assert.Empty(SurveyRules.MissingRequired(a));
        AttemptRules.End(a, a.Participant, AttemptEndReason.Submitted, DateTime.UtcNow);
        Assert.False(a.Participant.CompletionLabels.HasFlag(CompletionLabels.MissingRequiredSurvey));
        Assert.Equal(0, optional.AnswerCount);
    }

    [Fact]
    public void HetHanThieuKhaoSatBatBuocVanHoanThanhKemNhan()
    {
        var a = Build(0);
        Add(a, QuestionType.SurveyText, required: true);
        AttemptRules.End(a, a.Participant, AttemptEndReason.DueDate, DateTime.UtcNow);
        Assert.Equal(CompletionStatus.Completed, a.Participant.CompletionStatus);
        Assert.True(a.Participant.CompletionLabels.HasFlag(CompletionLabels.MissingRequiredSurvey));
    }

    [Fact]
    public void KiemTraThieuKhaoSatVanChoCongBoDiemKemNhan()
    {
        var a = Build(1); a.MaxScore = 1; a.Participant.Assignment.Mode = AssignmentMode.Test;
        Add(a, QuestionType.SurveyText, required: true);
        AttemptRules.End(a, a.Participant, AttemptEndReason.DueDate, DateTime.UtcNow);
        Assert.Equal(CompletionStatus.SubmittedAwaitingScore, a.Participant.CompletionStatus);
        Assert.True(a.Participant.CompletionLabels.HasFlag(CompletionLabels.MissingRequiredSurvey));
        Assert.False(AttemptService.CanShowScores(a.Participant));
    }

    [Theory]
    [InlineData(ScoreScale.Ten), InlineData(ScoreScale.Original), InlineData(ScoreScale.Hundred)]
    public void ToanKhaoSatKhongHienTiSo(ScoreScale scale) => Assert.Equal("Không chấm điểm", ScoreFormatter.Format(0, 0, scale, RoundingMode.TwoDecimals));

    [Theory]
    [InlineData(1, false), InlineData(2, true)]
    public void GioiHanLanTraLoiQuyetDinhCoKetThucDuoc(int limit, bool retryable)
    {
        var a = Build(1, 100);
        a.Participant.Assignment.AnswersPerQuestion = limit;
        var q = a.Questions[0]; q.AnswerCount = 1;
        q.Submissions.Add(new() { IsFinal = true, IsCorrect = false });
        Assert.Equal(retryable, MasteryRules.CanRetry(a, q));
        Assert.Equal(!retryable, MasteryRules.Progress(a)!.CanFinish);
    }

    [Theory]
    [InlineData(ScoreSelection.Highest, 8, 9), InlineData(ScoreSelection.First, 8, 9)]
    [InlineData(ScoreSelection.Latest, 5, 10), InlineData(ScoreSelection.Average, 6.5, 9.5)]
    public void BangDiemChonTheoLanDauVaKemDiemSauLuyenCungLuot(ScoreSelection mode, decimal raw, decimal practice)
    {
        Attempt[] attempts = [new() { Status = AttemptStatus.Ended, AttemptNumber = 1, FirstScore = 8, MaxScore = 10, PracticeScore = 9 },
            new() { Status = AttemptStatus.Ended, AttemptNumber = 2, FirstScore = 5, MaxScore = 10, PracticeScore = 10 },
            new() { Status = AttemptStatus.Cancelled, AttemptNumber = 3, FirstScore = 10, MaxScore = 10, PracticeScore = 10 }];
        var score = GradebookService.Select(attempts, mode)!;
        Assert.Equal(raw, score.Raw); Assert.Equal(practice, score.Practice);
    }

    [Theory]
    [InlineData(QuestionType.SurveySingle), InlineData(QuestionType.SurveyMultiple), InlineData(QuestionType.SurveyText)]
    public void SnapshotLuuBatBuocVaKhaoSatKhongCoDapAnDiem(QuestionType type)
    {
        var q = new Question { Type = type, Prompt = "Đề", Points = 0, IsRequired = true, MaxLength = type == QuestionType.SurveyText ? 500 : null,
            Options = type == QuestionType.SurveyText ? [] : [new() { Text = "A" }, new() { Text = "B" }] };
        Assert.Null(QuestionRules.Validate(q));
        var snap = QuestionSnapshot.FromJson(QuestionSnapshot.From(q).ToJson());
        Assert.True(snap.IsRequired);
        Assert.Equal(q.MaxLength, snap.MaxLength);
        var a = Build(0); var aq = Add(a, type);
        aq.AnswerCount = 1; aq.Submissions.Add(new() { IsFinal = true, PayloadJson = type == QuestionType.SurveyText ? "{\"Value\":\"Ý kiến\"}" : "{\"Selected\":[1]}" });
        var browser = AttemptService.ProjectQuestions(a, false).Single();
        Assert.Null(browser.IsCorrect); Assert.Null(browser.CorrectIds); Assert.Null(browser.Explanation);
        Assert.Null(browser.CorrectAnswers); Assert.False(browser.CanRetry);
    }

    [Fact]
    public void JsonOnTapChuaTraLoiKhongCoDapAnVaCauSaiMoLaiChiLoPhanHoiCu()
    {
        var a = Build(2);
        var json = JsonSerializer.Serialize(AttemptService.ProjectQuestions(a, false));
        Assert.DoesNotContain("BÍ_MẬT", json);
        Assert.DoesNotContain("CorrectIds", json); Assert.DoesNotContain("IsCorrect", json);
        var q = a.Questions[0]; q.AnswerCount = 1;
        q.Submissions.Add(new() { IsFinal = true, IsCorrect = false, PayloadJson = "{\"Selected\":[2]}" });
        var browser = AttemptService.ProjectQuestions(a, false);
        Assert.True(browser[0].CanRetry); Assert.Equal("BÍ_MẬT", browser[0].Explanation);
        Assert.Null(browser[1].Explanation); Assert.Null(browser[1].CorrectIds);
    }

    [Theory]
    [InlineData(QuestionType.SurveySingle), InlineData(QuestionType.SurveyMultiple)]
    public void KhaoSatLuaChonChanIdLaVaChonMotKhongDuocChonNhieu(QuestionType type)
    {
        var a = Build(0); var s = QuestionSnapshot.FromJson(Add(a, type).SnapshotJson);
        var grader = new SurveyGrader();
        Assert.Throws<BusinessException>(() => grader.Grade(s, "{\"Selected\":[999]}"));
        Assert.Throws<BusinessException>(() => grader.Grade(s, "{\"Selected\":[]}"));
        if (type == QuestionType.SurveySingle) Assert.Throws<BusinessException>(() => grader.Grade(s, "{\"Selected\":[1,2]}"));
        else Assert.Null(grader.Grade(s, "{\"Selected\":[1,2]}").IsCorrect);
    }

    [Theory]
    [InlineData(QuestionType.SurveySingle), InlineData(QuestionType.SurveyMultiple)]
    public void SanSangGiaoKhaoSatCanHaiDenSauLuaChonKhongRong(QuestionType type)
    {
        var q = new Question { Type = type, Prompt = "Khảo sát", Points = 0, Options = [new() { Text = "A" }] };
        Assert.NotNull(QuestionRules.Validate(q));
        q.Options.Add(new() { Text = " " }); Assert.NotNull(QuestionRules.Validate(q));
        q.Options[1].Text = "B"; Assert.Null(QuestionRules.Validate(q));
        while (q.Options.Count < 7) q.Options.Add(new() { Text = "C" });
        Assert.NotNull(QuestionRules.Validate(q));
    }

    [Fact]
    public void BaoCaoTienDoTotNhatChiTrongCacLuotDaKetThuc()
    {
        var first = Build(10); first.Status = AttemptStatus.Ended; first.MasteredCount = 7;
        var second = Build(10); second.Status = AttemptStatus.Ended; second.MasteredCount = 3;
        var open = Build(10); open.MasteredCount = 9;
        var cancelled = Build(10); cancelled.Status = AttemptStatus.Cancelled; cancelled.MasteredCount = 10;
        Assert.Equal("Chưa đạt mốc (7/10, mốc 80%)", MasteryRules.Report(first.Participant.Assignment, [first, second, open, cancelled], false));
    }
}
