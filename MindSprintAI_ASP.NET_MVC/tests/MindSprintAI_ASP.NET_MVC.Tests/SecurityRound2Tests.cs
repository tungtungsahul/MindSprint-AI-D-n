using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MindSprintAI_ASP.NET_MVC.Controllers;
using MindSprintAI_ASP.NET_MVC.Data.Entities;
using MindSprintAI_ASP.NET_MVC.Models;
using MindSprintAI_ASP.NET_MVC.Services;

namespace MindSprintAI_ASP.NET_MVC.Tests;

public class SecurityRound2PureTests
{
    private static string Root => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));

    [Fact]
    public void N2_FormDoiCachVaoLopDungChuVaCoTheXuongDong()
    {
        var view = File.ReadAllText(Path.Combine(Root, "Views/Classrooms/Details.cshtml"));
        Assert.Contains("asp-action=\"JoinPolicy\"", view);
        foreach (var text in new[] { "Cách vào lớp bằng mã", "Vào ngay bằng mã", "Chờ giảng viên duyệt", "Lưu cách vào lớp",
            "Người đang chờ duyệt vẫn cần bạn duyệt. Học viên từng bị bạn xóa khỏi lớp luôn phải chờ duyệt khi vào lại." })
            Assert.Contains(text, view);
        Assert.Contains("d-flex flex-wrap gap-2 align-items-end", view);
        Assert.DoesNotContain("User.IsInRole", view);
    }

    [Fact]
    public void N2_NhatKyThemCuoiVaPostDungBaoVeChung()
    {
        var action = Enum.Parse<AuditAction>("ClassroomJoinPolicyChanged");
        Assert.Equal(112, (int)action);
        Assert.Equal("Đổi cách vào lớp", action.Text());
        Assert.NotNull(typeof(ClassroomsController).GetMethod("JoinPolicy")!.GetCustomAttributes(typeof(HttpPostAttribute), true).Single());
        var startup = File.ReadAllText(Path.Combine(Root, "Program.cs"));
        Assert.Contains("AutoValidateAntiforgeryTokenAttribute", startup);
    }
}

[Collection("db")]
public class SecurityRound2DbTests
{
    private const string TestLock = "Đang làm Kiểm tra: văn bản nguồn, thẻ ghi nhớ và trợ lý AI của bộ này đang bị khóa cho đến khi làm xong.";
    private static StudySetCloneService Clones(Harness h) => new(h.Db, h.Clock);
    private static ClassTransferService Transfers(Harness h) => new(h.Db, h.Classrooms, Clones(h),
        new(h.Db, h.Clock, h.Notifier), h.Clock, h.Invitations);
    private static int[] Correct(Attempt attempt) => [QuestionSnapshot.FromJson(attempt.Questions.Single().SnapshotJson).Options.Single(o => o.IsCorrect).Id];

    private static async Task<ApplicationUser> LecturerAsync(Harness h)
    {
        var id = await h.UserAsync("Giảng viên");
        var user = (await h.Db.Users.FindAsync(id))!;
        user.EmailConfirmed = true;
        await h.Db.SaveChangesAsync();
        return user;
    }

