using Microsoft.EntityFrameworkCore;
using MindSprintAI_ASP.NET_MVC.Data.Entities;
using MindSprintAI_ASP.NET_MVC.Services;

namespace MindSprintAI_ASP.NET_MVC.Tests;

// Mỗi test ứng với một tiêu chí nghiệm thu trong docs/SPEC_TANG1.md (ghi số mục ở tên).
[Collection("db")]
public class StudyLibraryTests
{
    [Fact]
    public async Task S1_ChonMotKhongCoDapAnDung_SanSangGiaoBaoLoiDungCau()
    {
        using var h = new Harness();
        var gv = await h.UserAsync("Lan");
        var set = await h.Library.CreateStudySetAsync("Bộ", gv);
        var quiz = await h.Library.CreateQuizAsync(set.Id, "Q", gv);
        await h.Library.SaveChoiceQuestionAsync(quiz.Id, null, null, Harness.Single(), gv);
        await h.Library.SaveChoiceQuestionAsync(quiz.Id, null, null, Harness.Single() with
        {
            Options = [new("A", false), new("B", false)]
        }, gv);

        var errors = await h.Library.MarkReadyAsync(quiz.Id, gv);

        Assert.Equal(["Câu 2: Câu chọn một phải có đúng 1 đáp án đúng."], errors);
        Assert.Equal(QuizStatus.Draft, (await h.Library.GetOwnedQuizAsync(quiz.Id, gv)).Status);
    }

    [Fact]
    public async Task S2_ChonMotHaiDapAnDung_BiTuChoi()
    {
        using var h = new Harness();
        var gv = await h.UserAsync("Lan");
        var quiz = await h.Library.CreateQuizAsync((await h.Library.CreateStudySetAsync("Bộ", gv)).Id, "Q", gv);
        await Assert.ThrowsAsync<BusinessException>(() => h.Library.SaveChoiceQuestionAsync(quiz.Id, null, null,
            Harness.Single() with { Options = [new("A", true), new("B", true)] }, gv));
    }

    [Fact]
    public async Task S3_KhongPhaiChuBoHoc_KhongSuaDuoc()
    {
        using var h = new Harness();
        var gv = await h.UserAsync("Lan");
        var other = await h.UserAsync("Nam");
        var quiz = await h.Library.CreateQuizAsync((await h.Library.CreateStudySetAsync("Bộ", gv)).Id, "Q", gv);
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            h.Library.SaveChoiceQuestionAsync(quiz.Id, null, null, Harness.Single(), other));
        await Assert.ThrowsAsync<ForbiddenException>(() => h.Library.RenameStudySetAsync(quiz.StudySetId, "Chiếm", other));
        await Assert.ThrowsAsync<ForbiddenException>(() => h.Library.CreateQuizAsync(quiz.StudySetId, "Lén thêm", other));
        await Assert.ThrowsAsync<ForbiddenException>(() => h.Library.DeleteStudySetAsync(quiz.StudySetId, other));
    }

    [Fact]
    public async Task S4_HaiTabCungSua_TabLuuSauBiBaoTaiLai()
    {
        using var h = new Harness();
        var gv = await h.UserAsync("Lan");
        var quiz = await h.Library.CreateQuizAsync((await h.Library.CreateStudySetAsync("Bộ", gv)).Id, "Q", gv);
        var q = await h.Library.SaveChoiceQuestionAsync(quiz.Id, null, null, Harness.Single(), gv);
        var versionSeenByBothTabs = q.Version;

        using var tab1 = new Harness();
        await tab1.Library.SaveChoiceQuestionAsync(quiz.Id, q.Id, versionSeenByBothTabs, Harness.Single("Tab 1"), gv);

        using var tab2 = new Harness();
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            tab2.Library.SaveChoiceQuestionAsync(quiz.Id, q.Id, versionSeenByBothTabs, Harness.Single("Tab 2"), gv));
        Assert.Contains("tải lại", ex.Message);
    }
}

