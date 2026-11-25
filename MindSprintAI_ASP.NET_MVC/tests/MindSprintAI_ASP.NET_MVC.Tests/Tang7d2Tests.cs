using System.Text.Json;
using System.Reflection;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using MindSprintAI_ASP.NET_MVC.Controllers;
using Microsoft.EntityFrameworkCore;
using MindSprintAI_ASP.NET_MVC.Data;
using MindSprintAI_ASP.NET_MVC.Data.Entities;
using MindSprintAI_ASP.NET_MVC.Services;

namespace MindSprintAI_ASP.NET_MVC.Tests;

public class Tang7d2PureTests
{
    [Fact]
    public async Task D5cMacDinhMigrationVaFormRiengCoChongGiaMao()
    {
        Assert.True(new ApplicationUser().SrsBellEnabled);
        foreach (var enabled in new[] { true, false })
        {
            var html = await Tang4ViewTests.Render("Account", "Index", new ApplicationUser { SrsBellEnabled = enabled });
            var form = Assert.Single(Regex.Matches(html, "<form\\b[^>]*>[\\s\\S]*?</form>").Select(m => m.Value), f => f.Contains("data-action=\"SrsBell\""));
            Assert.Equal(enabled, form.Contains("checked")); Assert.Contains("__RequestVerificationToken", form);
            Assert.Contains("Báo thẻ đến hạn ôn ở chuông", WebUtility.HtmlDecode(form));
            var action = typeof(AccountController).GetMethod("SrsBell")!;
            Assert.True(action.IsDefined(typeof(HttpPostAttribute)));
            Assert.Equal("enabled", action.GetParameters()[0].Name);
        }
        var session = new CardSession(1, 2, "Bộ SRS", [], 0, 0, 0, null, true, false,
            Mode: FlashcardMode.SpacedRepetition, DueCount: 3);
        var study = WebUtility.HtmlDecode(await Tang4ViewTests.Render("Flashcards", "Study", session));
        Assert.Contains("Hôm nay có 3 thẻ đến hạn ôn", study);
    }

