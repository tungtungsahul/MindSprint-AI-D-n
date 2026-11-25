using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MindSprintAI_ASP.NET_MVC.Controllers;
using MindSprintAI_ASP.NET_MVC.Data.Entities;
using MindSprintAI_ASP.NET_MVC.Services;

namespace MindSprintAI_ASP.NET_MVC.Tests;

[Collection("db")]
public class Tang2ConcurrencyTests
{
    private static RetakeService Retakes(Harness h) => new(h.Db, h.Assignments, h.Attempts, h.Clock);

    private static async Task<Scenario> TestAsync(Harness h, bool start = true)
    {
        var s = await h.ScenarioAsync(questions: 1);
        s.Quiz.Questions.Single().Type = QuestionType.MultipleChoice;
        await h.Db.SaveChangesAsync();
        var assignment = await h.Assignments.CreateTestAsync(s.Room.Id, s.Quiz.Id,
            new TestSettings(null, 45), s.Lecturer);
        if (start) await h.Assignments.StartTestAsync(assignment.Id, s.Lecturer);
        return s with { Assignment = assignment };
    }

    [Fact]
    public async Task B4_HaiContextHetGio_ChiChamNhapMotLan()
    {
        using var h = new Harness();
        var s = await TestAsync(h);
        var attempt = await h.Attempts.StartAsync(s.Assignment.Id, s.Student);
        var question = attempt.Questions.Single();
        await h.Attempts.SaveDraftAsync(attempt.Id, question.Id, [AssignmentTests.CorrectOption(question)], s.Student);
        using var tab1 = new Harness();
        using var tab2 = new Harness();
        await tab1.Attempts.GetForLearnerAsync(attempt.Id, s.Student);
        await tab2.Attempts.GetForLearnerAsync(attempt.Id, s.Student);
        // (B4, 66) Hai context đã cùng giữ bản nháp cũ trước khi context thứ nhất chốt hết giờ.
        tab1.Clock.UtcNow = tab2.Clock.UtcNow = attempt.DeadlineAt!.Value;
        await tab1.Attempts.GetForLearnerAsync(attempt.Id, s.Student);
        await tab2.Attempts.GetForLearnerAsync(attempt.Id, s.Student);

        using var fresh = new Harness();
        var saved = await fresh.Db.Attempts.Include(a => a.Questions).ThenInclude(q => q.Submissions)
            .SingleAsync(a => a.Id == attempt.Id);
        Assert.Equal(AttemptStatus.Ended, saved.Status);
        Assert.Equal(1m, saved.FirstScore);
        Assert.Equal(1, saved.Questions.Single().AnswerCount);
        Assert.Single(saved.Questions.Single().Submissions);
        Assert.Single(saved.Questions.Single().Submissions, x => x.IsFinal);
    }

    [Fact]
    public async Task C_ContextCuHetGio_KhongHoiSinhLuotDaHuy()
    {
        using var h = new Harness();
        var s = await TestAsync(h);
        var attempt = await h.Attempts.StartAsync(s.Assignment.Id, s.Student);
        using var stale = new Harness();
        await stale.Attempts.GetForLearnerAsync(attempt.Id, s.Student);
        using var lecturer = new Harness();
        await Retakes(lecturer).CancelAsync(attempt.Id, "Sự cố máy", s.Lecturer);

        stale.Clock.UtcNow = attempt.DeadlineAt!.Value;
        var loaded = await stale.Attempts.GetForLearnerAsync(attempt.Id, s.Student);
        await stale.Attempts.FinishAsync(attempt.Id, s.Student);

        Assert.Equal(AttemptStatus.Cancelled, loaded.Status);
        using var fresh = new Harness();
        var saved = await fresh.Db.Attempts.SingleAsync(a => a.Id == attempt.Id);
        Assert.Equal(AttemptStatus.Cancelled, saved.Status);
        Assert.Equal("Sự cố máy", saved.CancelReason);
        Assert.Null(saved.EndReason);
    }