    [Theory]
    [InlineData(false, false), InlineData(false, true), InlineData(true, false), InlineData(true, true)]
    public async Task N1_ChiMangNoiDungDaGiaoTrongLopCuaCaGvChinhVaPhu(bool assistant, bool cards)
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(1);
        var owner = s.Lecturer;
        if (assistant)
        {
            var colleague = await LecturerAsync(h);
            await h.InviteAndAcceptAssistantAsync(s.Room.Id, colleague.Email!, s.Lecturer);
            owner = colleague.Id;
            (await h.Db.StudySets.FindAsync(s.Quiz.StudySetId))!.OwnerId = owner;
        }
        var unused = await h.Library.CreateQuizAsync(s.Quiz.StudySetId, "Chỉ giao lớp khác", owner);
        await h.Library.SaveChoiceQuestionAsync(unused.Id, null, null, Harness.Single(), owner);
        Assert.Empty(await h.Library.MarkReadyAsync(unused.Id, owner));
        var otherRoom = await h.Classrooms.CreateAsync("Lớp khác", ClassJoinPolicy.Immediate, owner);
        await h.Assignments.CreatePracticeAsync(otherRoom.Id, unused.Id, new(null, null, 3), owner);
        var deleted = await h.Library.CreateQuizAsync(s.Quiz.StudySetId, "Quiz đã xóa", owner);
        await h.Library.DeleteQuizAsync(deleted.Id, owner);
        // Dữ liệu cũ có câu đã xóa trước khi giao; không gọi thao tác xóa câu của quiz đã giao.
        h.Db.Questions.Add(new() { QuizId = s.Quiz.Id, Prompt = "Câu đã xóa", Type = QuestionType.SingleChoice,
            OrderIndex = 1, DeletedAt = h.Clock.UtcNow });
        var flashcards = new FlashcardService(h.Db, h.Library, h.Clock, h.Notifier);
        await flashcards.SaveAsync(s.Quiz.StudySetId, null, "Còn dùng", "Đáp án", owner);
        await flashcards.SaveAsync(s.Quiz.StudySetId, null, "Thẻ đã xóa", "Không mang theo", owner);
        var removedCard = await h.Db.Flashcards.SingleAsync(c => c.StudySetId == s.Quiz.StudySetId && c.Front == "Thẻ đã xóa");
        removedCard.DeletedAt = h.Clock.UtcNow;
        h.Db.SourceTexts.Add(new() { StudySetId = s.Quiz.StudySetId, Content = "Nguồn AI của bài đã giao", Version = 1 });
        await h.Db.SaveChangesAsync();
        if (cards) await h.Assignments.CreatePracticeAsync(s.Room.Id, s.Quiz.StudySetId,
            new(null, null, 3, Form: AssignmentForm.FlashcardOnly, StudySetId: s.Quiz.StudySetId), s.Lecturer);
        var attempt = await h.Attempts.StartAsync(s.Assignment.Id, s.Student);
        var q = attempt.Questions.Single();
        await h.Attempts.AnswerAsync(attempt.Id, q.Id, Guid.NewGuid(),
            [QuestionSnapshot.FromJson(q.SnapshotJson).Options.Single(o => o.IsCorrect).Id], s.Student);
        await h.Attempts.FinishAsync(attempt.Id, s.Student);
        var before = JsonSerializer.Serialize(await h.Attempts.GetPlayQuestionsAsync(attempt, s.Student));
        var score = attempt.FirstScore;
        var receiver = await LecturerAsync(h);
        var transfer = await Transfers(h).OfferAsync(s.Room.Id, receiver.Email!, true, s.Lecturer);
        await Transfers(h).DecideAsync(transfer.Id, true, receiver.Id);
        var copy = await h.Db.StudySets.Include(x => x.Quizzes).ThenInclude(x => x.Questions)
            .SingleAsync(x => x.OwnerId == receiver.Id && x.ClonedFromId == s.Quiz.StudySetId);
        var quiz = Assert.Single(copy.Quizzes);
        Assert.Equal(s.Quiz.Title, quiz.Title);
        Assert.Null(quiz.DeletedAt);
        Assert.Single(quiz.Questions); Assert.All(quiz.Questions, x => Assert.Null(x.DeletedAt));
        Assert.Equal(cards ? 1 : 0, await h.Db.Flashcards.CountAsync(c => c.StudySetId == copy.Id));
        Assert.False(await h.Db.Flashcards.AnyAsync(c => c.StudySetId == copy.Id && c.DeletedAt != null));
        Assert.Equal("Nguồn AI của bài đã giao", (await h.Db.SourceTexts.SingleAsync(x => x.StudySetId == copy.Id)).Content);
        Assert.Equal(quiz.Id, s.Assignment.QuizId);
        Assert.Equal(copy.Id, s.Assignment.StudySetId);
        Assert.Equal(unused.Id, (await h.Db.Assignments.SingleAsync(a => a.ClassroomId == otherRoom.Id)).QuizId);
        Assert.Equal(score, (await h.Attempts.GetForLearnerAsync(attempt.Id, s.Student)).FirstScore);
        Assert.Equal(before, JsonSerializer.Serialize(await h.Attempts.GetPlayQuestionsAsync(attempt, s.Student)));
        Assert.Equal(owner, (await h.Db.StudySets.FindAsync(s.Quiz.StudySetId))!.OwnerId);
    }

    [Fact]
    public async Task N1_QuizDaGiaoDaXoaKhongDuocNhanBanVaKhongLamHongChuyenLop()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(1);
        await h.Assignments.CloseNowAsync(s.Assignment.Id, s.Lecturer);
        await h.Library.DeleteQuizAsync(s.Quiz.Id, s.Lecturer);
        var receiver = await LecturerAsync(h);
        var transfer = await Transfers(h).OfferAsync(s.Room.Id, receiver.Email!, true, s.Lecturer);
        await Transfers(h).DecideAsync(transfer.Id, true, receiver.Id);
        var copy = await h.Db.StudySets.SingleAsync(x => x.OwnerId == receiver.Id && x.ClonedFromId == s.Quiz.StudySetId);
        Assert.False(await h.Db.Quizzes.AnyAsync(q => q.StudySetId == copy.Id));
        Assert.Equal(copy.Id, s.Assignment.StudySetId);
        Assert.Equal(s.Quiz.Id, s.Assignment.QuizId); // Chỉ tham chiếu lịch sử; nội dung gốc vẫn đã xóa.
        Assert.NotNull((await h.Db.Quizzes.FindAsync(s.Quiz.Id))!.DeletedAt);
    }

    [Fact]
    public async Task N1_NhanBanThuCongVanChonQuizChuaGiaoVaTheTheoQuyTac50()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(1);
        var unused = await h.Library.CreateQuizAsync(s.Quiz.StudySetId, "Chưa giao", s.Lecturer);
        await h.Library.SaveChoiceQuestionAsync(unused.Id, null, null, Harness.Single(), s.Lecturer);
        var cards = new FlashcardService(h.Db, h.Library, h.Clock, h.Notifier);
        await cards.SaveAsync(s.Quiz.StudySetId, null, "Trước", "Sau", s.Lecturer);
        var copy = await Clones(h).CloneAsync(s.Quiz.StudySetId, new([unused.Id], true, true), s.Lecturer);
        Assert.Equal(unused.Title, Assert.Single(copy.Quizzes).Title);
        Assert.Equal(QuizStatus.Draft, copy.Quizzes.Single().Status);
        Assert.Single(await h.Db.Flashcards.Where(c => c.StudySetId == copy.Id).ToListAsync());
        Assert.False(await h.Db.Assignments.AnyAsync(a => a.StudySetId == copy.Id));
    }

    [Theory]
    [InlineData(ClassMemberStatus.Removed, ClassJoinPolicy.Immediate, JoinOutcome.PendingApproval)]
    [InlineData(ClassMemberStatus.Removed, ClassJoinPolicy.RequireApproval, JoinOutcome.PendingApproval)]
    [InlineData(ClassMemberStatus.Left, ClassJoinPolicy.Immediate, JoinOutcome.Joined)]
    [InlineData(ClassMemberStatus.Left, ClassJoinPolicy.RequireApproval, JoinOutcome.PendingApproval)]
    public async Task N2_VaoLaiPhanBietBiXoaVaTuRoi(ClassMemberStatus status, ClassJoinPolicy policy, JoinOutcome expected)
    {
        using var h = new Harness(); var s = await h.ScenarioAsync();
        if (status == ClassMemberStatus.Removed) await h.Classrooms.RemoveMemberAsync(
            (await h.Db.ClassMembers.SingleAsync(m => m.ClassroomId == s.Room.Id && m.UserId == s.Student)).Id, s.Lecturer);
        else await h.Classrooms.LeaveAsync(s.Room.Id, s.Student);
        s.Room.JoinPolicy = policy; await h.Db.SaveChangesAsync();
        Assert.Equal(expected, (await h.Classrooms.JoinByCodeAsync(s.ClassCode, s.Student)).Outcome);
        Assert.Equal(expected == JoinOutcome.Joined ? ClassMemberStatus.Active : ClassMemberStatus.PendingApproval,
            (await h.Db.ClassMembers.SingleAsync(m => m.ClassroomId == s.Room.Id && m.UserId == s.Student)).Status);
    }

    [Theory]
    [InlineData(false), InlineData(true)]
    public async Task N2_GvChinhVaPhuDoiDuocCaHaiChieuGiuNguoiChoDuyetVaGhiNhatKy(bool assistant)
    {
        using var h = new Harness(); var s = await h.ScenarioAsync();
        var actor = s.Lecturer;
        if (assistant)
        {
            var colleague = await LecturerAsync(h);
            await h.InviteAndAcceptAssistantAsync(s.Room.Id, colleague.Email!, s.Lecturer);
            actor = colleague.Id;
        }
        await h.Classrooms.ChangeJoinPolicyAsync(s.Room.Id, ClassJoinPolicy.RequireApproval, actor);
        var waiting = await h.UserAsync("Chờ duyệt");
        await h.Classrooms.JoinByCodeAsync(s.ClassCode, waiting);
        using var services = new ServiceCollection().BuildServiceProvider();
        var audit = new AuditLog(h.Db, new HttpContextAccessor(), services.GetRequiredService<IServiceScopeFactory>(),
            h.Clock, NullLogger<AuditLog>.Instance);
        audit.Attach();
        await h.Classrooms.ChangeJoinPolicyAsync(s.Room.Id, ClassJoinPolicy.Immediate, actor);
        Assert.Equal(ClassJoinPolicy.Immediate, (await h.Db.Classrooms.AsNoTracking().SingleAsync(c => c.Id == s.Room.Id)).JoinPolicy);
        Assert.Equal(ClassMemberStatus.PendingApproval,
            (await h.Db.ClassMembers.SingleAsync(m => m.ClassroomId == s.Room.Id && m.UserId == waiting)).Status);
        Assert.Equal(JoinOutcome.Joined, (await h.Classrooms.JoinByCodeAsync(s.ClassCode, await h.UserAsync("Vào ngay"))).Outcome);
        var entry = await h.Db.AuditEntries.SingleAsync(e => e.Action == AuditAction.ClassroomJoinPolicyChanged && e.TargetId == s.Room.Id.ToString());
        Assert.Equal(actor, entry.ActorUserId); Assert.Equal("Classroom", entry.TargetType);
    }

    [Theory]
    [InlineData("student"), InlineData("outsider"), InlineData("removed-assistant")]
    public async Task N2_SaiQuyenKhongDoiDuoc(string kind)
    {
        using var h = new Harness(); var s = await h.ScenarioAsync();
        var actor = kind == "student" ? s.Student : await h.UserAsync("Ngoài lớp");
        if (kind == "removed-assistant")
        {
            h.Db.ClassMembers.Add(new() { ClassroomId = s.Room.Id, UserId = actor, Role = ClassRole.AssistantLecturer,
                Status = ClassMemberStatus.Removed, AddedAt = h.Clock.UtcNow });
            await h.Db.SaveChangesAsync();
        }
        await Assert.ThrowsAsync<ForbiddenException>(() => h.Classrooms.ChangeJoinPolicyAsync(s.Room.Id, ClassJoinPolicy.RequireApproval, actor));
        Assert.Equal(ClassJoinPolicy.Immediate, (await h.Db.Classrooms.AsNoTracking().SingleAsync(c => c.Id == s.Room.Id)).JoinPolicy);
    }

    [Fact]
    public async Task N2_TuChoiGiaTriCachVaoLopKhongHopLe()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync();
        await Assert.ThrowsAsync<BusinessException>(() => h.Classrooms.ChangeJoinPolicyAsync(s.Room.Id, (ClassJoinPolicy)999, s.Lecturer));
        Assert.Equal(ClassJoinPolicy.Immediate, (await h.Db.Classrooms.AsNoTracking().SingleAsync(c => c.Id == s.Room.Id)).JoinPolicy);
    }

    [Theory]
    [InlineData(AttemptStatus.InProgress, false), InlineData(AttemptStatus.Paused, false), InlineData(AttemptStatus.InProgress, true)]
    public async Task N3_KiemTraKhoaBatDauTraLoiDocVaReviewCungBo_NopXongMoLai(AttemptStatus status, bool legacy)
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(1);
        var old = await h.Attempts.StartAsync(s.Assignment.Id, s.Student);
        var oldQ = old.Questions.Single();
        await h.Attempts.AnswerAsync(old.Id, oldQ.Id, Guid.NewGuid(),
            [QuestionSnapshot.FromJson(oldQ.SnapshotJson).Options.Single(o => o.IsCorrect).Id], s.Student);
        await h.Attempts.FinishAsync(old.Id, s.Student);
        var practice = await h.Attempts.StartAsync(s.Assignment.Id, s.Student);
        var testQuiz = await h.Library.CreateQuizAsync(s.Quiz.StudySetId, "Quiz kiểm tra cùng bộ", s.Lecturer);
        await h.Library.SaveChoiceQuestionAsync(testQuiz.Id, null, null, Harness.Single(), s.Lecturer);
        Assert.Empty(await h.Library.MarkReadyAsync(testQuiz.Id, s.Lecturer));
        var test = await h.Assignments.CreateTestAsync(s.Room.Id, testQuiz.Id, new(null, 60), s.Lecturer);
        await h.Assignments.StartTestAsync(test.Id, s.Lecturer);
        var taking = await h.Attempts.StartAsync(test.Id, s.Student);
        if (legacy) s.Assignment.StudySetId = null; // Bài cũ tra bộ học qua QuizId.
        taking.Status = status; await h.Db.SaveChangesAsync();
        async Task Blocked(Func<Task> action) => Assert.Equal(TestLock, (await Assert.ThrowsAsync<BusinessException>(action)).Message);
        await Blocked(async () => await h.Attempts.StartAsync(s.Assignment.Id, s.Student));
        await Blocked(async () => await h.Attempts.AnswerAsync(practice.Id, practice.Questions.Single().Id, Guid.NewGuid(), [], s.Student));
        await Blocked(async () => await h.Attempts.GetForLearnerAsync(practice.Id, s.Student));
        await Blocked(async () => await h.Attempts.GetPlayQuestionsAsync(old, s.Student));
        Assert.Empty(await h.Db.AnswerSubmissions.Where(a => a.AttemptQuestion.AttemptId == practice.Id).ToListAsync());
        // Lượt Kiểm tra vẫn đọc/trả lời được; xong bài mới mở lại Ôn tập.
        taking.Status = AttemptStatus.InProgress; await h.Db.SaveChangesAsync();
        Assert.Equal(taking.Id, (await h.Attempts.GetForLearnerAsync(taking.Id, s.Student)).Id);
        await h.Attempts.AnswerAsync(taking.Id, taking.Questions.Single().Id, Guid.NewGuid(), Correct(taking), s.Student);
        await h.Attempts.FinishAsync(taking.Id, s.Student);
        Assert.Equal(practice.Id, (await h.Attempts.StartAsync(s.Assignment.Id, s.Student)).Id);
        Assert.NotEmpty(await h.Attempts.GetPlayQuestionsAsync(old, s.Student));
    }

    [Theory]
    [InlineData(false), InlineData(true)]
    public async Task N3_TuOnSauHanBiKhoaKeCaLuotDaMo(bool continuing)
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(1);
        s.Assignment.AllowQuizSelfReview = true;
        await h.Db.SaveChangesAsync();
        await h.Assignments.CloseNowAsync(s.Assignment.Id, s.Lecturer);
        Attempt? self = continuing ? await h.Attempts.StartSelfReviewAsync(s.Assignment.Id, s.Student) : null;
        var test = await h.Assignments.CreateTestAsync(s.Room.Id, s.Quiz.Id, new(null, 60), s.Lecturer);
        await h.Assignments.StartTestAsync(test.Id, s.Lecturer);
        await h.Attempts.StartAsync(test.Id, s.Student);
        Assert.Equal(TestLock, (await Assert.ThrowsAsync<BusinessException>(() => h.Attempts.StartSelfReviewAsync(s.Assignment.Id, s.Student))).Message);
        if (self != null)
            Assert.Equal(TestLock, (await Assert.ThrowsAsync<BusinessException>(() => h.Attempts.AnswerAsync(self.Id,
                self.Questions.Single().Id, Guid.NewGuid(), [], s.Student))).Message);
    }

    [Fact]
    public async Task N3_BoKhacVaNguoiKhacVanOnTapDuoc()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(1);
        var otherSet = await h.Library.CreateStudySetAsync("Bộ khác", s.Lecturer);
        var quiz = await h.Library.CreateQuizAsync(otherSet.Id, "Quiz khác", s.Lecturer);
        await h.Library.SaveChoiceQuestionAsync(quiz.Id, null, null, Harness.Single(), s.Lecturer);
        Assert.Empty(await h.Library.MarkReadyAsync(quiz.Id, s.Lecturer));
        var practice = await h.Assignments.CreatePracticeAsync(s.Room.Id, quiz.Id, new(null, null, 3), s.Lecturer);
        var test = await h.Assignments.CreateTestAsync(s.Room.Id, s.Quiz.Id, new(null, 60), s.Lecturer);
        await h.Assignments.StartTestAsync(test.Id, s.Lecturer);
        await h.Attempts.StartAsync(test.Id, s.Student);
        var attempt = await h.Attempts.StartAsync(practice.Id, s.Student);
        await h.Attempts.AnswerAsync(attempt.Id, attempt.Questions.Single().Id, Guid.NewGuid(), Correct(attempt), s.Student);
        await h.Attempts.FinishAsync(attempt.Id, s.Student);
        Assert.NotEmpty(await h.Attempts.GetPlayQuestionsAsync(attempt, s.Student));
        var other = await h.UserAsync("Học viên khác");
        await h.Classrooms.JoinByCodeAsync(s.ClassCode, other);
        Assert.NotNull(await h.Attempts.StartAsync(s.Assignment.Id, other));
    }
}
