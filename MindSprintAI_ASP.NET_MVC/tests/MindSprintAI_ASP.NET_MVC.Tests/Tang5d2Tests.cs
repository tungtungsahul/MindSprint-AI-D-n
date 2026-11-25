using System.Text.Json;
using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.Logging.Abstractions;
using MindSprintAI_ASP.NET_MVC.Controllers;
using MindSprintAI_ASP.NET_MVC.Data;
using MindSprintAI_ASP.NET_MVC.Data.Entities;
using MindSprintAI_ASP.NET_MVC.Models;
using MindSprintAI_ASP.NET_MVC.Services;

namespace MindSprintAI_ASP.NET_MVC.Tests;

public class Tang5d2PureTests
{
    [Fact]
    public void PresetCuSaiMotTruongChiVeMacDinhTruongDo()
    {
        var p = SettingPresetRules.Assignment("{\"DurationMinutes\":999,\"MaxAttempts\":0,\"ScoreScale\":999,\"ShuffleQuestions\":false,\"Rounding\":\"sai\"}", AssignmentMode.Test);
        Assert.Equal(45, p.DurationMinutes);
        Assert.Null(p.MaxAttempts);
        Assert.Equal(ScoreScale.Ten, p.ScoreScale);
        Assert.False(p.ShuffleQuestions);
        Assert.Equal(RoundingMode.TwoDecimals, p.Rounding);
    }

    [Fact]
    public void PresetChiChuaOptionKhongChuaNguoiNhanHayNoiDung()
    {
        var assignment = JsonSerializer.Serialize(new AssignmentPreset());
        foreach (var name in new[] { "ClassroomId", "QuizId", "StudySetId", "OpensAt", "DueAt", "TestEndsAt", "Code", "AudiencePolicy", "StrangerPolicy" })
            Assert.DoesNotContain($"\"{name}\"", assignment);
        var question = JsonSerializer.Serialize(new QuestionPreset());
        foreach (var name in new[] { "Prompt", "Explanation", "Options", "BlankAnswers", "NumberAnswer", "NumberTolerance" })
            Assert.DoesNotContain($"\"{name}\"", question);
    }

    [Theory]
    [InlineData(null), InlineData("{"), InlineData("[]"), InlineData("null")]
    public void JsonHongDungMacDinhCuaTungCheDo(string? json)
    {
        var practice = SettingPresetRules.Assignment(json, AssignmentMode.Practice);
        var test = SettingPresetRules.Assignment(json, AssignmentMode.Test);
        Assert.False(practice.ShuffleOptions); Assert.True(test.ShuffleOptions);
        Assert.Equal(ScoreSelection.Highest, practice.ScoreSelection); Assert.Equal(ScoreSelection.Latest, test.ScoreSelection);
        Assert.Equal(45, test.DurationMinutes);
    }

    [Fact]
    public void CauHoiCuKiemLaiGioDiemVaCachCham()
    {
        var p = SettingPresetRules.Question("{\"Points\":-1,\"TimeLimitSeconds\":4,\"BlankScoring\":999,\"CaseSensitive\":true,\"MaxLength\":9999}", QuestionType.TextInput);
        Assert.Equal(1, p.Points); Assert.Null(p.TimeLimitSeconds); Assert.Equal(BlankScoring.AllOrNothing, p.BlankScoring);
        Assert.True(p.CaseSensitive); Assert.Null(p.MaxLength);
        var form = p.ToForm(12, QuestionType.TextInput);
        Assert.Equal("", form.Prompt); Assert.Null(form.Explanation); Assert.All(form.Options, o => Assert.Null(o.Text));
        Assert.Equal(new[] { "" }, form.BlankAnswers);
        Assert.Equal(500, SettingPresetRules.Question(null, QuestionType.SurveyText).MaxLength);
    }

    [Fact]
    public void KiemTraKhongMangSettingLamChuTuPresetCu()
    {
        var p = SettingPresetRules.Assignment("{\"Form\":3,\"MasteryThreshold\":80,\"AnswersPerQuestion\":2,\"ScoreRetries\":true}", AssignmentMode.Test);
        Assert.Equal(AssignmentForm.QuizOnly, p.Form); Assert.Null(p.MasteryThreshold); Assert.Null(p.AnswersPerQuestion); Assert.False(p.ScoreRetries);
    }

    [Fact]
    public void PrefillKhongGanBaiCuHayNguoiNhanCu()
    {
        var p = SettingPresetRules.Assignment("{\"ClassroomId\":99,\"QuizId\":99,\"StudySetId\":99,\"OpensAt\":\"2026-11-06T10:00:00\",\"AudiencePolicy\":2,\"StrangerPolicy\":3,\"MaxAttempts\":5}", AssignmentMode.Practice);
        var form = p.ToInput(1, AssignmentMode.Practice);
        Assert.Equal(1, form.ClassroomId); Assert.Equal(0, form.QuizId); Assert.Null(form.StudySetId);
        Assert.Null(form.OpensAt); Assert.Null(form.DueAt); Assert.Equal(AudiencePolicy.IncludeLateJoiners, form.AudiencePolicy);
        Assert.Equal(StrangerPolicy.Reject, form.StrangerPolicy); Assert.Equal(5, form.MaxAttempts);
    }