[Collection("db")]
public class ClassroomTests
{
    [Fact]
    public async Task C1_C2_DoiMa_MaCuHetHieuLuc_MaMoiKhacMaCu()
    {
        using var h = new Harness();
        var s = await h.ScenarioAsync(questions: 1);
        var fresh = await h.Classrooms.ChangeCodeAsync(s.Room.Id, s.Lecturer);

        Assert.Matches("^[0-9]{8}$", fresh.Code);
        Assert.NotEqual(s.ClassCode, fresh.Code);
        var ex = await Assert.ThrowsAsync<BusinessException>(() => h.Codes.ResolveAsync(s.ClassCode));
        Assert.Equal("Mã không tồn tại. Hãy hỏi giảng viên mã mới.", ex.Message);
    }

    [Fact]
    public async Task C3_LopChoDuyet_HocVienChuaThayBai()
    {
        using var h = new Harness();
        var s = await h.ScenarioAsync(questions: 1, policy: ClassJoinPolicy.RequireApproval);
        Assert.Empty(await h.Assignments.ListForLearnerAsync(s.Student));
        var member = await h.Db.ClassMembers.SingleAsync(m => m.ClassroomId == s.Room.Id && m.UserId == s.Student);
        Assert.Equal(ClassMemberStatus.PendingApproval, member.Status);
    }

    [Fact]
    public async Task C4_HocVienGoiChucNangQuanLyLop_BiChan()
    {
        using var h = new Harness();
        var s = await h.ScenarioAsync(questions: 1);
        await Assert.ThrowsAsync<ForbiddenException>(() => h.Classrooms.ChangeCodeAsync(s.Room.Id, s.Student));
        await Assert.ThrowsAsync<ForbiddenException>(() => h.Gradebook.GetReportAsync(s.Assignment.Id, s.Student));
    }
}

[Collection("db")]
public class AssignmentTests
{
    [Fact]
    public async Task G1_QuizNhap_KhongGiaoDuoc()
    {
        using var h = new Harness();
        var gv = await h.UserAsync("Lan");
        var quiz = await h.Library.CreateQuizAsync((await h.Library.CreateStudySetAsync("Bộ", gv)).Id, "Q", gv);
        var room = await h.Classrooms.CreateAsync("Lớp", ClassJoinPolicy.Immediate, gv);
        await Assert.ThrowsAsync<BusinessException>(() =>
            h.Assignments.CreatePracticeAsync(room.Id, quiz.Id, new PracticeSettings(null, null, null), gv));
    }

    [Fact]
    public async Task G2_RutHanConDuoi15Phut_BiTuChoi_Tren15PhutThiDuoc()
    {
        using var h = new Harness();
        var s = await h.ScenarioAsync(questions: 1);
        await Assert.ThrowsAsync<BusinessException>(() =>
            h.Assignments.ChangeDueAsync(s.Assignment.Id, h.Clock.UtcNow.AddMinutes(10), null, s.Lecturer));
        await h.Assignments.ChangeDueAsync(s.Assignment.Id, h.Clock.UtcNow.AddMinutes(20), "Lùi lịch", s.Lecturer);
        Assert.Single(await h.Db.AssignmentChanges.Where(c => c.AssignmentId == s.Assignment.Id).ToListAsync());
    }

    [Fact]
    public async Task G3_ChiNguoiDangOLop_NguoiVaoSauKhongThayBai()
    {
        using var h = new Harness();
        var s = await h.ScenarioAsync(questions: 1, settings: new PracticeSettings(null, null, null,
            AudiencePolicy: AudiencePolicy.OnlyCurrentMembers));
        var late = await h.UserAsync("Hoa");
        h.Clock.UtcNow = h.Clock.UtcNow.AddMinutes(5);
        await h.Classrooms.JoinByCodeAsync(s.ClassCode, late);

        Assert.Empty(await h.Assignments.ListForLearnerAsync(late));
        Assert.Single(await h.Assignments.ListForLearnerAsync(s.Student));
        await Assert.ThrowsAsync<ForbiddenException>(() => h.Attempts.StartAsync(s.Assignment.Id, late));
    }

