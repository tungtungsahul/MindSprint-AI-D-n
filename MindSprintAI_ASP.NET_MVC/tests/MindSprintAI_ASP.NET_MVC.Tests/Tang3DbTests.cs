using Microsoft.EntityFrameworkCore;
using MindSprintAI_ASP.NET_MVC.Data.Entities;
using MindSprintAI_ASP.NET_MVC.Services;

namespace MindSprintAI_ASP.NET_MVC.Tests;

[Collection("db")]
public class Tang3DbTests
{
    private static AttemptService Attempts(Harness h) => new(h.Db, h.Clock, [new ChoiceGrader(), new TextInputGrader(), new NumberInputGrader()], h.Device, h.Notifier, h.Connections);
    private static FlashcardService Cards(Harness h) => new(h.Db, h.Library, h.Clock, h.Notifier);
    private static ExcelImportService Excel(Harness h) => new(h.Db, h.Library, h.Clock);

    private static async Task<Scenario> ScenarioAsync(Harness h, AssignmentForm form = AssignmentForm.FlashcardOnly,
        RequiredParts required = RequiredParts.Flashcards, QuestionType type = QuestionType.TextInput, bool test = false)
    {
        var lecturer = await h.UserAsync("Lan");
        var student = await h.UserAsync("Minh");
        var set = await h.Library.CreateStudySetAsync("Bộ tầng 3", lecturer);
        var quiz = await h.Library.CreateQuizAsync(set.Id, "Quiz tầng 3", lecturer);
        await h.Library.SaveInputQuestionAsync(quiz.Id, null, null, new InputQuestionInput(type, "Đề", "Giải thích bí mật", 1,
            [new[] { "Một" }, new[] { "Hai" }, new[] { "Ba" }], BlankScoring: BlankScoring.PerBlank, NumberAnswer: "3,14", NumberTolerance: "0,01"), lecturer);
        Assert.Empty(await h.Library.MarkReadyAsync(quiz.Id, lecturer));
        await Cards(h).SaveAsync(set.Id, null, "Thẻ 1", "Sau 1", lecturer);
        await Cards(h).SaveAsync(set.Id, null, "Thẻ 2", "Sau 2", lecturer);
        var room = await h.Classrooms.CreateAsync("Lớp tầng 3", ClassJoinPolicy.Immediate, lecturer);
        var code = (await h.Codes.GetActiveAsync(JoinCodeTarget.Classroom, room.Id))!.Code;
        await h.Classrooms.JoinByCodeAsync(code, student);
        h.Clock.UtcNow = h.Clock.UtcNow.AddMinutes(1);
        var assignment = test ? await h.Assignments.CreateTestAsync(room.Id, quiz.Id, new TestSettings(null, 45), lecturer)
            : await h.Assignments.CreatePracticeAsync(room.Id, quiz.Id, new PracticeSettings(null, h.Clock.UtcNow.AddHours(2), null,
                Form: form, StudySetId: set.Id, RequiredParts: required), lecturer);
        return new(lecturer, student, quiz, room, assignment, code);
    }

    private static async Task RateAll(Harness h, Scenario s)
    {
        var session = await Cards(h).GetSessionAsync(s.Quiz.StudySetId, s.Assignment.Id, s.Student);
        foreach (var card in session.Cards) await Cards(h).RateAsync(session.StudySetId, session.AssignmentId, card.Id, FlashcardRating.NotRemembered, s.Student);
    }