    [Fact]
    public void BangCaiDatVaModelKhopSnapshot()
    {
        using var db = DatabaseFixture.NewContext();
        var migrations = db.GetService<IMigrationsAssembly>();
        // MindSprint gộp migration Tang5d2 vào KhoiTao: kiểm khóa chính trên model.
        var entity = db.Model.FindEntityType(typeof(UserSettingPreset))!;
        Assert.Equal("UserSettingPresets", entity.GetTableName());
        Assert.Equal(new[] { "UserId", "Scope" }, entity.FindPrimaryKey()!.Properties.Select(p => p.Name));
        var snapshot = db.GetService<IModelRuntimeInitializer>().Initialize(migrations.ModelSnapshot!.Model, true);
        Assert.False(db.GetService<IMigrationsModelDiffer>().HasDifferences(snapshot.GetRelationalModel(), db.GetService<Microsoft.EntityFrameworkCore.Metadata.IDesignTimeModel>().Model.GetRelationalModel()));
    }

    [Fact]
    public async Task FormMoiKhopTenThamSoVaCoAntiforgery()
    {
        var assignment = new AssignmentCreateModel { Classroom = new() { Id = 1 }, ReadyQuizzes = [new() { Id = 2 }], StudySets = [new() { Id = 3 }], Form = new() };
        var create = await Tang4ViewTests.Render("Assignments", "Create", assignment);
        CheckFields(create, typeof(AssignmentsController), nameof(AssignmentsController.ResetPreset));
        Assert.Contains("data-practice-preset=", create); Assert.Contains("data-test-preset=", create);
        var quiz = await Tang4ViewTests.Render("StudySets", "Quiz", new QuizEditorModel { Quiz = new() { Id = 2, StudySet = new() }, Form = new() { Options = [new(), new()] } });
        CheckFields(quiz, typeof(StudySetsController), nameof(StudySetsController.ResetQuestionPreset));
        Assert.Contains("data-question-presets=", quiz);
        var trash = await Tang4ViewTests.Render("StudySets", "Trash", new List<TrashItem> { new(TrashKind.Quiz, 1, "<script>quiz</script>", new(2026, 11, 6, 1, 0, 0), false, false, true) });
        CheckFields(trash, typeof(StudySetsController), nameof(StudySetsController.Restore));
        Assert.DoesNotContain("<script>quiz", trash); Assert.Contains("&lt;script&gt;quiz", trash);
        Assert.Contains("08:00 06/11/2026", trash); Assert.Contains("08:00 06/12/2026", trash);
        var report = await Tang4ViewTests.Render("Assignments", "Details", new AssignmentDetailsModel { Assignment = new() { Id = 3, Status = AssignmentStatus.Closed }, CanDeleteReport = true, Rows = [], Code = "", OpenAttempts = 0 });
        CheckFields(report, typeof(AssignmentsController), nameof(AssignmentsController.DeleteReport));
    }

    [Fact]
    public async Task PrefillDaRenderDiemVaSettingNhungNoiDungVanTrong()
    {
        var preset = new QuestionPreset { Points = 2.5m, TimeLimitSeconds = 20, CaseSensitive = true, KeepOptionOrder = true };
        var html = await Tang4ViewTests.Render("StudySets", "Quiz", new QuizEditorModel
        { Quiz = new() { StudySet = new() }, Form = preset.ToForm(1, QuestionType.TextInput), Presets = new() { [3] = preset } });
        Assert.Contains("value=\"2.5\"", html); Assert.Contains("value=\"20\"", html);
        Assert.Contains("name=\"Prompt\"", html); Assert.Contains("data-new-question=\"true\"", html);
    }

    private static void CheckFields(string html, Type controller, string action)
    {
        Assert.Contains("__RequestVerificationToken", html);
        var method = controller.GetMethod(action)!;
        Assert.True(method.IsDefined(typeof(HttpPostAttribute), true));
        foreach (var parameter in method.GetParameters()) Assert.Contains($"name=\"{parameter.Name}\"", html);
        Assert.DoesNotContain(method.GetParameters(), p => p.Name is "userId" or "ownerId");
    }
}

