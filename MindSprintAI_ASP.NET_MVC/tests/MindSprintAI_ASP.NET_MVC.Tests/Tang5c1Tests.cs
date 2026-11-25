using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MindSprintAI_ASP.NET_MVC.Data.Entities;
using MindSprintAI_ASP.NET_MVC.Services;

namespace MindSprintAI_ASP.NET_MVC.Tests;

public class Tang5c1RuleTests
{
    [Fact]
    public void KhaoSatSauChuyenVanDemDungCauVaLuaChonCu()
    {
        var at = new Attempt { ParticipantId = 1, Status = AttemptStatus.Ended };
        var snapshot = new QuestionSnapshot(1, 0, QuestionType.SurveySingle, "Khảo sát", null, 0,
            [new(1, 0, "A", false), new(2, 1, "B", false)]);
        at.Questions.Add(new() { QuestionId = 1, SnapshotJson = snapshot.ToJson(),
            Submissions = [new() { IsFinal = true, PayloadJson = "{\"Selected\":[2]}" }] });
        var clone = snapshot with { QuestionId = 10, Options = [new(10, 0, "A", false), new(20, 1, "B", false)] };
        var result = Assert.Single(SurveyAggregation.Build([at], [clone]));
        Assert.Equal(1, result.Respondents); Assert.Equal(0, result.Options[0].Count); Assert.Equal(1, result.Options[1].Count);
    }

    [Fact]
    public async Task ChamLaiSauSuaBanSaoDungIdTrongBanChupMoi()
    {
        var at = Tang5aPureTests.MakeAttempt(); var q = at.Questions[0];
        var clone = QuestionSnapshot.FromJson(q.SnapshotJson) with { QuestionId = 90 };
        QuestionEditRules.Apply(at, q, clone, 2, QuestionEditKind.Content);
        at.Participant.Assignment.Regrades.Add(new() { QuestionId = 90, Method = RegradeMethod.FullCredit, Reason = "Cham lai ban sao" });
        RegradeRules.Recompute(at);
        Assert.Equal(2, at.FirstScore);
        var html = await Tang4ViewTests.Render("Assignments", "Attempt", at, new()
        {
            ["Items"] = at.Questions.Select(x => (x, QuestionSnapshot.FromJson(x.SnapshotJson))).ToList(),
            ["RegradeQuestions"] = new List<RegradeQuestion> { new(90, 1, clone.Prompt, true, clone.Options) }
        });
        Assert.Contains("Cham lai ban sao", html);
        Assert.Contains("value=\"90\"", html);
    }

    [Fact]
    public void ChiMotChuyenLopDangChoMoiLop()
    {
        // MindSprint gộp migration Tang5c1 vào KhoiTao: kiểm index trên model.
        using var db = new Data.AppDbContext(Data.AppDbContextFactory.BuildOptions(DatabaseFixture.ConnectionString));
        var index = Assert.Single(db.Model.FindEntityType(typeof(ClassTransfer))!.GetIndexes(), i => i.GetDatabaseName() == "IX_ClassTransfers_OnePendingPerClass");
        Assert.True(index.IsUnique); Assert.Equal("[Status] = 1", index.GetFilter());
    }

    [Fact]
    public void SuaNhoSauChuyenGiuIdLuaChonVaDiemCu()
    {
        var at = Tang5aPureTests.MakeAttempt(); var q = at.Questions[0]; var before = q.SnapshotJson;
        var snap = QuestionSnapshot.FromJson(before);
        var clone = snap with { QuestionId = 90, Prompt = "Chữ mới", Options = snap.Options.Select(o => o with { Id = o.Id + 100, Text = "Chữ lựa chọn mới" }).ToList() };
        QuestionEditRules.Apply(at, q, clone, 2, QuestionEditKind.Minor);
        var after = QuestionSnapshot.FromJson(q.SnapshotJson);
        Assert.Equal(snap.QuestionId, after.QuestionId); Assert.Equal(snap.Options.Select(o => o.Id), after.Options.Select(o => o.Id));
        Assert.Equal("Chữ mới", after.Prompt);
    }