    [Fact]
    public async Task CongDonChuaNhoVanDaHocVaDanhGiaGanNhatThayThe()
    {
        using var h = new Harness();
        var s = await ScenarioAsync(h);
        var session = await Cards(h).GetSessionAsync(s.Quiz.StudySetId, s.Assignment.Id, s.Student);
        await Cards(h).RateAsync(session.StudySetId, session.AssignmentId, session.Cards[0].Id, FlashcardRating.NotRemembered, s.Student);
        h.Db.ChangeTracker.Clear();
        session = await Cards(h).GetSessionAsync(s.Quiz.StudySetId, s.Assignment.Id, s.Student);
        Assert.Equal(1, session.Studied);
        Assert.Equal(1, session.StartIndex);
        await Cards(h).RateAsync(session.StudySetId, session.AssignmentId, session.Cards[1].Id, FlashcardRating.NotRemembered, s.Student);
        Assert.Equal(CompletionStatus.Completed, (await Cards(h).GetSessionAsync(session.StudySetId, session.AssignmentId, s.Student)).Status);
        await Cards(h).RateAsync(session.StudySetId, session.AssignmentId, session.Cards[0].Id, FlashcardRating.Remembered, s.Student);
        var forgotten = await Cards(h).GetSessionAsync(session.StudySetId, session.AssignmentId, s.Student, forgotten: true);
        Assert.Single(forgotten.Cards);
        Assert.Empty(await h.Db.Attempts.Where(a => a.Participant.AssignmentId == s.Assignment.Id).ToListAsync());
        var report = await h.Gradebook.GetForgottenCardsAsync(s.Assignment.Id, s.Lecturer);
        Assert.Single(report);
        Assert.Equal(1, report[0].Count);
    }

    [Fact]
    public async Task ThemTheSauKhongBatBuocSuaKhongMatTienDo()
    {
        using var h = new Harness();
        var s = await ScenarioAsync(h);
        var session = await Cards(h).GetSessionAsync(s.Quiz.StudySetId, s.Assignment.Id, s.Student);
        await RateAll(h, s);
        h.Clock.UtcNow = h.Clock.UtcNow.AddMinutes(1);
        await Cards(h).SaveAsync(session.StudySetId, null, "Thẻ mới", "Sau", s.Lecturer);
        await Cards(h).SaveAsync(session.StudySetId, session.Cards[0].Id, "Đã sửa", "Sau sửa", s.Lecturer);
        session = await Cards(h).GetSessionAsync(session.StudySetId, s.Assignment.Id, s.Student);
        Assert.Equal(2, session.Required);
        Assert.Equal(2, session.Studied);
        Assert.Equal(CompletionStatus.Completed, session.Status);
        Assert.Single(session.Cards, c => c.IsNew);
        Assert.True(session.Cards[0].Edited);
        Assert.NotNull(session.Cards[0].Rating);
    }

    [Fact]
    public async Task XoaTheKhiMoTuHoanThanhKhongXoaCung()
    {
        using var h = new Harness();
        var s = await ScenarioAsync(h);
        var session = await Cards(h).GetSessionAsync(s.Quiz.StudySetId, s.Assignment.Id, s.Student);
        await Cards(h).RateAsync(session.StudySetId, session.AssignmentId, session.Cards[0].Id, FlashcardRating.NotRemembered, s.Student);
        await Cards(h).DeleteAsync(session.StudySetId, session.Cards[1].Id, s.Lecturer);
        var after = await Cards(h).GetSessionAsync(session.StudySetId, session.AssignmentId, s.Student);
        Assert.Equal(CompletionStatus.Completed, after.Status);
        Assert.Equal(1, after.Required);
        Assert.NotNull((await h.Db.Flashcards.FindAsync(session.Cards[1].Id))!.DeletedAt);
    }

