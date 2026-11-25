using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using MindSprintAI_ASP.NET_MVC.Data.Entities;
using MindSprintAI_ASP.NET_MVC.Models;
using MindSprintAI_ASP.NET_MVC.Services;

namespace MindSprintAI_ASP.NET_MVC.Tests;

[Collection("db")]
public class CongTacTuOnTests
{
    private static async Task<(Scenario S, FlashcardService Cards, int CardId)> CombinedAsync(Harness h)
    {
        var s = await h.ScenarioAsync(1);
        var cards = new FlashcardService(h.Db, h.Library, h.Clock, h.Notifier);
        await cards.SaveAsync(s.Quiz.StudySetId, null, "Mặt trước", "Mặt sau", s.Lecturer);
        s.Assignment.Form = AssignmentForm.Combined;
        s.Assignment.AllowFlashcardsAfterDue = false;
        await h.Db.SaveChangesAsync();
        h.Clock.UtcNow = s.Assignment.DueAt!.Value.AddMinutes(1);
        return (s, cards, await h.Db.Flashcards.Where(c => c.StudySetId == s.Quiz.StudySetId).Select(c => c.Id).SingleAsync());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task HaiQuyenDocLapVaServiceChanKhiTat(bool quiz, bool card)
    {
        using var h = new Harness();
        var (s, cards, cardId) = await CombinedAsync(h);
        Assert.False(s.Assignment.AllowQuizSelfReview);
        Assert.False(s.Assignment.AllowCardSelfReview);
        await h.Assignments.ChangeSelfReviewSettingsAsync(s.Assignment.Id, quiz, card, s.Lecturer);
        Assert.Equal(quiz, await h.Attempts.CanSelfReviewAsync(s.Assignment, s.Student));
        Assert.Equal(card, await h.Attempts.CanSelfReviewCardsAsync(s.Assignment, s.Student));
        var ids = await h.Attempts.SelfReviewAssignmentIdsAsync([s.Assignment], s.Student);
        Assert.Equal(quiz || card, ids.Contains(s.Assignment.Id));
        var model = await h.Attempts.GetAssignmentPageAsync(s.Assignment.Id, s.Student);
        var html = WebUtility.HtmlDecode(await Tang4ViewTests.Render("Learn", "Assignment", model));
        Assert.Equal(quiz, html.Contains("Tự ôn quiz (không tính điểm)"));
        Assert.Equal(card, html.Contains("Tự ôn thẻ ghi nhớ (không tính điểm)"));
        Assert.Equal(card, (await cards.GetSessionAsync(s.Quiz.StudySetId, s.Assignment.Id, s.Student)).CanStudy);
        if (quiz) Assert.Equal(AttemptKind.SelfReview, (await h.Attempts.StartSelfReviewAsync(s.Assignment.Id, s.Student)).Kind);
        else await Assert.ThrowsAsync<BusinessException>(() => h.Attempts.StartSelfReviewAsync(s.Assignment.Id, s.Student));
        if (card)
        {
            await cards.RateAsync(s.Quiz.StudySetId, s.Assignment.Id, cardId, FlashcardRating.Remembered, s.Student);
            Assert.True(await h.Db.FlashcardProgresses.AnyAsync(p => p.UserId == s.Student && p.ParticipantId == null));
            Assert.False(await h.Db.FlashcardProgresses.AnyAsync(p => p.UserId == s.Student && p.ParticipantId != null));
        }
        else
        {
            var error = await Assert.ThrowsAsync<BusinessException>(() => cards.RateAsync(s.Quiz.StudySetId, s.Assignment.Id, cardId, FlashcardRating.Remembered, s.Student));
            Assert.Equal("Bài không cho học thẻ sau hạn.", error.Message);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HocTheSauHanCuVanGhiTienDoBai(bool selfReview)
    {
        using var h = new Harness();
        var (s, cards, cardId) = await CombinedAsync(h);
        s.Assignment.AllowFlashcardsAfterDue = true;
        await h.Db.SaveChangesAsync();
        await h.Assignments.ChangeSelfReviewSettingsAsync(s.Assignment.Id, false, selfReview, s.Lecturer);
        Assert.False(await h.Attempts.CanSelfReviewCardsAsync(s.Assignment, s.Student));
        Assert.True((await cards.GetSessionAsync(s.Quiz.StudySetId, s.Assignment.Id, s.Student)).CanStudy);
        await cards.RateAsync(s.Quiz.StudySetId, s.Assignment.Id, cardId, FlashcardRating.Remembered, s.Student);
        var p = await h.Db.Participants.SingleAsync(p => p.AssignmentId == s.Assignment.Id && p.UserId == s.Student);
        Assert.Equal(1, (await CardCompletion.CountsAsync(h.Db, s.Assignment, p.Id)).Studied);
        Assert.False(await h.Db.FlashcardProgresses.AnyAsync(p => p.UserId == s.Student && p.ParticipantId == null));
    }

    [Fact]
    public async Task TatQuyenVanLamNotLuotCuNhungKhongTaoLuotMoi()
    {
        using var h = new Harness();
        var s = await h.ScenarioAsync(1);
        await h.Assignments.ChangeSelfReviewSettingsAsync(s.Assignment.Id, true, false, s.Lecturer);
        h.Clock.UtcNow = s.Assignment.DueAt!.Value.AddMinutes(1);
        var at = await h.Attempts.StartSelfReviewAsync(s.Assignment.Id, s.Student);
        await h.Assignments.ChangeSelfReviewSettingsAsync(s.Assignment.Id, false, false, s.Lecturer);
        Assert.False(await h.Attempts.CanSelfReviewAsync(s.Assignment, s.Student));
        Assert.Empty(await h.Attempts.SelfReviewAssignmentIdsAsync([s.Assignment], s.Student));
        Assert.Equal(at.Id, (await h.Attempts.StartSelfReviewAsync(s.Assignment.Id, s.Student)).Id);
        var html = WebUtility.HtmlDecode(await Tang4ViewTests.Render("Learn", "Assignment", await h.Attempts.GetAssignmentPageAsync(s.Assignment.Id, s.Student)));
        Assert.Contains("Tiếp tục tự ôn (không tính điểm)", html);
        Assert.DoesNotContain("Tự ôn quiz (không tính điểm)", html);
        var q = at.Questions.Single();
        var answer = QuestionSnapshot.FromJson(q.SnapshotJson).Options.Single(o => o.IsCorrect).Id;
        await h.Attempts.AnswerAsync(at.Id, q.Id, Guid.NewGuid(), [answer], s.Student, questionVersion: q.QuestionVersion);
        await h.Attempts.FinishAsync(at.Id, s.Student);
        Assert.Equal(AttemptStatus.Ended, at.Status);
        await Assert.ThrowsAsync<BusinessException>(() => h.Attempts.StartSelfReviewAsync(s.Assignment.Id, s.Student));
        Assert.Single(await h.Db.Attempts.Where(a => a.ParticipantId == at.ParticipantId).ToListAsync());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task KiemTraTuChoiLuuCongTac(bool quiz, bool card)
    {
        using var h = new Harness();
        var s = await h.ScenarioAsync();
        var a = await h.Assignments.CreateTestAsync(s.Room.Id, s.Quiz.Id, new(null, 45), s.Lecturer);
        await Assert.ThrowsAsync<BusinessException>(() => h.Assignments.ChangeSelfReviewSettingsAsync(a.Id, quiz, card, s.Lecturer));
        Assert.False(a.AllowQuizSelfReview);
        Assert.False(a.AllowCardSelfReview);
        Assert.False(await h.Db.AssignmentChanges.AnyAsync(c => c.AssignmentId == a.Id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChiNguoiQuanLyDuocLuuVaCoLichSuMau(bool outsider)
    {
        using var h = new Harness();
        var s = await h.ScenarioAsync();
        var user = outsider ? await h.UserAsync("Người khác") : s.Student;
        await Assert.ThrowsAsync<ForbiddenException>(() => h.Assignments.ChangeSelfReviewSettingsAsync(s.Assignment.Id, true, true, user));
        Assert.False(s.Assignment.AllowQuizSelfReview);
        Assert.False(s.Assignment.AllowCardSelfReview);
        await h.Assignments.ChangeSelfReviewSettingsAsync(s.Assignment.Id, true, true, s.Lecturer);
        var changes = await h.Db.AssignmentChanges.Where(c => c.AssignmentId == s.Assignment.Id).ToListAsync();
        Assert.Equal(2, changes.Count);
        Assert.All(changes, c => { Assert.Equal("False", c.OldValue); Assert.Equal("True", c.NewValue); Assert.Equal(s.Lecturer, c.ChangedById); });
        var preset = await new UserSettingPresetService(h.Db, h.Clock).AssignmentAsync(AssignmentMode.Practice, s.Lecturer);
        Assert.True(preset.AllowQuizSelfReview);
        Assert.True(preset.AllowCardSelfReview);
    }

    [Fact]
    public async Task TaoBaiLuuHaiQuyenVaMoLaiKhongPhuThuocCongTac()
    {
        using var h = new Harness();
        var s = await h.ScenarioAsync(settings: new(null, null, 3, AllowQuizSelfReview: true, AllowCardSelfReview: true));
        await h.Db.Entry(s.Assignment).ReloadAsync();
        Assert.True(s.Assignment.AllowQuizSelfReview);
        Assert.True(s.Assignment.AllowCardSelfReview);
        await h.Assignments.ChangeSelfReviewSettingsAsync(s.Assignment.Id, false, false, s.Lecturer);
        await h.Assignments.CloseNowAsync(s.Assignment.Id, s.Lecturer);
        await h.Assignments.ReopenPracticeAsync(s.Assignment.Id, null, null, s.Lecturer);
        Assert.Equal(AssignmentStatus.Open, s.Assignment.Status);
        Assert.Equal(AttemptKind.Normal, (await h.Attempts.StartAsync(s.Assignment.Id, s.Student)).Kind);
    }
}

public class CongTacTuOnPureTests
{
    [Fact]
    public void HaiCongTacTuOnMacDinhTat()
    {
        // MindSprint gộp migration Tang8_CongTacTuOn vào KhoiTao: bài giao mới mặc định tắt cả hai công tắc.
        Assert.False(new Assignment().AllowQuizSelfReview);
        Assert.False(new Assignment().AllowCardSelfReview);
    }

    [Fact]
    public void MacDinhTatVaMauCuKhongBatNgam()
    {
        Assert.False(new Assignment().AllowQuizSelfReview);
        Assert.False(new Assignment().AllowCardSelfReview);
        Assert.False(new AssignmentInput().AllowQuizSelfReview);
        Assert.False(new AssignmentInput().AllowCardSelfReview);
        var old = SettingPresetRules.Assignment("{\"AllowFlashcardsAfterDue\":true}", AssignmentMode.Practice);
        Assert.False(old.AllowQuizSelfReview);
        Assert.False(old.AllowCardSelfReview);
        var test = SettingPresetRules.Assignment("{\"AllowQuizSelfReview\":true,\"AllowCardSelfReview\":true}", AssignmentMode.Test);
        Assert.False(test.AllowQuizSelfReview);
        Assert.False(test.AllowCardSelfReview);
    }

    [Theory]
    [InlineData(AssignmentMode.Practice, true)]
    [InlineData(AssignmentMode.Test, false)]
    public async Task FormDungActionChongGiaMaoVaTuLuu(AssignmentMode mode, bool visible)
    {
        var model = new AssignmentDetailsModel { Assignment = new Assignment { Id = 123, Mode = mode }, Code = "12345678", OpenAttempts = 0, Rows = [] };
        var html = WebUtility.HtmlDecode(await Tang4ViewTests.Render("Assignments", "Details", model));
        var form = Regex.Match(html, "<form[^>]*data-autosave[^>]*>[\\s\\S]*?allowQuizSelfReview[\\s\\S]*?</form>").Value;
        Assert.Equal(visible, form.Length > 0);
        if (visible)
        {
            Assert.Contains("method=\"post\"", form);
            Assert.Contains("__RequestVerificationToken", form);
            Assert.Contains("name=\"allowCardSelfReview\"", form);
            Assert.Contains("form-switch", form);
            Assert.Contains("asp-action=\"ChangeSelfReviewSettings\" asp-route-id=\"@a.Id\"", File.ReadAllText(Path.Combine(Tang8PureTests.Root, "Views/Assignments/Details.cshtml")));
        }
        Assert.False(PlainLanguageAudit.HasJargon(form));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NhanOnLaiLayTuDanhSachService(bool available)
    {
        var a = new Assignment { Id = 1, Mode = AssignmentMode.Practice, DueAt = DateTime.UtcNow.AddDays(-1) };
        var html = WebUtility.HtmlDecode(await Tang4ViewTests.Render("Learn", "Index", new List<Assignment> { a }, new()
        {
            ["Participations"] = new List<Participant>(), ["Classes"] = new List<ClassMember>(),
            ["SelfReviewAssignmentIds"] = available ? new HashSet<int> { 1 } : new HashSet<int>()
        }));
        Assert.Equal(available, html.Contains("Ôn lại được"));
    }

    [Fact]
    public void ChuMoiVaFormTaoBaiDungQuyTac()
    {
        foreach (var path in new[] { "Views/Assignments/Create.cshtml", "Views/Assignments/Details.cshtml", "Views/Learn/Assignment.cshtml" })
        {
            var source = File.ReadAllText(Path.Combine(Tang8PureTests.Root, path));
            Assert.DoesNotContain(PlainLanguageAudit.UserTexts(path, source), PlainLanguageAudit.HasJargon);
        }
        var create = File.ReadAllText(Path.Combine(Tang8PureTests.Root, "Views/Assignments/Create.cshtml"));
        Assert.Matches("data-practice-field>[\\s\\S]*?name=\"AllowQuizSelfReview\"[\\s\\S]*?name=\"AllowCardSelfReview\"", create);
    }
}