    [Fact]
    public void D5cTruyVanGopDichSangSqlVaCoGioVn()
    {
        using var db = new AppDbContext(AppDbContextFactory.BuildOptions(DatabaseFixture.ConnectionString));
        var clock = new TestClock(new DateTime(2026, 11, 8, 0, 0, 0, DateTimeKind.Utc));
        var service = new ReminderService(db, clock, null!, null!, null!, null!, null!, null!);
        var query = (IQueryable)typeof(ReminderService).GetMethod("Candidates", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(service, [ReminderKind.SrsDue])!;
        var sql = query.ToQueryString();
        Assert.Contains("UNION ALL", sql); Assert.Contains("GROUP BY", sql);
        Assert.Contains("SrsBellEnabled", sql); Assert.DoesNotContain("EmailRemindersEnabled", sql);
        Assert.Contains("SrsDueDate", sql); Assert.Contains("AccountLocks", sql);
    }

    private static Attempt Attempt(int number, decimal score, CriticalLabel label, bool failedQuestion = false) => new()
    {
        AttemptNumber = number, Status = AttemptStatus.Ended, FirstScore = score, MaxScore = 10,
        CriticalLabel = label, FailedCriticalQuestion = failedQuestion
    };

    [Theory]
    [InlineData(CriticalLabel.Failed, CriticalLabel.None, 7, 7, CriticalLabel.None)]
    [InlineData(CriticalLabel.Failed, CriticalLabel.Failed, 6, 9, CriticalLabel.Failed)]
    [InlineData(CriticalLabel.Pending, CriticalLabel.None, 7, 7, CriticalLabel.None)]
    [InlineData(CriticalLabel.Pending, CriticalLabel.Failed, 6, 6, CriticalLabel.Failed)]
    [InlineData(CriticalLabel.Pending, CriticalLabel.Pending, 7, 9, CriticalLabel.Pending)]
    public void E6bCaoNhatUuTienDaChamKhongLiet(CriticalLabel firstLabel, CriticalLabel secondLabel, int secondScore, int expectedScore, CriticalLabel expected)
    {
        var first = Attempt(1, 9, firstLabel); var second = Attempt(2, secondScore, secondLabel);
        var selected = GradebookService.Select([first, second], ScoreSelection.Highest)!;
        Assert.Equal(expected, selected.CriticalLabel);
        Assert.Equal(expectedScore, selected.Raw);
        Assert.Equal(firstLabel, GradebookService.Select([first, second], ScoreSelection.First)!.CriticalLabel);
        Assert.Equal(9, GradebookService.Select([first, second], ScoreSelection.First)!.Raw);
        Assert.Equal(secondLabel, GradebookService.Select([first, second], ScoreSelection.Latest)!.CriticalLabel);
        Assert.Equal(secondScore, GradebookService.Select([first, second], ScoreSelection.Latest)!.Raw);
    }

    [Theory]
    [InlineData(true, false, null, false, CriticalLabel.None)]
    [InlineData(true, true, null, false, CriticalLabel.Failed)]
    [InlineData(false, false, 8, false, CriticalLabel.Failed)]
    [InlineData(true, true, 8, true, CriticalLabel.Pending)]
    public void E6cTrungBinhMoiLuotVaNhan(bool firstFailed, bool secondFailed, int? threshold, bool pending, CriticalLabel expected)
    {
        var a = new Assignment { Mode = AssignmentMode.Practice, ScoreSelection = ScoreSelection.Average,
            ScoreScale = ScoreScale.Ten, CriticalThreshold = threshold, CriticalQuestionsEnabled = true };
        var first = Attempt(1, 9, firstFailed ? CriticalLabel.Failed : CriticalLabel.None, firstFailed);
        var second = Attempt(2, 7, pending ? CriticalLabel.Pending : secondFailed ? CriticalLabel.Failed : CriticalLabel.None, secondFailed);
        var score = GradebookService.SelectForAssignment([first, second], a)!;
        Assert.Equal(8, score.Raw); Assert.Equal(expected, score.CriticalLabel);
        if (expected == CriticalLabel.Failed) Assert.False(string.IsNullOrWhiteSpace(score.CriticalReasons));
    }

    [Fact]
    public void E6aLietKhongDatVaKiemTraChiLayLuotMoiNhat()
    {
        var a = new Assignment { Mode = AssignmentMode.Test, PassMark = 5, ScoreSelection = ScoreSelection.Highest };
        var first = Attempt(1, 9, CriticalLabel.None); var last = Attempt(2, 7, CriticalLabel.Failed);
        var selected = GradebookService.SelectForAssignment([first, last], a)!;
        Assert.Equal(7, selected.Raw); Assert.Equal(CriticalLabel.Failed, selected.CriticalLabel);
        Assert.False(GradebookService.Passed(selected, a));
    }

    [Fact]
    public void PresetKhongLuuQuizHayCauVaBoQuaTruongJsonCu()
    {
        var a = new Assignment { QuizId = 77, CriticalQuestionIdsJson = "[8,9]", CriticalQuestionsEnabled = true,
            FlashcardMode = FlashcardMode.SpacedRepetition, CriticalThreshold = 4 };
        var json = JsonSerializer.Serialize(AssignmentPreset.From(a));
        Assert.DoesNotContain("QuizId", json); Assert.DoesNotContain("QuestionIds", json);
        var input = SettingPresetRules.Assignment(json, AssignmentMode.Practice).ToInput(1, AssignmentMode.Practice);
        Assert.Equal(0, input.QuizId); Assert.Empty(input.CriticalQuestionIds);
        var legacy = SettingPresetRules.Assignment("{\"CriticalQuizId\":77,\"CriticalQuestionIds\":[8,9],\"CriticalQuestionsEnabled\":true}", AssignmentMode.Practice)
            .ToInput(1, AssignmentMode.Practice);
        Assert.Equal(0, legacy.QuizId); Assert.Empty(legacy.CriticalQuestionIds); Assert.True(legacy.CriticalQuestionsEnabled);
    }
}

[Collection("db")]
public class Tang7d2Tests
{
    private static FlashcardService Cards(Harness h) => new(h.Db, h.Library, h.Clock, h.Notifier);