    [Theory]
    [InlineData(false), InlineData(true)]
    public async Task HetHanHoacDongThieuTheChuaHoanThanhHocSauKhongDoiTrangThai(bool close)
    {
        using var h = new Harness();
        var s = await ScenarioAsync(h);
        var session = await Cards(h).GetSessionAsync(s.Quiz.StudySetId, s.Assignment.Id, s.Student);
        await Cards(h).RateAsync(session.StudySetId, session.AssignmentId, session.Cards[0].Id, FlashcardRating.NotRemembered, s.Student);
        if (close) await h.Assignments.CloseNowAsync(s.Assignment.Id, s.Lecturer);
        else h.Clock.UtcNow = s.Assignment.DueAt!.Value.AddSeconds(1);
        var report = await h.Gradebook.GetReportAsync(s.Assignment.Id, s.Lecturer);
        Assert.Equal(CompletionStatus.Incomplete, report.Rows[0].Status);
        Assert.Equal("Thiếu thẻ (1/2)", report.Rows[0].MissingCards);
        await Cards(h).RateAsync(session.StudySetId, session.AssignmentId, session.Cards[1].Id, FlashcardRating.Remembered, s.Student);
        Assert.Equal(CompletionStatus.Incomplete, (await Cards(h).GetSessionAsync(session.StudySetId, session.AssignmentId, s.Student)).Status);
        await Cards(h).DeleteAsync(session.StudySetId, session.Cards[1].Id, s.Lecturer);
        Assert.Equal(CompletionStatus.Incomplete, (await Cards(h).GetSessionAsync(session.StudySetId, session.AssignmentId, s.Student)).Status);
    }

    [Fact]
    public async Task KhongChoHocSauHanVaNguoiLaKhongHocKhongXemDuoc()
    {
        using var h = new Harness();
        var s = await ScenarioAsync(h);
        var session = await Cards(h).GetSessionAsync(s.Quiz.StudySetId, s.Assignment.Id, s.Student);
        var stranger = await h.UserAsync("Người lạ");
        await Assert.ThrowsAsync<ForbiddenException>(() => Cards(h).GetSessionAsync(session.StudySetId, session.AssignmentId, stranger));
        await Assert.ThrowsAsync<ForbiddenException>(() => Cards(h).RateAsync(session.StudySetId, session.AssignmentId, session.Cards[0].Id, FlashcardRating.Remembered, stranger));
        var other = await h.Library.CreateStudySetAsync("Bộ khác", s.Lecturer);
        await Cards(h).SaveAsync(other.Id, null, "Khác", "Khác", s.Lecturer);
        var card = await h.Db.Flashcards.SingleAsync(c => c.StudySetId == other.Id);
        await Assert.ThrowsAsync<ForbiddenException>(() => Cards(h).RateAsync(session.StudySetId, session.AssignmentId, card.Id, FlashcardRating.Remembered, s.Student));
        s.Assignment.AllowFlashcardsAfterDue = false;
        await h.Db.SaveChangesAsync();
        h.Clock.UtcNow = s.Assignment.DueAt!.Value.AddSeconds(1);
        Assert.False((await Cards(h).GetSessionAsync(session.StudySetId, session.AssignmentId, s.Student)).CanStudy);
        await Assert.ThrowsAsync<BusinessException>(() => Cards(h).RateAsync(session.StudySetId, session.AssignmentId, session.Cards[0].Id, FlashcardRating.Remembered, s.Student));
    }

    [Fact]
    public async Task ChuBoTuOnKhongCanBaiGiaoVaTienDoRieng()
    {
        using var h = new Harness();
        var s = await ScenarioAsync(h);
        var session = await Cards(h).GetSessionAsync(s.Quiz.StudySetId, null, s.Lecturer);
        await Cards(h).RateAsync(session.StudySetId, null, session.Cards[0].Id, FlashcardRating.Remembered, s.Lecturer);
        Assert.Equal(1, (await Cards(h).GetSessionAsync(session.StudySetId, null, s.Lecturer)).Studied);
        Assert.Equal(0, (await Cards(h).GetSessionAsync(session.StudySetId, s.Assignment.Id, s.Student)).Studied);
        await Assert.ThrowsAsync<ForbiddenException>(() => Cards(h).GetSessionAsync(session.StudySetId, null, s.Student));
    }

