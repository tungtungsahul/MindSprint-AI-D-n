using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MindSprintAI_ASP.NET_MVC.Data.Entities;
using MindSprintAI_ASP.NET_MVC.Services;

namespace MindSprintAI_ASP.NET_MVC.Tests;

[Collection("db")]
public class Tang2Tests
{
    private static async Task<Scenario> TestAsync(Harness h, bool start = true, int duration = 45)
    {
        var s = await h.ScenarioAsync(questions: 6);
        var a = await h.Assignments.CreateTestAsync(s.Room.Id, s.Quiz.Id, new TestSettings(null, duration), s.Lecturer);
        if (start) await h.Assignments.StartTestAsync(a.Id, s.Lecturer);
        return s with { Assignment = a };
    }

    private static RetakeService Retakes(Harness h) => new(h.Db, h.Assignments, h.Attempts, h.Clock);

    [Fact]
    public async Task A1_DaoMotLan_GiuNguyenKhiTaiLai()
    {
        using var h = new Harness();
        var s = await h.ScenarioAsync(questions: 8, settings: new PracticeSettings(null, null, null, ShuffleQuestions: true, ShuffleOptions: true));
        var orders = new HashSet<string>();
        for (var i = 0; i < 6; i++)
        {
            var a = await h.Attempts.StartAsync(s.Assignment.Id, s.Student);
            var before = a.Questions.OrderBy(q => q.DisplayOrder).Select(q => (q.QuestionId, q.OptionOrderJson)).ToArray();
            using var fresh = new Harness();
            var loaded = await fresh.Attempts.GetForLearnerAsync(a.Id, s.Student);
            Assert.Equal(before, loaded.Questions.OrderBy(q => q.DisplayOrder).Select(q => (q.QuestionId, q.OptionOrderJson)).ToArray());
            orders.Add(string.Join(",", before.Select(q => q.QuestionId)));
            await h.Attempts.FinishAsync(a.Id, s.Student);
        }
        Assert.True(orders.Count > 1);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A2_A3_GiuThuTuGoc(bool keep)
    {
        using var h = new Harness();
        var s = await h.ScenarioAsync(settings: new PracticeSettings(null, null, null, ShuffleOptions: keep));
        foreach (var q in s.Quiz.Questions) q.KeepOptionOrder = keep;
        await h.Db.SaveChangesAsync();
        var a = await h.Attempts.StartAsync(s.Assignment.Id, s.Student);
        Assert.Equal(s.Quiz.Questions.OrderBy(q => q.OrderIndex).Select(q => q.Id), a.Questions.OrderBy(q => q.DisplayOrder).Select(q => q.QuestionId));
        foreach (var q in a.Questions)
            Assert.Equal(QuestionSnapshot.FromJson(q.SnapshotJson).Options.Select(o => o.Id), JsonSerializer.Deserialize<int[]>(q.OptionOrderJson!)!);
    }

    [Fact]
    public async Task B1_PhongCho_TaoParticipant_ChuaDuocLam()
    {
        using var h = new Harness();
        var s = await TestAsync(h, start: false);
        await h.Attempts.GetAssignmentPageAsync(s.Assignment.Id, s.Student);
        var status = await h.Attempts.GetTestStatusAsync(s.Assignment.Id, s.Student);
        Assert.False(status.Started);
        Assert.Single(await h.Db.Participants.Where(p => p.AssignmentId == s.Assignment.Id).ToListAsync());
        await Assert.ThrowsAsync<BusinessException>(() => h.Attempts.StartAsync(s.Assignment.Id, s.Student));
        Assert.Empty(await h.Db.Attempts.Where(a => a.Participant.AssignmentId == s.Assignment.Id).ToListAsync());
    }

    [Fact]
    public async Task B2_VaoMuon_GioChung_BatDauChiMotLan()
    {
        using var h = new Harness();
        var s = await TestAsync(h);
        var start = s.Assignment.TestStartedAt!.Value;
        await Assert.ThrowsAsync<BusinessException>(() => h.Assignments.StartTestAsync(s.Assignment.Id, s.Lecturer));
        h.Clock.UtcNow = start.AddMinutes(10);
        var a = await h.Attempts.StartAsync(s.Assignment.Id, s.Student);
        Assert.Equal(start.AddMinutes(45), a.DeadlineAt);
        h.Clock.UtcNow = start.AddMinutes(45);
        await Assert.ThrowsAsync<BusinessException>(() => h.Attempts.StartAsync(s.Assignment.Id, s.Student));
    }

    [Fact]
    public async Task B3_B5_DoiDapAn_ChiChamLanCuoi_KhongLoKetQua()
    {
        using var h = new Harness();
        var s = await TestAsync(h);
        var a = await h.Attempts.StartAsync(s.Assignment.Id, s.Student);
        var q = a.Questions.First();
        var first = await h.Attempts.AnswerAsync(a.Id, q.Id, Guid.NewGuid(), [AssignmentTests.CorrectOption(q)], s.Student);
        Assert.Equal("{}", JsonSerializer.Serialize(first));
        await h.Attempts.AnswerAsync(a.Id, q.Id, Guid.NewGuid(), [AssignmentTests.WrongOption(q)], s.Student);
        Assert.Equal(0m, a.FirstScore);
        Assert.Equal(0m, q.FirstScore);
        Assert.Equal(0m, q.LatestScore);
        Assert.Single(q.Submissions, x => x.IsFinal);
        Assert.False(q.Submissions.Single(x => x.IsFinal).IsCorrect);
        Assert.All(await h.Attempts.GetPlayQuestionsAsync(a, s.Student), item =>
        {
            var json = JsonSerializer.Serialize(item);
            Assert.DoesNotContain("IsCorrect", json);
            Assert.DoesNotContain("CorrectIds", json);
            Assert.DoesNotContain("Explanation", json);
        });
    }

    [Fact]
    public async Task B4_HetGio_ChamNhapChuaXacNhan_GiuLanXacNhanCuoi()
    {
        using var h = new Harness();
        var s = await TestAsync(h);
        foreach (var q in s.Quiz.Questions) q.Type = QuestionType.MultipleChoice;
        await h.Db.SaveChangesAsync();
        var a = await h.Attempts.StartAsync(s.Assignment.Id, s.Student);
        var qs = a.Questions.ToList();
        await h.Attempts.SaveDraftAsync(a.Id, qs[0].Id, [AssignmentTests.CorrectOption(qs[0])], s.Student);
        await h.Attempts.AnswerAsync(a.Id, qs[1].Id, Guid.NewGuid(), [AssignmentTests.WrongOption(qs[1])], s.Student);
        await h.Attempts.SaveDraftAsync(a.Id, qs[1].Id, [AssignmentTests.CorrectOption(qs[1])], s.Student);
        h.Clock.UtcNow = a.DeadlineAt!.Value;
        await h.Attempts.GetForLearnerAsync(a.Id, s.Student);
        Assert.Equal(1m, a.FirstScore);
        Assert.Equal(1m, qs[0].FirstScore);
        Assert.Equal(0m, qs[1].FirstScore);
        Assert.Equal(CompletionStatus.SubmittedAwaitingScore, a.Participant.CompletionStatus);
    }

    [Fact]
    public async Task B6_CongBoDiemVaDapAnDocLap()
    {
        using var h = new Harness();
        var s = await TestAsync(h);
        var a = await h.Attempts.StartAsync(s.Assignment.Id, s.Student);
        await h.Attempts.FinishAsync(a.Id, s.Student);
        await h.Assignments.CloseNowAsync(s.Assignment.Id, s.Lecturer); // công bố chỉ sau khi phiên kết thúc
        Assert.False(AttemptService.CanShowScores(a.Participant));
        Assert.All(await h.Attempts.GetPlayQuestionsAsync(a, s.Student), q => Assert.Null(q.CorrectIds));
        await h.Assignments.PublishScoresAsync(s.Assignment.Id, s.Lecturer);
        Assert.True(AttemptService.CanShowScores(a.Participant));
        Assert.Equal(CompletionStatus.Completed, a.Participant.CompletionStatus);
        Assert.All(await h.Attempts.GetPlayQuestionsAsync(a, s.Student), q => Assert.Null(q.CorrectIds));
        await h.Assignments.PublishAnswersAsync(s.Assignment.Id, false, s.Lecturer);
        Assert.All(await h.Attempts.GetPlayQuestionsAsync(a, s.Student), q => Assert.NotNull(q.CorrectIds));
    }

    [Fact]
    public async Task B7_NguoiNgoai_StatusVaStartBi403()
    {
        using var h = new Harness();
        var s = await TestAsync(h);
        var outsider = await h.UserAsync("Ngoài lớp");
        await Assert.ThrowsAsync<ForbiddenException>(() => h.Attempts.GetTestStatusAsync(s.Assignment.Id, outsider));
        await Assert.ThrowsAsync<ForbiddenException>(() => h.Attempts.StartAsync(s.Assignment.Id, outsider));
        await Assert.ThrowsAsync<ForbiddenException>(() => h.Assignments.StartTestAsync(s.Assignment.Id, s.Student));
    }

    [Fact]
    public async Task B_CongBoKhiPhienChuaKetThuc_BiChan()
    {
        using var h = new Harness();
        var s = await TestAsync(h, start: false);
        await Assert.ThrowsAsync<BusinessException>(() => h.Assignments.PublishAnswersAsync(s.Assignment.Id, true, s.Lecturer));
        await h.Assignments.StartTestAsync(s.Assignment.Id, s.Lecturer);
        var early = await h.Attempts.StartAsync(s.Assignment.Id, s.Student);
        await h.Attempts.FinishAsync(early.Id, s.Student); // nộp sớm, phiên vẫn còn giờ
        await Assert.ThrowsAsync<BusinessException>(() => h.Assignments.PublishAnswersAsync(s.Assignment.Id, true, s.Lecturer));
        await Assert.ThrowsAsync<BusinessException>(() => h.Assignments.PublishScoresAsync(s.Assignment.Id, s.Lecturer));

        h.Clock.UtcNow = h.Clock.UtcNow.AddMinutes(46);
        await h.Assignments.PublishScoresAsync(s.Assignment.Id, s.Lecturer);
        await h.Assignments.PublishAnswersAsync(s.Assignment.Id, true, s.Lecturer);
    }

    private static async Task<(Scenario Scenario, Attempt Original, RetakeRequest Request)> ApprovedAsync(Harness h)
    {
        var s = await TestAsync(h);
        var a = await h.Attempts.StartAsync(s.Assignment.Id, s.Student);
        var q = a.Questions.First();
        await h.Attempts.AnswerAsync(a.Id, q.Id, Guid.NewGuid(), [AssignmentTests.CorrectOption(q)], s.Student);
        await h.Attempts.FinishAsync(a.Id, s.Student);
        await h.Assignments.CloseNowAsync(s.Assignment.Id, s.Lecturer); // công bố chỉ sau khi phiên kết thúc
        var request = await Retakes(h).RequestAsync(s.Assignment.Id, "Mất mạng", s.Student);
        await Retakes(h).DecideAsync(request.Id, true, h.Clock.UtcNow.AddHours(2), s.Lecturer);
        return (s, a, request);
    }

    [Fact]
    public async Task C68_DuocDuyetLamLaiChuaBatDau_KhongXemDuocDapAnDaCongBo()
    {
        using var h = new Harness();
        var (s, a, _) = await ApprovedAsync(h);
        await h.Assignments.PublishAnswersAsync(s.Assignment.Id, true, s.Lecturer);

        var review = await h.Attempts.GetPlayQuestionsAsync(await h.Attempts.GetForLearnerAsync(a.Id, s.Student), s.Student);

        Assert.All(review, q => { Assert.Null(q.CorrectIds); Assert.Null(q.IsCorrect); Assert.Null(q.Explanation); });
    }

    [Fact]
    public async Task C1_DanhSachXinLamLaiCuaGiangVienChotHetHanBangKhoaHocVien()
    {
        // 13/11/2026: chốt hết hạn ở trang chi tiết bài giờ đi qua khóa học viên (không ghi đè lần "Bắt đầu làm lại" giây cuối).
        using var h = new Harness();
        var (s, _, r) = await ApprovedAsync(h);
        h.Clock.UtcNow = r.WindowEndsAt!.Value;
        var list = await Retakes(h).ListForLecturerAsync(s.Assignment.Id, s.Lecturer);
        Assert.Equal(RetakeRequestStatus.Expired, list.Single().Status);
        using var fresh = new Harness();
        var saved = await fresh.Db.Participants.Include(p => p.RetakeRequests).SingleAsync(p => p.AssignmentId == s.Assignment.Id && p.UserId == s.Student);
        Assert.Equal(RetakeRequestStatus.Expired, saved.RetakeRequests.Single().Status);
        Assert.True(saved.CompletionLabels.HasFlag(CompletionLabels.RetakeNotUsed));
    }

    [Fact]
    public async Task C1_HetKhungKhongLam_GiuDiemCuVaNhan()
    {
        using var h = new Harness();
        var (s, a, r) = await ApprovedAsync(h);
        h.Clock.UtcNow = r.WindowEndsAt!.Value;
        var report = await h.Gradebook.GetReportAsync(s.Assignment.Id, s.Lecturer);
        Assert.Equal(1m, report.Rows.Single().Score!.Raw);
        Assert.True(report.Rows.Single().Labels.HasFlag(CompletionLabels.RetakeNotUsed));
        Assert.Equal(RetakeRequestStatus.Expired, r.Status);
        Assert.Equal(AttemptStatus.Ended, a.Status);
        await Assert.ThrowsAsync<BusinessException>(() => Retakes(h).StartAsync(r.Id, s.Student));
    }

    [Fact]
    public async Task C2_C4_BatDauLamLaiKhiDaDong_ThayDiemTrongBang()
    {
        using var h = new Harness();
        var (s, old, r) = await ApprovedAsync(h);
        await h.Assignments.CloseNowAsync(s.Assignment.Id, s.Lecturer);
        var a = await Retakes(h).StartAsync(r.Id, s.Student);
        Assert.Equal(AttemptKind.Retake, a.Kind);
        Assert.Equal(r.Id, a.RetakeRequestId);
        Assert.Equal(h.Clock.UtcNow.AddMinutes(45), a.DeadlineAt);
        Assert.Equal(old.Id, (await h.Db.Attempts.FindAsync(old.Id))!.Id);
        var report = await h.Gradebook.GetReportAsync(s.Assignment.Id, s.Lecturer);
        Assert.Null(report.Rows.Single().Score);
        Assert.Equal(a.Id, report.Rows.Single().LatestAttemptId);
        await h.Attempts.FinishAsync(a.Id, s.Student);
        Assert.Equal(0m, (await h.Gradebook.GetReportAsync(s.Assignment.Id, s.Lecturer)).Rows.Single().Score!.Raw);
    }

    [Fact]
    public async Task C3_TrenLamLai_HuyLuotGiaiPhongCho()
    {
        using var h = new Harness();
        var (s, _, r) = await ApprovedAsync(h);
        var a = await Retakes(h).StartAsync(r.Id, s.Student);
        await h.Attempts.FinishAsync(a.Id, s.Student);
        await Assert.ThrowsAsync<BusinessException>(() => Retakes(h).RequestAsync(s.Assignment.Id, "Thêm lần nữa", s.Student));
        await Retakes(h).CancelAsync(a.Id, "Sự cố máy", s.Lecturer);
        Assert.Equal(AttemptStatus.Cancelled, a.Status);
        var next = await Retakes(h).RequestAsync(s.Assignment.Id, "Cấp lại", s.Student);
        await Retakes(h).DecideAsync(next.Id, true, h.Clock.UtcNow.AddMinutes(10), s.Lecturer);
        var replacement = await Retakes(h).StartAsync(next.Id, s.Student);
        Assert.Equal(next.WindowEndsAt, replacement.DeadlineAt);
        Assert.True(replacement.AttemptNumber > a.AttemptNumber);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(181)]
    public async Task B1_ThoiLuongNgoai1Den180BiTuChoi(int duration)
    {
        using var h = new Harness();
        var s = await h.ScenarioAsync();
        await Assert.ThrowsAsync<BusinessException>(() => h.Assignments.CreateTestAsync(s.Room.Id, s.Quiz.Id,
            new TestSettings(null, duration), s.Lecturer));
        Assert.False(await h.Db.Assignments.AnyAsync(a => a.ClassroomId == s.Room.Id && a.Mode == AssignmentMode.Test));
    }

    [Fact]
    public async Task B1_MacDinhChiQuiz_TuChoiNguoiLa_KhongCoSoLuotOnTap()
    {
        using var h = new Harness();
        var s = await TestAsync(h, start: false);
        Assert.True(s.Assignment.ShuffleQuestions);
        Assert.True(s.Assignment.ShuffleOptions);
        Assert.Equal(StrangerPolicy.Reject, s.Assignment.StrangerPolicy);
        Assert.Equal(AssignmentForm.QuizOnly, s.Assignment.Form);
        Assert.False(s.Assignment.AllowAiAssistant);
        Assert.Null(s.Assignment.MaxAttempts);
        Assert.Null(s.Assignment.ScoresPublishedAt);
        Assert.Null(s.Assignment.AnswersPublishedAt);
        var page = await h.Attempts.GetAssignmentPageAsync(s.Assignment.Id, s.Student);
        Assert.True(page.IsWaiting);
        Assert.False(page.CanStart);
    }

    [Fact]
    public async Task B3_ChonNhieuXacNhanLai_GiuLanCuoi_ChongGuiTrung()
    {
        using var h = new Harness();
        var s = await TestAsync(h);
        foreach (var q in s.Quiz.Questions) q.Type = QuestionType.MultipleChoice;
        await h.Db.SaveChangesAsync();
        var a = await h.Attempts.StartAsync(s.Assignment.Id, s.Student);
        var aq = a.Questions.First();
        await h.Attempts.AnswerAsync(a.Id, aq.Id, Guid.NewGuid(), [AssignmentTests.WrongOption(aq)], s.Student);
        var id = Guid.NewGuid();
        var result = await h.Attempts.AnswerAsync(a.Id, aq.Id, id, [AssignmentTests.CorrectOption(aq)], s.Student);
        var duplicate = await h.Attempts.AnswerAsync(a.Id, aq.Id, id, [AssignmentTests.CorrectOption(aq)], s.Student);
        Assert.Equal(result, duplicate);
        Assert.Equal("{}", JsonSerializer.Serialize(duplicate));
        Assert.Equal(2, aq.Submissions.Count);
        Assert.Single(aq.Submissions, x => x.IsFinal && x.IsCorrect == true);
        Assert.Equal(1m, a.FirstScore);
        await h.Attempts.FinishAsync(a.Id, s.Student);
        await Assert.ThrowsAsync<BusinessException>(() => h.Attempts.AnswerAsync(a.Id, aq.Id, Guid.NewGuid(), [AssignmentTests.WrongOption(aq)], s.Student));
    }

    [Fact]
    public async Task B3_NopKhiConCauTrong_Diem0_ChoCongBo()
    {
        using var h = new Harness();
        var s = await TestAsync(h);
        var a = await h.Attempts.StartAsync(s.Assignment.Id, s.Student);
        await h.Attempts.FinishAsync(a.Id, s.Student);
        using var fresh = new Harness();
        var loaded = await fresh.Attempts.GetForLearnerAsync(a.Id, s.Student);
        Assert.Equal(AttemptStatus.Ended, loaded.Status);
        Assert.Equal(0m, loaded.FirstScore);
        Assert.Equal(CompletionStatus.SubmittedAwaitingScore, loaded.Participant.CompletionStatus);
        var page = await h.Attempts.GetAssignmentPageAsync(s.Assignment.Id, s.Student);
        Assert.False(page.ShowScores);
        Assert.Null(page.Score);
        Assert.False(page.CanStart);
        Assert.True(page.CanRequestRetake);
    }

    [Fact]
    public async Task B4_AnswerQuaHan_VanGhiNhanHetGioVaChamNhap()
    {
        using var h = new Harness();
        var s = await TestAsync(h);
        foreach (var q in s.Quiz.Questions) q.Type = QuestionType.MultipleChoice;
        await h.Db.SaveChangesAsync();
        var a = await h.Attempts.StartAsync(s.Assignment.Id, s.Student);
        var aq = a.Questions.First();
        await h.Attempts.SaveDraftAsync(a.Id, aq.Id, [AssignmentTests.CorrectOption(aq)], s.Student);
        h.Clock.UtcNow = a.DeadlineAt!.Value;
        await Assert.ThrowsAsync<BusinessException>(() => h.Attempts.AnswerAsync(a.Id, aq.Id, Guid.NewGuid(), [AssignmentTests.WrongOption(aq)], s.Student));
        using var fresh = new Harness();
        var loaded = await fresh.Db.Attempts.SingleAsync(x => x.Id == a.Id);
        Assert.Equal(AttemptStatus.Ended, loaded.Status);
        Assert.Equal(1m, loaded.FirstScore);
    }

    [Fact]
    public async Task B4_DongNgay_ChamNhapVaChoCongBo()
    {
        using var h = new Harness();
        var s = await TestAsync(h);
        foreach (var q in s.Quiz.Questions) q.Type = QuestionType.MultipleChoice;
        await h.Db.SaveChangesAsync();
        var a = await h.Attempts.StartAsync(s.Assignment.Id, s.Student);
        var aq = a.Questions.First();
        await h.Attempts.SaveDraftAsync(a.Id, aq.Id, [AssignmentTests.CorrectOption(aq)], s.Student);
        await h.Assignments.CloseNowAsync(s.Assignment.Id, s.Lecturer);
        Assert.Equal(AttemptEndReason.ClosedByLecturer, a.EndReason);
        Assert.Equal(1m, a.FirstScore);
        Assert.Equal(CompletionStatus.SubmittedAwaitingScore, a.Participant.CompletionStatus);
        await Assert.ThrowsAsync<BusinessException>(() => h.Attempts.SaveDraftAsync(a.Id, aq.Id, [], s.Student));
    }

    [Fact]
    public async Task C_YeuCauCanLyDo_ChiMotYeuCau_TuChoiThiXinLaiDuoc()
    {
        using var h = new Harness();
        var s = await TestAsync(h);
        await Assert.ThrowsAsync<BusinessException>(() => Retakes(h).RequestAsync(s.Assignment.Id, "Lý do", s.Student));
        var a = await h.Attempts.StartAsync(s.Assignment.Id, s.Student);
        await h.Attempts.FinishAsync(a.Id, s.Student);
        await Assert.ThrowsAsync<BusinessException>(() => Retakes(h).RequestAsync(s.Assignment.Id, "  ", s.Student));
        var r = await Retakes(h).RequestAsync(s.Assignment.Id, "Mất mạng", s.Student);
        await Assert.ThrowsAsync<BusinessException>(() => Retakes(h).RequestAsync(s.Assignment.Id, "Thêm yêu cầu", s.Student));
        await Assert.ThrowsAsync<BusinessException>(() => Retakes(h).DecideAsync(r.Id, true, h.Clock.UtcNow, s.Lecturer));
        await Retakes(h).DecideAsync(r.Id, false, null, s.Lecturer);
        Assert.Equal(RetakeRequestStatus.Rejected, r.Status);
        Assert.Equal(s.Lecturer, r.DecidedById);
        await Retakes(h).RequestAsync(s.Assignment.Id, "Xin lại", s.Student);
    }

    [Fact]
    public async Task C_68_CongBoKhiConKhung_CanhBao_AnDapAnKhiDangLamLai()
    {
        using var h = new Harness();
        var (s, original, r) = await ApprovedAsync(h);
        await Assert.ThrowsAsync<BusinessException>(() => h.Assignments.PublishAnswersAsync(s.Assignment.Id, false, s.Lecturer));
        await h.Assignments.PublishAnswersAsync(s.Assignment.Id, true, s.Lecturer);
        // (68) đã được duyệt làm lại thì ẩn đáp án cho đến khi nộp lượt làm lại.
        Assert.All(await h.Attempts.GetPlayQuestionsAsync(original, s.Student), q => Assert.Null(q.CorrectIds));
        var retake = await Retakes(h).StartAsync(r.Id, s.Student);
        Assert.All(await h.Attempts.GetPlayQuestionsAsync(original, s.Student), q => Assert.Null(q.CorrectIds));
        Assert.All(await h.Attempts.GetPlayQuestionsAsync(retake, s.Student), q => Assert.Null(q.CorrectIds));
        await h.Attempts.FinishAsync(retake.Id, s.Student);
        Assert.All(await h.Attempts.GetPlayQuestionsAsync(original, s.Student), q => Assert.NotNull(q.CorrectIds));
    }

    [Fact]
    public async Task C_DapAnDaCongBo_VanDuyetDuocVaGhiNhanCanhBao()
    {
        using var h = new Harness();
        var s = await TestAsync(h);
        var a = await h.Attempts.StartAsync(s.Assignment.Id, s.Student);
        await h.Attempts.FinishAsync(a.Id, s.Student);
        await h.Assignments.CloseNowAsync(s.Assignment.Id, s.Lecturer); // công bố chỉ sau khi phiên kết thúc
        await h.Assignments.PublishAnswersAsync(s.Assignment.Id, false, s.Lecturer);
        var r = await Retakes(h).RequestAsync(s.Assignment.Id, "Lỗi máy", s.Student);
        await Retakes(h).DecideAsync(r.Id, true, h.Clock.UtcNow.AddHours(1), s.Lecturer);
        Assert.True(r.AnswersWerePublished);
        Assert.Equal(h.Clock.UtcNow, r.WindowStartsAt);
        Assert.Equal(0m, (await h.Gradebook.GetReportAsync(s.Assignment.Id, s.Lecturer)).Rows.Single().Score!.Raw);
    }

    [Fact]
    public async Task C_HuyLuotCanLyDoVaQuyenGv()
    {
        using var h = new Harness();
        var (s, a, r) = await ApprovedAsync(h);
        await Assert.ThrowsAsync<BusinessException>(() => Retakes(h).CancelAsync(a.Id, " ", s.Lecturer));
        await Assert.ThrowsAsync<ForbiddenException>(() => Retakes(h).CancelAsync(a.Id, "Hủy", s.Student));
        await Assert.ThrowsAsync<ForbiddenException>(() => Retakes(h).DecideAsync(r.Id, false, null, s.Student));
        var other = await h.UserAsync("Người khác");
        await Assert.ThrowsAsync<ForbiddenException>(() => Retakes(h).StartAsync(r.Id, other));
        await Assert.ThrowsAsync<ForbiddenException>(() => h.Assignments.PublishScoresAsync(s.Assignment.Id, other));
        await Assert.ThrowsAsync<ForbiddenException>(() => h.Assignments.PublishAnswersAsync(s.Assignment.Id, true, other));
        await Retakes(h).CancelAsync(a.Id, "Sự cố", s.Lecturer);
        Assert.Equal(s.Lecturer, a.CancelledById);
        Assert.Equal("Sự cố", a.CancelReason);
        Assert.Null((await h.Gradebook.GetReportAsync(s.Assignment.Id, s.Lecturer)).Rows.Single().Score);
    }

    [Fact]
    public async Task C_HaiTabBatDauLamLai_ChiTaoMotLuot()
    {
        using var h = new Harness();
        var (s, _, r) = await ApprovedAsync(h);
        using var tab1 = new Harness();
        using var tab2 = new Harness();
        tab1.Clock.UtcNow = tab2.Clock.UtcNow = h.Clock.UtcNow;
        var result = await Task.WhenAll(Retakes(tab1).StartAsync(r.Id, s.Student), Retakes(tab2).StartAsync(r.Id, s.Student));
        Assert.Equal(result[0].Id, result[1].Id);
        using var fresh = new Harness();
        Assert.Equal(1, await fresh.Db.Attempts.CountAsync(a => a.RetakeRequestId == r.Id));
    }
}