    private static async Task<(ApplicationUser User, StudySet Set)> Personal(Harness h, string? userId = null)
    {
        var user = (await h.Db.Users.FindAsync(userId ?? await h.UserAsync("SRS 7d2")))!;
        user.EmailConfirmed = true;
        var set = await h.Library.CreateStudySetAsync("Bộ SRS", user.Id);
        await Cards(h).SaveAsync(set.Id, null, "Trước", "Sau", user.Id);
        await Cards(h).ChangeModeAsync(set.Id, null, FlashcardMode.SpacedRepetition, user.Id);
        return (user, set);
    }

    [Fact]
    public async Task D5bThuVienVaTrangBoTheDungMocNgayVn()
    {
        using var h = new Harness(); h.Clock.UtcNow = new(2026, 11, 8, 16, 59, 59, DateTimeKind.Utc);
        var (user, set) = await Personal(h); var cards = Cards(h);
        var all = await cards.GetSessionAsync(set.Id, null, user.Id);
        await cards.RateAsync(set.Id, null, all.Cards[0].Id, FlashcardRating.NotRemembered, user.Id);
        Assert.Equal(0, (await h.Library.SrsDueCountsAsync(user.Id)).Total);
        Assert.Equal(0, await cards.PersonalDueCountAsync(set.Id, user.Id));
        Assert.Equal(0, (await cards.GetSessionAsync(set.Id, null, user.Id)).DueCount);
        h.Clock.UtcNow = h.Clock.UtcNow.AddSeconds(1);
        Assert.Equal(1, (await h.Library.SrsDueCountsAsync(user.Id)).Sets[set.Id]);
        Assert.Equal(1, (await h.Library.SrsDueCountsAsync(user.Id)).Total);
        Assert.Equal(1, await cards.PersonalDueCountAsync(set.Id, user.Id));
        Assert.Equal(1, (await cards.GetSessionAsync(set.Id, null, user.Id)).DueCount);
        await cards.ChangeModeAsync(set.Id, null, FlashcardMode.Traditional, user.Id);
        Assert.Equal(0, (await h.Library.SrsDueCountsAsync(user.Id)).Total);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task D5cMotChuongMoiNgayVnVaKhongEmail(bool emailEnabled)
    {
        using var h = new Harness(); h.Clock.UtcNow = new(2026, 11, 8, 0, 0, 0, DateTimeKind.Utc);
        var (user, _) = await Personal(h); user.EmailRemindersEnabled = emailEnabled; await h.Db.SaveChangesAsync();
        var services = new Tang7cServices(h);
        await services.Reminders.RunAsync(); await services.Reminders.RunAsync();
        var reminder = Assert.Single(await h.Db.Reminders.Where(r => r.UserId == user.Id).ToListAsync());
        Assert.Equal(ReminderKind.SrsDue, reminder.Kind); Assert.Equal(20261108, reminder.TargetId); Assert.NotNull(reminder.EmailedAt);
        var notice = NotificationService.Payload(Assert.Single(await h.Db.Notifications.Where(n => n.UserId == user.Id).ToListAsync()));
        Assert.Equal("Hôm nay bạn có 1 thẻ đến hạn ôn", notice.Text); Assert.Equal("/StudySets", notice.Url);
        // Thử cả dòng chuông chưa có EmailedAt: digest vẫn phải loại kind này.
        reminder.EmailedAt = null; await h.Db.SaveChangesAsync(); await services.Reminders.RunAsync();
        Assert.DoesNotContain(services.Email.Sent, e => e.To == user.Email);
        Assert.False(await h.Db.ReminderDigests.AnyAsync(d => d.UserId == user.Id));
        h.Clock.UtcNow = new(2026, 11, 8, 17, 0, 0, DateTimeKind.Utc); // 00:00 ngày 9 VN, chưa đến 07:00.
        await services.Reminders.RunAsync();
        Assert.Single(await h.Db.Reminders.Where(r => r.UserId == user.Id).ToListAsync());
        h.Clock.UtcNow = new(2026, 11, 9, 0, 0, 0, DateTimeKind.Utc);
        await services.Reminders.RunAsync();
        Assert.Equal(new long[] { 20261108, 20261109 }, await h.Db.Reminders.Where(r => r.UserId == user.Id).OrderBy(r => r.TargetId).Select(r => r.TargetId).ToArrayAsync());
        Assert.Equal(2, await h.Db.Notifications.CountAsync(n => n.UserId == user.Id));
        Assert.DoesNotContain(services.Email.Sent, e => e.To == user.Email);
    }

    [Fact]
    public async Task D5cKhongTruocBayGioKhongKhiKhongCoTheDenHan()
    {
        using var h = new Harness(); h.Clock.UtcNow = new(2026, 11, 7, 23, 59, 59, DateTimeKind.Utc);
        var (user, set) = await Personal(h); var services = new Tang7cServices(h);
        await services.Reminders.RunAsync();
        Assert.False(await h.Db.Reminders.AnyAsync(r => r.UserId == user.Id));
        var card = Assert.Single((await Cards(h).GetSessionAsync(set.Id, null, user.Id)).Cards);
        await Cards(h).RateAsync(set.Id, null, card.Id, FlashcardRating.Remembered, user.Id);
        h.Clock.UtcNow = h.Clock.UtcNow.AddSeconds(1); await services.Reminders.RunAsync();
        Assert.False(await h.Db.Reminders.AnyAsync(r => r.UserId == user.Id));
        Assert.False(await h.Db.Notifications.AnyAsync(n => n.UserId == user.Id));
    }

    [Theory]
    [InlineData("toggle")]
    [InlineData("guest")]
    [InlineData("identity-lock")]
    [InlineData("account-lock")]
    public async Task D5cBoQuaTatChuongKhachVaTaiKhoanKhoa(string reason)
    {
        using var h = new Harness(); var (user, _) = await Personal(h); var services = new Tang7cServices(h);
        if (reason == "toggle") await services.Reminders.SetSrsBellEnabledAsync(user.Id, false);
        if (reason == "guest") user.IsGuest = true;
        if (reason == "identity-lock") user.LockoutEnd = h.Clock.GetUtcNow().AddDays(1);
        if (reason == "account-lock") h.Db.AccountLocks.Add(new() { UserId = user.Id, Kind = AccountLockKind.Violation, StartsAt = h.Clock.UtcNow });
        await h.Db.SaveChangesAsync(); await services.Reminders.RunAsync();
        Assert.False(await h.Db.Reminders.AnyAsync(r => r.UserId == user.Id));
        Assert.False(await h.Db.Notifications.AnyAsync(n => n.UserId == user.Id));
        Assert.DoesNotContain(services.Email.Sent, e => e.To == user.Email);
    }

    [Fact]
    public async Task D5bD5cGopMoiBoCaNhanVaBaiGiaoDungCheDo()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(); var cards = Cards(h);
        await cards.SaveAsync(s.Quiz.StudySetId, null, "A", "B", s.Lecturer);
        var a = await h.Assignments.CreatePracticeAsync(s.Room.Id, s.Quiz.Id,
            new(null, h.Clock.UtcNow.AddDays(2), null, Form: AssignmentForm.Combined, FlashcardMode: FlashcardMode.LearnerChoice), s.Lecturer);
        await cards.ChangeModeAsync(s.Quiz.StudySetId, a.Id, FlashcardMode.SpacedRepetition, s.Student);
        var (user, _) = await Personal(h, s.Student); await Personal(h, s.Student);
        var page = await h.Attempts.GetAssignmentPageAsync(a.Id, s.Student);
        Assert.Equal(1, page.Cards!.DueCount);
        Assert.Equal(1, (await cards.GetSessionAsync(s.Quiz.StudySetId, a.Id, s.Student)).DueCount);
        Assert.Equal(2, (await h.Library.SrsDueCountsAsync(s.Student)).Total);
        var services = new Tang7cServices(h); await services.Reminders.RunAsync();
        var notice = NotificationService.Payload(Assert.Single(await h.Db.Notifications.Where(n => n.UserId == user.Id && n.Type == NotificationType.Reminder).ToListAsync()));
        Assert.Equal("Hôm nay bạn có 3 thẻ đến hạn ôn", notice.Text);
        // Bài đóng và bài truyền thống không còn trong tổng SRS.
        a.Status = AssignmentStatus.Closed; await h.Db.SaveChangesAsync();
        Assert.Equal(0, await new SrsDueService(h.Db, h.Clock).AssignmentCards().CountAsync(c => c.UserId == user.Id));
    }