    [Theory]
    [InlineData(RequiredParts.Quiz), InlineData(RequiredParts.Flashcards), InlineData(RequiredParts.Both)]
    public async Task KetHopTheoPhanBatBuocVaQuizTuNguyenVanCoDiem(RequiredParts required)
    {
        using var h = new Harness();
        var s = await ScenarioAsync(h, AssignmentForm.Combined, required);
        await RateAll(h, s);
        var session = await Cards(h).GetSessionAsync(s.Quiz.StudySetId, s.Assignment.Id, s.Student);
        Assert.Equal(required == RequiredParts.Flashcards ? CompletionStatus.Completed : CompletionStatus.InProgress, session.Status);
        var attempt = await Attempts(h).StartAsync(s.Assignment.Id, s.Student);
        await Attempts(h).AnswerAsync(attempt.Id, attempt.Questions[0].Id, Guid.NewGuid(), [], s.Student, ["Một", "Hai", "Ba"]);
        await Attempts(h).FinishAsync(attempt.Id, s.Student);
        var report = await h.Gradebook.GetReportAsync(s.Assignment.Id, s.Lecturer);
        Assert.Equal(CompletionStatus.Completed, report.Rows[0].Status);
        Assert.Equal(1, report.Rows[0].Score!.Raw);
    }

    [Fact]
    public async Task DoiPhanBatBuocGiuNguoiHoanThanhVaTatTheHoanThanhNgay()
    {
        using var h = new Harness();
        var s = await ScenarioAsync(h, AssignmentForm.Combined, RequiredParts.Quiz);
        var attempt = await Attempts(h).StartAsync(s.Assignment.Id, s.Student);
        await Attempts(h).FinishAsync(attempt.Id, s.Student);
        Assert.Single(await h.Assignments.PreviewRequiredPartsAsync(s.Assignment.Id, RequiredParts.Both, s.Lecturer));
        await h.Assignments.ChangeRequiredPartsAsync(s.Assignment.Id, RequiredParts.Both, s.Lecturer);
        var report = await h.Gradebook.GetReportAsync(s.Assignment.Id, s.Lecturer);
        Assert.Equal(CompletionStatus.Completed, report.Rows[0].Status);
        Assert.True(report.Rows[0].Labels.HasFlag(CompletionLabels.CompletedBeforeCardsRequired));
        Assert.Contains("khuyến khích học thêm thẻ", (await Attempts(h).GetAssignmentPageAsync(s.Assignment.Id, s.Student)).RequirementsNotice);
        Assert.Single(await h.Db.AssignmentChanges.Where(c => c.AssignmentId == s.Assignment.Id).ToListAsync());
        var another = await h.UserAsync("Bình");
        await h.Classrooms.JoinByCodeAsync(s.ClassCode, another);
        var a2 = await Attempts(h).StartAsync(s.Assignment.Id, another);
        await Attempts(h).FinishAsync(a2.Id, another);
        Assert.Equal(CompletionStatus.InProgress, (await Attempts(h).GetAssignmentPageAsync(s.Assignment.Id, another)).Completion);
        await h.Assignments.ChangeRequiredPartsAsync(s.Assignment.Id, RequiredParts.Quiz, s.Lecturer);
        Assert.Equal(CompletionStatus.Completed, (await Attempts(h).GetAssignmentPageAsync(s.Assignment.Id, another)).Completion);
    }

    [Fact]
    public async Task DiemChiaTheoOLuuDuSoLeVaONullKhongXacNhanDuoc()
    {
        using var h = new Harness();
        var s = await ScenarioAsync(h, AssignmentForm.QuizOnly);
        var attempt = await Attempts(h).StartAsync(s.Assignment.Id, s.Student);
        var aq = attempt.Questions[0];
        await Assert.ThrowsAsync<BusinessException>(() => Attempts(h).AnswerAsync(attempt.Id, aq.Id, Guid.NewGuid(), [], s.Student, ["Một", " ", "Ba"]));
        var result = await Attempts(h).AnswerAsync(attempt.Id, aq.Id, Guid.NewGuid(), [], s.Student, ["Một", "Sai", "Sai"]);
        Assert.Equal(1m / 3, result.Score);
        h.Db.ChangeTracker.Clear();
        Assert.Equal(1m / 3, (await Attempts(h).GetForLearnerAsync(attempt.Id, s.Student)).FirstScore);
    }