    [Fact]
    public async Task G4_DongNgay_LuotDoKetThuc_CauChuaLam0Diem()
    {
        using var h = new Harness();
        var s = await h.ScenarioAsync(questions: 3);
        var attempt = await h.Attempts.StartAsync(s.Assignment.Id, s.Student);
        var first = attempt.Questions.OrderBy(q => q.DisplayOrder).First();
        await h.Attempts.AnswerAsync(attempt.Id, first.Id, Guid.NewGuid(), [CorrectOption(first)], s.Student);

        await h.Assignments.CloseNowAsync(s.Assignment.Id, s.Lecturer);

        using var fresh = new Harness();
        var ended = await fresh.Db.Attempts.Include(a => a.Participant).SingleAsync(a => a.Id == attempt.Id);
        Assert.Equal(AttemptStatus.Ended, ended.Status);
        Assert.Equal(AttemptEndReason.ClosedByLecturer, ended.EndReason);
        Assert.Equal(1m, ended.FirstScore);
        Assert.Equal(3m, ended.MaxScore);
        Assert.True(ended.Participant.CompletionLabels.HasFlag(CompletionLabels.ClosedByLecturer));
    }

    [Fact]
    public async Task GioDocTuDatabase_LuonLaUtc_KhongLech7Tieng()
    {
        using var h = new Harness();
        var s = await h.ScenarioAsync(questions: 1);
        using var fresh = new Harness();
        var due = (await fresh.Db.Assignments.SingleAsync(a => a.Id == s.Assignment.Id)).DueAt!.Value;
        Assert.Equal(DateTimeKind.Utc, due.Kind);
        Assert.EndsWith("Z", due.ToString("O"));
    }

    internal static int CorrectOption(AttemptQuestion aq) =>
        QuestionSnapshot.FromJson(aq.SnapshotJson).Options.Single(o => o.IsCorrect).Id;

    internal static int WrongOption(AttemptQuestion aq) =>
        QuestionSnapshot.FromJson(aq.SnapshotJson).Options.First(o => !o.IsCorrect).Id;
}

[Collection("db")]
public class AttemptTests
{
    [Fact]
    public async Task P2_BamXacNhanHaiLan_ChiGhiMotLan()
    {
        using var h = new Harness();
        var s = await h.ScenarioAsync(questions: 1);
        var attempt = await h.Attempts.StartAsync(s.Assignment.Id, s.Student);
        var aq = attempt.Questions.Single();
        var request = Guid.NewGuid();

        var r1 = await h.Attempts.AnswerAsync(attempt.Id, aq.Id, request, [AssignmentTests.CorrectOption(aq)], s.Student);
        var r2 = await h.Attempts.AnswerAsync(attempt.Id, aq.Id, request, [AssignmentTests.CorrectOption(aq)], s.Student);

        Assert.True(r1.IsCorrect);
        Assert.Equal(r1.Score, r2.Score);
        Assert.Equal(1, await h.Db.AnswerSubmissions.CountAsync(x => x.AttemptQuestionId == aq.Id));
    }

    [Fact]
    public async Task P3_HaiTabCungBamBatDau_ChiMotLuot()
    {
        using var setup = new Harness();
        var s = await setup.ScenarioAsync(questions: 2);

        using var tab1 = new Harness();
        using var tab2 = new Harness();
        var results = await Task.WhenAll(
            tab1.Attempts.StartAsync(s.Assignment.Id, s.Student),
            tab2.Attempts.StartAsync(s.Assignment.Id, s.Student));

        Assert.Equal(results[0].Id, results[1].Id);
        using var check = new Harness();
        Assert.Equal(1, await check.Db.Attempts.CountAsync(a => a.Participant.AssignmentId == s.Assignment.Id));
    }