    [Fact]
    public async Task B3_B4_ContextCuHetGio_GiuDapAnMoiDaXacNhan()
    {
        using var h = new Harness();
        var s = await TestAsync(h);
        var attempt = await h.Attempts.StartAsync(s.Assignment.Id, s.Student);
        var question = attempt.Questions.Single();
        await h.Attempts.SaveDraftAsync(attempt.Id, question.Id, [AssignmentTests.WrongOption(question)], s.Student);
        using var stale = new Harness();
        await stale.Attempts.GetForLearnerAsync(attempt.Id, s.Student);
        using var other = new Harness();
        await other.Attempts.AnswerAsync(attempt.Id, question.Id, Guid.NewGuid(),
            [AssignmentTests.CorrectOption(question)], s.Student);

        stale.Clock.UtcNow = attempt.DeadlineAt!.Value;
        await stale.Attempts.GetForLearnerAsync(attempt.Id, s.Student);

        using var fresh = new Harness();
        var saved = await fresh.Db.Attempts.Include(a => a.Questions).ThenInclude(q => q.Submissions)
            .SingleAsync(a => a.Id == attempt.Id);
        Assert.Equal(1m, saved.FirstScore);
        Assert.Equal(1, saved.Questions.Single().AnswerCount);
        Assert.True(Assert.Single(saved.Questions.Single().Submissions, x => x.IsFinal).IsCorrect);
    }

    [Fact]
    public async Task P4_StartFinishXenKe_HaiContextKhongVuotMaxAttempts()
    {
        using var h = new Harness();
        var s = await h.ScenarioAsync(questions: 1, settings: new PracticeSettings(null, null, 2));
        using var tab1 = new Harness();
        using var tab2 = new Harness();
        var first = await tab1.Attempts.StartAsync(s.Assignment.Id, s.Student);
        await tab2.Attempts.GetOverviewAsync(s.Assignment.Id, s.Student);
        await tab1.Attempts.FinishAsync(first.Id, s.Student);
        var second = await tab1.Attempts.StartAsync(s.Assignment.Id, s.Student);
        await tab1.Attempts.FinishAsync(second.Id, s.Student);

        // (P4, C2) Tab thứ hai vẫn giữ danh sách lúc mới có một lượt đang mở.
        await Assert.ThrowsAsync<BusinessException>(() => tab2.Attempts.StartAsync(s.Assignment.Id, s.Student));
        await Assert.ThrowsAsync<BusinessException>(() => tab1.Attempts.StartAsync(s.Assignment.Id, s.Student));
        using var fresh = new Harness();
        var attempts = await fresh.Db.Attempts.Where(a => a.Participant.AssignmentId == s.Assignment.Id).ToListAsync();
        Assert.Equal(2, attempts.Count);
        Assert.Equal(new[] { 1, 2 }, attempts.Select(a => a.AttemptNumber).OrderBy(n => n));
    }

    [Fact]
    public async Task P4_HaiContextCungStart_ChiCoMotParticipantVaLuotMo()
    {
        using var h = new Harness();
        var s = await h.ScenarioAsync(questions: 1, settings: new PracticeSettings(null, null, 1));
        using var tab1 = new Harness();
        using var tab2 = new Harness();
        var results = await Task.WhenAll(tab1.Attempts.StartAsync(s.Assignment.Id, s.Student),
            tab2.Attempts.StartAsync(s.Assignment.Id, s.Student));

        Assert.Equal(results[0].Id, results[1].Id);
        using var fresh = new Harness();
        Assert.Single(await fresh.Db.Participants.Where(p => p.AssignmentId == s.Assignment.Id).ToListAsync());
        Assert.Single(await fresh.Db.Attempts.Where(a => a.Participant.AssignmentId == s.Assignment.Id).ToListAsync());
    }