    [Fact]
    public async Task D5cTatChuongKhongTatEmailVaDigestKhongChuaSrs()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync();
        var (user, _) = await Personal(h, s.Student);
        s.Assignment.DueAt = h.Clock.UtcNow.AddHours(20); await h.Db.SaveChangesAsync();
        var services = new Tang7cServices(h);
        await services.Reminders.SetSrsBellEnabledAsync(user.Id, false);
        await services.Reminders.RunAsync();
        var email = Assert.Single(services.Email.Sent, e => e.To == user.Email);
        Assert.Contains("sắp đến hạn", WebUtility.HtmlDecode(email.Body));
        Assert.DoesNotContain("thẻ đến hạn ôn", WebUtility.HtmlDecode(email.Body));
        Assert.False(await h.Db.Reminders.AnyAsync(r => r.UserId == user.Id && r.Kind == ReminderKind.SrsDue));
        Assert.True((await h.Db.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id)).EmailRemindersEnabled);
    }

    [Theory]
    [InlineData(FlashcardMode.SpacedRepetition, FlashcardMode.Traditional, 1)]
    [InlineData(FlashcardMode.Traditional, FlashcardMode.SpacedRepetition, 0)]
    [InlineData(FlashcardMode.LearnerChoice, FlashcardMode.SpacedRepetition, 1)]
    [InlineData(FlashcardMode.LearnerChoice, FlashcardMode.Traditional, 0)]
    public async Task D5cBaiGiaoTheoCheDoThucTeVaQuyenHoc(FlashcardMode assignmentMode, FlashcardMode learnerMode, int expected)
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(); var cards = Cards(h);
        await cards.SaveAsync(s.Quiz.StudySetId, null, "A", "B", s.Lecturer);
        var a = await h.Assignments.CreatePracticeAsync(s.Room.Id, s.Quiz.Id,
            new(null, h.Clock.UtcNow.AddDays(2), null, Form: AssignmentForm.Combined, FlashcardMode: assignmentMode), s.Lecturer);
        await cards.GetSessionAsync(s.Quiz.StudySetId, a.Id, s.Student);
        var p = await h.Db.Participants.SingleAsync(p => p.AssignmentId == a.Id && p.UserId == s.Student);
        p.FlashcardMode = learnerMode; await h.Db.SaveChangesAsync();
        var due = new SrsDueService(h.Db, h.Clock);
        Assert.Equal(expected, await due.AssignmentCards().CountAsync(c => c.UserId == s.Student));
        p.ApprovalStatus = ApprovalStatus.Pending; await h.Db.SaveChangesAsync();
        Assert.Equal(0, await due.AssignmentCards().CountAsync(c => c.UserId == s.Student));
        p.ApprovalStatus = ApprovalStatus.Approved;
        a.DueAt = h.Clock.UtcNow; await h.Db.SaveChangesAsync();
        Assert.Equal(0, await due.AssignmentCards().CountAsync(c => c.UserId == s.Student));
    }

    [Fact]
    public async Task D5cXuLyHonMotTrangTrongCungLuotWorker()
    {
        using var h = new Harness(); var users = new List<string>();
        for (var i = 0; i < 201; i++)
        {
            var user = new ApplicationUser { UserName = "srs-page-" + Guid.NewGuid().ToString("N") };
            users.Add(user.Id); h.Db.Users.Add(user);
            var set = new StudySet { OwnerId = user.Id, Title = "Bộ SRS", FlashcardMode = FlashcardMode.SpacedRepetition };
            h.Db.StudySets.Add(set);
            h.Db.Flashcards.Add(new() { StudySet = set, Front = "A", Back = "B" });
        }
        await h.Db.SaveChangesAsync(); var services = new Tang7cServices(h);
        await services.Reminders.RunAsync(); await services.Reminders.RunAsync();
        Assert.Equal(201, await h.Db.Reminders.CountAsync(r => users.Contains(r.UserId) && r.Kind == ReminderKind.SrsDue));
        Assert.Equal(201, await h.Db.Notifications.CountAsync(n => users.Contains(n.UserId)));
    }
}