    [Fact]
    public async Task P4_HetSoLuot_KhongBatDauDuocLuotMoi()
    {
        using var h = new Harness();
        var s = await h.ScenarioAsync(questions: 1, settings: new PracticeSettings(null, null, 1));
        var attempt = await h.Attempts.StartAsync(s.Assignment.Id, s.Student);
        await h.Attempts.FinishAsync(attempt.Id, s.Student);

        var ex = await Assert.ThrowsAsync<BusinessException>(() => h.Attempts.StartAsync(s.Assignment.Id, s.Student));
        Assert.Contains("hết số lượt", ex.Message);
    }

    [Fact]
    public async Task P5_HetHanKhiDangLam_TaiLaiThayLuotDaKetThuc()
    {
        using var h = new Harness();
        var s = await h.ScenarioAsync(questions: 2, settings: new PracticeSettings(null, new DateTime(2026, 11, 5, 9, 0, 0, DateTimeKind.Utc), null));
        var attempt = await h.Attempts.StartAsync(s.Assignment.Id, s.Student);
        h.Clock.UtcNow = new DateTime(2026, 11, 5, 9, 30, 0, DateTimeKind.Utc);

        var reloaded = await h.Attempts.GetForLearnerAsync(attempt.Id, s.Student);

        Assert.Equal(AttemptStatus.Ended, reloaded.Status);
        Assert.Equal(AttemptEndReason.DueDate, reloaded.EndReason);
        Assert.Equal(new DateTime(2026, 11, 5, 9, 0, 0, DateTimeKind.Utc), reloaded.EndedAt);
        Assert.Equal(0m, reloaded.FirstScore);
        Assert.True(reloaded.Participant.CompletionLabels.HasFlag(CompletionLabels.Expired));
    }

    [Fact]
    public async Task P_ChonNhieuThieuDapAn_0Diem()
    {
        using var h = new Harness();
        var gv = await h.UserAsync("Lan");
        var question = new ChoiceQuestionInput(QuestionType.MultipleChoice, "Ngôn ngữ .NET?", null, 2, false,
            [new("C#", true), new("F#", true), new("Python", false)]);
        var snapshot = new QuestionSnapshot(1, 0, question.Type, question.Prompt, null, question.Points,
            question.Options.Select((o, i) => new OptionSnapshot(i + 1, i, o.Text, o.IsCorrect)).ToList());
        var grader = new ChoiceGrader();

        Assert.Equal(new GradeResult(false, 0), grader.Grade(snapshot, """{"Selected":[1]}"""));
        Assert.Equal(new GradeResult(false, 0), grader.Grade(snapshot, """{"Selected":[1,2,3]}"""));
        Assert.Equal(new GradeResult(true, 2), grader.Grade(snapshot, """{"Selected":[2,1]}"""));
        Assert.Throws<BusinessException>(() => grader.Grade(snapshot, """{"Selected":[99]}"""));
    }
}

[Collection("db")]
public class GradebookTests
{
    [Fact]
    public void R6_11Tren15_Thang10_HaiChuSo() =>
        Assert.Equal("7,33 (11/15)", ScoreFormatter.Format(11, 15, ScoreScale.Ten, RoundingMode.TwoDecimals));

    [Theory]
    [InlineData(7.335, RoundingMode.TwoDecimals, 7.34)]
    [InlineData(7.334, RoundingMode.TwoDecimals, 7.33)]
    [InlineData(7.345, RoundingMode.TwoDecimals, 7.35)] // làm tròn lên từ 5, không làm tròn về số chẵn (7,34)
    [InlineData(7.67, RoundingMode.OneDecimal, 7.7)]
    [InlineData(7.13, RoundingMode.Quarter, 7.25)]
    [InlineData(7.24, RoundingMode.Half, 7.0)]
    public void LamTron_65C(decimal value, RoundingMode mode, decimal expected) =>
        Assert.Equal(expected, ScoreFormatter.Round(value, mode));

