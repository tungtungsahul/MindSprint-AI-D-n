using Microsoft.EntityFrameworkCore;
using MindSprintAI_ASP.NET_MVC.Data;
using MindSprintAI_ASP.NET_MVC.Data.Entities;
using MindSprintAI_ASP.NET_MVC.Services;

namespace MindSprintAI_ASP.NET_MVC.Tests;

public class TestClock(DateTime utcNow) : TimeProvider
{
    public DateTime UtcNow { get; set; } = utcNow;
    public override DateTimeOffset GetUtcNow() => new(UtcNow, TimeSpan.Zero);
}

public sealed class TestDevice : ILearnDevice
{
    public string Id { get; set; } = "test-browser";
}

// Database riêng cho test trên SQL Server máy (không đụng database MindSprintAI); tạo lại một lần mỗi lần chạy.
public sealed class DatabaseFixture
{
    // MINDSPRINT_TEST_DB: chạy test trên Linux/Docker (không có đăng nhập Windows), xem tools/debian/run-tests-docker.sh.
    public static readonly string ConnectionString = Environment.GetEnvironmentVariable("MINDSPRINT_TEST_DB") is { Length: > 0 } env ? env
        : "Server=localhost;Database=MindSprintAI_Tests;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True";

    public DatabaseFixture()
    {
        // Fixture xóa sạch database: chỉ cho phép database test, lỡ trỏ nhầm DB thật thì dừng ngay.
        var name = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(ConnectionString).InitialCatalog;
        if (name != "MindSprintAI_Tests") throw new InvalidOperationException($"Database test phải là MindSprintAI_Tests, không phải '{name}'.");
        using var db = NewContext();
        db.Database.EnsureDeleted();
        db.Database.Migrate(); // chạy đúng migration thật để kiểm luôn migration
    }

    public static AppDbContext NewContext() =>
        new(AppDbContextFactory.BuildOptions(ConnectionString));
}

[CollectionDefinition("db")]
public class DbCollection : ICollectionFixture<DatabaseFixture>;

// Mỗi test tự tạo người dùng/dữ liệu mới nên các test không giẫm lên nhau.
public sealed class Harness : IDisposable
{
    public AppDbContext Db { get; } = DatabaseFixture.NewContext();
    public TestClock Clock { get; } = new(new DateTime(2026, 11, 5, 8, 0, 0, DateTimeKind.Utc));
    public TestDevice Device { get; } = new();
    public ILearnNotifier Notifier { get; set; } = new NullLearnNotifier();
    public LearnConnectionTracker Connections { get; } = new();
    public JoinCodeService Codes => new(Db, Clock);
    public StudyLibraryService Library => new(Db, Clock, Notifier);
    public RegradeService Regrades => new(Db, Clock, Notifier);
    internal SafetyEmail InvitationEmail { get; } = new();
    public InvitationService Invitations => new(Db, Clock, new(Db, Clock, Notifier), InvitationEmail,
        new(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(), new Microsoft.AspNetCore.Http.HttpContextAccessor()));
    public ClassroomService Classrooms => new(Db, Codes, Clock, Notifier, Invitations);
    // (7c/L1) các ca 5c/5e thử quyền GV phụ phải chấp nhận lời mời trước.
    public async Task InviteAndAcceptAssistantAsync(int id, string email, string inviter)
    {
        await Classrooms.AddAssistantAsync(id, email, inviter);
        var invitation = await Db.Invitations.SingleAsync(i => i.ClassroomId == id && i.NormalizedEmail == InvitationService.Normalize(email)
            && i.Status == InvitationStatus.Pending);
        var user = await Db.Users.SingleAsync(u => u.Email == email);
        await Invitations.DecideAssistantAsync(invitation.Id, true, user.Id);
    }
    public AssignmentService Assignments => new(Db, Classrooms, Codes, Clock, Notifier);
    public AttemptService Attempts => new(Db, Clock, [new ChoiceGrader()], Device, Notifier, Connections);
    public GradebookService Gradebook => new(Db, Assignments, Clock);

    public async Task<string> UserAsync(string name)
    {
        var u = new ApplicationUser { UserName = $"{name}-{Guid.NewGuid():N}", Email = $"{Guid.NewGuid():N}@test.vn", DisplayName = name };
        Db.Users.Add(u);
        await Db.SaveChangesAsync();
        return u.Id;
    }

    public static ChoiceQuestionInput Single(string prompt = "2 + 2 = ?", decimal points = 1) => new(
        QuestionType.SingleChoice, prompt, null, points, false,
        [new("3", false), new("4", true), new("5", false), new("6", false)]);

    // Lớp + quiz sẵn sàng giao n câu chọn một + bài giao Ôn tập + một học viên đã vào lớp.
    public async Task<Scenario> ScenarioAsync(int questions = 3, PracticeSettings? settings = null, ClassJoinPolicy policy = ClassJoinPolicy.Immediate)
    {
        var lecturer = await UserAsync("Lan");
        var student = await UserAsync("Minh");
        var set = await Library.CreateStudySetAsync("C# Chương 1", lecturer);
        var quiz = await Library.CreateQuizAsync(set.Id, "Quiz 1", lecturer);
        for (var i = 0; i < questions; i++) await Library.SaveChoiceQuestionAsync(quiz.Id, null, null, Single($"Câu {i + 1}"), lecturer);
        Assert.Empty(await Library.MarkReadyAsync(quiz.Id, lecturer));
        var room = await Classrooms.CreateAsync("Lớp A", policy, lecturer);
        var classCode = (await Codes.GetActiveAsync(JoinCodeTarget.Classroom, room.Id))!.Code;
        await Classrooms.JoinByCodeAsync(classCode, student);
        Clock.UtcNow = Clock.UtcNow.AddMinutes(1);
        var assignment = await Assignments.CreatePracticeAsync(room.Id, quiz.Id,
            settings ?? new PracticeSettings(null, Clock.UtcNow.AddDays(2), 3), lecturer);
        return new Scenario(lecturer, student, quiz, room, assignment, classCode);
    }

    public void Dispose() => Db.Dispose();
}

public record Scenario(string Lecturer, string Student, Quiz Quiz, Classroom Room, Assignment Assignment, string ClassCode);
