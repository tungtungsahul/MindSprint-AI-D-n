using System.Net;
using Microsoft.EntityFrameworkCore;
using MindSprintAI_ASP.NET_MVC.Data.Entities;
using MindSprintAI_ASP.NET_MVC.Models;
using MindSprintAI_ASP.NET_MVC.Services;

namespace MindSprintAI_ASP.NET_MVC.Tests;

[Collection("db")]
public class MoLaiOnTapTests
{
    // Các ca R1–R3 cũ dựng bài đã được giảng viên bật tự ôn.
    private static async Task<Scenario> SelfReviewScenarioAsync(Harness h, int questions = 3, PracticeSettings? settings = null)
    {
        var s = await h.ScenarioAsync(questions, settings);
        s.Assignment.AllowQuizSelfReview = true;
        s.Assignment.AllowCardSelfReview = true;
        await h.Db.SaveChangesAsync();
        return s;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task R1_MoLaiGiuMaVaLuotCu_LuotMoiTinhDiem(bool closed)
    {
        using var h = new Harness();
        var s = await SelfReviewScenarioAsync(h, 1);
        var old = await h.Attempts.StartAsync(s.Assignment.Id, s.Student);
        if (closed) await h.Assignments.CloseNowAsync(s.Assignment.Id, s.Lecturer);
        else h.Clock.UtcNow = s.Assignment.DueAt!.Value.AddMinutes(1);
        var code = (await h.Codes.GetActiveAsync(JoinCodeTarget.Assignment, s.Assignment.Id))!.Code;
        var due = h.Clock.UtcNow.AddHours(1);
        await h.Assignments.ReopenPracticeAsync(s.Assignment.Id, due, "Ôn thi", s.Lecturer);
        Assert.Null(s.Assignment.ClosedAt);
        Assert.Null(s.Assignment.CloseReason);
        Assert.Equal(AssignmentStatus.Open, s.Assignment.Status);
        Assert.Equal(due, s.Assignment.DueAt);
        Assert.Equal(code, (await h.Codes.GetActiveAsync(JoinCodeTarget.Assignment, s.Assignment.Id))!.Code);
        Assert.Equal(AttemptStatus.Ended, old.Status);
        var attempt = await h.Attempts.StartAsync(s.Assignment.Id, s.Student);
        Assert.NotEqual(old.Id, attempt.Id);
        Assert.Equal(AttemptKind.Normal, attempt.Kind);
        Assert.False(attempt.ExcludeFromGradebook);
        Assert.Equal(due, attempt.DeadlineAt);
        var notice = await h.Db.Notifications.SingleAsync(n => n.UserId == s.Student);
        Assert.Contains("đã được mở lại để ôn tập", NotificationService.Payload(notice).Text);
        Assert.Contains(VnTime.Format(due), NotificationService.Payload(notice).Text);
        Assert.True(await h.Db.AssignmentChanges.AnyAsync(c => c.AssignmentId == s.Assignment.Id && c.Field == "ReopenPractice"));
    }

    [Fact]
    public async Task R1_KhongHanVaKhoiPhucDungMaCuNeuBiTat()
    {
        using var h = new Harness();
        var s = await SelfReviewScenarioAsync(h);
        await h.Assignments.CloseNowAsync(s.Assignment.Id, s.Lecturer);
        var code = (await h.Codes.GetActiveAsync(JoinCodeTarget.Assignment, s.Assignment.Id))!;
        code.IsActive = false;
        await h.Db.SaveChangesAsync();
        await h.Assignments.ReopenPracticeAsync(s.Assignment.Id, null, null, s.Lecturer);
        Assert.Null(s.Assignment.DueAt);
        Assert.Equal(code.Code, (await h.Codes.GetActiveAsync(JoinCodeTarget.Assignment, s.Assignment.Id))!.Code);
        Assert.Contains("Không có hạn.", NotificationService.Payload(await h.Db.Notifications.SingleAsync(n => n.UserId == s.Student)).Text);
    }

    [Fact]
    public async Task R1_LuotConGioCongRiengDichHan_LuotTuOnKhongNhanHanChung()
    {
        using var h = new Harness();
        var s = await SelfReviewScenarioAsync(h, 1, new(null, h.Clock.UtcNow.AddHours(1), 3));
        var normal = await h.Attempts.StartAsync(s.Assignment.Id, s.Student);
        var p = normal.Participant;
        p.ExtraMinutes = 60;
        normal.DeadlineAt = s.Assignment.DueAt!.Value.AddHours(1);
        await h.Db.SaveChangesAsync();
        h.Clock.UtcNow = s.Assignment.DueAt.Value.AddMinutes(1);
        var due = h.Clock.UtcNow.AddHours(2);
        await h.Assignments.ReopenPracticeAsync(s.Assignment.Id, due, null, s.Lecturer);
        Assert.Equal(due.AddHours(1), normal.DeadlineAt);
        Assert.True(AttemptRules.IsOpen(normal));
        await h.Attempts.FinishAsync(normal.Id, s.Student);
        await h.Assignments.CloseNowAsync(s.Assignment.Id, s.Lecturer);
        var self = await h.Attempts.StartSelfReviewAsync(s.Assignment.Id, s.Student);
        await h.Assignments.ReopenPracticeAsync(s.Assignment.Id, due, null, s.Lecturer);
        await h.Assignments.ChangeDueAsync(s.Assignment.Id, due.AddHours(1), null, s.Lecturer);
        await h.Assignments.ChangeExtraMinutesAsync(s.Assignment.Id, s.Student, 90, "Thêm giờ", s.Lecturer);
        await h.Assignments.CloseNowAsync(s.Assignment.Id, s.Lecturer);
        Assert.Null(self.DeadlineAt);
        Assert.Equal(AttemptStatus.Ended, self.Status);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(14)]
    public async Task R1_HanMoiDuoi15PhutBiTuChoi(int minutes)
    {
        using var h = new Harness();
        var s = await SelfReviewScenarioAsync(h);
        await h.Assignments.CloseNowAsync(s.Assignment.Id, s.Lecturer);
        var ex = await Assert.ThrowsAsync<BusinessException>(() => h.Assignments.ReopenPracticeAsync(s.Assignment.Id, h.Clock.UtcNow.AddMinutes(minutes), null, s.Lecturer));
        Assert.Contains("15 phút", ex.Message);
        Assert.Equal(AssignmentStatus.Closed, (await h.Db.Assignments.AsNoTracking().SingleAsync(a => a.Id == s.Assignment.Id)).Status);
    }

    [Fact]
    public async Task R1_KiemTraVaNguoiKhongQuanLyBiTuChoi()
    {
        using var h = new Harness();
        var s = await SelfReviewScenarioAsync(h);
        await h.Assignments.CloseNowAsync(s.Assignment.Id, s.Lecturer);
        await Assert.ThrowsAsync<ForbiddenException>(() => h.Assignments.ReopenPracticeAsync(s.Assignment.Id, null, null, s.Student));
        s.Assignment.Mode = AssignmentMode.Test;
        await h.Db.SaveChangesAsync();
        var ex = await Assert.ThrowsAsync<BusinessException>(() => h.Assignments.ReopenPracticeAsync(s.Assignment.Id, null, null, s.Lecturer));
        Assert.False(PlainLanguageAudit.HasJargon(ex.Message));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task R2_R3_TuOnKhongDoiDiemTrangThaiTranLuotVaKhongGiamSat(bool closed)
    {
        using var h = new Harness();
        var s = await SelfReviewScenarioAsync(h, 1, new(null, h.Clock.UtcNow.AddHours(1), 2, DeviceLock: true, MonitorScreen: true));
        (await h.Db.Users.FindAsync(s.Lecturer))!.ScreenLeaveBellEnabled = true;
        await h.Db.SaveChangesAsync();
        var old = await h.Attempts.StartAsync(s.Assignment.Id, s.Student, consent: true);
        await h.Attempts.ScreenEventAsync(old.Id, s.Student, "start", ScreenLeaveKind.Hidden);
        h.Clock.UtcNow = h.Clock.UtcNow.AddSeconds(3);
        await h.Attempts.ScreenEventAsync(old.Id, s.Student, "end");
        await h.Attempts.FinishAsync(old.Id, s.Student);
        if (closed) await h.Assignments.CloseNowAsync(s.Assignment.Id, s.Lecturer);
        else h.Clock.UtcNow = s.Assignment.DueAt!.Value.AddMinutes(1);
        var before = (await h.Gradebook.GetReportAsync(s.Assignment.Id, s.Lecturer)).Rows.Single();
        var p = await h.Db.Participants.SingleAsync(p => p.AssignmentId == s.Assignment.Id);
        var completion = p.CompletionStatus;
        var labels = p.CompletionLabels;
        var bells = await h.Db.Notifications.CountAsync(n => n.Type == NotificationType.ScreenLeave);
        var self = await h.Attempts.StartSelfReviewAsync(s.Assignment.Id, s.Student);
        Assert.Equal(AttemptKind.SelfReview, self.Kind);
        Assert.True(self.ExcludeFromGradebook);
        Assert.Null(self.DeadlineAt);
        Assert.Null(self.DeviceConsentAt);
        Assert.False(self.MonitorScreen);
        Assert.Null(self.AutoSubmitLeaves);
        Assert.Equal(self.Id, (await h.Attempts.StartSelfReviewAsync(s.Assignment.Id, s.Student)).Id);
        h.Device.Id = "other-browser";
        Assert.False((await h.Attempts.OpenPlayAsync(self.Id, s.Student)).NeedsTakeover);
        await Assert.ThrowsAsync<ForbiddenException>(() => h.Attempts.ScreenEventAsync(self.Id, s.Student, "start", ScreenLeaveKind.Hidden));
        var q = self.Questions.Single();
        var answer = QuestionSnapshot.FromJson(q.SnapshotJson).Options.Single(o => o.IsCorrect).Id;
        await h.Attempts.AnswerAsync(self.Id, q.Id, Guid.NewGuid(), [answer], s.Student, questionVersion: q.QuestionVersion);
        await h.Attempts.FinishAsync(self.Id, s.Student);
        Assert.Empty(self.ScreenLeaves);
        Assert.Equal(bells, await h.Db.Notifications.CountAsync(n => n.Type == NotificationType.ScreenLeave));
        Assert.Equal(completion, p.CompletionStatus);
        Assert.Equal(labels, p.CompletionLabels);
        var row = (await h.Gradebook.GetReportAsync(s.Assignment.Id, s.Lecturer)).Rows.Single();
        Assert.Equal(before.Score, row.Score);
        Assert.Equal(before.Status, row.Status);
        Assert.Equal(before.AttemptsUsed, row.AttemptsUsed);
        Assert.Equal(before.LatestAttemptId, row.LatestAttemptId);
        Assert.Equal(1, row.SelfReviewCount);
        Assert.Equal(1, (await h.Attempts.GetAssignmentPageAsync(s.Assignment.Id, s.Student)).RemainingAttempts);
        await h.Assignments.ReopenPracticeAsync(s.Assignment.Id, null, null, s.Lecturer);
        Assert.Equal(AttemptKind.Normal, (await h.Attempts.StartAsync(s.Assignment.Id, s.Student, consent: true)).Kind);
    }

    [Theory]
    [InlineData("test")]
    [InlineData("guest")]
    [InlineData("hidden")]
    [InlineData("removed")]
    [InlineData("left")]
    [InlineData("open")]
    public async Task R2_KhongDuDieuKienThiTuChoi(string state)
    {
        using var h = new Harness();
        var s = await SelfReviewScenarioAsync(h);
        if (state != "open") h.Clock.UtcNow = s.Assignment.DueAt!.Value.AddMinutes(1);
        if (state == "test") s.Assignment.Mode = AssignmentMode.Test;
        if (state == "guest") (await h.Db.Users.FindAsync(s.Student))!.IsGuest = true;
        if (state == "hidden") (await h.Db.StudySets.FindAsync(s.Quiz.StudySetId))!.HiddenByAdminAt = h.Clock.UtcNow;
        if (state == "removed") (await h.Db.ClassMembers.SingleAsync(m => m.ClassroomId == s.Room.Id && m.UserId == s.Student)).Status = ClassMemberStatus.Removed;
        if (state == "left") (await h.Db.ClassMembers.SingleAsync(m => m.ClassroomId == s.Room.Id && m.UserId == s.Student)).Status = ClassMemberStatus.Left;
        await h.Db.SaveChangesAsync();
        Assert.False(await h.Attempts.CanSelfReviewAsync(s.Assignment, s.Student));
        h.Db.ChangeTracker.Clear(); // quyền/nội dung ẩn phải đúng cả trong yêu cầu mới chưa tải Quiz.
        var ex = await Assert.ThrowsAsync<BusinessException>(() => h.Attempts.StartSelfReviewAsync(s.Assignment.Id, s.Student));
        Assert.False(PlainLanguageAudit.HasJargon(ex.Message));
        Assert.Empty(await h.Db.Attempts.Where(a => a.Participant.AssignmentId == s.Assignment.Id).ToListAsync());
    }

    [Fact]
    public async Task R2_ChiCoTuOnKhongHoanThanhVaKhongTruLuotSauMoLai()
    {
        using var h = new Harness();
        var s = await SelfReviewScenarioAsync(h, 1, new(null, h.Clock.UtcNow.AddHours(1), 1));
        h.Clock.UtcNow = s.Assignment.DueAt!.Value.AddMinutes(1);
        var self = await h.Attempts.StartSelfReviewAsync(s.Assignment.Id, s.Student);
        await h.Attempts.FinishAsync(self.Id, s.Student);
        var row = (await h.Gradebook.GetReportAsync(s.Assignment.Id, s.Lecturer)).Rows.Single();
        Assert.Equal(CompletionStatus.NotStarted, row.Status);
        Assert.Null(row.Score);
        Assert.Equal(0, row.AttemptsUsed);
        await h.Assignments.ReopenPracticeAsync(s.Assignment.Id, h.Clock.UtcNow.AddMinutes(15), null, s.Lecturer);
        var attempt = await h.Attempts.StartAsync(s.Assignment.Id, s.Student);
        Assert.Equal(AttemptKind.Normal, attempt.Kind);
        Assert.False(attempt.ExcludeFromGradebook);
    }

    [Fact]
    public async Task R2_TheBiChanSauHanDuocTuOnNhungKhongGhiTienDoBatBuoc()
    {
        using var h = new Harness();
        var s = await SelfReviewScenarioAsync(h);
        var cards = new FlashcardService(h.Db, h.Library, h.Clock, h.Notifier);
        await cards.SaveAsync(s.Quiz.StudySetId, null, "Mặt trước", "Mặt sau", s.Lecturer);
        s.Assignment.StudySetId = s.Quiz.StudySetId;
        s.Assignment.Form = AssignmentForm.Combined;
        s.Assignment.RequiredParts = RequiredParts.Both;
        s.Assignment.AllowFlashcardsAfterDue = false;
        await h.Db.SaveChangesAsync();
        h.Clock.UtcNow = s.Assignment.DueAt!.Value.AddMinutes(1);
        var before = await h.Db.Participants.Where(p => p.AssignmentId == s.Assignment.Id).Select(p => (CompletionStatus?)p.CompletionStatus).SingleOrDefaultAsync();
        var session = await cards.GetSessionAsync(s.Quiz.StudySetId, s.Assignment.Id, s.Student);
        Assert.True(session.CanStudy);
        await cards.RateAsync(s.Quiz.StudySetId, s.Assignment.Id, session.Cards.Single().Id, FlashcardRating.Remembered, s.Student);
        var participant = await h.Db.Participants.SingleAsync(p => p.AssignmentId == s.Assignment.Id);
        Assert.Equal(before ?? CompletionStatus.NotStarted, participant.CompletionStatus);
        Assert.False(await h.Db.FlashcardProgresses.AnyAsync(p => p.ParticipantId == participant.Id));
        Assert.True(await h.Db.FlashcardProgresses.AnyAsync(p => p.UserId == s.Student && p.ParticipantId == null));
        Assert.Equal(0, (await CardCompletion.CountsAsync(h.Db, s.Assignment, participant.Id)).Studied);
    }

    [Fact]
    public async Task R2_GiuGioTungCauVaLocDapAnCauBoTrongNhuOnTap()
    {
        using var h = new Harness();
        var s = await SelfReviewScenarioAsync(h, 2);
        var source = await h.Db.Questions.FirstAsync(q => q.QuizId == s.Quiz.Id);
        source.TimeLimitSeconds = 30;
        await h.Db.SaveChangesAsync();
        h.Clock.UtcNow = s.Assignment.DueAt!.Value.AddMinutes(1);
        var self = await h.Attempts.StartSelfReviewAsync(s.Assignment.Id, s.Student);
        Assert.Contains(self.Questions, q => QuestionSnapshot.FromJson(q.SnapshotJson).TimeLimitSeconds == 30);
        await h.Attempts.FinishAsync(self.Id, s.Student);
        var questions = await h.Attempts.GetPlayQuestionsAsync(self, s.Student);
        Assert.All(questions, q => { Assert.Null(q.CorrectIds); Assert.Null(q.Explanation); });
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Quyen_NguoiVaoSauHanHoacKhongThuocDanhSachGiaoKhongDuocTuOn(bool onlyCurrent)
    {
        using var h = new Harness();
        var s = await SelfReviewScenarioAsync(h);
        s.Assignment.Form = AssignmentForm.Combined;
        s.Assignment.StudySetId = s.Quiz.StudySetId;
        s.Assignment.AllowFlashcardsAfterDue = false;
        if (onlyCurrent)
        {
            s.Assignment.AudiencePolicy = AudiencePolicy.OnlyCurrentMembers;
            h.Clock.UtcNow = s.Assignment.CreatedAt.AddSeconds(1);
        }
        else h.Clock.UtcNow = s.Assignment.DueAt!.Value.AddMinutes(1);
        var user = await h.UserAsync("Vào muộn");
        await h.Classrooms.JoinByCodeAsync(s.ClassCode, user);
        if (onlyCurrent)
            Assert.True((await h.Db.ClassMembers.SingleAsync(m => m.ClassroomId == s.Room.Id && m.UserId == user)).JoinedAt > s.Assignment.CreatedAt);
        h.Clock.UtcNow = s.Assignment.DueAt!.Value.AddMinutes(2);
        await h.Db.SaveChangesAsync();
        Assert.False(await h.Attempts.CanSelfReviewAsync(s.Assignment, user));
        Assert.Empty(await h.Attempts.SelfReviewAssignmentIdsAsync([s.Assignment], user));
        await Assert.ThrowsAsync<ForbiddenException>(() => h.Attempts.GetAssignmentPageAsync(s.Assignment.Id, user));
        await Assert.ThrowsAsync<BusinessException>(() => h.Attempts.StartSelfReviewAsync(s.Assignment.Id, user));
        var cards = new FlashcardService(h.Db, h.Library, h.Clock, h.Notifier);
        await Assert.ThrowsAsync<ForbiddenException>(() => cards.GetSessionAsync(s.Quiz.StudySetId, s.Assignment.Id, user));
        Assert.False(await h.Db.Participants.AnyAsync(p => p.AssignmentId == s.Assignment.Id && p.UserId == user));
    }

    [Fact]
    public async Task Quyen_DongNguoiNgoaiLopKhongMoNgoaiLeTuOn()
    {
        using var h = new Harness();
        var s = await SelfReviewScenarioAsync(h);
        h.Db.Participants.Add(new Participant { Assignment = s.Assignment, UserId = s.Student, IsStranger = true, CreatedAt = h.Clock.UtcNow });
        await h.Db.SaveChangesAsync();
        h.Clock.UtcNow = s.Assignment.DueAt!.Value.AddMinutes(1);
        Assert.False(await h.Attempts.CanSelfReviewAsync(s.Assignment, s.Student));
        await Assert.ThrowsAsync<BusinessException>(() => h.Attempts.StartSelfReviewAsync(s.Assignment.Id, s.Student));
    }

    [Fact]
    public async Task GioCongRiengConMoKhongDuocTuOnKeCaThe()
    {
        using var h = new Harness();
        var s = await SelfReviewScenarioAsync(h);
        var at = await h.Attempts.StartAsync(s.Assignment.Id, s.Student);
        at.Participant.ExtraMinutes = 60;
        at.DeadlineAt = s.Assignment.DueAt!.Value.AddHours(1);
        s.Assignment.Form = AssignmentForm.Combined;
        s.Assignment.StudySetId = s.Quiz.StudySetId;
        s.Assignment.AllowFlashcardsAfterDue = false;
        await h.Db.SaveChangesAsync();
        h.Clock.UtcNow = s.Assignment.DueAt.Value.AddMinutes(1);
        Assert.False(await h.Attempts.CanSelfReviewAsync(s.Assignment, s.Student));
        Assert.Empty(await h.Attempts.SelfReviewAssignmentIdsAsync([s.Assignment], s.Student));
        await Assert.ThrowsAsync<BusinessException>(() => h.Attempts.StartSelfReviewAsync(s.Assignment.Id, s.Student));
        var cards = new FlashcardService(h.Db, h.Library, h.Clock, h.Notifier);
        Assert.False((await cards.GetSessionAsync(s.Quiz.StudySetId, s.Assignment.Id, s.Student)).CanStudy);
        Assert.True(AttemptRules.IsOpen(at));
    }

    [Fact]
    public async Task MoLaiChotTuOnBaoKetThucVaBatDauLuotTinhDiem()
    {
        using var h = new Harness();
        var notifier = new RecordingLearnNotifier();
        h.Notifier = notifier;
        var s = await SelfReviewScenarioAsync(h);
        h.Clock.UtcNow = s.Assignment.DueAt!.Value.AddMinutes(1);
        var self = await h.Attempts.StartSelfReviewAsync(s.Assignment.Id, s.Student);
        var status = self.Participant.CompletionStatus;
        var labels = self.Participant.CompletionLabels;
        await h.Assignments.ReopenPracticeAsync(s.Assignment.Id, null, null, s.Lecturer);
        Assert.Equal(AttemptStatus.Ended, self.Status);
        Assert.Contains(notifier.Events, n => n.Notice.AttemptId == self.Id && n.Event == "closed");
        Assert.Equal(status, self.Participant.CompletionStatus);
        Assert.Equal(labels, self.Participant.CompletionLabels);
        var page = await h.Attempts.GetAssignmentPageAsync(s.Assignment.Id, s.Student);
        Assert.True(page.CanStart);
        Assert.Equal(AttemptKind.Normal, (await h.Attempts.StartAsync(s.Assignment.Id, s.Student)).Kind);
    }

    [Fact]
    public async Task DongBaiChotTuOnDangDoKhongDoiKetQuaNguoiLam()
    {
        using var h = new Harness();
        var notifier = new RecordingLearnNotifier();
        h.Notifier = notifier;
        var s = await SelfReviewScenarioAsync(h);
        h.Clock.UtcNow = s.Assignment.DueAt!.Value.AddMinutes(1);
        var self = await h.Attempts.StartSelfReviewAsync(s.Assignment.Id, s.Student);
        var status = self.Participant.CompletionStatus;
        var labels = self.Participant.CompletionLabels;
        var before = (await h.Gradebook.GetReportAsync(s.Assignment.Id, s.Lecturer)).Rows.Single();
        await h.Assignments.CloseNowAsync(s.Assignment.Id, s.Lecturer);
        Assert.Equal(AttemptStatus.Ended, self.Status);
        Assert.Equal(status, self.Participant.CompletionStatus);
        Assert.Equal(labels, self.Participant.CompletionLabels);
        Assert.Equal(before.Score, (await h.Gradebook.GetReportAsync(s.Assignment.Id, s.Lecturer)).Rows.Single().Score);
        Assert.Contains(notifier.Events, n => n.Notice.AttemptId == self.Id && n.Event == "closed");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TheSauHan_NguoiNgoaiLopVaKhachVanXemChiDoc(bool guest)
    {
        using var h = new Harness();
        var s = await SelfReviewScenarioAsync(h);
        var user = await h.UserAsync("Người ngoài");
        (await h.Db.Users.FindAsync(user))!.IsGuest = guest;
        s.Assignment.Form = AssignmentForm.Combined;
        s.Assignment.StudySetId = s.Quiz.StudySetId;
        s.Assignment.AllowFlashcardsAfterDue = false;
        var cards = new FlashcardService(h.Db, h.Library, h.Clock, h.Notifier);
        await cards.SaveAsync(s.Quiz.StudySetId, null, "Trước", "Sau", s.Lecturer);
        h.Db.Participants.Add(new Participant { Assignment = s.Assignment, UserId = user, IsStranger = true,
            ApprovalStatus = ApprovalStatus.Approved, CreatedAt = h.Clock.UtcNow });
        await h.Db.SaveChangesAsync();
        h.Clock.UtcNow = s.Assignment.DueAt!.Value.AddMinutes(1);
        var page = await cards.GetSessionAsync(s.Quiz.StudySetId, s.Assignment.Id, user);
        Assert.False(page.CanStudy);
        Assert.Empty(page.Cards);
        var card = await h.Db.Flashcards.SingleAsync(c => c.StudySetId == s.Quiz.StudySetId);
        var ex = await Assert.ThrowsAsync<BusinessException>(() => cards.RateAsync(s.Quiz.StudySetId, s.Assignment.Id, card.Id, FlashcardRating.Remembered, user));
        Assert.Equal("Bài không cho học thẻ sau hạn.", ex.Message);
    }

    [Fact]
    public async Task HuyLuotKhachKhongLayTuOnDeTinhHoanThanh()
    {
        using var h = new Harness();
        var s = await SelfReviewScenarioAsync(h);
        var normal = await h.Attempts.StartAsync(s.Assignment.Id, s.Student);
        normal.MadeAsGuest = true;
        await h.Db.SaveChangesAsync();
        h.Clock.UtcNow = s.Assignment.DueAt!.Value.AddMinutes(1);
        var self = await h.Attempts.StartSelfReviewAsync(s.Assignment.Id, s.Student);
        await new RetakeService(h.Db, h.Assignments, h.Attempts, h.Clock).CancelAsync(normal.Id, "Giữ lượt khác", s.Lecturer);
        Assert.Equal(CompletionStatus.NotStarted, self.Participant.CompletionStatus);
        Assert.True(AttemptRules.IsOpen(self));
    }

    [Theory]
    [InlineData("class")]
    [InlineData("report")]
    [InlineData("set")]
    public async Task TuOnMoKhongChanThaoTacQuanLy(string action)
    {
        using var h = new Harness();
        var s = await SelfReviewScenarioAsync(h);
        h.Clock.UtcNow = s.Assignment.DueAt!.Value.AddMinutes(1);
        var self = await h.Attempts.StartSelfReviewAsync(s.Assignment.Id, s.Student);
        Assert.Equal(0, await h.Assignments.CountOpenAttemptsAsync(s.Assignment.Id));
        if (action == "class") { await h.Classrooms.DeleteAsync(s.Room.Id, s.Lecturer); Assert.NotNull(s.Room.DeletedAt); }
        if (action == "report") Assert.True(await new TrashService(h.Db, h.Classrooms, h.Clock).CanDeleteReportAsync(s.Assignment.Id, s.Lecturer));
        if (action == "set") { await h.Library.DeleteStudySetAsync(s.Quiz.StudySetId, s.Lecturer); Assert.NotNull((await h.Db.StudySets.FindAsync(s.Quiz.StudySetId))!.ArchivedAt); }
        Assert.True(AttemptRules.IsOpen(self));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RoiHoacXoaThanhVienChiTuOnKhongCanCanhBaoKhongGanNhan(bool leaving)
    {
        using var h = new Harness();
        var s = await SelfReviewScenarioAsync(h);
        h.Clock.UtcNow = s.Assignment.DueAt!.Value.AddMinutes(1);
        var self = await h.Attempts.StartSelfReviewAsync(s.Assignment.Id, s.Student);
        var labels = self.Participant.CompletionLabels;
        var status = self.Participant.CompletionStatus;
        if (leaving) Assert.Null(await h.Classrooms.LeaveAsync(s.Room.Id, s.Student));
        else
        {
            var member = await h.Db.ClassMembers.SingleAsync(m => m.ClassroomId == s.Room.Id && m.UserId == s.Student);
            Assert.Null(await h.Classrooms.RemoveMemberAsync(member.Id, s.Lecturer));
        }
        Assert.Equal(AttemptStatus.Ended, self.Status);
        Assert.Equal(labels, self.Participant.CompletionLabels);
        Assert.Equal(status, self.Participant.CompletionStatus);
    }

    [Fact]
    public async Task LuotTuOnNguoiKhacBiTuChoi()
    {
        using var h = new Harness();
        var s = await SelfReviewScenarioAsync(h);
        h.Clock.UtcNow = s.Assignment.DueAt!.Value.AddMinutes(1);
        var self = await h.Attempts.StartSelfReviewAsync(s.Assignment.Id, s.Student);
        var other = await h.UserAsync("Khác");
        await Assert.ThrowsAsync<ForbiddenException>(() => h.Attempts.OpenPlayAsync(self.Id, other));
        await Assert.ThrowsAsync<ForbiddenException>(() => h.Attempts.FinishAsync(self.Id, other));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(80)]
    public async Task KhaoSatBatBuocKhongChanNopTuOnVaTuOnKhongMoKetQua(int? threshold)
    {
        using var h = new Harness();
        var s = await Tang4DbTests.Mixed(h, choices: 1, input: false, threshold: threshold);
        s.Assignment.AllowQuizSelfReview = true; // công tắc tự ôn mặc định tắt (10/11)
        await h.Db.SaveChangesAsync();
        h.Clock.UtcNow = s.Assignment.DueAt!.Value.AddMinutes(1);
        var service = Tang4DbTests.Attempts(h);
        var self = await service.StartSelfReviewAsync(s.Assignment.Id, s.Student);
        if (threshold != null)
            await Tang4DbTests.Answer(h, self, self.Questions.Single(q => !SurveyRules.IsSurvey(QuestionSnapshot.FromJson(q.SnapshotJson).Type)), s.Student);
        Assert.NotEmpty(SurveyRules.MissingRequired(self));
        await service.FinishAsync(self.Id, s.Student);
        await Tang4DbTests.Surveys(h).PublishAsync(s.Assignment.Id, true, s.Lecturer);
        Assert.False((await service.GetAssignmentPageAsync(s.Assignment.Id, s.Student)).CanSeeSurveyResults);
        await Assert.ThrowsAsync<ForbiddenException>(() => Tang4DbTests.Surveys(h).GetForLearnerAsync(s.Assignment.Id, s.Student));
        Assert.True(await h.Assignments.CanEditMasteryAsync(s.Assignment.Id));
    }

    [Fact]
    public async Task ChuongMoLaiChiGuiNguoiDuocGiaoVaXoaChanNhacHanCu()
    {
        using var h = new Harness();
        var s = await SelfReviewScenarioAsync(h);
        s.Assignment.AudiencePolicy = AudiencePolicy.OnlyCurrentMembers;
        var late = await h.UserAsync("Vào sau");
        h.Clock.UtcNow = h.Clock.UtcNow.AddMinutes(1);
        await h.Classrooms.JoinByCodeAsync(s.ClassCode, late);
        h.Db.Reminders.Add(new() { UserId = s.Student, Kind = ReminderKind.Due, TargetId = s.Assignment.Id, Text = "Hạn cũ", Url = "/Learn", CreatedAt = h.Clock.UtcNow });
        await h.Db.SaveChangesAsync();
        h.Clock.UtcNow = s.Assignment.DueAt!.Value.AddMinutes(1);
        await h.Assignments.ReopenPracticeAsync(s.Assignment.Id, h.Clock.UtcNow.AddHours(1), null, s.Lecturer);
        Assert.True(await h.Db.Notifications.AnyAsync(n => n.UserId == s.Student && n.Type == NotificationType.ExtraMinutes));
        Assert.False(await h.Db.Notifications.AnyAsync(n => n.UserId == late && n.Type == NotificationType.ExtraMinutes));
        Assert.False(await h.Db.Reminders.AnyAsync(r => r.Kind == ReminderKind.Due && r.TargetId == s.Assignment.Id));
        await new Tang7cServices(h).Reminders.RunAsync();
        Assert.True(await h.Db.Reminders.AnyAsync(r => r.UserId == s.Student && r.Kind == ReminderKind.Due && r.TargetId == s.Assignment.Id));
    }

    [Fact]
    public async Task GopKhachChotTuOnGiuLuotTinhDiemDangDo()
    {
        using var h = new Harness();
        var s = await SelfReviewScenarioAsync(h);
        s.Assignment.StrangerPolicy = StrangerPolicy.Allow;
        var user = (await h.Db.Users.FindAsync(s.Student))!;
        user.EmailConfirmed = true;
        await h.Db.SaveChangesAsync();
        var guests = new GuestService(h.Db, h.Clock, h.Notifier);
        var token = GuestService.NewToken();
        var guest = await guests.JoinAsync(s.Assignment.Id, "Khách", token);
        var normal = await h.Attempts.StartAsync(s.Assignment.Id, guest.UserId);
        normal.Participant.ExtraMinutes = 60;
        normal.DeadlineAt = s.Assignment.DueAt!.Value.AddHours(1);
        await h.Db.SaveChangesAsync();
        h.Clock.UtcNow = s.Assignment.DueAt.Value.AddMinutes(1);
        var self = await h.Attempts.StartSelfReviewAsync(s.Assignment.Id, s.Student);
        Assert.True(await guests.MergeAsync(token, s.Student));
        Assert.Equal(AttemptStatus.Ended, self.Status);
        Assert.True(AttemptRules.IsOpen(normal));
        Assert.Equal(self.ParticipantId, normal.ParticipantId);
        Assert.False(normal.ExcludeFromGradebook);
    }

    [Fact]
    public async Task HaiBaiTuOnChamCungTheKhongTrungTienDoCaNhan()
    {
        using var h = new Harness();
        var s = await SelfReviewScenarioAsync(h);
        var second = await h.Assignments.CreatePracticeAsync(s.Room.Id, s.Quiz.Id,
            new(null, s.Assignment.DueAt, 3), s.Lecturer);
        var cards = new FlashcardService(h.Db, h.Library, h.Clock, h.Notifier);
        await cards.SaveAsync(s.Quiz.StudySetId, null, "Trước", "Sau", s.Lecturer);
        foreach (var a in new[] { s.Assignment, second })
        {
            a.Form = AssignmentForm.Combined;
            a.StudySetId = s.Quiz.StudySetId;
            a.AllowFlashcardsAfterDue = false;
            a.AllowCardSelfReview = true;
        }
        await h.Db.SaveChangesAsync();
        var cardId = await h.Db.Flashcards.Where(c => c.StudySetId == s.Quiz.StudySetId).Select(c => c.Id).SingleAsync();
        h.Clock.UtcNow = s.Assignment.DueAt!.Value.AddMinutes(1);
        using var other = DatabaseFixture.NewContext();
        var otherCards = new FlashcardService(other, new(other, h.Clock, h.Notifier), h.Clock, h.Notifier);
        await Task.WhenAll(
            cards.RateAsync(s.Quiz.StudySetId, s.Assignment.Id, cardId, FlashcardRating.Remembered, s.Student),
            otherCards.RateAsync(s.Quiz.StudySetId, second.Id, cardId, FlashcardRating.Remembered, s.Student));
        var progress = await h.Db.FlashcardProgresses.AsNoTracking().SingleAsync(p => p.UserId == s.Student && p.FlashcardId == cardId && p.ParticipantId == null);
        Assert.Equal(2, progress.ReviewCount);
        Assert.Equal(2, await h.Db.FlashcardReviews.CountAsync(p => p.UserId == s.Student && p.FlashcardId == cardId && p.ParticipantId == null));
    }

    [Fact]
    public async Task HuyCauKhongBiBanChupTuOnChanCauCuoiTinhDiem()
    {
        using var h = new Harness();
        var s = await SelfReviewScenarioAsync(h, 2);
        h.Clock.UtcNow = s.Assignment.DueAt!.Value.AddMinutes(1);
        var self = await h.Attempts.StartSelfReviewAsync(s.Assignment.Id, s.Student);
        // Bản chụp tự ôn chỉ còn câu sắp hủy; đề hiện tại vẫn còn câu tính điểm khác.
        var removed = self.Questions.Last();
        self.Questions.Remove(removed);
        h.Db.Remove(removed);
        await h.Db.SaveChangesAsync();
        var question = QuestionSnapshot.FromJson(self.Questions.Single().SnapshotJson).QuestionId;
        await h.Regrades.ApplyAsync(s.Assignment.Id, new(question, RegradeMethod.Cancel, "Câu sai", Guid.NewGuid(), []), s.Lecturer);
        Assert.True(await h.Db.Regrades.AnyAsync(r => r.AssignmentId == s.Assignment.Id && r.QuestionId == question));
    }
}

public class MoLaiOnTapPureTests
{
    [Fact]
    public async Task View_TuOnKhongChiếmNutLuotTinhDiemVaCoNhanKhiLamBai()
    {
        var self = Tang4RuleTests.Build(1);
        self.Kind = AttemptKind.SelfReview;
        var model = new LearnerAssignmentModel { Assignment = self.Participant.Assignment, Attempts = [self], CanStart = true };
        var html = WebUtility.HtmlDecode(await Tang4ViewTests.Render("Learn", "Assignment", model));
        Assert.Contains("Làm lượt mới", html);
        Assert.DoesNotContain("Tiếp tục lượt", html);
        var play = WebUtility.HtmlDecode(await Tang4ViewTests.Render("Learn", "Play", self,
            new() { ["Questions"] = AttemptService.ProjectQuestions(self, false) }));
        Assert.Contains("Lượt tự ôn · không tính điểm", play);
        Assert.False(PlainLanguageAudit.HasJargon(play));
    }

    [Fact]
    public async Task View_XemLaiLuotLamKhiLaKhachChiHienNhanMotLan()
    {
        var a = Tang4RuleTests.Build(1); a.Status = AttemptStatus.Ended; a.MadeAsGuest = true;
        a.Participant.CompletionLabels = CompletionLabels.MadeAsGuest;
        var html = WebUtility.HtmlDecode(await Tang4ViewTests.Render("Learn", "Review", a, new()
            { ["Questions"] = new List<PlayQuestion>(), ["ShowScores"] = false }));
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(html, "Làm khi là khách"));
    }

    [Theory]
    [InlineData(AssignmentMode.Test, true)]
    [InlineData(AssignmentMode.Practice, false)]
    public async Task View_BatDauBaiKiemTraHoiXacNhanConOnTapThiKhong(AssignmentMode mode, bool confirms)
    {
        var model = new LearnerAssignmentModel { Assignment = new Assignment { Id = 1, Mode = mode, DurationMinutes = 45 }, Attempts = [], CanStart = true };
        var html = WebUtility.HtmlDecode(await Tang4ViewTests.Render("Learn", "Assignment", model));
        var button = html.IndexOf("Bắt đầu làm bài", StringComparison.Ordinal);
        Assert.True(button > 0);
        var form = html[html.LastIndexOf("<form", button, StringComparison.Ordinal)..button];
        // Razor vẫn in data-confirm="" khi giá trị null; site.js bỏ qua thông điệp rỗng nên chỉ cần không có nội dung.
        Assert.True(confirms == !form.Contains("data-confirm=\"\""), form);
        if (confirms) Assert.Contains("Đồng hồ 45 phút", form);
        Assert.False(PlainLanguageAudit.HasJargon(html));
    }

    [Theory]
    [InlineData(AssignmentMode.Practice, true, false, true)]
    [InlineData(AssignmentMode.Practice, false, true, true)]
    [InlineData(AssignmentMode.Practice, false, false, false)]
    [InlineData(AssignmentMode.Test, true, true, false)]
    public void R1_DieuKienMoLai(AssignmentMode mode, bool closed, bool overdue, bool expected)
    {
        var now = DateTime.UtcNow;
        var a = new Assignment { Mode = mode, ClosedAt = closed ? now : null, DueAt = overdue ? now : now.AddDays(1) };
        Assert.Equal(expected, AssignmentService.CanReopenPractice(a, now));
    }

    [Theory]
    [InlineData(ScoreSelection.Highest)]
    [InlineData(ScoreSelection.First)]
    [InlineData(ScoreSelection.Latest)]
    [InlineData(ScoreSelection.Average)]
    public void R2_TatCaCachLayDiemDeuBoTuOn(ScoreSelection selection)
    {
        var normal = new Attempt { AttemptNumber = 1, Status = AttemptStatus.Ended, FirstScore = 1, MaxScore = 2 };
        var self = new Attempt { AttemptNumber = 2, Kind = AttemptKind.SelfReview, ExcludeFromGradebook = true, Status = AttemptStatus.Ended, FirstScore = 2, MaxScore = 2 };
        Assert.Equal(1, GradebookService.Select([normal, self], selection)!.Raw);
        Assert.Equal(normal, Assert.Single(GradebookService.ScoredAttempts([normal, self], selection)));
    }

    [Fact]
    public void R2_ChotLuotTuOnKhongDoiHoanThanhVaNhan()
    {
        var a = Tang4RuleTests.Build(1);
        a.Kind = AttemptKind.SelfReview;
        a.Participant.CompletionStatus = CompletionStatus.Incomplete;
        a.Participant.CompletionLabels = CompletionLabels.Expired;
        AttemptRules.End(a, a.Participant, AttemptEndReason.Submitted, DateTime.UtcNow);
        Assert.Equal(AttemptStatus.Ended, a.Status);
        Assert.Equal(CompletionStatus.Incomplete, a.Participant.CompletionStatus);
        Assert.Equal(CompletionLabels.Expired, a.Participant.CompletionLabels);
        Assert.Equal(3, (int)AttemptKind.SelfReview);
        Assert.Equal(1, (int)AttemptKind.Normal);
        Assert.Equal(2, (int)AttemptKind.Retake);
        Assert.Equal("Tự ôn sau hạn", DisplayText.Of(AttemptKind.SelfReview));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task View_NutTuOnChiHienTheoQuyenService(bool allowed)
    {
        var model = new LearnerAssignmentModel { Assignment = new Assignment { Id = 1 }, Attempts = [], CanStart = false, CanSelfReview = allowed };
        var html = WebUtility.HtmlDecode(await Tang4ViewTests.Render("Learn", "Assignment", model));
        Assert.Equal(allowed, html.Contains("Tự ôn quiz (không tính điểm)"));
        if (allowed)
        {
            Assert.Contains("asp-action=\"SelfReview\" asp-route-id=\"@a.Id\"", File.ReadAllText(Path.Combine(Tang8PureTests.Root, "Views/Learn/Assignment.cshtml")));
            Assert.Contains("__RequestVerificationToken", html);
        }
        Assert.False(PlainLanguageAudit.HasJargon(html));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task View_FormMoLaiDungActionVaXacNhan(bool allowed)
    {
        var model = new AssignmentDetailsModel { Assignment = new Assignment { Id = 1 }, Code = "12345678", OpenAttempts = 0, Rows = [], CanReopenPractice = allowed };
        var html = WebUtility.HtmlDecode(await Tang4ViewTests.Render("Assignments", "Details", model));
        Assert.Equal(allowed, html.Contains("Mở lại bài ôn tập</h3>"));
        if (allowed)
        {
            Assert.Contains("data-confirm=\"Mở lại bài ôn tập này cho cả lớp? Mã vào bài giữ nguyên.\"", html);
            Assert.Contains("type=\"datetime-local\"", html);
            Assert.Contains("__RequestVerificationToken", html);
            Assert.Contains("asp-action=\"ReopenPractice\" asp-route-id=\"@a.Id\"", File.ReadAllText(Path.Combine(Tang8PureTests.Root, "Views/Assignments/Details.cshtml")));
        }
        Assert.DoesNotContain(PlainLanguageAudit.UserTexts("Details.cshtml", File.ReadAllText(Path.Combine(Tang8PureTests.Root, "Views/Assignments/Details.cshtml"))), PlainLanguageAudit.HasJargon);
    }

    [Fact]
    public async Task View_TuOnCoNhanRiengVaBangDiemCoSoLuot()
    {
        var attempt = Tang4RuleTests.Build(1);
        attempt.Kind = AttemptKind.SelfReview;
        attempt.ExcludeFromGradebook = true;
        attempt.Status = AttemptStatus.Ended;
        var html = WebUtility.HtmlDecode(await Tang4ViewTests.Render("Assignments", "SelfReview", attempt,
            new() { ["Questions"] = AttemptService.ProjectQuestions(attempt, false), ["ShowScores"] = true }));
        Assert.Contains("Lượt tự ôn sau hạn · không tính điểm", html);
        var model = new AssignmentDetailsModel { Assignment = attempt.Participant.Assignment, Code = "", OpenAttempts = 0,
            Rows = [new("student", "Học viên", CompletionStatus.Completed, CompletionLabels.None, 1, new(1, 1), 1, SelfReviewCount: 3)] };
        var table = WebUtility.HtmlDecode(await Tang4ViewTests.Render("Assignments", "_LiveTable", model));
        Assert.Contains("Đã tự ôn lại sau hạn: 3 lượt", table);
        Assert.False(PlainLanguageAudit.HasJargon(html + table));
    }
}