    private static Attempt Ended(int n, decimal score) =>
        new() { AttemptNumber = n, Status = AttemptStatus.Ended, FirstScore = score, MaxScore = 10 };

    [Fact]
    public void R1_TrungBinh7_8_8_Ra7Phay67()
    {
        var picked = GradebookService.Select([Ended(1, 7), Ended(2, 8), Ended(3, 8)], ScoreSelection.Average)!;
        Assert.Equal("7,67 (7,67/10)", ScoreFormatter.Format(picked.Raw, picked.Max, ScoreScale.Ten, RoundingMode.TwoDecimals));
    }

    [Fact]
    public void R2_CaoNhatHoaDiem_LayLuotMoiHon()
    {
        Attempt a1 = Ended(1, 8), a2 = Ended(2, 8);
        a1.Id = 1; a2.Id = 2;
        var picked = GradebookService.Select([a1, a2, Ended(3, 6)], ScoreSelection.Highest);
        Assert.Equal(new SelectedScore(8, 10), picked);
        // Cùng tỉ lệ nhưng khác điểm gốc (8/10 và 4/5) thì phải phân biệt được: lấy lượt mới hơn.
        var newer = new Attempt { AttemptNumber = 2, Status = AttemptStatus.Ended, FirstScore = 4, MaxScore = 5 };
        Assert.Equal(new SelectedScore(4, 5), GradebookService.Select([Ended(1, 8), newer], ScoreSelection.Highest));
        Assert.Null(GradebookService.Select([new Attempt { Status = AttemptStatus.InProgress, FirstScore = 9, MaxScore = 10 }], ScoreSelection.Highest));
    }

    [Fact]
    public async Task R3_GvLopKhac_KhongXemDuocBangDiem()
    {
        using var h = new Harness();
        var s = await h.ScenarioAsync(questions: 1);
        var outsider = await h.UserAsync("Tuấn");
        await h.Classrooms.CreateAsync("Lớp của Tuấn", ClassJoinPolicy.Immediate, outsider);
        await Assert.ThrowsAsync<ForbiddenException>(() => h.Gradebook.GetReportAsync(s.Assignment.Id, outsider));
    }

    [Fact]
    public async Task R_BangDiem_TrangThaiVaDiemDungTheoLuot()
    {
        using var h = new Harness();
        var s = await h.ScenarioAsync(questions: 2);
        var idle = await h.UserAsync("Hoa");
        await h.Classrooms.JoinByCodeAsync(s.ClassCode, idle);

        var attempt = await h.Attempts.StartAsync(s.Assignment.Id, s.Student);
        var qs = attempt.Questions.OrderBy(q => q.DisplayOrder).ToList();
        await h.Attempts.AnswerAsync(attempt.Id, qs[0].Id, Guid.NewGuid(), [AssignmentTests.CorrectOption(qs[0])], s.Student);
        await h.Attempts.AnswerAsync(attempt.Id, qs[1].Id, Guid.NewGuid(), [AssignmentTests.WrongOption(qs[1])], s.Student);
        await h.Attempts.FinishAsync(attempt.Id, s.Student);

        var (_, rows) = await h.Gradebook.GetReportAsync(s.Assignment.Id, s.Lecturer);

        var minh = rows.Single(r => r.UserId == s.Student);
        Assert.Equal(CompletionStatus.Completed, minh.Status);
        Assert.Equal(new SelectedScore(1, 2), minh.Score);
        var hoa = rows.Single(r => r.UserId == idle);
        Assert.Equal(CompletionStatus.NotStarted, hoa.Status); // "Chưa làm" (67)
        Assert.Null(hoa.Score);
    }
}