    [Fact]
    public async Task G4_StartSauKhiContextKhacDong_BiTuChoi()
    {
        using var h = new Harness();
        var s = await h.ScenarioAsync(questions: 1);
        using var stale = new Harness();
        stale.Clock.UtcNow = h.Clock.UtcNow; // cùng giờ với kịch bản, để lỗi đúng là "bài đã đóng" chứ không phải "chưa được giao"
        await stale.Attempts.GetOverviewAsync(s.Assignment.Id, s.Student);
        using var lecturer = new Harness();
        lecturer.Clock.UtcNow = h.Clock.UtcNow;
        await lecturer.Assignments.CloseNowAsync(s.Assignment.Id, s.Lecturer);

        await Assert.ThrowsAsync<BusinessException>(() => stale.Attempts.StartAsync(s.Assignment.Id, s.Student));
        using var fresh = new Harness();
        Assert.Empty(await fresh.Db.Attempts.Where(a => a.Participant.AssignmentId == s.Assignment.Id).ToListAsync());
    }

    [Fact]
    public async Task G4_StartSauDoiHan_DungHanMoiTuContextKhac()
    {
        using var h = new Harness();
        var s = await h.ScenarioAsync(questions: 1);
        using var stale = new Harness();
        await stale.Attempts.GetOverviewAsync(s.Assignment.Id, s.Student);
        using var lecturer = new Harness();
        lecturer.Clock.UtcNow = h.Clock.UtcNow;
        var newDue = h.Clock.UtcNow.AddMinutes(30);
        await lecturer.Assignments.ChangeDueAsync(s.Assignment.Id, newDue, "Điều chỉnh hạn", s.Lecturer);

        var attempt = await stale.Attempts.StartAsync(s.Assignment.Id, s.Student);

        Assert.Equal(newDue, attempt.DeadlineAt);
        using var fresh = new Harness();
        Assert.Equal(newDue, (await fresh.Db.Attempts.SingleAsync(a => a.Id == attempt.Id)).DeadlineAt);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task B3_41A_AnswerVaDraftSauRemove_BiTuChoi(bool draft)
    {
        using var h = new Harness();
        var s = await TestAsync(h);
        var attempt = await h.Attempts.StartAsync(s.Assignment.Id, s.Student);
        var question = attempt.Questions.Single();
        using var stale = new Harness();
        await stale.Attempts.GetForLearnerAsync(attempt.Id, s.Student);
        await stale.Db.ClassMembers.Where(m => m.ClassroomId == s.Room.Id && m.UserId == s.Student).LoadAsync();
        using var lecturer = new Harness();
        // (41A) Phải nộp trước khi xóa; tab cũ vẫn đang giữ trạng thái InProgress và Active.
        await lecturer.Attempts.FinishAsync(attempt.Id, s.Student);
        var member = await lecturer.Db.ClassMembers.SingleAsync(m => m.ClassroomId == s.Room.Id && m.UserId == s.Student);
        await lecturer.Classrooms.RemoveMemberAsync(member.Id, s.Lecturer);

        if (draft)
            await Assert.ThrowsAsync<ForbiddenException>(() => stale.Attempts.SaveDraftAsync(attempt.Id, question.Id,
                [AssignmentTests.CorrectOption(question)], s.Student));
        else
            await Assert.ThrowsAsync<ForbiddenException>(() => stale.Attempts.AnswerAsync(attempt.Id, question.Id,
                Guid.NewGuid(), [AssignmentTests.CorrectOption(question)], s.Student));
        // (41A) Rời lớp vẫn được xem lại lịch sử lượt của chính mình.
        Assert.Equal(AttemptStatus.Ended, (await stale.Attempts.GetForLearnerAsync(attempt.Id, s.Student)).Status);
        using var fresh = new Harness();
        Assert.Empty(await fresh.Db.AnswerSubmissions.Where(x => x.AttemptQuestionId == question.Id).ToListAsync());
        Assert.Null((await fresh.Db.AttemptQuestions.SingleAsync(q => q.Id == question.Id)).DraftJson);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task G3_41A_StartSauRemoveHoacLeave_KhongDungMembershipCu(bool leave)
    {
        using var h = new Harness();
        var s = await h.ScenarioAsync(questions: 1);
        using var stale = new Harness();
        await stale.Attempts.GetOverviewAsync(s.Assignment.Id, s.Student);
        using var other = new Harness();
        if (leave) await other.Classrooms.LeaveAsync(s.Room.Id, s.Student);
        else
        {
            var member = await other.Db.ClassMembers.SingleAsync(m => m.ClassroomId == s.Room.Id && m.UserId == s.Student);
            await other.Classrooms.RemoveMemberAsync(member.Id, s.Lecturer);
        }

        await Assert.ThrowsAsync<ForbiddenException>(() => stale.Attempts.StartAsync(s.Assignment.Id, s.Student));
    }

    [Fact]
    public async Task C_41A_RetakeStartSauRemove_BiTuChoiDuDaDuyet()
    {
        using var h = new Harness();
        var s = await TestAsync(h);
        var original = await h.Attempts.StartAsync(s.Assignment.Id, s.Student);
        await h.Attempts.FinishAsync(original.Id, s.Student);
        await h.Assignments.CloseNowAsync(s.Assignment.Id, s.Lecturer);
        var request = await Retakes(h).RequestAsync(s.Assignment.Id, "Mất mạng", s.Student);
        await Retakes(h).DecideAsync(request.Id, true, h.Clock.UtcNow.AddHours(1), s.Lecturer);
        using var stale = new Harness();
        await stale.Attempts.GetOverviewAsync(s.Assignment.Id, s.Student);
        using var lecturer = new Harness();
        var member = await lecturer.Db.ClassMembers.SingleAsync(m => m.ClassroomId == s.Room.Id && m.UserId == s.Student);
        await lecturer.Classrooms.RemoveMemberAsync(member.Id, s.Lecturer);

        await Assert.ThrowsAsync<ForbiddenException>(() => Retakes(stale).StartAsync(request.Id, s.Student));
        using var fresh = new Harness();
        Assert.False(await fresh.Db.Attempts.AnyAsync(a => a.RetakeRequestId == request.Id));
    }

    [Fact]
    public async Task C_33A_B3a_LamLaiHopLe_VanLuuNhapVaTraLoiKhiBaiDaDong()
    {
        using var h = new Harness();
        var s = await TestAsync(h);
        var original = await h.Attempts.StartAsync(s.Assignment.Id, s.Student);
        await h.Attempts.FinishAsync(original.Id, s.Student);
        await h.Assignments.CloseNowAsync(s.Assignment.Id, s.Lecturer);
        var request = await Retakes(h).RequestAsync(s.Assignment.Id, "Mất mạng", s.Student);
        await Retakes(h).DecideAsync(request.Id, true, h.Clock.UtcNow.AddHours(1), s.Lecturer);
        var retake = await Retakes(h).StartAsync(request.Id, s.Student);
        var question = retake.Questions.Single();
        using var other = new Harness();
        other.Clock.UtcNow = h.Clock.UtcNow;

        await other.Attempts.SaveDraftAsync(retake.Id, question.Id, [AssignmentTests.CorrectOption(question)], s.Student);
        await other.Attempts.AnswerAsync(retake.Id, question.Id, Guid.NewGuid(),
            [AssignmentTests.CorrectOption(question)], s.Student);

        using var fresh = new Harness();
        Assert.Equal(AssignmentStatus.Closed, (await fresh.Db.Assignments.SingleAsync(a => a.Id == s.Assignment.Id)).Status);
        Assert.Equal(1m, (await fresh.Db.Attempts.SingleAsync(a => a.Id == retake.Id)).FirstScore);
        Assert.Single(await fresh.Db.AnswerSubmissions.Where(x => x.AttemptQuestionId == question.Id && x.IsFinal).ToListAsync());
    }

    [Fact]
    public async Task B1_TestStatusChiDoc_TrangAssignmentMoiTaoParticipant()
    {
        using var h = new Harness();
        var s = await TestAsync(h, start: false);
        var status = await h.Attempts.GetTestStatusAsync(s.Assignment.Id, s.Student);
        Assert.False(status.Started);
        Assert.False(status.CanStart);
        Assert.Empty(await h.Db.Participants.Where(p => p.AssignmentId == s.Assignment.Id).ToListAsync());

        var page = await h.Attempts.GetAssignmentPageAsync(s.Assignment.Id, s.Student);
        Assert.True(page.IsWaiting);
        Assert.Single(await h.Db.Participants.Where(p => p.AssignmentId == s.Assignment.Id).ToListAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task C_HuyLuot_XoaNhanHetHanHoacGvDong(bool closed)
    {
        using var h = new Harness();
        var s = await TestAsync(h);
        var attempt = await h.Attempts.StartAsync(s.Assignment.Id, s.Student);
        if (closed) await h.Assignments.CloseNowAsync(s.Assignment.Id, s.Lecturer);
        else
        {
            h.Clock.UtcNow = attempt.DeadlineAt!.Value;
            await h.Attempts.GetForLearnerAsync(attempt.Id, s.Student);
        }
        var label = closed ? CompletionLabels.ClosedByLecturer : CompletionLabels.Expired;
        Assert.True(attempt.Participant.CompletionLabels.HasFlag(label));
        using var lecturer = new Harness();
        lecturer.Clock.UtcNow = h.Clock.UtcNow;
        await Retakes(lecturer).CancelAsync(attempt.Id, "Hủy lượt gặp sự cố", s.Lecturer);

        using var fresh = new Harness();
        var participant = await fresh.Db.Participants.SingleAsync(p => p.Id == attempt.ParticipantId);
        Assert.False(participant.CompletionLabels.HasFlag(CompletionLabels.Expired));
        Assert.False(participant.CompletionLabels.HasFlag(CompletionLabels.ClosedByLecturer));
        Assert.Equal(CompletionStatus.NotStarted, participant.CompletionStatus);
        Assert.False((await fresh.Gradebook.GetReportAsync(s.Assignment.Id, s.Lecturer)).Rows.Single().Labels.HasFlag(label));
    }

    [Fact]
    public async Task C_HuyLuotLamLai_KhoiPhucNhanCuaLuotConLai()
    {
        using var h = new Harness();
        var s = await TestAsync(h);
        var original = await h.Attempts.StartAsync(s.Assignment.Id, s.Student);
        h.Clock.UtcNow = original.DeadlineAt!.Value;
        await h.Attempts.GetForLearnerAsync(original.Id, s.Student);
        var request = await Retakes(h).RequestAsync(s.Assignment.Id, "Mất mạng", s.Student);
        await Retakes(h).DecideAsync(request.Id, true, h.Clock.UtcNow.AddHours(1), s.Lecturer);
        var retake = await Retakes(h).StartAsync(request.Id, s.Student);
        await h.Attempts.FinishAsync(retake.Id, s.Student);
        Assert.False(retake.Participant.CompletionLabels.HasFlag(CompletionLabels.Expired));
        using var lecturer = new Harness();
        lecturer.Clock.UtcNow = h.Clock.UtcNow;
        await Retakes(lecturer).CancelAsync(retake.Id, "Hủy lượt gặp sự cố", s.Lecturer);

        using var fresh = new Harness();
        var participant = await fresh.Db.Participants.SingleAsync(p => p.Id == original.ParticipantId);
        Assert.True(participant.CompletionLabels.HasFlag(CompletionLabels.Expired));
        Assert.False(participant.CompletionLabels.HasFlag(CompletionLabels.ClosedByLecturer));
    }
}

// Không cần SQL Server để kiểm chứng điều kiện bảo vệ token và dữ liệu lịch sử hỏng.
public class Tang2DevLinkAndProjectionTests
{
    [Theory]
    [InlineData(true, "127.0.0.1", true)]
    [InlineData(true, "::1", true)]
    [InlineData(true, "::ffff:127.0.0.1", true)]
    [InlineData(true, "192.168.1.20", false)]
    [InlineData(true, "203.0.113.7", false)]
    [InlineData(true, null, false)]
    [InlineData(false, "127.0.0.1", false)]
    [InlineData(false, "::1", false)]
    public void DevLink_ChiDevelopmentVaLoopback(bool development, string? remote, bool expected) =>
        Assert.Equal(expected, AccountController.CanShowDevLink(development, remote == null ? null : IPAddress.Parse(remote)));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void C2_61_66_KetThucLap_KhongChamLaiHoacHoiSinhLuotHuy(bool cancelled)
    {
        var now = new DateTime(2026, 11, 5, 9, 0, 0, DateTimeKind.Utc);
        var snapshot = new QuestionSnapshot(1, 0, QuestionType.MultipleChoice, "Chọn đáp án", null, 2,
            [new OptionSnapshot(1, 0, "Đúng", true), new OptionSnapshot(2, 1, "Sai", false)]);
        var attempt = new Attempt
        {
            Status = cancelled ? AttemptStatus.Cancelled : AttemptStatus.InProgress, DeadlineAt = now,
            Participant = new Participant { Assignment = new Assignment { Mode = AssignmentMode.Test } },
            Questions = [new AttemptQuestion { SnapshotJson = snapshot.ToJson(), DraftJson = "{\"Selected\":[1]}" }]
        };

        AttemptRules.End(attempt, attempt.Participant, AttemptEndReason.DueDate, now);
        AttemptRules.End(attempt, attempt.Participant, AttemptEndReason.ClosedByLecturer, now.AddMinutes(1));

        Assert.Equal(cancelled ? AttemptStatus.Cancelled : AttemptStatus.Ended, attempt.Status);
        Assert.Equal(cancelled ? 0 : 1, attempt.Questions.Single().Submissions.Count);
        Assert.Equal(cancelled ? 0m : 2m, attempt.FirstScore);
        Assert.Equal(!cancelled, attempt.Participant.CompletionLabels.HasFlag(CompletionLabels.Expired));
        Assert.False(attempt.Participant.CompletionLabels.HasFlag(CompletionLabels.ClosedByLecturer));
    }

    [Fact]
    public void B3_LichSuCoHaiIsFinal_HienLanMoiNhat()
    {
        var snapshot = new QuestionSnapshot(1, 0, QuestionType.MultipleChoice, "Chọn đáp án", null, 1,
            [new OptionSnapshot(1, 0, "Đúng", true), new OptionSnapshot(2, 1, "Sai", false)]);
        var attempt = new Attempt
        {
            Status = AttemptStatus.Ended,
            Participant = new Participant { Assignment = new Assignment { Mode = AssignmentMode.Practice } }
        };
        attempt.Questions.Add(new AttemptQuestion
        {
            Attempt = attempt, SnapshotJson = snapshot.ToJson(), AnswerCount = 2,
            Submissions =
            [
                new AnswerSubmission { Id = 1, IsFinal = true, IsCorrect = false,
                    SubmittedAt = new DateTime(2026, 11, 5, 8, 0, 0, DateTimeKind.Utc),
                    PayloadJson = JsonSerializer.Serialize(new ChoiceAnswer([2])) },
                new AnswerSubmission { Id = 2, IsFinal = true, IsCorrect = true,
                    SubmittedAt = new DateTime(2026, 11, 5, 8, 1, 0, DateTimeKind.Utc),
                    PayloadJson = JsonSerializer.Serialize(new ChoiceAnswer([1])) }
            ]
        });

        var question = Assert.Single(AttemptService.ProjectQuestions(attempt, false));

        Assert.Equal(new[] { 1 }, question.Selected);
        Assert.True(question.IsCorrect);
    }
}