    [Theory]
    [InlineData(QuestionType.TextInput), InlineData(QuestionType.NumberInput)]
    public async Task KiemTraNhapsVaChupDuDapAnNhungPhanHoiKhongLo(QuestionType type)
    {
        using var h = new Harness();
        var s = await ScenarioAsync(h, AssignmentForm.QuizOnly, RequiredParts.Quiz, type, test: true);
        await h.Assignments.StartTestAsync(s.Assignment.Id, s.Lecturer);
        var attempt = await Attempts(h).StartAsync(s.Assignment.Id, s.Student);
        var aq = attempt.Questions[0];
        await Attempts(h).SaveDraftAsync(attempt.Id, aq.Id, [], s.Student, ["Một", "Hai", "Ba"], "3,13");
        h.Clock.UtcNow = attempt.DeadlineAt!.Value.AddSeconds(1);
        attempt = await Attempts(h).GetForLearnerAsync(attempt.Id, s.Student);
        Assert.Equal(1, attempt.FirstScore);
        Assert.Null((await Attempts(h).GetPlayQuestionsAsync(attempt, s.Student))[0].CorrectAnswers);
        await h.Assignments.PublishAnswersAsync(s.Assignment.Id, true, s.Lecturer);
        Assert.NotNull((await Attempts(h).GetPlayQuestionsAsync(attempt, s.Student))[0].CorrectAnswers);
    }

    [Fact]
    public async Task ImportChuaLuuKhongGhiLuuDocLaiVaChanQuizDaGiaoChuKhac()
    {
        using var h = new Harness();
        var owner = await h.UserAsync("Chủ");
        var other = await h.UserAsync("Khác");
        var set = await h.Library.CreateStudySetAsync("Bộ import", owner);
        var quiz = await h.Library.CreateQuizAsync(set.Id, "Quiz import", owner);
        var file = ExcelTemplate.Create();
        var preview = await Excel(h).PreviewAsync(quiz.Id, file, "mau.xlsx", owner);
        Assert.Empty(await h.Db.Questions.Where(q => q.QuizId == quiz.Id).ToListAsync());
        await Assert.ThrowsAsync<ForbiddenException>(() => Excel(h).PreviewAsync(quiz.Id, file, "mau.xlsx", other));
        await Assert.ThrowsAsync<ForbiddenException>(() => Excel(h).SaveAsync(preview, other));
        preview.Rows[0].Values[ExcelTemplate.Correct] = "1,2";
        preview.Rows[0].Error = null; // giả mạo cờ hợp lệ ở trình duyệt không vượt qua máy chủ.
        await Assert.ThrowsAsync<BusinessException>(() => Excel(h).SaveAsync(preview, owner));
        preview.Rows[0].Values[ExcelTemplate.Correct] = "C";
        preview = await Excel(h).RecheckAsync(preview, owner);
        Assert.Equal(7, await Excel(h).SaveAsync(preview, owner));
        Assert.Equal(5, await h.Db.Questions.CountAsync(q => q.QuizId == quiz.Id));
        Assert.Empty(await h.Library.MarkReadyAsync(quiz.Id, owner));
        var room = await h.Classrooms.CreateAsync("Lớp import", ClassJoinPolicy.Immediate, owner);
        await h.Assignments.CreatePracticeAsync(room.Id, quiz.Id, new PracticeSettings(null, null, null), owner);
        await Assert.ThrowsAsync<BusinessException>(() => Excel(h).SaveAsync(preview, owner));
        await Assert.ThrowsAsync<BusinessException>(() => Excel(h).PreviewAsync(quiz.Id, file, "mau.xlsx", owner));
    }