    [Fact]
    public async Task SignalRGuiDungUserVaKhongGuiChu()
    {
        var hub = new TestLearnHub();
        await new SignalRLearnNotifier(hub).NotificationAsync("nguoi-nhan", new(2));
        Assert.Equal(("notification", "user:nguoi-nhan"), Assert.Single(hub.Sent));
        var args = Assert.Single(hub.Payloads);
        Assert.Equal(new NotificationNotice(2), Assert.Single(args));
        Assert.Equal("{\"unread\":2}", JsonSerializer.Serialize(args[0], new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    [Fact]
    public async Task RazorMaHoaChuVaTatCaThaoTacThongBaoDungPost()
    {
        var payload = new NotificationPayload("<script>alert('tên')</script>", "/Classrooms/Details/1");
        var row = new Notification { Id = 1, PayloadJson = JsonSerializer.Serialize(payload), CreatedAt = DateTime.UtcNow };
        var html = await Tang4ViewTests.Render("Notifications", "Index", new NotificationPage([row], 1, true));
        Assert.DoesNotContain("<script>alert", html); Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("method=\"post\"", html); Assert.Contains("__RequestVerificationToken", html);
        Assert.Equal(3, System.Text.RegularExpressions.Regex.Matches(html, "method=\"post\"").Count);
        Assert.Contains("Trang sau", html);
    }

    [Fact]
    public void PushChiChuaSoChuaDoc()
    {
        var json = JsonSerializer.Serialize(new NotificationNotice(3), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal("{\"unread\":3}", json);
    }

    [Theory]
    [InlineData("https://outside.test")]
    [InlineData("//outside.test")]
    [InlineData("/\\outside.test")]
    [InlineData("/a\n")]
    public void KhongChuyenHuongRaNgoai(string url) =>
        Assert.Throws<BusinessException>(() => NotificationService.Validate(new("Thông báo", url)));
}

[Collection("db")]
public class Tang5c1Tests
{
    private static NotificationService Notices(Harness h) => new(h.Db, h.Clock, h.Notifier);
    private static StudySetCloneService Clones(Harness h) => new(h.Db, h.Clock);
    private static ClassTransferService Transfers(Harness h) => new(h.Db, h.Classrooms, Clones(h), Notices(h), h.Clock, h.Invitations);
    private static async Task<(string Id, string Email)> Colleague(Harness h, string name = "GV mới")
    {
        var id = await h.UserAsync(name); var user = (await h.Db.Users.FindAsync(id))!;
        user.EmailConfirmed = true; await h.Db.SaveChangesAsync();
        return (id, user.Email!);
    }

    [Fact]
    public async Task GvPhuGiaoDongBatDauXoaHocVienChamLaiNhungKhongSuaNoiDung()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(2); var assistant = await Colleague(h);
        await h.InviteAndAcceptAssistantAsync(s.Room.Id, assistant.Email, s.Lecturer);
        var a = await h.Assignments.CreatePracticeAsync(s.Room.Id, s.Quiz.Id, new(null, null, 2), assistant.Id);
        await h.Assignments.CloseNowAsync(a.Id, assistant.Id);
        var test = await h.Assignments.CreateTestAsync(s.Room.Id, s.Quiz.Id, new(null, 30), assistant.Id);
        await h.Assignments.StartTestAsync(test.Id, assistant.Id);
        Assert.NotNull(test.TestStartedAt);
        var q = s.Quiz.Questions[0];
        await h.Regrades.ApplyAsync(s.Assignment.Id, new(q.Id, RegradeMethod.FullCredit, "Chấp nhận", Guid.NewGuid(), []), assistant.Id);
        await h.Classrooms.RemoveMemberAsync((await h.Db.ClassMembers.SingleAsync(m => m.ClassroomId == s.Room.Id && m.UserId == s.Student)).Id, assistant.Id);
        await Assert.ThrowsAsync<ForbiddenException>(() => h.Library.SaveChoiceQuestionAsync(s.Quiz.Id, q.Id, q.Version, Harness.Single("Sửa lén"), assistant.Id));
    }

    [Fact]
    public async Task GvPhuKhongDuocChuyenXoaLopThemBotGvPhuVaBiGoMatQuyen()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(); var assistant = await Colleague(h); var receiver = await Colleague(h);
        await h.InviteAndAcceptAssistantAsync(s.Room.Id, assistant.Email, s.Lecturer);
        var member = await h.Db.ClassMembers.SingleAsync(m => m.ClassroomId == s.Room.Id && m.UserId == assistant.Id);
        await Assert.ThrowsAsync<ForbiddenException>(() => Transfers(h).OfferAsync(s.Room.Id, receiver.Email, true, assistant.Id));
        await Assert.ThrowsAsync<ForbiddenException>(() => h.Classrooms.DeleteAsync(s.Room.Id, assistant.Id));
        await Assert.ThrowsAsync<ForbiddenException>(() => h.Classrooms.AddAssistantAsync(s.Room.Id, receiver.Email, assistant.Id));
        await Assert.ThrowsAsync<ForbiddenException>(() => h.Classrooms.RemoveAssistantAsync(s.Room.Id, member.Id, assistant.Id));
        await h.Classrooms.RemoveAssistantAsync(s.Room.Id, member.Id, s.Lecturer);
        Assert.False(await h.Classrooms.IsLecturerAsync(s.Room.Id, assistant.Id));
        await Assert.ThrowsAsync<ForbiddenException>(() => h.Assignments.CloseNowAsync(s.Assignment.Id, assistant.Id));
        await Assert.ThrowsAsync<ForbiddenException>(() => h.Regrades.ApplyAsync(s.Assignment.Id,
            new(s.Quiz.Questions[0].Id, RegradeMethod.FullCredit, "Lén", Guid.NewGuid(), []), assistant.Id));
        await h.InviteAndAcceptAssistantAsync(s.Room.Id, assistant.Email, s.Lecturer);
        Assert.True(await h.Classrooms.IsLecturerAsync(s.Room.Id, assistant.Id));
        Assert.Equal(1, await h.Db.ClassMembers.CountAsync(m => m.ClassroomId == s.Room.Id && m.UserId == assistant.Id));
    }

    [Fact]
    public async Task EmailKhongTonTaiHoacChuaXacMinhNhanLoiMoiNhungChuaGhiNotification()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(); var id = await h.UserAsync("Chưa xác minh");
        foreach (var email in new[] { "unknown@test.vn", (await h.Db.Users.FindAsync(id))!.Email! })
        {
            // (7c/L3) cả hai được lưu lời mời; email chưa xác minh chưa thấy chuông.
            await h.Classrooms.AddAssistantAsync(s.Room.Id, email, s.Lecturer);
            Assert.True(await h.Db.Invitations.AnyAsync(i => i.ClassroomId == s.Room.Id && i.Email == email && i.Status == InvitationStatus.Pending));
        }
        Assert.False(await h.Db.Notifications.AnyAsync(n => n.UserId == id));
    }

    [Fact]
    public async Task ChoNhanVaTuChoiKhongDoiLopChiNguoiNhanDuocQuyetDinh()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(); var target = await Colleague(h); var outsider = await Colleague(h);
        var row = await Transfers(h).OfferAsync(s.Room.Id, target.Email, false, s.Lecturer);
        Assert.True(await h.Classrooms.IsMainLecturerAsync(s.Room.Id, s.Lecturer));
        Assert.Equal(s.Quiz.Id, (await h.Db.Assignments.AsNoTracking().SingleAsync(a => a.Id == s.Assignment.Id)).QuizId);
        Assert.False(await h.Db.StudySets.AnyAsync(x => x.OwnerId == target.Id));
        await Assert.ThrowsAsync<ForbiddenException>(() => Transfers(h).DecideAsync(row.Id, true, s.Lecturer));
        await Assert.ThrowsAsync<ForbiddenException>(() => Transfers(h).DecideAsync(row.Id, false, outsider.Id));
        await Transfers(h).DecideAsync(row.Id, false, target.Id);
        Assert.True(await h.Classrooms.IsMainLecturerAsync(s.Room.Id, s.Lecturer));
        Assert.Equal(ClassTransferStatus.Declined, (await h.Db.ClassTransfers.AsNoTracking().SingleAsync(t => t.Id == row.Id)).Status);
        Assert.False(await h.Db.StudySets.AnyAsync(x => x.OwnerId == target.Id));
        Assert.Equal(2, await h.Db.Notifications.CountAsync(n => n.UserId == target.Id));
        Assert.Equal(2, await h.Db.Notifications.CountAsync(n => n.UserId == s.Lecturer));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NhanLopNhanBanGiuDiemVaReview(bool stays)
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(2); var target = await Colleague(h);
        var at = await h.Attempts.StartAsync(s.Assignment.Id, s.Student); var question = at.Questions[0];
        await h.Attempts.AnswerAsync(at.Id, question.Id, Guid.NewGuid(),
            [QuestionSnapshot.FromJson(question.SnapshotJson).Options.Single(o => o.IsCorrect).Id], s.Student);
        await h.Attempts.FinishAsync(at.Id, s.Student);
        var before = await h.Attempts.GetPlayQuestionsAsync(at, s.Student); var score = at.FirstScore;
        var snapshots = at.Questions.Select(q => q.SnapshotJson).ToArray();
        var originalId = s.Quiz.StudySetId;
        var row = await Transfers(h).OfferAsync(s.Room.Id, target.Email, stays, s.Lecturer);
        await Transfers(h).DecideAsync(row.Id, true, target.Id);
        Assert.True(await h.Classrooms.IsMainLecturerAsync(s.Room.Id, target.Id));
        var old = await h.Db.ClassMembers.AsNoTracking().SingleAsync(m => m.ClassroomId == s.Room.Id && m.UserId == s.Lecturer);
        Assert.Equal(ClassRole.AssistantLecturer, old.Role);
        Assert.Equal(stays ? ClassMemberStatus.Active : ClassMemberStatus.Left, old.Status);
        Assert.NotEqual(s.Quiz.Id, s.Assignment.QuizId);
        var copy = await h.Db.StudySets.SingleAsync(x => x.Id == s.Assignment.StudySetId);
        Assert.Equal(originalId, copy.ClonedFromId); Assert.Equal(target.Id, copy.OwnerId);
        Assert.Equal(s.Lecturer, (await h.Db.StudySets.FindAsync(originalId))!.OwnerId);
        Assert.Equal(score, (await h.Attempts.GetForLearnerAsync(at.Id, s.Student)).FirstScore);
        Assert.Equal(snapshots, at.Questions.Select(q => q.SnapshotJson));
        var after = await h.Attempts.GetPlayQuestionsAsync(at, s.Student);
        Assert.Equal(JsonSerializer.Serialize(before), JsonSerializer.Serialize(after));
    }