[Collection("db")]
public class Tang5d2Tests
{
    private static UserSettingPresetService Presets(Harness h) => new(h.Db, h.Clock);
    private static TrashService Trash(Harness h) => new(h.Db, h.Classrooms, h.Clock);
    private static TrashPurgeService Purge(Harness h) => new(h.Db, h.Clock, NullLogger<TrashPurgeService>.Instance);
    private static FlashcardService Cards(Harness h) => new(h.Db, h.Library, h.Clock, h.Notifier);
    private static AssignmentsController Assignments(Harness h, string user) => AsUser(new AssignmentsController(h.Assignments, h.Classrooms,
        h.Library, h.Gradebook, h.Codes, new(h.Db, h.Assignments, h.Attempts, h.Clock), Tang4DbTests.Surveys(h), h.Regrades, Presets(h), Trash(h)), user);
    private static StudySetsController StudySets(Harness h, string user) => AsUser(new StudySetsController(h.Library, new(h.Db, h.Clock), Presets(h), Trash(h)), user);
    private static T AsUser<T>(T controller, string user) where T : Controller
    {
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user)], "test")) };
        controller.ControllerContext = new() { HttpContext = http, RouteData = new(), ActionDescriptor = new() };
        controller.TempData = new TempDataDictionary(http, new TestTempData());
        return controller;
    }
    private sealed class TestTempData : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }

    private static async Task<(StudySet Set, Quiz Quiz)> Draft(Harness h, string owner)
    {
        var set = await h.Library.CreateStudySetAsync("Bộ thử", owner);
        var quiz = await h.Library.CreateQuizAsync(set.Id, "Quiz thử", owner);
        await h.Library.SaveChoiceQuestionAsync(quiz.Id, null, null, Harness.Single(), owner);
        return (set, quiz);
    }

    [Fact]
    public async Task OnTapVaKiemTraNhoRiengChiSauTaoThanhCong()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(1, new(null, h.Clock.UtcNow.AddDays(1), 7,
            ScoreSelection.Average, ScoreScale.Hundred, RoundingMode.Quarter, AudiencePolicy.OnlyCurrentMembers,
            true, false, MasteryThreshold: 80, AnswersPerQuestion: 3, ScoreRetries: true, IgnoreQuestionTimers: true, StrangerPolicy: StrangerPolicy.Allow));
        await h.Assignments.CreateTestAsync(s.Room.Id, s.Quiz.Id, new(null, 90, ScoreScale.Original, RoundingMode.Half, ShuffleQuestions: false, ShuffleOptions: true), s.Lecturer);
        var controller = Assignments(h, s.Lecturer);
        var model = Assert.IsType<AssignmentCreateModel>(Assert.IsType<ViewResult>(await controller.Create(s.Room.Id)).Model);
        Assert.Equal(7, model.Form.MaxAttempts); Assert.Equal(80, model.Form.MasteryThreshold); Assert.Equal(3, model.Form.AnswersPerQuestion);
        Assert.True(model.Form.ScoreRetries); Assert.True(model.Form.IgnoreQuestionTimers); Assert.True(model.Form.ShuffleQuestions);
        Assert.Equal(ScoreScale.Hundred, model.Form.ScoreScale); Assert.Equal(ScoreSelection.Average, model.Form.ScoreSelection);
        Assert.Equal(RoundingMode.Quarter, model.Form.Rounding); Assert.False(model.Form.ShuffleOptions);
        Assert.Equal(AudiencePolicy.IncludeLateJoiners, model.Form.AudiencePolicy); Assert.Equal(StrangerPolicy.Reject, model.Form.StrangerPolicy);
        Assert.Equal(0, model.Form.QuizId); Assert.Null(model.Form.StudySetId); Assert.Null(model.Form.OpensAt); Assert.Null(model.Form.DueAt);
        var test = Assert.IsType<AssignmentCreateModel>(Assert.IsType<ViewResult>(await controller.Create(s.Room.Id, AssignmentMode.Test)).Model);
        Assert.Equal(90, test.Form.DurationMinutes); Assert.Equal(ScoreScale.Original, test.Form.ScoreScale);
        Assert.Equal(RoundingMode.Half, test.Form.Rounding); Assert.False(test.Form.ShuffleQuestions); Assert.True(test.Form.ShuffleOptions);
        Assert.Null(test.Form.MasteryThreshold);
        var json = await h.Db.UserSettingPresets.SingleAsync(p => p.UserId == s.Lecturer && p.Scope == "Assignment.Practice");
        foreach (var field in new[] { "ClassroomId", "QuizId", "StudySetId", "OpensAt", "DueAt", "Code", "AudiencePolicy", "StrangerPolicy" }) Assert.DoesNotContain($"\"{field}\"", json.ValuesJson);
        Assert.Equal(h.Clock.UtcNow, json.UpdatedAt);
    }

    [Fact]
    public async Task ResetXoaDungDongVaTaiKhoanKhacKhongDocHayResetDuoc()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(1); var other = await h.UserAsync("Người khác");
        Assert.Null((await Presets(h).AssignmentAsync(AssignmentMode.Practice, other)).MaxAttempts);
        await Presets(h).ResetAsync("Assignment.Practice", other);
        Assert.Equal(3, (await Presets(h).AssignmentAsync(AssignmentMode.Practice, s.Lecturer)).MaxAttempts);
        await Assert.ThrowsAsync<ForbiddenException>(() => Assignments(h, other).ResetPreset(s.Room.Id, AssignmentMode.Practice));
        await Assignments(h, s.Lecturer).ResetPreset(s.Room.Id, AssignmentMode.Practice);
        Assert.False(await h.Db.UserSettingPresets.AnyAsync(p => p.UserId == s.Lecturer && p.Scope == "Assignment.Practice"));
        Assert.Null((await Presets(h).AssignmentAsync(AssignmentMode.Practice, s.Lecturer)).MaxAttempts);
    }

    [Fact]
    public async Task GiaTriCuTrongDbDocLaiTheoTungTruong()
    {
        using var h = new Harness(); var user = await h.UserAsync("GV");
        h.Db.UserSettingPresets.Add(new() { UserId = user, Scope = "Assignment.Test", ValuesJson = "{\"DurationMinutes\":999,\"Rounding\":999,\"ShuffleOptions\":false}" });
        await h.Db.SaveChangesAsync();
        var p = await Presets(h).AssignmentAsync(AssignmentMode.Test, user);
        Assert.Equal(45, p.DurationMinutes); Assert.Equal(RoundingMode.TwoDecimals, p.Rounding); Assert.False(p.ShuffleOptions);
    }

    [Fact]
    public async Task CauHoiNhoTheoDangKhongNhoDeDapAnVaKhongGhiKhiSuaCu()
    {
        using var h = new Harness(); var user = await h.UserAsync("GV"); var (set, quiz) = await Draft(h, user);
        var q = await h.Library.SaveInputQuestionAsync(quiz.Id, null, null, new(QuestionType.TextInput, "ĐỀ BÍ MẬT", "GIẢI THÍCH", 2.5m,
            [["ĐÁP ÁN"]], true, true, true, true, BlankScoring.PerBlank, TimeLimitSeconds: 30), user);
        var form = Assert.IsType<QuizEditorModel>(Assert.IsType<ViewResult>(await StudySets(h, user).Quiz(quiz.Id, null, QuestionType.TextInput)).Model).Form;
        Assert.Equal(2.5m, form.Points); Assert.Equal(30, form.TimeLimitSeconds); Assert.True(form.CaseSensitive); Assert.True(form.AcceptNoDiacritics);
        Assert.True(form.IgnorePunctuation); Assert.True(form.AcceptAnyBlankOrder); Assert.Equal(BlankScoring.PerBlank, form.BlankScoring);
        Assert.Equal("", form.Prompt); Assert.Null(form.Explanation); Assert.All(form.Options, o => Assert.Null(o.Text)); Assert.Equal(new[] { "" }, form.BlankAnswers);
        Assert.False((await Presets(h).QuestionAsync(QuestionType.NumberInput, user)).CaseSensitive);
        var json = (await h.Db.UserSettingPresets.SingleAsync(p => p.UserId == user && p.Scope == "Question.TextInput")).ValuesJson;
        Assert.DoesNotContain("BÍ MẬT", WebUtility.HtmlDecode(json)); Assert.DoesNotContain("Prompt", json); Assert.DoesNotContain("Answers", json);
        await h.Library.SaveInputQuestionAsync(quiz.Id, q.Id, q.Version, new(QuestionType.TextInput, "Sửa", null, 7, [["Khác"]]), user);
        Assert.Equal(2.5m, (await Presets(h).QuestionAsync(QuestionType.TextInput, user)).Points);
        var other = await h.UserAsync("Khác");
        Assert.Equal(1, (await Presets(h).QuestionAsync(QuestionType.TextInput, other)).Points);
        await Assert.ThrowsAsync<ForbiddenException>(() => StudySets(h, other).ResetQuestionPreset(quiz.Id, QuestionType.TextInput));
        await StudySets(h, user).ResetQuestionPreset(quiz.Id, QuestionType.TextInput);
        Assert.False(await h.Db.UserSettingPresets.AnyAsync(p => p.UserId == user && p.Scope == "Question.TextInput"));
    }

    [Fact]
    public async Task LoiValidationKhongGhiPresetKeCaPostModelStateSai()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(1);
        var old = (await h.Db.UserSettingPresets.SingleAsync(p => p.UserId == s.Lecturer && p.Scope == "Assignment.Practice")).ValuesJson;
        await Assert.ThrowsAsync<BusinessException>(() => h.Assignments.CreatePracticeAsync(s.Room.Id, s.Quiz.Id, new(null, h.Clock.UtcNow.AddDays(-1), 99), s.Lecturer));
        Assert.Equal(old, (await h.Db.UserSettingPresets.SingleAsync(p => p.UserId == s.Lecturer && p.Scope == "Assignment.Practice")).ValuesJson);
        // Lỗi thì hiện lại đúng form với giá trị GV vừa nhập (không chuyển trang làm mất chữ), vẫn không ghi gì.
        var assignment = Assignments(h, s.Lecturer); assignment.ModelState.AddModelError("MaxAttempts", "Sai");
        var due = new DateTime(2026, 12, 1, 8, 30, 0);
        var created = Assert.IsType<ViewResult>(await assignment.Create(new AssignmentInput { ClassroomId = s.Room.Id, QuizId = s.Quiz.Id, MaxAttempts = 99, DueAt = due }));
        Assert.Equal("Create", created.ViewName);
        var kept = Assert.IsType<AssignmentCreateModel>(created.Model).Form;
        Assert.Equal((99, due), (kept.MaxAttempts, kept.DueAt));
        Assert.NotNull(assignment.TempData["Error"]);
        Assert.False(await h.Db.Assignments.AnyAsync(a => a.MaxAttempts == 99));
        var owner = await h.UserAsync("Chủ mới"); var (_, quiz) = await Draft(h, owner);
        var question = StudySets(h, owner); question.ModelState.AddModelError("Points", "Sai");
        var editor = Assert.IsType<ViewResult>(await question.SaveQuestion(new() { QuizId = quiz.Id, Type = QuestionType.TextInput, Prompt = "Câu GV vừa gõ rất dài" }));
        Assert.Equal("Quiz", editor.ViewName);
        Assert.Equal("Câu GV vừa gõ rất dài", Assert.IsType<QuizEditorModel>(editor.Model).Form.Prompt);
        Assert.Contains("chưa đúng định dạng", (string)question.TempData["Error"]!);
        await Assert.ThrowsAsync<BusinessException>(() => h.Library.SaveInputQuestionAsync(quiz.Id, null, null, new(QuestionType.TextInput, "X", null, 1, [["A"]], TimeLimitSeconds: 4), owner));
        Assert.False(await h.Db.UserSettingPresets.AnyAsync(p => p.UserId == owner && p.Scope == "Question.TextInput"));
    }

    [Fact]
    public async Task TenQuaDaiBaoLoiDeHieuKhongRaTrangLoi()
    {
        using var h = new Harness(); var owner = await h.UserAsync("Chủ tên dài");
        var tooLong = new string('a', 201);
        Assert.Equal("Tên bộ học tối đa 200 ký tự.", (await Assert.ThrowsAsync<BusinessException>(() => h.Library.CreateStudySetAsync(tooLong, owner))).Message);
        Assert.Equal("Tên lớp tối đa 200 ký tự.", (await Assert.ThrowsAsync<BusinessException>(() => h.Classrooms.CreateAsync(tooLong, ClassJoinPolicy.Immediate, owner))).Message);
        var set = await h.Library.CreateStudySetAsync(new string('b', 200), owner);
        Assert.Equal("Tên quiz tối đa 200 ký tự.", (await Assert.ThrowsAsync<BusinessException>(() => h.Library.CreateQuizAsync(set.Id, tooLong, owner))).Message);
        Assert.Equal(200, (await h.Classrooms.CreateAsync(new string('c', 200), ClassJoinPolicy.Immediate, owner)).Name.Length);
    }

    [Fact]
    public async Task LoiNghiepVuHienLaiFormGiuChuVuaNhap()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(1);
        // Hạn nộp đã qua: service từ chối; trang Giao bài hiện lại với hạn GV vừa nhập và mở mục cài đặt thêm.
        var controller = Assignments(h, s.Lecturer); var before = await h.Db.Assignments.CountAsync();
        var past = h.Clock.UtcNow.AddHours(7).AddDays(-1);
        var result = Assert.IsType<ViewResult>(await controller.Create(new AssignmentInput { ClassroomId = s.Room.Id, QuizId = s.Quiz.Id, DueAt = past, MaxAttempts = 3 }));
        Assert.Equal((past, 3), (Assert.IsType<AssignmentCreateModel>(result.Model).Form.DueAt, Assert.IsType<AssignmentCreateModel>(result.Model).Form.MaxAttempts));
        Assert.False(controller.ModelState.IsValid);
        Assert.Equal(before, await h.Db.Assignments.CountAsync());

        // Nhập Excel: bỏ chọn hết dòng rồi Lưu → hiện lại bảng đang sửa thay vì chuyển tới trang lỗi.
        var owner = await h.UserAsync("Chủ nhập"); var (_, quiz) = await Draft(h, owner);
        var import = AsUser(new ImportController(new ExcelImportService(h.Db, h.Library, h.Clock)), owner);
        var preview = new ImportPreview { QuizId = quiz.Id, FileName = "cau-hoi.xlsx",
            Rows = [new() { Sheet = "CauHoi", Row = 2, Include = false, Values = ["Chọn một", "Câu GV vừa sửa", "A", "B", "", "", "A", "", "1"] }] };
        var saved = Assert.IsType<ViewResult>(await import.Save(preview));
        Assert.Equal("Preview", saved.ViewName);
        Assert.Equal("Câu GV vừa sửa", Assert.IsType<ImportPreview>(saved.Model).Rows[0].Values[1]);
        Assert.NotNull(import.TempData["Error"]);
        var again = Assert.IsType<RedirectToActionResult>(import.PreviewAgain(quiz.Id));
        Assert.Equal(("Index", quiz.Id), (again.ActionName, again.RouteValues!["id"]));
    }

    [Theory]
    [InlineData(TrashKind.StudySet), InlineData(TrashKind.Quiz), InlineData(TrashKind.Flashcard)]
    public async Task XoaMemVaoDanhSachVaKhoiPhucTrong30Ngay(TrashKind kind)
    {
        using var h = new Harness(); var owner = await h.UserAsync("Chủ"); var (set, quiz) = await Draft(h, owner);
        await h.Library.MarkReadyAsync(quiz.Id, owner);
        await Cards(h).SaveAsync(set.Id, null, "Mặt trước", "Mặt sau", owner);
        var card = await h.Db.Flashcards.SingleAsync(c => c.StudySetId == set.Id);
        var id = kind == TrashKind.StudySet ? set.Id : kind == TrashKind.Quiz ? quiz.Id : card.Id;
        if (kind == TrashKind.StudySet) await h.Library.DeleteStudySetAsync(id, owner);
        else if (kind == TrashKind.Quiz) await h.Library.DeleteQuizAsync(id, owner);
        else await Cards(h).DeleteAsync(set.Id, id, owner);
        var item = Assert.Single(await Trash(h).ListAsync(owner), x => x.Id == id && x.Kind == kind);
        Assert.Equal(h.Clock.UtcNow, item.DeletedAt); Assert.Equal(h.Clock.UtcNow.AddDays(30), item.PurgeAt); Assert.True(item.CanRestore);
        if (kind == TrashKind.StudySet) { Assert.Empty(await h.Library.ListOwnedAsync(owner)); await Assert.ThrowsAsync<BusinessException>(() => h.Library.GetOwnedQuizAsync(quiz.Id, owner)); }
        else if (kind == TrashKind.Quiz) await Assert.ThrowsAsync<BusinessException>(() => h.Library.GetOwnedQuizAsync(quiz.Id, owner));
        else Assert.Empty((await Cards(h).GetEditorAsync(set.Id, owner)).Cards);
        h.Clock.UtcNow = h.Clock.UtcNow.AddDays(29);
        await Trash(h).RestoreAsync(kind, id, owner);
        Assert.Empty(await Trash(h).ListAsync(owner)); Assert.Equal(QuizStatus.Ready, (await h.Library.GetOwnedQuizAsync(quiz.Id, owner)).Status);
    }

    [Theory]
    [InlineData(TrashKind.Quiz), InlineData(TrashKind.Flashcard)]
    public async Task CanKhoiPhucBoTruocQuizVaThe(TrashKind kind)
    {
        using var h = new Harness(); var owner = await h.UserAsync("Chủ"); var (set, quiz) = await Draft(h, owner);
        await Cards(h).SaveAsync(set.Id, null, "A", "B", owner); var card = await h.Db.Flashcards.SingleAsync(c => c.StudySetId == set.Id);
        var id = kind == TrashKind.Quiz ? quiz.Id : card.Id;
        if (kind == TrashKind.Quiz) await h.Library.DeleteQuizAsync(id, owner); else await Cards(h).DeleteAsync(set.Id, id, owner);
        await h.Library.DeleteStudySetAsync(set.Id, owner);
        Assert.Contains(await Trash(h).ListAsync(owner), x => x.Kind == kind && x.ParentDeleted && !x.CanRestore);
        Assert.Contains("bộ học trước", (await Assert.ThrowsAsync<BusinessException>(() => Trash(h).RestoreAsync(kind, id, owner))).Message);
        await Trash(h).RestoreAsync(TrashKind.StudySet, set.Id, owner); await Trash(h).RestoreAsync(kind, id, owner);
    }

    [Fact]
    public async Task Het30NgayTuChoiKhoiPhucVaPurgeDonCaCayChuaGiao()
    {
        using var h = new Harness(); var owner = await h.UserAsync("Chủ"); var (set, quiz) = await Draft(h, owner);
        await h.Library.SaveInputQuestionAsync(quiz.Id, null, null, new(QuestionType.TextInput, "Điền", null, 1, [["A", "B"]]), owner);
        var q = quiz.Questions.First(); await h.Library.SaveChoiceQuestionAsync(quiz.Id, q.Id, q.Version, Harness.Single("Sửa"), owner);
        await Cards(h).SaveAsync(set.Id, null, "A", "B", owner);
        var ids = quiz.Questions.Select(x => x.Id).ToArray();
        await h.Library.DeleteStudySetAsync(set.Id, owner);
        h.Clock.UtcNow = h.Clock.UtcNow.AddDays(30);
        await Assert.ThrowsAsync<BusinessException>(() => Trash(h).RestoreAsync(TrashKind.StudySet, set.Id, owner));
        h.Clock.UtcNow = h.Clock.UtcNow.AddSeconds(1);
        var result = await Purge(h).RunAsync();
        Assert.True(result.StudySets >= 1); Assert.False(await h.Db.StudySets.AnyAsync(s => s.Id == set.Id));
        Assert.False(await h.Db.Quizzes.AnyAsync(q => q.Id == quiz.Id)); Assert.False(await h.Db.Questions.IgnoreQueryFilters().AnyAsync(q => ids.Contains(q.Id)));
        Assert.False(await h.Db.QuestionOptions.AnyAsync(o => ids.Contains(o.QuestionId))); Assert.False(await h.Db.QuestionBlanks.AnyAsync(b => ids.Contains(b.QuestionId)));
        Assert.False(await h.Db.QuestionRevisions.IgnoreQueryFilters().AnyAsync(r => ids.Contains(r.QuestionId)));
        Assert.False(await h.Db.Flashcards.AnyAsync(c => c.StudySetId == set.Id));
    }

    [Fact]
    public async Task PurgeRiengQuizTheVaCauXoaMemGiuBoDangDung()
    {
        using var h = new Harness(); var owner = await h.UserAsync("Chủ"); var (set, quiz) = await Draft(h, owner);
        var second = await h.Library.CreateQuizAsync(set.Id, "Giữ lại", owner);
        var q = await h.Library.SaveChoiceQuestionAsync(second.Id, null, null, Harness.Single(), owner);
        await Cards(h).SaveAsync(set.Id, null, "A", "B", owner); var card = await h.Db.Flashcards.SingleAsync(c => c.StudySetId == set.Id);
        await h.Library.DeleteQuizAsync(quiz.Id, owner); await h.Library.DeleteQuestionAsync(second.Id, q.Id, owner); await Cards(h).DeleteAsync(set.Id, card.Id, owner);
        Assert.True(await h.Db.Questions.IgnoreQueryFilters().AnyAsync(x => x.Id == q.Id && x.DeletedAt != null));
        h.Clock.UtcNow = h.Clock.UtcNow.AddDays(31); await Purge(h).RunAsync();
        Assert.True(await h.Db.StudySets.AnyAsync(s => s.Id == set.Id)); Assert.True(await h.Db.Quizzes.AnyAsync(x => x.Id == second.Id));
        Assert.False(await h.Db.Quizzes.AnyAsync(x => x.Id == quiz.Id)); Assert.False(await h.Db.Flashcards.AnyAsync(x => x.Id == card.Id));
        Assert.False(await h.Db.Questions.IgnoreQueryFilters().AnyAsync(x => x.Id == q.Id));
    }

    [Fact]
    public async Task PurgeKhongBaoGioDongVaoDaGiaoVaLuotThamChieuDeGoc()
    {
        using var h = new Harness(); var assigned = await h.ScenarioAsync(1); var attempted = await h.ScenarioAsync(1);
        var attempt = await h.Attempts.StartAsync(attempted.Assignment.Id, attempted.Student); await h.Attempts.FinishAsync(attempt.Id, attempted.Student);
        // Mô phỏng bàn giao: bài giao trỏ bản sao, bản đề lượt cũ vẫn tham chiếu câu gốc, không dựa vào dấu EverAssignedAt.
        var (_, replacement) = await Draft(h, attempted.Lecturer);
        attempted.Assignment.QuizId = replacement.Id; attempted.Assignment.Quiz = replacement; attempted.Assignment.StudySetId = replacement.StudySetId;
        var original = await h.Db.StudySets.SingleAsync(s => s.Id == attempted.Quiz.StudySetId);
        original.EverAssignedAt = null; attempted.Quiz.EverAssignedAt = null;
        foreach (var quiz in new[] { assigned.Quiz, attempted.Quiz })
        {
            quiz.DeletedAt = h.Clock.UtcNow.AddDays(-31);
            (await h.Db.StudySets.SingleAsync(s => s.Id == quiz.StudySetId)).DeletedAt = h.Clock.UtcNow.AddDays(-31);
        }
        await h.Db.SaveChangesAsync(); await Purge(h).RunAsync();
        Assert.True(await h.Db.Quizzes.AnyAsync(q => q.Id == assigned.Quiz.Id)); Assert.True(await h.Db.Quizzes.AnyAsync(q => q.Id == attempted.Quiz.Id));
        Assert.True(await h.Db.StudySets.AnyAsync(s => s.Id == original.Id)); Assert.True(await h.Db.Attempts.AnyAsync(a => a.Id == attempt.Id));
        Assert.True(await h.Db.Assignments.AnyAsync(a => a.Id == assigned.Assignment.Id));
        Assert.Equal(1, await h.Db.AttemptQuestions.CountAsync(q => q.AttemptId == attempt.Id));
    }

    [Fact]
    public async Task BoDaGiaoChiLuuTruVaLichSuVanDocDuoc()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(1);
        var a = await h.Attempts.StartAsync(s.Assignment.Id, s.Student); await h.Attempts.FinishAsync(a.Id, s.Student);
        await Assert.ThrowsAsync<BusinessException>(() => h.Library.DeleteStudySetAsync(s.Quiz.StudySetId, s.Lecturer));
        await Assert.ThrowsAsync<BusinessException>(() => h.Library.DeleteQuizAsync(s.Quiz.Id, s.Lecturer));
        await h.Assignments.CloseNowAsync(s.Assignment.Id, s.Lecturer);
        await h.Library.DeleteStudySetAsync(s.Quiz.StudySetId, s.Lecturer);
        var item = Assert.Single(await Trash(h).ListAsync(s.Lecturer), x => x.Kind == TrashKind.StudySet);
        Assert.True(item.Archived); Assert.Null(item.PurgeAt);
        var review = await h.Attempts.GetForLearnerAsync(a.Id, s.Student); Assert.Single(await h.Attempts.GetPlayQuestionsAsync(review, s.Student));
        h.Clock.UtcNow = h.Clock.UtcNow.AddDays(31); await Purge(h).RunAsync();
        Assert.True(await h.Db.StudySets.AnyAsync(x => x.Id == s.Quiz.StudySetId));
        await Trash(h).RestoreAsync(TrashKind.StudySet, s.Quiz.StudySetId, s.Lecturer);
    }

    [Fact]
    public async Task QuizDaGiaoChuaCoLuotDuocLuuTruGiuTrangThaiKhiKhoiPhuc()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(1); await h.Assignments.CloseNowAsync(s.Assignment.Id, s.Lecturer);
        await h.Library.DeleteQuizAsync(s.Quiz.Id, s.Lecturer);
        Assert.True(Assert.Single(await Trash(h).ListAsync(s.Lecturer), x => x.Kind == TrashKind.Quiz).Archived);
        h.Clock.UtcNow = h.Clock.UtcNow.AddDays(31); await Purge(h).RunAsync();
        await Trash(h).RestoreAsync(TrashKind.Quiz, s.Quiz.Id, s.Lecturer);
        Assert.Equal(QuizStatus.Ready, (await h.Library.GetOwnedQuizAsync(s.Quiz.Id, s.Lecturer)).Status);
    }

    [Fact]
    public async Task QuizChuaGiaoTrongBoDaGiaoVanDuocDonRieng()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(1);
        var unused = await h.Library.CreateQuizAsync(s.Quiz.StudySetId, "Chưa từng giao", s.Lecturer);
        await h.Library.SaveChoiceQuestionAsync(unused.Id, null, null, Harness.Single(), s.Lecturer);
        await h.Library.DeleteQuizAsync(unused.Id, s.Lecturer); h.Clock.UtcNow = h.Clock.UtcNow.AddDays(31);
        await Purge(h).RunAsync();
        Assert.False(await h.Db.Quizzes.AnyAsync(q => q.Id == unused.Id));
        Assert.True(await h.Db.Quizzes.AnyAsync(q => q.Id == s.Quiz.Id));
        Assert.True(await h.Db.StudySets.AnyAsync(x => x.Id == s.Quiz.StudySetId));
    }

    [Fact]
    public async Task ResetXongCoTheLuuLaiCungScope()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(1);
        await Presets(h).ResetAsync("Assignment.Practice", s.Lecturer);
        await h.Assignments.CreatePracticeAsync(s.Room.Id, s.Quiz.Id, new(null, null, 8), s.Lecturer);
        Assert.Equal(8, (await Presets(h).AssignmentAsync(AssignmentMode.Practice, s.Lecturer)).MaxAttempts);
    }

    [Fact]
    public async Task BaiKetHopNhoHinhThucPhanBatBuocVaHocTheSauHan()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(1);
        await Cards(h).SaveAsync(s.Quiz.StudySetId, null, "A", "B", s.Lecturer);
        await h.Assignments.CreatePracticeAsync(s.Room.Id, s.Quiz.Id,
            new(null, null, 2, Form: AssignmentForm.Combined, RequiredParts: RequiredParts.Both, AllowFlashcardsAfterDue: false), s.Lecturer);
        var form = (await Presets(h).AssignmentAsync(AssignmentMode.Practice, s.Lecturer)).ToInput(s.Room.Id, AssignmentMode.Practice);
        Assert.Equal(AssignmentForm.Combined, form.Form); Assert.Equal(RequiredParts.Both, form.RequiredParts);
        Assert.False(form.AllowFlashcardsAfterDue); Assert.Null(form.StudySetId); Assert.Equal(0, form.QuizId);
    }

    [Fact]
    public async Task XoaBaoCaoChiAnPhiaGiangVienHocVienVanThayReviewVaKhoiPhucDuoc()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(1);
        var a = await h.Attempts.StartAsync(s.Assignment.Id, s.Student); await h.Attempts.FinishAsync(a.Id, s.Student);
        await Assert.ThrowsAsync<BusinessException>(() => Trash(h).DeleteReportAsync(s.Assignment.Id, s.Lecturer));
        await h.Assignments.CloseNowAsync(s.Assignment.Id, s.Lecturer);
        var before = await h.Attempts.GetPlayQuestionsAsync(await h.Attempts.GetForLearnerAsync(a.Id, s.Student), s.Student);
        await Trash(h).DeleteReportAsync(s.Assignment.Id, s.Lecturer);
        Assert.DoesNotContain(await h.Assignments.ListForClassAsync(s.Room.Id), x => x.Id == s.Assignment.Id);
        Assert.Contains(await h.Assignments.ListForLearnerAsync(s.Student), x => x.Id == s.Assignment.Id);
        var after = await h.Attempts.GetPlayQuestionsAsync(await h.Attempts.GetForLearnerAsync(a.Id, s.Student), s.Student);
        Assert.Equal(JsonSerializer.Serialize(before), JsonSerializer.Serialize(after));
        Assert.Contains(await Trash(h).ListAsync(s.Lecturer), x => x.Kind == TrashKind.Report && x.CanRestore);
        await Trash(h).RestoreAsync(TrashKind.Report, s.Assignment.Id, s.Lecturer);
        Assert.Contains(await h.Assignments.ListForClassAsync(s.Room.Id), x => x.Id == s.Assignment.Id);
    }

    [Fact]
    public async Task BaoCaoQua30NgayKhongKhoiPhucNhungKhongBaoGioPurge()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(1); await h.Assignments.CloseNowAsync(s.Assignment.Id, s.Lecturer);
        await Trash(h).DeleteReportAsync(s.Assignment.Id, s.Lecturer); h.Clock.UtcNow = h.Clock.UtcNow.AddDays(31);
        await Assert.ThrowsAsync<BusinessException>(() => Trash(h).RestoreAsync(TrashKind.Report, s.Assignment.Id, s.Lecturer));
        await Purge(h).RunAsync(); Assert.True(await h.Db.Assignments.AnyAsync(x => x.Id == s.Assignment.Id));
    }

    [Fact]
    public async Task GiangVienPhuXoaKhoiPhucBaoCaoNhungKhongDuocXuLyBoCuaNguoiKhac()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(1); var assistant = await h.UserAsync("GV phụ");
        h.Db.ClassMembers.Add(new() { ClassroomId = s.Room.Id, UserId = assistant, Role = ClassRole.AssistantLecturer, Status = ClassMemberStatus.Active, JoinedAt = h.Clock.UtcNow });
        await h.Db.SaveChangesAsync(); await h.Assignments.CloseNowAsync(s.Assignment.Id, s.Lecturer);
        await Trash(h).DeleteReportAsync(s.Assignment.Id, assistant);
        Assert.Contains(await Trash(h).ListAsync(assistant), x => x.Kind == TrashKind.Report);
        await Trash(h).RestoreAsync(TrashKind.Report, s.Assignment.Id, assistant);
        await Assert.ThrowsAsync<ForbiddenException>(() => h.Library.DeleteStudySetAsync(s.Quiz.StudySetId, assistant));
        await h.Library.DeleteStudySetAsync(s.Quiz.StudySetId, s.Lecturer);
        await Assert.ThrowsAsync<ForbiddenException>(() => Trash(h).RestoreAsync(TrashKind.StudySet, s.Quiz.StudySetId, assistant));
    }

    [Theory]
    [InlineData(TrashKind.StudySet), InlineData(TrashKind.Quiz), InlineData(TrashKind.Flashcard), InlineData(TrashKind.Report)]
    public async Task NguoiKhacBi403KhiMoThungRacChuKhacHayPostKhoiPhuc(TrashKind kind)
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(1); var other = await h.UserAsync("Khác");
        await Cards(h).SaveAsync(s.Quiz.StudySetId, null, "A", "B", s.Lecturer); var card = await h.Db.Flashcards.SingleAsync(c => c.StudySetId == s.Quiz.StudySetId);
        var controller = StudySets(h, other);
        foreach (var work in new Func<Task>[] { async () => { await controller.Trash(s.Lecturer); }, async () => { await controller.Restore(kind, kind == TrashKind.Report ? s.Assignment.Id : kind == TrashKind.Quiz ? s.Quiz.Id : kind == TrashKind.Flashcard ? card.Id : s.Quiz.StudySetId); } })
        {
            var error = await Assert.ThrowsAsync<ForbiddenException>(work);
            var context = new ExceptionContext(new ActionContext(controller.HttpContext, new RouteData(), new ActionDescriptor()), []) { Exception = error };
            new BusinessExceptionFilter(null!).OnException(context);
            Assert.Equal(403, Assert.IsType<ViewResult>(context.Result).StatusCode);
        }
    }
}