    [Fact]
    public async Task ChanTheCungBoKhiDangThiChoBoKhacVaMoLaiSauNop()
    {
        using var h = new Harness();
        var s = await ScenarioAsync(h);
        var session = await Cards(h).GetSessionAsync(s.Quiz.StudySetId, s.Assignment.Id, s.Student);
        var test = await h.Assignments.CreateTestAsync(s.Room.Id, s.Quiz.Id, new TestSettings(null, 45), s.Lecturer);
        await h.Assignments.StartTestAsync(test.Id, s.Lecturer);
        var attempt = await Attempts(h).StartAsync(test.Id, s.Student);
        await Assert.ThrowsAsync<BusinessException>(() => Cards(h).GetSessionAsync(session.StudySetId, session.AssignmentId, s.Student));
        await Assert.ThrowsAsync<BusinessException>(() => Cards(h).RateAsync(session.StudySetId, session.AssignmentId, session.Cards[0].Id, FlashcardRating.Remembered, s.Student));
        var otherSet = await h.Library.CreateStudySetAsync("Bộ khác", s.Student);
        await Cards(h).SaveAsync(otherSet.Id, null, "Thẻ", "Sau", s.Student);
        Assert.NotEmpty((await Cards(h).GetSessionAsync(otherSet.Id, null, s.Student)).Cards);
        await Attempts(h).FinishAsync(attempt.Id, s.Student);
        Assert.NotEmpty((await Cards(h).GetSessionAsync(session.StudySetId, session.AssignmentId, s.Student)).Cards);
    }

    [Fact]
    public async Task ImportSuaTrenTrangBoTickKhongNhapDongLoi()
    {
        using var h = new Harness();
        var owner = await h.UserAsync("Chủ import");
        var set = await h.Library.CreateStudySetAsync("Bộ import", owner);
        var quiz = await h.Library.CreateQuizAsync(set.Id, "Quiz", owner);
        var preview = await Excel(h).PreviewAsync(quiz.Id, ExcelTemplate.Create(), "mau.xlsx", owner);
        preview.Rows[0].Values[ExcelTemplate.Prompt] = "Đề đã sửa trên trang";
        preview.Rows[1].Values[ExcelTemplate.Correct] = "Z";
        preview.Rows[1].Include = false;
        var checkedInput = await Excel(h).RecheckAsync(preview, owner);
        Assert.NotNull(checkedInput.Rows[1].Error);
        Assert.Equal(6, await Excel(h).SaveAsync(checkedInput, owner));
        Assert.Equal(4, await h.Db.Questions.CountAsync(q => q.QuizId == quiz.Id));
        Assert.Contains(await h.Db.Questions.Where(q => q.QuizId == quiz.Id).ToListAsync(), q => q.Prompt == "Đề đã sửa trên trang");
    }

    [Fact]
    public async Task DapAnSoKhongBiLamTronOSauChuSoThuSau()
    {
        using var h = new Harness();
        var owner = await h.UserAsync("Chủ số");
        var set = await h.Library.CreateStudySetAsync("Số chính xác", owner);
        var quiz = await h.Library.CreateQuizAsync(set.Id, "Quiz số", owner);
        await h.Library.SaveInputQuestionAsync(quiz.Id, null, null,
            new InputQuestionInput(QuestionType.NumberInput, "Số", null, 1, [], NumberAnswer: "2,123456789", NumberTolerance: "0,000000001"), owner);
        h.Db.ChangeTracker.Clear();
        var saved = (await h.Library.GetOwnedQuizAsync(quiz.Id, owner)).Questions[0];
        Assert.Equal(2.123456789m, saved.NumberAnswer);
        Assert.Equal(.000000001m, saved.NumberTolerance);
    }
}