    [Fact]
    public async Task TestDangThiGiuHanVaTraLoiTiepDuocSauChuyen()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(2); var target = await Colleague(h);
        var test = await h.Assignments.CreateTestAsync(s.Room.Id, s.Quiz.Id, new(null, 60), s.Lecturer);
        await h.Assignments.StartTestAsync(test.Id, s.Lecturer);
        var at = await h.Attempts.StartAsync(test.Id, s.Student); var deadline = at.DeadlineAt; var end = test.TestEndsAt;
        var q = at.Questions[0]; var snapshot = q.SnapshotJson;
        var row = await Transfers(h).OfferAsync(s.Room.Id, target.Email, true, s.Lecturer);
        await Transfers(h).DecideAsync(row.Id, true, target.Id);
        await h.Attempts.AnswerAsync(at.Id, q.Id, Guid.NewGuid(),
            [QuestionSnapshot.FromJson(snapshot).Options.Single(o => o.IsCorrect).Id], s.Student);
        Assert.Equal(deadline, at.DeadlineAt); Assert.Equal(end, test.TestEndsAt);
        Assert.Equal(snapshot, q.SnapshotJson); Assert.True(AttemptRules.IsOpen(at)); Assert.Equal(1, q.AnswerCount);
    }

    [Fact]
    public async Task MotDeNghiChoNhanVaHocVienBiTuChoiHuyThiGuiLaiDuoc()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(); var target = await Colleague(h);
        var student = (await h.Db.Users.FindAsync(s.Student))!; student.EmailConfirmed = true; await h.Db.SaveChangesAsync();
        var error = await Assert.ThrowsAsync<BusinessException>(() => Transfers(h).OfferAsync(s.Room.Id, student.Email!, true, s.Lecturer));
        Assert.Contains("học viên", error.Message);
        var row = await Transfers(h).OfferAsync(s.Room.Id, target.Email, true, s.Lecturer);
        await Assert.ThrowsAsync<BusinessException>(() => Transfers(h).OfferAsync(s.Room.Id, target.Email, true, s.Lecturer));
        await Assert.ThrowsAsync<ForbiddenException>(() => Transfers(h).CancelAsync(row.Id, target.Id));
        await Transfers(h).CancelAsync(row.Id, s.Lecturer);
        var again = await Transfers(h).OfferAsync(s.Room.Id, target.Email, false, s.Lecturer);
        Assert.NotEqual(row.Id, again.Id);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task NhanBanTheoTickNhungQuizLuonNhapKhongChepBaiGiao(bool cards, bool matching)
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(); var set = s.Quiz.StudySetId;
        var excluded = await h.Library.CreateQuizAsync(set, "Không chọn", s.Lecturer);
        h.Db.Flashcards.Add(new() { StudySetId = set, Front = "Trước", Back = "Sau" });
        var q = s.Quiz.Questions[0]; q.CaseSensitive = q.AcceptNoDiacritics = q.IgnorePunctuation = q.AcceptAnyBlankOrder = true;
        q.NumberTolerance = .1m; q.BlankScoring = BlankScoring.PerBlank;
        await h.Db.SaveChangesAsync();
        var assignmentCount = await h.Db.Assignments.CountAsync();
        var copy = await Clones(h).CloneAsync(set, new([s.Quiz.Id], cards, matching), s.Lecturer);
        Assert.EndsWith(" (bản sao)", copy.Title); Assert.Equal(set, copy.ClonedFromId);
        var copiedQuiz = Assert.Single(copy.Quizzes); Assert.Equal(QuizStatus.Draft, copiedQuiz.Status);
        Assert.DoesNotContain(copy.Quizzes, x => x.Title == excluded.Title);
        var copiedQuestion = copiedQuiz.Questions.First(); Assert.Equal(matching, copiedQuestion.CaseSensitive);
        Assert.Equal(matching, copiedQuestion.AcceptAnyBlankOrder); Assert.Equal(matching ? .1m : 0, copiedQuestion.NumberTolerance);
        Assert.Equal(matching ? BlankScoring.PerBlank : BlankScoring.AllOrNothing, copiedQuestion.BlankScoring);
        Assert.Equal(cards ? 1 : 0, await h.Db.Flashcards.CountAsync(c => c.StudySetId == copy.Id));
        Assert.Equal(assignmentCount, await h.Db.Assignments.CountAsync());
        Assert.False(await h.Db.Assignments.AnyAsync(a => a.StudySetId == copy.Id));
    }

    [Fact]
    public async Task NhanBanChiChuVaTuChoiBoBiAn()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(); var set = s.Quiz.StudySet;
        await Assert.ThrowsAsync<ForbiddenException>(() => Clones(h).CloneAsync(set.Id, new([s.Quiz.Id], true, true), s.Student));
        set.HiddenByAdminAt = h.Clock.UtcNow; await h.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<BusinessException>(() => Clones(h).CloneAsync(set.Id, new([s.Quiz.Id], true, true), s.Lecturer));
        Assert.False(await h.Db.StudySets.AnyAsync(x => x.ClonedFromId == set.Id));
    }

    [Theory]
    [InlineData(31, true)]
    [InlineData(29, false)]
    public async Task KhoaQua30NgayNangGvSomNhatVaThongBaoMoiGv(int days, bool promotes)
    {
        using var h = new Harness(); var s = await h.ScenarioAsync();
        var first = await Colleague(h, "GV phụ sớm"); var second = await Colleague(h, "GV phụ sau");
        await h.InviteAndAcceptAssistantAsync(s.Room.Id, first.Email, s.Lecturer); h.Clock.UtcNow = h.Clock.UtcNow.AddMinutes(1);
        await h.InviteAndAcceptAssistantAsync(s.Room.Id, second.Email, s.Lecturer);
        h.Db.AccountLocks.Add(new() { UserId = s.Lecturer, Kind = AccountLockKind.Protective, StartsAt = h.Clock.UtcNow });
        await h.Db.SaveChangesAsync(); h.Clock.UtcNow = h.Clock.UtcNow.AddDays(days);
        var job = new LecturerPromotionService(h.Db, Notices(h), h.Clock, h.Invitations);
        Assert.Equal(promotes ? 1 : 0, await job.RunAsync());
        Assert.True(await h.Classrooms.IsMainLecturerAsync(s.Room.Id, promotes ? first.Id : s.Lecturer));
        Assert.Equal(promotes ? 3 : 0, await h.Db.Notifications.CountAsync(n => n.Type == NotificationType.Promotion
            && (n.UserId == s.Lecturer || n.UserId == first.Id || n.UserId == second.Id)));
        Assert.Equal(0, await job.RunAsync());
        var locked = await h.Db.AccountLocks.SingleAsync(l => l.UserId == s.Lecturer);
        locked.UnlockedAt = h.Clock.UtcNow;
        await h.Db.SaveChangesAsync(); // Không để khóa 29 ngày của ca này ảnh hưởng đồng hồ 31 ngày ở ca sau.
        if (promotes)
        {
            await job.RunAsync();
            Assert.False(await h.Classrooms.IsMainLecturerAsync(s.Room.Id, s.Lecturer));
        }
    }

    [Fact]
    public async Task KhongGvPhuKhongTuDongAdminChiGuiChuyenKhongXemDiem()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(); var target = await Colleague(h); var admin = await h.UserAsync("Admin");
        (await h.Db.Users.FindAsync(admin))!.IsSystemAdmin = true;
        h.Db.AccountLocks.Add(new() { UserId = s.Lecturer, StartsAt = h.Clock.UtcNow, Kind = AccountLockKind.Violation, Reason = "Vi phạm" });
        await h.Db.SaveChangesAsync(); h.Clock.UtcNow = h.Clock.UtcNow.AddDays(31);
        Assert.Equal(0, await new LecturerPromotionService(h.Db, Notices(h), h.Clock, h.Invitations).RunAsync());
        Assert.Contains(await Transfers(h).AdminClassesAsync(admin), c => c.Id == s.Room.Id);
        await Assert.ThrowsAsync<ForbiddenException>(() => Transfers(h).AdminOfferAsync(s.Room.Id, target.Email, s.Student));
        var row = await Transfers(h).AdminOfferAsync(s.Room.Id, target.Email, admin);
        Assert.True(await h.Classrooms.IsMainLecturerAsync(s.Room.Id, s.Lecturer));
        await Assert.ThrowsAsync<ForbiddenException>(() => h.Gradebook.GetReportAsync(s.Assignment.Id, admin));
        await Transfers(h).DecideAsync(row.Id, true, target.Id);
        Assert.True(await h.Classrooms.IsMainLecturerAsync(s.Room.Id, target.Id));
        await Assert.ThrowsAsync<ForbiddenException>(() => h.Gradebook.GetReportAsync(s.Assignment.Id, admin));
    }

    [Fact]
    public async Task ChamLaiChiBaoNguoiDoiDiemKhongLoDapAnTest()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(2); var other = await h.UserAsync("Đúng sẵn");
        await h.Classrooms.JoinByCodeAsync(s.ClassCode, other);
        var test = await h.Assignments.CreateTestAsync(s.Room.Id, s.Quiz.Id, new(null, 30), s.Lecturer);
        await h.Assignments.StartTestAsync(test.Id, s.Lecturer);
        var wrong = await h.Attempts.StartAsync(test.Id, s.Student); var correct = await h.Attempts.StartAsync(test.Id, other);
        var q = s.Quiz.Questions[0];
        foreach (var at in new[] { wrong, correct })
        {
            var aq = at.Questions.Single(x => x.QuestionId == q.Id); var snap = QuestionSnapshot.FromJson(aq.SnapshotJson);
            await h.Attempts.AnswerAsync(at.Id, aq.Id, Guid.NewGuid(), [snap.Options.First(o => o.IsCorrect == (at == correct)).Id], at.Participant.UserId!);
            await h.Attempts.FinishAsync(at.Id, at.Participant.UserId!);
        }
        await h.Regrades.ApplyAsync(test.Id, new(q.Id, RegradeMethod.FullCredit, "Đề thiếu dữ kiện", Guid.NewGuid(), []), s.Lecturer);
        var row = Assert.Single(await h.Db.Notifications.Where(n => n.Type == NotificationType.Regrade && (n.UserId == s.Student || n.UserId == other)).ToListAsync());
        Assert.Equal(s.Student, row.UserId);
        var payload = NotificationService.Payload(row);
        Assert.Equal($"Câu {wrong.Questions.Single(x => x.QuestionId == q.Id).DisplayOrder + 1} đã được chấm lại: Đề thiếu dữ kiện", payload.Text);
        Assert.DoesNotContain("đáp án", payload.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Null(test.AnswersPublishedAt);
    }

    [Fact]
    public async Task RollbackKhongGiuThongBaoVaChiPushSauCommit()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(); var assistant = await Colleague(h);
        var fake = new NotificationRecorder(h.Db); h.Notifier = fake;
        await using (var tx = await h.Db.Database.BeginTransactionAsync())
        {
            await Notices(h).AddAsync(assistant.Id, NotificationType.Assistant, new("Phải rollback", "/Classrooms"));
            await h.Db.SaveChangesAsync(); await tx.RollbackAsync();
        }
        using (var fresh = DatabaseFixture.NewContext()) Assert.False(await fresh.Notifications.AnyAsync(n => n.UserId == assistant.Id));
        await h.Classrooms.AddAssistantAsync(s.Room.Id, assistant.Email, s.Lecturer);
        // (7c/L7) cả người mời và người nhận có chuông; vẫn chỉ push sau commit.
        Assert.Equal(2, fake.Events.Count);
        var push = Assert.Single(fake.Events, e => e.User == assistant.Id);
        Assert.Equal(1, push.Notice.Unread); Assert.All(fake.Events, e => Assert.True(e.Committed));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DuyetTuChoiLamLaiGuiThongBaoChoHocVien(bool approve)
    {
        using var h = new Harness(); var s = await h.ScenarioAsync();
        var test = await h.Assignments.CreateTestAsync(s.Room.Id, s.Quiz.Id, new(null, 30), s.Lecturer);
        await h.Assignments.StartTestAsync(test.Id, s.Lecturer);
        var at = await h.Attempts.StartAsync(test.Id, s.Student); await h.Attempts.FinishAsync(at.Id, s.Student);
        var retakes = new RetakeService(h.Db, h.Assignments, h.Attempts, h.Clock);
        var request = await retakes.RequestAsync(test.Id, "Xin làm lại", s.Student);
        await retakes.DecideAsync(request.Id, approve, approve ? h.Clock.UtcNow.AddHours(1) : null, s.Lecturer);
        var row = Assert.Single(await h.Db.Notifications.Where(n => n.UserId == s.Student && n.Type == NotificationType.Retake).ToListAsync());
        Assert.Contains(approve ? "được duyệt" : "từ chối", NotificationService.Payload(row).Text);
    }

    [Fact]
    public async Task KhoaMoKhoaLuuThongBaoDeLanDangNhapSauVanThay()
    {
        using var h = new Harness(); using var identity = new SafetyIdentity(h);
        var admin = await identity.PrepareAsync(await h.UserAsync("Admin"), admin: true);
        var target = await identity.PrepareAsync(await h.UserAsync("GV"));
        await identity.Locks.LockAsync(target.Id, AccountLockKind.Violation, "Vi phạm", null, admin.Id);
        await identity.Locks.UnlockAsync(target.Id, admin.Id);
        var rows = await Notices(h).ListAsync(target.Id);
        Assert.Equal(2, rows.Items.Count); Assert.All(rows.Items, n => Assert.Null(n.ReadAt));
        Assert.Contains("mở khóa", NotificationService.Payload(rows.Items[0]).Text);
        Assert.Contains("bị khóa", NotificationService.Payload(rows.Items[1]).Text);
    }

    [Fact]
    public async Task KhoaTuHetHanChiThongBaoMotLanKhiDangNhapLai()
    {
        using var h = new Harness(); using var identity = new SafetyIdentity(h);
        var admin = await identity.PrepareAsync(await h.UserAsync("Admin"), admin: true);
        var target = await identity.PrepareAsync(await h.UserAsync("GV"));
        await identity.Locks.LockAsync(target.Id, AccountLockKind.Violation, "Vi phạm", h.Clock.UtcNow.AddMinutes(1), admin.Id);
        h.Clock.UtcNow = h.Clock.UtcNow.AddMinutes(2);
        await identity.Locks.ExpireAsync(target.Id); await identity.Locks.ExpireAsync(target.Id);
        Assert.Null(await identity.Locks.ActiveAsync(target.Id));
        Assert.Equal(2, await h.Db.Notifications.CountAsync(n => n.UserId == target.Id));
    }

    [Fact]
    public async Task NhanLopTuChoiNeuNguoiNhanVuaVaoLamHocVien()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(); var target = await Colleague(h);
        var row = await Transfers(h).OfferAsync(s.Room.Id, target.Email, true, s.Lecturer);
        await h.Classrooms.JoinByCodeAsync(s.ClassCode, target.Id);
        await Assert.ThrowsAsync<BusinessException>(() => Transfers(h).DecideAsync(row.Id, true, target.Id));
        Assert.True(await h.Classrooms.IsMainLecturerAsync(s.Room.Id, s.Lecturer));
        Assert.False(await h.Db.StudySets.AnyAsync(x => x.OwnerId == target.Id));
        Assert.Equal(1, await h.Db.Notifications.CountAsync(n => n.UserId == target.Id));
    }

    [Fact]
    public async Task NhanBanChiTheHoacRongKhongTuChepQuiz()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync();
        h.Db.Flashcards.Add(new() { StudySetId = s.Quiz.StudySetId, Front = "Trước", Back = "Sau" }); await h.Db.SaveChangesAsync();
        var cards = await Clones(h).CloneAsync(s.Quiz.StudySetId, new([], true, false), s.Lecturer);
        Assert.Empty(cards.Quizzes); Assert.Equal(1, await h.Db.Flashcards.CountAsync(c => c.StudySetId == cards.Id));
        var empty = await Clones(h).CloneAsync(s.Quiz.StudySetId, new([], false, false), s.Lecturer);
        Assert.Empty(empty.Quizzes); Assert.False(await h.Db.Flashcards.AnyAsync(c => c.StudySetId == empty.Id));
    }

    [Fact]
    public async Task ThongBaoPhanTrangMoiNhatTruocVaDocTatCaChiTacDongChu()
    {
        using var h = new Harness(); var user = await h.UserAsync("Người nhận"); var other = await h.UserAsync("Khác");
        for (var i = 0; i < 21; i++) await Notices(h).AddAsync(user, NotificationType.Assistant, new($"Thông báo {i}", "/Classrooms"));
        await Notices(h).AddAsync(other, NotificationType.Assistant, new("Giữ chưa đọc", "/Classrooms")); await h.Db.SaveChangesAsync();
        var first = await Notices(h).ListAsync(user); Assert.Equal(20, first.Items.Count); Assert.True(first.HasNext);
        Assert.Equal("Thông báo 20", NotificationService.Payload(first.Items[0]).Text);
        Assert.Single((await Notices(h).ListAsync(user, 2)).Items);
        await Notices(h).ReadAllAsync(user); Assert.Equal(0, await Notices(h).UnreadAsync(user));
        Assert.Equal(1, await Notices(h).UnreadAsync(other));
    }

    [Fact]
    public async Task SuaGocKhongAnhHuongReviewSauChuyenVaChuMoiSuaChamLaiDuoc()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(2); var target = await Colleague(h);
        var at = await h.Attempts.StartAsync(s.Assignment.Id, s.Student); var q = at.Questions[0];
        var wrong = QuestionSnapshot.FromJson(q.SnapshotJson).Options.First(o => !o.IsCorrect).Id; // sai để chấm lại "tính đúng" mới đổi điểm
        await h.Attempts.AnswerAsync(at.Id, q.Id, Guid.NewGuid(), [wrong], s.Student); await h.Attempts.FinishAsync(at.Id, s.Student);
        var before = JsonSerializer.Serialize(await h.Attempts.GetPlayQuestionsAsync(at, s.Student));
        var row = await Transfers(h).OfferAsync(s.Room.Id, target.Email, false, s.Lecturer);
        await Transfers(h).DecideAsync(row.Id, true, target.Id);
        var original = s.Quiz.Questions[0];
        await h.Library.SaveChoiceQuestionAsync(s.Quiz.Id, original.Id, original.Version, Harness.Single("Gốc thay đổi"), s.Lecturer);
        Assert.Equal(before, JsonSerializer.Serialize(await h.Attempts.GetPlayQuestionsAsync(at, s.Student)));
        await Assert.ThrowsAsync<BusinessException>(() => h.Library.DeleteQuestionAsync(s.Quiz.Id, original.Id, s.Lecturer));
        var quiz = await h.Library.GetOwnedQuizAsync(s.Assignment.QuizId!.Value, target.Id);
        var clonedQuestion = quiz.Questions.Single(x => x.OrderIndex == QuestionSnapshot.FromJson(q.SnapshotJson).OrderIndex);
        await h.Regrades.ApplyAsync(s.Assignment.Id, new(clonedQuestion.Id, RegradeMethod.FullCredit, "Chấm sau chuyển", Guid.NewGuid(), []), target.Id);
        Assert.Equal(1, at.FirstScore);
        var next = await h.Attempts.StartAsync(s.Assignment.Id, s.Student);
        await h.Library.SaveChoiceQuestionAsync(quiz.Id, clonedQuestion.Id, clonedQuestion.Version, Harness.Single("Bản sao thay đổi"), target.Id);
        Assert.Contains(next.Questions, x => QuestionSnapshot.FromJson(x.SnapshotJson).Prompt == "Bản sao thay đổi");
        Assert.DoesNotContain(at.Questions, x => QuestionSnapshot.FromJson(x.SnapshotJson).Prompt == "Bản sao thay đổi");
    }

    [Fact]
    public async Task ChuyenLopSaoChepDuCauDienVaTheGiuTienDo()
    {
        using var h = new Harness(); var s = await Tang4DbTests.Mixed(h, threshold: null, surveys: false); var target = await Colleague(h);
        var cards = new FlashcardService(h.Db, h.Library, h.Clock, h.Notifier);
        await cards.SaveAsync(s.Quiz.StudySetId, null, "Trước", "Sau", s.Lecturer);
        var card = await h.Db.Flashcards.SingleAsync(c => c.StudySetId == s.Quiz.StudySetId);
        h.Clock.UtcNow = h.Clock.UtcNow.AddMinutes(1);
        var combined = await h.Assignments.CreatePracticeAsync(s.Room.Id, s.Quiz.Id,
            new(null, null, 3, Form: AssignmentForm.Combined, RequiredParts: RequiredParts.Both), s.Lecturer);
        await cards.RateAsync(s.Quiz.StudySetId, combined.Id, card.Id, FlashcardRating.Remembered, s.Student);
        var row = await Transfers(h).OfferAsync(s.Room.Id, target.Email, true, s.Lecturer);
        await Transfers(h).DecideAsync(row.Id, true, target.Id);
        var cloned = await h.Library.GetOwnedQuizAsync(combined.QuizId!.Value, target.Id);
        var input = Assert.Single(cloned.Questions, q => q.Type == QuestionType.TextInput);
        Assert.Equal("Đúng", Assert.Single(Assert.Single(input.Blanks).Answers).Text);
        var counts = await CardCompletion.CountsAsync(h.Db, combined,
            (await h.Db.Participants.SingleAsync(p => p.AssignmentId == combined.Id && p.UserId == s.Student)).Id);
        Assert.Equal(1, counts.Studied); Assert.Equal(1, counts.Total);
        Assert.Equal(1, await h.Db.StudySets.CountAsync(x => x.OwnerId == target.Id && x.ClonedFromId == s.Quiz.StudySetId));
    }

    [Fact]
    public async Task HaiNguoiGuiCungLucChiMotDeNghiVaKhongThongBaoKhiThatBai()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(); var receiver = await Colleague(h);
        using var first = new Harness(); using var second = new Harness();
        async Task<bool> Offer(Harness tab)
        {
            try { await Transfers(tab).OfferAsync(s.Room.Id, receiver.Email, true, s.Lecturer); return true; }
            catch (BusinessException) { return false; }
        }
        var results = await Task.WhenAll(Offer(first), Offer(second));
        Assert.Single(results, x => x);
        Assert.Equal(1, await h.Db.ClassTransfers.CountAsync(t => t.ClassroomId == s.Room.Id && t.Status == ClassTransferStatus.Pending));
        Assert.Equal(1, await h.Db.Notifications.CountAsync(n => n.UserId == receiver.Id));
    }

    [Fact]
    public async Task NhanLopCungLucNangGvKhongTaoHaiGvChinh()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(); var assistant = await Colleague(h); var receiver = await Colleague(h);
        await h.InviteAndAcceptAssistantAsync(s.Room.Id, assistant.Email, s.Lecturer);
        var row = await Transfers(h).OfferAsync(s.Room.Id, receiver.Email, true, s.Lecturer);
        h.Db.AccountLocks.Add(new() { UserId = s.Lecturer, Kind = AccountLockKind.Protective, StartsAt = h.Clock.UtcNow });
        await h.Db.SaveChangesAsync();
        using var acceptContext = new Harness(); using var jobContext = new Harness();
        acceptContext.Clock.UtcNow = jobContext.Clock.UtcNow = h.Clock.UtcNow.AddDays(31);
        async Task Accept()
        {
            try { await Transfers(acceptContext).DecideAsync(row.Id, true, receiver.Id); }
            // (7c/L2) sau 31 ngày đề nghị có thể hết hạn trước job nâng GV.
            catch (BusinessException ex) { Assert.True(ex.Message.Contains("đã được xử lý") || ex.Message.Contains("hết hạn")); }
        }
        await Task.WhenAll(Accept(), new LecturerPromotionService(jobContext.Db, Notices(jobContext), jobContext.Clock, jobContext.Invitations).RunAsync());
        using var fresh = DatabaseFixture.NewContext();
        var main = Assert.Single(await fresh.ClassMembers.Where(m => m.ClassroomId == s.Room.Id && m.Role == ClassRole.MainLecturer
            && m.Status == ClassMemberStatus.Active).ToListAsync());
        var transfer = await fresh.ClassTransfers.SingleAsync(t => t.Id == row.Id);
        Assert.Equal(transfer.Status == ClassTransferStatus.Accepted ? receiver.Id : assistant.Id, main.UserId);
        Assert.NotEqual(ClassTransferStatus.Pending, transfer.Status);
    }

    [Fact]
    public async Task NhanLopCungLucTraLoiTestGiuHanBanChupVaChiLuuMotLan()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(); var receiver = await Colleague(h);
        var test = await h.Assignments.CreateTestAsync(s.Room.Id, s.Quiz.Id, new(null, 30), s.Lecturer);
        await h.Assignments.StartTestAsync(test.Id, s.Lecturer); var at = await h.Attempts.StartAsync(test.Id, s.Student);
        var q = at.Questions[0]; var deadline = at.DeadlineAt; var snapshot = q.SnapshotJson;
        var row = await Transfers(h).OfferAsync(s.Room.Id, receiver.Email, true, s.Lecturer);
        using var accept = new Harness(); using var answer = new Harness();
        accept.Clock.UtcNow = answer.Clock.UtcNow = h.Clock.UtcNow;
        await Task.WhenAll(Transfers(accept).DecideAsync(row.Id, true, receiver.Id), answer.Attempts.AnswerAsync(at.Id, q.Id, Guid.NewGuid(),
            [QuestionSnapshot.FromJson(snapshot).Options.Single(o => o.IsCorrect).Id], s.Student));
        using var fresh = DatabaseFixture.NewContext();
        Assert.Equal(deadline, (await fresh.Attempts.SingleAsync(a => a.Id == at.Id)).DeadlineAt);
        Assert.Equal(snapshot, (await fresh.AttemptQuestions.SingleAsync(x => x.Id == q.Id)).SnapshotJson);
        Assert.Single(await fresh.AnswerSubmissions.Where(x => x.AttemptQuestionId == q.Id).ToListAsync());
    }

    [Fact]
    public async Task ThongBaoChiChuDocVaDanhDau()
    {
        using var h = new Harness();
        var owner = await h.UserAsync("Người nhận"); var other = await h.UserAsync("Khác");
        var service = new NotificationService(h.Db, h.Clock, h.Notifier);
        await using (var tx = await h.Db.Database.BeginTransactionAsync())
        {
            await service.AddAsync(owner, NotificationType.Assistant, new("Đã thêm GV phụ", "/Classrooms"));
            await h.Db.SaveChangesAsync(); await tx.CommitAsync();
        }
        var row = Assert.Single((await service.ListAsync(owner)).Items);
        await Assert.ThrowsAsync<ForbiddenException>(() => service.ReadAsync(row.Id, other));
        await Assert.ThrowsAsync<ForbiddenException>(() => service.MarkReadAsync(row.Id, other));
        Assert.Null((await service.ReadAsync(row.Id, owner)).ReadAt);
        await service.MarkReadAsync(row.Id, owner);
        Assert.Equal(0, await service.UnreadAsync(owner));
        await service.AddAsync(owner, NotificationType.Transfer, new("Chuyển lớp", "/Classrooms"));
        await h.Db.SaveChangesAsync();
        await service.ReadAllAsync(owner);
        Assert.Equal(0, await service.UnreadAsync(owner));
    }

    [Fact]
    public async Task BoHocBiAnKhongHienTrongDanhSachGiaoBai()
    {
        // (5b) bộ học bị Admin ẩn không còn được chọn khi giao bài mới.
        using var h = new Harness(); var s = await h.ScenarioAsync(1);
        Assert.Contains(await h.Library.ListAssignableAsync(s.Room.Id, s.Lecturer), x => x.Id == s.Quiz.StudySetId);
        var set = await h.Db.StudySets.SingleAsync(x => x.Id == s.Quiz.StudySetId);
        set.HiddenByAdminAt = h.Clock.UtcNow; set.HiddenReason = "Thử ẩn"; await h.Db.SaveChangesAsync();
        Assert.DoesNotContain(await h.Library.ListAssignableAsync(s.Room.Id, s.Lecturer), x => x.Id == s.Quiz.StudySetId);
    }

    [Fact]
    public async Task GiangVienPhuChiThayVaGiaoBoHocLopDaDung()
    {
        // (48A, người dùng chốt 2026-11-05) bộ học riêng của GV chính, chưa dùng cho lớp, không lộ cho GV phụ.
        using var h = new Harness(); var s = await h.ScenarioAsync(1);
        var assistant = await h.UserAsync("GV phụ");
        h.Db.ClassMembers.Add(new ClassMember { ClassroomId = s.Room.Id, UserId = assistant,
            Role = ClassRole.AssistantLecturer, Status = ClassMemberStatus.Active });
        var priv = new StudySet { OwnerId = s.Lecturer, Title = "Bộ riêng của GV chính" };
        var privQuiz = new Quiz { StudySet = priv, Title = "Quiz riêng", Status = QuizStatus.Ready };
        priv.Quizzes.Add(privQuiz); h.Db.StudySets.Add(priv);
        await h.Db.SaveChangesAsync();
        var list = await h.Library.ListAssignableAsync(s.Room.Id, assistant);
        Assert.Contains(list, x => x.Id == s.Quiz.StudySetId);
        Assert.DoesNotContain(list, x => x.Id == priv.Id);
        Assert.Contains(await h.Library.ListAssignableAsync(s.Room.Id, s.Lecturer), x => x.Id == priv.Id);
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            h.Assignments.CreatePracticeAsync(s.Room.Id, privQuiz.Id, new PracticeSettings(null, null, null), assistant));
        Assert.NotNull(await h.Assignments.CreatePracticeAsync(s.Room.Id, s.Quiz.Id, new PracticeSettings(null, null, null), assistant));
    }
}

internal sealed class NotificationRecorder(MindSprintAI_ASP.NET_MVC.Data.AppDbContext db) : ILearnNotifier
{
    public List<(string User, NotificationNotice Notice, bool Committed)> Events { get; } = [];
    public async Task NotificationAsync(string userId, NotificationNotice notice)
    {
        using var fresh = DatabaseFixture.NewContext(); fresh.Database.SetCommandTimeout(3);
        Events.Add((userId, notice, db.Database.CurrentTransaction == null && await fresh.Notifications.AnyAsync(n => n.UserId == userId)));
    }
    public Task AttemptAsync(long attemptId, string eventName, LearnNotice notice) => Task.CompletedTask;
    public Task TestStartedAsync(int assignmentId) => Task.CompletedTask;
    public Task LecturersAsync(int assignmentId, int? recheckAfterSeconds = null) => Task.CompletedTask;
    public Task MovedAsync(long attemptId, IReadOnlyList<string> oldConnections) => Task.CompletedTask;
}
