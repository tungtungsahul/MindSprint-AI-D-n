using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MindSprintAI_ASP.NET_MVC.Data;
using MindSprintAI_ASP.NET_MVC.Data.Entities;

namespace MindSprintAI_ASP.NET_MVC.Services;

public record ClassDemoResult(int Students, int Classes, int Assignments, int Attempts, string Password);

// Dữ liệu mẫu lớp học (người dùng yêu cầu 17/11/2026, chỉ quản trị viên bấm ở trang Quản trị): 36 học viên mẫu đăng nhập được
// bằng một mật khẩu chung, 3 lớp do chính người bấm làm giảng viên chính, 3 bộ học, ~13 bài (ôn tập, kiểm tra, thẻ ghi nhớ,
// bài theo đề) diễn ra trong 20 ngày trước. Mọi thứ đi qua đúng các service thật với một đồng hồ giả chạy dần tới hiện tại,
// nên điểm, xếp hạng, bảng điểm, cảnh báo rời màn hình, làm lại… đều do chính hệ thống tính.
// Nhận biết dữ liệu mẫu: email @mindsprint-demo.invalid (tên miền không bao giờ có thật, bộ gửi email bỏ qua),
// lớp có đuôi "(mẫu)", bộ học bắt đầu "Mẫu — ".
public sealed class ClassDemoSeedService(AppDbContext db, UserManager<ApplicationUser> users, IEnumerable<IQuestionGrader> graders,
    PaperFileStore files, EmailLinks links, IWebHostEnvironment env, TimeProvider realClock, DemoSeedService? personal = null)
{
    public const string Domain = "mindsprint-demo.invalid";
    public const string ClassSuffix = " (mẫu)";
    public const string SetPrefix = "Mẫu — ";
    public const string AccountsFile = "demo-accounts-lop-mau.txt";

    private sealed class SeedClock(DateTime start) : TimeProvider
    {
        public DateTime UtcNow { get; set; } = start;
        public override DateTimeOffset GetUtcNow() => new(UtcNow, TimeSpan.Zero);
    }

    private sealed class SeedDevice : ILearnDevice { public string Id { get; set; } = "demo"; }
    private sealed class NoEmail : IEmailSender { public Task SendAsync(string to, string subject, string htmlBody) => Task.CompletedTask; }
    private InvitationService Invitations => new(db, clock, new NotificationService(db, clock, new NullLearnNotifier()), new NoEmail(), links);

    private sealed record Learner(string Id, string Name, double Skill, double Diligence, int Speed);

    private SeedClock clock = null!;
    private readonly SeedDevice device = new();
    private readonly Random rng = new(20261117);   // cố định: tạo lại ra cùng một bộ dữ liệu
    private DateTime day0;
    private int attemptCount;

    private JoinCodeService Codes => new(db, clock);
    private StudyLibraryService Library => new(db, clock, new NullLearnNotifier());
    private ClassroomService Classrooms => new(db, Codes, clock, new NullLearnNotifier(),
        Invitations);
    private AssignmentService Assignments => new(db, Classrooms, Codes, clock, new NullLearnNotifier());
    private AttemptService Attempts => new(db, clock, graders, device, new NullLearnNotifier(), new LearnConnectionTracker());
    private FlashcardService Flashcards => new(db, Library, clock, new NullLearnNotifier());
    private RetakeService Retakes => new(db, Assignments, Attempts, clock);
    private PaperTestService Papers => new(db, Assignments, Attempts, Classrooms, Codes, files, clock);

    public Task<bool> ExistsAsync(string lecturerId) =>
        db.ClassMembers.AnyAsync(m => m.UserId == lecturerId && m.Role == ClassRole.MainLecturer
            && m.Classroom.DeletedAt == null && m.Classroom.Name.EndsWith(ClassSuffix));

    // Mọi việc của cả 3 lớp xếp vào một hàng đợi theo thời gian rồi chạy đúng thứ tự (đồng hồ giả chỉ chạy tới), để học viên
    // làm bài ôn tập, ôn thẻ, kiểm tra xen kẽ nhau như thật. Việc có hẹn giờ sau "bây giờ" thật thì bỏ (không tạo dữ liệu tương lai).
    private readonly List<(DateTime At, int Seq, Func<Task> Run)> queue = [];
    private int seq;
    private DateTime realNow;
    private DateTime T(int day, int hour, double minute = 0) => day0.AddDays(day).AddHours(hour).AddMinutes(minute);
    private void On(DateTime at, Func<Task> run) => queue.Add((at, seq++, run));
    private void Later(double minutes) => clock.UtcNow = clock.UtcNow.AddMinutes(minutes);

    private async Task RunQueueAsync()
    {
        while (queue.Count > 0)
        {
            var next = queue.OrderBy(q => q.At).ThenBy(q => q.Seq).First();
            queue.Remove(next);
            if (next.At > realNow.AddMinutes(-5)) continue;
            if (next.At > clock.UtcNow) clock.UtcNow = next.At;
            await next.Run();
            db.ChangeTracker.Clear();
        }
    }

    public async Task<ClassDemoResult> SeedAsync(string lecturerId)
    {
        if (await ExistsAsync(lecturerId)) throw new BusinessException("Đã có dữ liệu mẫu lớp học. Hãy xóa dữ liệu mẫu cũ trước khi tạo lại.");
        realNow = realClock.GetUtcNow().UtcDateTime;
        // Ngày 0..20 theo giờ Việt Nam (UTC+7), ngày 20 là hôm nay: việc hôm nay chỉ tạo tới đúng lúc bấm (RunQueueAsync bỏ việc tương lai).
        day0 = realNow.AddHours(7).Date.AddDays(-20).AddHours(-7);
        clock = new SeedClock(day0);
        db.Audit = null;   // không ghi nhật ký kiểm toán cho từng thao tác giả lập

        var password = "Mau-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(5)) + "a!9";
        var learners = await CreateLearnersAsync(password);
        // Giảng viên mẫu (người dùng đồng ý 17/11): gv01 làm giảng viên phụ lớp Lập trình Web; gv02 có lớp riêng và đang đề nghị
        // chuyển lớp cho người bấm. Đăng nhập bằng cùng mật khẩu chung.
        var assistant = await CreateUserAsync("gv01", "Trần Minh Khoa", UserMode.Lecturer, password);
        var colleague = await CreateUserAsync("gv02", "Nguyễn Thị Hồng", UserMode.Lecturer, password);
        clock.UtcNow = T(0, 7);
        var web = await ImportAsync(SetPrefix + "Lập trình Web với ASP.NET Core MVC", WebCards, WebQuestions, lecturerId);
        var sql = await ImportAsync(SetPrefix + "Cơ sở dữ liệu — SQL căn bản", SqlCards, SqlQuestions, lecturerId);
        var eng = await ImportAsync(SetPrefix + "Từ vựng tiếng Anh chuyên ngành CNTT", EngCards, EngQuestions, lecturerId);
        await SourceAsync(web, WebSource); await SourceAsync(sql, SqlSource); await SourceAsync(eng, EngSource);

        // Người trong từng lớp. Lớp 1: 29 người từ đầu, 1 người vào muộn (ngày 12), 1 người rời lớp (ngày 15).
        var c1Base = learners.Take(29).ToList();
        var late = learners[29];
        var leaver = c1Base[28];
        var c1Mid = c1Base.Append(late).ToList();                       // ngày 12–15
        var c1End = c1Mid.Where(l => l != leaver).ToList();             // từ ngày 15
        var c2All = learners.Skip(18).Take(18).ToList();                 // lớp duyệt: 2 người cuối còn chờ duyệt
        var c2Active = c2All.SkipLast(2).ToList();
        var c3All = learners.Take(10).Concat(learners.Skip(30).Take(4)).ToList();
        int c1 = 0, c2 = 0, c3 = 0, count = 0;
        string c1Code = "";
        Assignment a1 = null!, a2 = null!, a3 = null!, a4 = null!, a5 = null!, b1 = null!, b2 = null!, b3 = null!, e1 = null!, e2 = null!;
        RetakeRequest request = null!;

        // ===== Lớp và thành viên =====
        On(T(0, 8), async () =>
        {
            c1 = (await Classrooms.CreateAsync("Lập trình Web — Nhóm 1" + ClassSuffix, ClassJoinPolicy.Immediate, lecturerId)).Id;
            c1Code = (await Codes.GetActiveAsync(JoinCodeTarget.Classroom, c1))!.Code;
            foreach (var l in c1Base) On(T(0, 9, rng.Next(0, 300)), () => Classrooms.JoinByCodeAsync(c1Code, l.Id));
        });
        On(T(1, 7), async () =>
        {
            c2 = (await Classrooms.CreateAsync("Cơ sở dữ liệu — Lớp chiều thứ Năm" + ClassSuffix, ClassJoinPolicy.RequireApproval, lecturerId)).Id;
            var code = (await Codes.GetActiveAsync(JoinCodeTarget.Classroom, c2))!.Code;
            foreach (var l in c2All) On(T(1, 8, rng.Next(0, 240)), () => Classrooms.JoinByCodeAsync(code, l.Id));
        });
        On(T(1, 14), async () =>
        {
            var pending = await db.ClassMembers.Where(m => m.ClassroomId == c2 && m.Status == ClassMemberStatus.PendingApproval).ToListAsync();
            foreach (var m in pending.Where(m => c2Active.Any(l => l.Id == m.UserId))) { Later(1); await Classrooms.DecideAsync(m.Id, true, lecturerId); }
        });
        On(T(2, 7), async () =>
        {
            c3 = (await Classrooms.CreateAsync("Tiếng Anh chuyên ngành CNTT" + ClassSuffix, ClassJoinPolicy.Immediate, lecturerId)).Id;
            var code = (await Codes.GetActiveAsync(JoinCodeTarget.Classroom, c3))!.Code;
            foreach (var l in c3All) On(T(2, 8, rng.Next(0, 300)), () => Classrooms.JoinByCodeAsync(code, l.Id));
            // Lớp này giảng viên tắt bảng xếp hạng với học viên.
            (await db.Classrooms.SingleAsync(c => c.Id == c3)).LeaderboardEnabled = false;
            await db.SaveChangesAsync();
        });
        On(T(12, 20), () => Classrooms.JoinByCodeAsync(c1Code, late.Id));
        On(T(3, 10), () => Classrooms.AddAssistantAsync(c1, $"gv01@{Domain}", lecturerId));
        On(T(3, 21), async () =>
        {
            var invite = await db.Invitations.SingleAsync(i => i.ClassroomId == c1 && i.NormalizedEmail == InvitationService.Normalize($"gv01@{Domain}")
                && i.Status == InvitationStatus.Pending);
            await Invitations.DecideAssistantAsync(invite.Id, true, assistant);
        });
        // Lớp riêng của gv02: 8 học viên, 1 bài ôn tập từ bộ học của gv02; ngày 18 gv02 đề nghị chuyển lớp cho người bấm (gv02 ở lại làm GV phụ).
        var oopPeople = learners.Skip(20).Take(8).ToList();
        int c4 = 0;
        On(T(0, 13), async () =>
        {
            var oop = await ImportAsync(SetPrefix + "Lập trình hướng đối tượng", OopCards, OopQuestions, colleague);
            c4 = (await Classrooms.CreateAsync("Lập trình hướng đối tượng — Nhóm 2" + ClassSuffix, ClassJoinPolicy.Immediate, colleague)).Id;
            var code = (await Codes.GetActiveAsync(JoinCodeTarget.Classroom, c4))!.Code;
            foreach (var l in oopPeople) On(T(0, 14, rng.Next(0, 240)), () => Classrooms.JoinByCodeAsync(code, l.Id));
            On(T(4, 8), async () =>
            {
                var f1 = await Assignments.CreatePracticeAsync(c4, oop.QuizId, new PracticeSettings(null, T(11, 16), 2), colleague); count++;
                Practice(() => f1, oopPeople, 4, 11, maxAttempts: 2);
            });
        });
        On(T(18, 16), async () =>
        {
            var me = await db.Users.Where(u => u.Id == lecturerId).Select(u => u.Email).SingleAsync();
            if (me != null) await new ClassTransferService(db, Classrooms, new StudySetCloneService(db, clock),
                new NotificationService(db, clock, new NullLearnNotifier()), clock, Invitations).OfferAsync(c4, me, true, colleague);
        });
        On(T(15, 21), () => Classrooms.LeaveAsync(c1, leaver.Id, confirmed: true));

        // ===== Lớp 1: Lập trình Web =====
        On(T(1, 15), async () =>
        {
            a1 = await Assignments.CreatePracticeAsync(c1, web.QuizId, new PracticeSettings(null, T(6, 16), 3, ScoreSelection.Highest), lecturerId); count++;
            a2 = await Assignments.CreatePracticeAsync(c1, 0, new PracticeSettings(null, T(8, 16), 1, Form: AssignmentForm.FlashcardOnly, StudySetId: web.SetId), lecturerId); count++;
            Practice(() => a1, c1Base, 1, 6, maxAttempts: 3);
            Cards(() => a2, web, c1Base, 1, 8);
        });
        // Kiểm tra 15 phút có giám sát (rời màn hình 3 lần thì tự nộp), 1 người được cộng 5 phút, 2 người vắng; đã công bố; 1 người làm lại.
        On(T(7, 8), async () => { a3 = await Assignments.CreateTestAsync(c1, web.QuizId, new TestSettings(null, 15, MonitorScreen: true, AutoSubmitLeaves: 3), lecturerId); count++; });
        On(T(7, 9), async () =>
        {
            await Assignments.StartTestAsync(a3.Id, lecturerId);
            await TestAsync(a3, c1Base, absent: [c1Base[27], c1Base[22]], leaves: new() { [c1Base[17]] = 1, [c1Base[26]] = 3 },
                extra: (c1Base[24], 5, "Học viên khiếm thị nhẹ, cần thêm thời gian đọc đề."), lecturerId);
        });
        On(T(7, 11), () => Assignments.PublishScoresAsync(a3.Id, lecturerId));
        On(T(7, 20), async () =>
        {
            device.Id = "demo-" + c1Base[19].Id;
            request = await Retakes.RequestAsync(a3.Id, "Em bị mất mạng giữa giờ nên làm vội phần cuối, mong cô cho em làm lại.", c1Base[19].Id);
        });
        On(T(8, 8), () => Retakes.DecideAsync(request.Id, true, T(9, 22), lecturerId));
        On(T(8, 19, 30), async () =>
        {
            var l = c1Base[19];
            device.Id = "demo-" + l.Id;
            var retake = await Retakes.StartAsync(request.Id, l.Id, consent: true);
            await AnswerAsync(retake, l with { Skill = 0.85 }, l.Id, 14);
            Later(0.5);
            await Attempts.FinishAsync(retake.Id, l.Id); attemptCount++;
        });
        // Ôn tập kết hợp quiz + thẻ (bắt buộc cả hai).
        On(T(10, 7), async () =>
        {
            a4 = await Assignments.CreatePracticeAsync(c1, web.QuizId, new PracticeSettings(null, T(17, 16), 2,
                Form: AssignmentForm.Combined, StudySetId: web.SetId, RequiredParts: RequiredParts.Both), lecturerId); count++;
            var people = c1Base.Where(l => l != leaver).ToList();   // người rời lớp ngày 15 không kịp làm
            Practice(() => a4, people, 10, 17, maxAttempts: 2);
            Cards(() => a4, web, people, 10, 17);
        });
        // Kiểm tra giữa kỳ 20 phút: đã kết thúc, CHƯA công bố điểm.
        On(T(14, 8), async () => { a5 = await Assignments.CreateTestAsync(c1, web.QuizId, new TestSettings(null, 20), lecturerId); count++; });
        On(T(14, 13, 30), async () =>
        {
            await Assignments.StartTestAsync(a5.Id, lecturerId);
            await TestAsync(a5, c1Mid, absent: [c1Mid[3]], leaves: [], extra: null, lecturerId);
        });
        // Bài làm theo đề (bài tập lớn): GV chấm phần lớn, còn vài bài chưa chấm nên chưa công bố.
        Assignment a6 = null!;
        var submitted = new List<long>();
        On(T(16, 7), async () =>
        {
            a6 = await Papers.CreateAsync(c1, new PaperSettings("Bài tập lớn: Thiết kế CSDL cho web bán sách", null, 45, MonitorScreen: true),
                Upload(ExamPdf, "de-bai-tap-lon.pdf"), lecturerId); count++;
        });
        On(T(16, 9), async () => { await Assignments.StartTestAsync(a6.Id, lecturerId); await PaperSessionAsync(a6, c1End, submitted); });
        On(T(17, 20), () => GradePapersAsync(submitted, c1End, lecturerId, assistant));
        // Bài ôn tập đang mở (hạn 3 ngày nữa): một phần đã làm, vài người làm dở; kiểm tra cuối kỳ đã lên lịch, chưa mở.
        On(T(19, 7), async () =>
        {
            var a7 = await Assignments.CreatePracticeAsync(c1, web.QuizId, new PracticeSettings(null, realNow.AddDays(3), 2), lecturerId); count++;
            Practice(() => a7, c1End, 19, 21, maxAttempts: 1, unfinished: 0.2, participation: 0.45);
            await Assignments.CreateTestAsync(c1, web.QuizId, new TestSettings(realNow.AddDays(2).AddHours(2), 30, DeviceLock: true, MonitorScreen: true, AutoSubmitLeaves: 5), lecturerId); count++;
        });

        // ===== Lớp 2: Cơ sở dữ liệu =====
        On(T(3, 7), async () =>
        {
            b1 = await Assignments.CreatePracticeAsync(c2, sql.QuizId, new PracticeSettings(null, T(9, 16), 2, ScoreSelection.Latest), lecturerId); count++;
            Practice(() => b1, c2Active, 3, 9, maxAttempts: 2);
        });
        On(T(10, 13), async () => { b2 = await Assignments.CreateTestAsync(c2, sql.QuizId, new TestSettings(null, 20, MonitorScreen: true), lecturerId); count++; });
        On(T(10, 14), async () =>
        {
            await Assignments.StartTestAsync(b2.Id, lecturerId);
            await TestAsync(b2, c2Active, absent: [c2Active[5]], leaves: new() { [c2Active[9]] = 2 }, extra: null, lecturerId);
        });
        On(T(10, 16), () => Assignments.PublishScoresAsync(b2.Id, lecturerId));
        On(T(15, 7), async () =>
        {
            b3 = await Assignments.CreatePracticeAsync(c2, 0, new PracticeSettings(null, realNow.AddDays(5), 1, Form: AssignmentForm.FlashcardOnly, StudySetId: sql.SetId), lecturerId); count++;
            Cards(() => b3, sql, c2Active, 15, 21, participation: 0.6);
        });

        // ===== Lớp 3: Tiếng Anh (xếp hạng tắt với học viên) =====
        On(T(3, 8), async () =>
        {
            e1 = await Assignments.CreatePracticeAsync(c3, 0, new PracticeSettings(null, realNow.AddDays(7), 1,
                Form: AssignmentForm.FlashcardOnly, StudySetId: eng.SetId, FlashcardMode: FlashcardMode.SpacedRepetition), lecturerId); count++;
            Cards(() => e1, eng, c3All, 3, 21, participation: 0.8);
        });
        On(T(11, 7), async () =>
        {
            e2 = await Assignments.CreatePracticeAsync(c3, eng.QuizId, new PracticeSettings(null, T(18, 16), 3, ScoreSelection.Average), lecturerId); count++;
            Practice(() => e2, c3All, 11, 18, maxAttempts: 3);
        });

        // Trợ lý AI (ở các bài ôn tập), Sổ tay AI và thông báo.
        AiChats(() => a1, web, WebSource, WebChats, c1Base, 1, 6, 10);
        AiChats(() => a4, web, WebSource, WebChats, c1Base.Where(l => l != leaver).ToList(), 10, 17, 6);
        AiChats(() => b1, sql, SqlSource, SqlChats, c2Active, 3, 9, 5);
        AiChats(() => e2, eng, EngSource, EngChats, c3All, 11, 18, 4);
        Notebooks(learners.Take(30).ToList(), 10);
        DueReminders(() => a1, "Quiz — " + SetPrefix + "Lập trình Web với ASP.NET Core MVC", T(6, 16), c1Base);
        DueReminders(() => a2, "Thẻ ghi nhớ", T(8, 16), c1Base);
        DueReminders(() => a4, "Quiz — " + SetPrefix + "Lập trình Web với ASP.NET Core MVC", T(17, 16), c1Base.Where(l => l != leaver).ToList());
        DueReminders(() => b1, "Quiz — " + SetPrefix + "Cơ sở dữ liệu — SQL căn bản", T(9, 16), c2Active);
        DueReminders(() => e2, "Quiz — " + SetPrefix + "Từ vựng tiếng Anh chuyên ngành CNTT", T(18, 16), c3All);
        CardReminders(c3All, 5, 20);
        On(T(2, 15), async () =>
        {
            await new NotificationService(db, clock, new NullLearnNotifier()).AddAsync(lecturerId, NotificationType.Reminder,
                new("Lớp \"Cơ sở dữ liệu — Lớp chiều thứ Năm" + ClassSuffix + "\": có yêu cầu vào lớp chờ duyệt quá 24 giờ.", $"/Classrooms/Details/{c2}", ClassroomId: c2));
            await db.SaveChangesAsync();
        });

        // Giảng viên tự ôn bộ thẻ của mình ~10 ngày gần đây (trang chủ có chuỗi học, Tổng quan có số liệu).
        for (var day = 10; day <= 20; day++)
            if (day is not (13 or 16)) On(T(day, 21, rng.Next(0, 40)), () => SelfStudyAsync(lecturerId, web));

        await RunQueueAsync();
        await MarkOldNotificationsReadAsync(learners, lecturerId);
        // Dữ liệu cá nhân của giảng viên (bộ học riêng, sổ tay, công việc, lịch học) nếu chưa có bộ demo đó; học viên mẫu có công việc + giờ học.
        if (personal != null && !await db.StudySets.AnyAsync(s => s.OwnerId == lecturerId && s.Title == DemoSeedService.SetTitle && s.DeletedAt == null))
            await personal.SeedAsync(lecturerId);
        await LearnerPlannerAsync(learners);

        var dir = Path.Combine(env.ContentRootPath, "App_Data");
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, AccountsFile),
            $"# Tài khoản học viên mẫu (tạo {VnTime.Format(realNow)}). Mật khẩu chung: {password}\n"
            + $"gv01@{Domain}  Trần Minh Khoa (giảng viên phụ lớp Lập trình Web)\ngv02@{Domain}  Nguyễn Thị Hồng (giảng viên lớp Lập trình hướng đối tượng, đang đề nghị chuyển lớp cho bạn)\n"
            + string.Join("\n", learners.Select((l, i) => $"hv{i + 1:00}@{Domain}  {l.Name}")) + "\n");
        return new(learners.Count, 4, count, attemptCount, password);   // 3 lớp của người bấm + 1 lớp của giảng viên mẫu gv02
    }

    // ---------- Người và nội dung ----------
    private static readonly string[] Names =
    [
        "Nguyễn Văn An", "Trần Thị Bích", "Lê Hoàng Cường", "Phạm Thu Dung", "Hoàng Minh Đức", "Vũ Thị Hà", "Đặng Quốc Huy",
        "Bùi Thanh Hương", "Đỗ Gia Khánh", "Ngô Thị Lan", "Dương Văn Long", "Lý Thị Mai", "Phan Nhật Minh", "Võ Thị Ngọc",
        "Trịnh Văn Phát", "Hồ Thị Phương", "Mai Đức Quang", "Tạ Thị Quỳnh", "Châu Minh Sơn", "Lưu Thị Tâm", "Kiều Văn Thắng",
        "Đinh Thị Thảo", "Tô Hữu Thiện", "Quách Thị Thu", "Lâm Văn Toàn", "Hà Thị Trang", "Cao Minh Trí", "Đoàn Thị Tuyết",
        "Thái Văn Tùng", "La Thị Uyên", "Mạc Văn Việt", "Ông Thị Xuân", "Nghiêm Hải Yến", "Khúc Văn Bảo", "Lục Thị Cúc", "Văn Đình Đạt"
    ];

    private async Task<string> CreateUserAsync(string handle, string name, UserMode mode, string password)
    {
        var user = new ApplicationUser
        {
            UserName = $"{handle}@{Domain}", Email = $"{handle}@{Domain}", EmailConfirmed = true, DisplayName = name, CurrentMode = mode,
            CreatedAt = day0.AddDays(-rng.Next(30, 90)), PolicyAcceptedAt = day0.AddDays(-1), PolicyVersion = LegalPolicy.CurrentVersion, EmailRemindersEnabled = false
        };
        var result = await users.CreateAsync(user, password);
        if (!result.Succeeded) throw new BusinessException("Không tạo được tài khoản mẫu: " + string.Join(" ", result.Errors.Select(e => e.Description)));
        return user.Id;
    }

    private async Task<List<Learner>> CreateLearnersAsync(string password)
    {
        var list = new List<Learner>();
        for (var i = 0; i < Names.Length; i++)
        {
            // Phân bố như một lớp thật: vài bạn giỏi, đa số khá/trung bình, vài bạn yếu, vài bạn ít làm bài.
            var (skill, diligence) = (i % 12) switch
            {
                0 or 7 => (0.95, 0.98), 1 or 4 or 9 => (0.82, 0.95), 2 or 5 or 10 => (0.7, 0.9),
                3 or 8 => (0.58, 0.85), 6 => (0.42, 0.75), _ => (0.62, 0.45)
            };
            var user = new ApplicationUser
            {
                UserName = $"hv{i + 1:00}@{Domain}", Email = $"hv{i + 1:00}@{Domain}", EmailConfirmed = true, DisplayName = Names[i],
                CurrentMode = UserMode.Learner, CreatedAt = day0.AddDays(-rng.Next(5, 40)),
                PolicyAcceptedAt = day0.AddDays(-1), PolicyVersion = LegalPolicy.CurrentVersion, EmailRemindersEnabled = false
            };
            var result = await users.CreateAsync(user, password);
            if (!result.Succeeded) throw new BusinessException("Không tạo được học viên mẫu: " + string.Join(" ", result.Errors.Select(e => e.Description)));
            list.Add(new(user.Id, Names[i], skill, diligence, 4 + rng.Next(0, 9)));
        }
        return list;
    }

    private sealed record SetInfo(int SetId, int QuizId, List<int> CardIds);

    private async Task<SetInfo> ImportAsync(string title, (string Front, string Back)[] cards, (string Q, string[] Options, int[] Correct)[] questions, string lecturerId)
    {
        var cardJson = JsonSerializer.SerializeToElement(cards.Select(c => new { front = c.Front, back = c.Back }));
        var questionJson = JsonSerializer.SerializeToElement(questions.Select(q => new { question = q.Q, options = q.Options, correct = q.Correct, explanation = "" }));
        var rows = AiGenerateService.ToRows(cardJson, cards: true, cards.Length).Concat(AiGenerateService.ToRows(questionJson, cards: false, questions.Length)).ToList();
        var preview = ExcelImportService.ToPreview(0, "Dữ liệu mẫu", ExcelTemplate.ValidateRows(rows), title);
        await new ExcelImportService(db, Library, clock).SaveAsync(preview, lecturerId);
        var quizId = preview.QuizId;
        var problems = await Library.MarkReadyAsync(quizId, lecturerId);
        if (problems.Count > 0) throw new BusinessException("Quiz mẫu chưa sẵn sàng: " + string.Join(" ", problems));
        var setId = await db.Quizzes.Where(q => q.Id == quizId).Select(q => q.StudySetId).SingleAsync();
        var cardIds = await db.Flashcards.Where(c => c.StudySetId == setId && c.DeletedAt == null).OrderBy(c => c.Id).Select(c => c.Id).ToListAsync();
        return new(setId, quizId, cardIds);
    }

    // ---------- Học viên làm bài ----------
    // budget: số phút còn lại của bài kiểm tra, chia đều cho các câu để không hết giờ giữa chừng; null = tự do như bài ôn tập.
    private async Task AnswerAsync(Attempt attempt, Learner l, string userId, double? budgetMinutes = null)
    {
        var count = Math.Max(1, attempt.Questions.Count);
        double Pace() => budgetMinutes is { } d ? Math.Max(0.1, d - 1) / count * Math.Min(0.95, 0.35 + l.Speed / 16.0) * (0.8 + rng.NextDouble() * 0.2)
            : 0.3 + rng.NextDouble() * l.Speed / 6.0;
        foreach (var q in attempt.Questions.OrderBy(q => q.Id))
        {
            var options = QuestionSnapshot.FromJson(q.SnapshotJson).Options;
            var correct = rng.NextDouble() < l.Skill;
            int[] pick = correct ? options.Where(o => o.IsCorrect).Select(o => o.Id).ToArray()
                : [options.Where(o => !o.IsCorrect).OrderBy(_ => rng.Next()).First().Id];
            Later(Pace());
            await Attempts.AnswerAsync(attempt.Id, q.Id, Guid.NewGuid(), pick, userId, questionVersion: q.QuestionVersion);
        }
    }

    // Mỗi học viên chăm chỉ ngồi làm vào một buổi tối trong khoảng ngày; bạn yếu hay làm thêm lượt (lượt sau khá hơn).
    private void Practice(Func<Assignment> assignment, List<Learner> people, int fromDay, int toDay, int maxAttempts,
        double unfinished = 0, double participation = 1)
    {
        foreach (var l in people.Where(l => rng.NextDouble() < l.Diligence * participation))
        {
            var tries = l.Skill < 0.75 && rng.NextDouble() < 0.6 ? Math.Min(maxAttempts, 1 + rng.Next(1, 3)) : 1;
            var leaveOpen = rng.NextDouble() < unfinished;
            On(T(fromDay + rng.Next(0, Math.Max(1, toDay - fromDay)), 18 + rng.Next(0, 5), rng.Next(0, 60)), async () =>
            {
                var a = assignment();
                device.Id = "demo-" + l.Id;
                for (var t = 0; t < tries; t++)
                {
                    var at = await Attempts.StartAsync(a.Id, l.Id);
                    await AnswerAsync(at, l with { Skill = Math.Min(0.97, l.Skill + 0.12 * t) }, l.Id);
                    attemptCount++;
                    if (t == tries - 1 && leaveOpen) break;   // làm dở, chưa nộp
                    Later(1);
                    await Attempts.FinishAsync(at.Id, l.Id);
                    Later(10 + rng.Next(0, 40));
                    db.ChangeTracker.Clear();
                }
            });
        }
    }

    private void Cards(Func<Assignment> assignment, SetInfo set, List<Learner> people, int fromDay, int toDay, double participation = 1)
    {
        foreach (var l in people.Where(l => rng.NextDouble() < l.Diligence * participation))
        {
            var cards = rng.NextDouble() < 0.25 ? set.CardIds.Take(set.CardIds.Count / 2).ToList() : set.CardIds;   // vài bạn ôn dở nửa bộ
            On(T(fromDay + rng.Next(0, Math.Max(1, toDay - fromDay)), 7 + rng.Next(0, 15), rng.Next(0, 60)), async () =>
            {
                var a = assignment();
                device.Id = "demo-" + l.Id;
                foreach (var card in cards)
                {
                    Later(0.2 + rng.NextDouble() * 0.8);
                    await Flashcards.RateAsync(set.SetId, a.Id, card, rng.NextDouble() < l.Skill ? FlashcardRating.Remembered : FlashcardRating.NotRemembered, l.Id);
                }
            });
        }
    }

    // Một phiên kiểm tra trọn vẹn: mọi người vào trong 3 phút đầu, có người rời màn hình, rồi làm và nộp trong giờ.
    private async Task TestAsync(Assignment a, List<Learner> people, List<Learner> absent, Dictionary<Learner, int> leaves,
        (Learner L, int Minutes, string Reason)? extra, string lecturerId)
    {
        var start = clock.UtcNow;
        var duration = (await db.Assignments.AsNoTracking().SingleAsync(x => x.Id == a.Id)).DurationMinutes ?? 15;
        var sessions = new List<(Learner L, long Id)>();
        foreach (var l in people.Except(absent))
        {
            clock.UtcNow = start.AddSeconds(rng.Next(10, 180));
            device.Id = "demo-" + l.Id;
            sessions.Add((l, (await Attempts.StartAsync(a.Id, l.Id, consent: true)).Id));
        }
        if (extra is { } e) { clock.UtcNow = start.AddMinutes(3); await Assignments.ChangeExtraMinutesAsync(a.Id, e.L.Id, e.Minutes, e.Reason, lecturerId); }
        foreach (var (l, id) in sessions)
        {
            clock.UtcNow = start.AddMinutes(3);
            device.Id = "demo-" + l.Id;
            var open = true;
            if (leaves.TryGetValue(l, out var times))
                for (var i = 0; i < times && open; i++)
                {
                    Later(1 + i);
                    await Attempts.ScreenEventAsync(id, l.Id, "start", ScreenLeaveKind.OtherWindow);
                    Later(0.2 + rng.NextDouble());
                    open = (await db.Attempts.AsNoTracking().SingleAsync(x => x.Id == id)).Status == AttemptStatus.InProgress;
                    if (open) await Attempts.ScreenEventAsync(id, l.Id, "end");
                }
            attemptCount++;
            // Ngưỡng tự nộp được chốt ở lần đọc kế tiếp (đọc lượt cũng chốt ngưỡng, H8): đọc qua service rồi mới xét.
            if (open && times > 0) { Later(0.1); await Attempts.ScreenEventAsync(id, l.Id, "heartbeat"); }
            var full = await db.Attempts.Include(x => x.Questions).SingleAsync(x => x.Id == id);
            if (!open || full.Status != AttemptStatus.InProgress) continue;   // đã bị tự nộp vì rời màn hình quá số lần
            await AnswerAsync(full, l, l.Id, (start.AddMinutes(duration) - clock.UtcNow).TotalMinutes);
            Later(rng.NextDouble());
            await Attempts.FinishAsync(id, l.Id);
            db.ChangeTracker.Clear();
        }
        clock.UtcNow = start.AddMinutes(duration + 6);   // cả phiên đã hết giờ (kể cả người được cộng giờ)
    }

    private static readonly byte[] ExamPdf = Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj\n2 0 obj<</Type/Pages/Kids[3 0 R]/Count 1>>endobj\n"
        + "3 0 obj<</Type/Page/Parent 2 0 R/MediaBox[0 0 595 842]/Contents 4 0 R/Resources<</Font<</F1 5 0 R>>>>>>endobj\n"
        + "4 0 obj<</Length 118>>stream\nBT /F1 18 Tf 60 780 Td (Bai tap lon - Thiet ke CSDL cho web ban sach) Tj 0 -30 Td /F1 12 Tf (Ve ERD, chuan hoa 3NF, viet lenh tao bang.) Tj ET\nendstream endobj\n"
        + "5 0 obj<</Type/Font/Subtype/Type1/BaseFont/Helvetica>>endobj\ntrailer<</Root 1 0 R>>\n%%EOF");

    private static readonly string[] Essays =
    [
        "Em chọn thiết kế gồm các bảng Sach, TacGia, TheLoai, KhachHang, DonHang và ChiTietDonHang. Quan hệ Sach–TacGia là nhiều–nhiều nên tách bảng Sach_TacGia. "
        + "Khóa chính dùng số tự tăng; DonHang lưu tổng tiền tại thời điểm mua để không đổi khi giá sách thay đổi.",
        "Mô hình của nhóm em: Sach(MaSach, TenSach, GiaBan, SoLuongTon, MaTheLoai), DonHang(MaDon, MaKH, NgayDat, TrangThai), ChiTietDonHang(MaDon, MaSach, SoLuong, DonGia). "
        + "Em đặt chỉ mục cho cột NgayDat để báo cáo doanh thu theo tháng chạy nhanh hơn.",
        "Em chưa kịp vẽ sơ đồ ERD đầy đủ, mới có các bảng chính: Sach, KhachHang, DonHang. Phần chuẩn hóa em sẽ bổ sung ở file đính kèm.",
        "Thiết kế đạt dạng chuẩn 3: tách địa chỉ giao hàng ra bảng DiaChi vì một khách có nhiều địa chỉ. Ràng buộc CHECK SoLuong > 0, khóa ngoại ON DELETE NO ACTION "
        + "để không xóa nhầm đơn hàng cũ khi xóa sách."
    ];

    private async Task PaperSessionAsync(Assignment a, List<Learner> people, List<long> submitted)
    {
        var start = clock.UtcNow;
        foreach (var l in people.Where(l => rng.NextDouble() < Math.Max(0.6, l.Diligence)))
        {
            clock.UtcNow = start.AddSeconds(rng.Next(20, 240));
            device.Id = "demo-" + l.Id;
            var at = await Attempts.StartAsync(a.Id, l.Id, consent: true);
            Later(10 + rng.Next(0, 25));
            var essay = Essays[rng.Next(Essays.Length)];
            await Papers.SaveTextAsync(at.Id, l.Skill < 0.5 ? essay[..Math.Min(essay.Length, 120)] : essay, l.Id);
            if (rng.NextDouble() < 0.4) await Papers.AddFileAsync(at.Id, Upload(ExamPdf, "erd-nhom.pdf"), l.Id);
            Later(2);
            await Attempts.FinishAsync(at.Id, l.Id);
            attemptCount++;
            submitted.Add(at.Id);
            db.ChangeTracker.Clear();
        }
        clock.UtcNow = start.AddMinutes(50);
    }

    // Chấm tay khoảng 80% bài, phần còn lại để giảng viên thấy "chưa chấm" (nên chưa công bố được).
    private async Task GradePapersAsync(List<long> submitted, List<Learner> people, string lecturerId, string? grader = null)
    {
        foreach (var id in submitted.Where((_, i) => i % 5 != 4))
        {
            Later(3);
            var learner = await db.Attempts.Where(x => x.Id == id).Select(x => x.Participant.UserId).SingleAsync();
            var skill = people.Single(p => p.Id == learner).Skill;
            var score = Math.Round((decimal)Math.Clamp(skill * 10 + (rng.NextDouble() - 0.5) * 2, 3, 10) * 4) / 4;   // bước 0,25
            await Papers.GradeAsync(id, score, score >= 8 ? "Thiết kế rõ ràng, chuẩn hóa tốt." : score >= 6 ? "Đạt yêu cầu, cần giải thích rõ lý do tách bảng." : "Còn thiếu ERD và phần chuẩn hóa.",
                grader != null && id % 3 == 0 ? grader : lecturerId);   // giảng viên phụ chấm khoảng 1/3
        }
    }

    // Công việc (Kanban) và giờ học tự đặt của học viên: phần lớn có, vài bạn để trống như người dùng thật.
    private static readonly (string Title, string? Note, KanbanPriority Priority)[] Tasks =
    [
        ("Ôn bộ thẻ Lập trình Web", "Ôn lại các thẻ hay quên trước bài kiểm tra", KanbanPriority.High),
        ("Làm bài ôn tập tuần này", null, KanbanPriority.Medium),
        ("Chuẩn bị bài tập lớn CSDL", "Vẽ ERD và chuẩn hóa 3NF", KanbanPriority.High),
        ("Học 10 từ vựng tiếng Anh CNTT", null, KanbanPriority.Low),
        ("Xem lại câu sai bài kiểm tra 15 phút", null, KanbanPriority.Medium),
        ("Đọc tài liệu Entity Framework Core", "Chương quan hệ giữa các bảng", KanbanPriority.Low)
    ];

    private async Task LearnerPlannerAsync(List<Learner> learners)
    {
        var kanban = new KanbanService(db);
        var plan = new StudyScheduleService(db);
        foreach (var l in learners.Where(l => l.Diligence >= 0.75 || rng.NextDouble() < 0.3))
        {
            foreach (var t in Tasks.OrderBy(_ => rng.Next()).Take(2 + rng.Next(0, 3)))
                await kanban.CreateAsync(l.Id, new(t.Title, t.Note, null, t.Priority, (KanbanStatus)rng.Next(0, 3)));
            var first = (DayOfWeek)rng.Next(1, 6);
            await plan.CreateAsync(l.Id, new(first, new(19, 30), new(21, 0), StudyTopic.Programming, "Lập trình Web"));
            if (rng.NextDouble() < 0.6) await plan.CreateAsync(l.Id, new((DayOfWeek)(((int)first + 2) % 7), new(20, 0), new(21, 0), StudyTopic.English, "Từ vựng CNTT"));
        }
    }

    private static IFormFile Upload(byte[] bytes, string name) => new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", name);

    private async Task SelfStudyAsync(string lecturerId, SetInfo web)
    {
        foreach (var card in web.CardIds.OrderBy(_ => rng.Next()).Take(4 + rng.Next(0, 5)))
        {
            Later(0.5);
            await Flashcards.RateAsync(web.SetId, null, card, rng.NextDouble() < 0.8 ? FlashcardRating.Remembered : FlashcardRating.NotRemembered, lecturerId);
        }
    }

    // ---------- Trợ lý AI, Sổ tay AI, thông báo (dữ liệu demo, người dùng đồng ý tự viết câu trả lời AI 17/11) ----------
    // Văn bản nguồn của bộ học: trợ lý AI chỉ trả lời dựa trên văn bản này (bộ học không có thì trợ lý báo "chưa có văn bản nguồn").
    private async Task SourceAsync(SetInfo set, string content)
    {
        db.SourceTexts.Add(new SourceText { StudySetId = set.SetId, Content = content });
        await db.SaveChangesAsync();
    }

    private sealed record Chat(int? Question, (string Q, string A, string Quote)[] Turns);

    // Hội thoại với trợ lý AI như khi học viên bấm "Hỏi trợ lý" trong bài ôn tập: câu trả lời có trích dẫn đúng đoạn văn bản nguồn,
    // một số hội thoại gắn với một câu trong quiz (GV thấy thống kê "số lượt hỏi theo câu").
    private void AiChats(Func<Assignment> assignment, SetInfo set, string source, Chat[] pool, List<Learner> people, int fromDay, int toDay, int count)
    {
        foreach (var l in people.OrderBy(_ => rng.Next()).Take(count))
        {
            var chats = pool.OrderBy(_ => rng.Next()).Take(1 + (rng.NextDouble() < 0.35 ? 1 : 0)).ToList();
            On(T(fromDay + rng.Next(0, Math.Max(1, toDay - fromDay)), 19 + rng.Next(0, 4), rng.Next(0, 60)), async () =>
            {
                var a = assignment();
                var questionIds = await db.Questions.Where(q => q.QuizId == set.QuizId).OrderBy(q => q.OrderIndex).Select(q => q.Id).ToListAsync();
                foreach (var chat in chats)
                {
                    int? questionId = chat.Question is { } i ? questionIds[i] : null;
                    var conversation = new AiConversation { UserId = l.Id, StudySetId = set.SetId, AssignmentId = a.Id, QuestionId = questionId,
                        Title = chat.Turns[0].Q[..Math.Min(chat.Turns[0].Q.Length, 120)], CreatedAt = clock.UtcNow };
                    foreach (var (q, answer, quote) in chat.Turns.Take(1 + rng.Next(0, chat.Turns.Length)))
                    {
                        var start = source.IndexOf(quote, StringComparison.Ordinal);
                        if (start < 0) throw new InvalidOperationException("Trích dẫn mẫu không có trong văn bản nguồn: " + quote);
                        conversation.Messages.Add(new() { Role = "user", Content = q, QuestionId = questionId, SourceVersion = 1, CreatedAt = clock.UtcNow });
                        Later(0.1 + rng.NextDouble() * 0.3);
                        conversation.Messages.Add(new() { Role = "assistant", Content = answer, QuestionId = questionId, SourceVersion = 1, CreatedAt = clock.UtcNow,
                            Citations = [new() { ExcerptText = quote, StartOffset = start, EndOffset = start + quote.Length }] });
                        Later(1 + rng.NextDouble() * 3);
                    }
                    db.AiConversations.Add(conversation);
                    Later(5);
                }
                await db.SaveChangesAsync();
            });
        }
    }

    // Sổ tay AI riêng của học viên: tài liệu dán vào + ghi chú (kể cả ghi chú lưu từ câu trả lời của Sổ tay AI).
    // Phần hỏi–đáp trong sổ tay chỉ lưu ở trình duyệt người dùng nên không tạo sẵn được.
    private void Notebooks(List<Learner> people, int count)
    {
        foreach (var l in people.OrderBy(_ => rng.Next()).Take(count))
        {
            var sample = NotebookSamples[rng.Next(NotebookSamples.Length)];
            On(T(4 + rng.Next(0, 15), 20 + rng.Next(0, 3), rng.Next(0, 60)), async () =>
            {
                var notebook = new Notebook { UserId = l.Id, Title = sample.Title, CreatedAt = clock.UtcNow };
                notebook.Sources.Add(new() { Title = sample.SourceTitle, Kind = NotebookSourceKind.Text, Content = sample.Source, CreatedAt = clock.UtcNow });
                foreach (var (title, content) in sample.Notes.Take(1 + rng.Next(0, sample.Notes.Length)))
                {
                    Later(2 + rng.Next(0, 20));
                    notebook.Notes.Add(new() { Title = title, Content = content, CreatedAt = clock.UtcNow });
                }
                db.Notebooks.Add(notebook);
                await db.SaveChangesAsync();
            });
        }
    }

    // Thông báo đúng loại hệ thống tự gửi: bài sắp đến hạn (bạn chưa xong lúc còn 20 giờ), thẻ đến hạn ôn; thông báo cũ phần lớn đã đọc.
    private void DueReminders(Func<Assignment> assignment, string title, DateTime due, List<Learner> people)
    {
        On(due.AddHours(-20), async () =>
        {
            var a = assignment();
            var done = await db.Participants.Where(p => p.AssignmentId == a.Id && p.CompletionStatus == CompletionStatus.Completed)
                .Select(p => p.UserId).ToListAsync();
            var notices = new NotificationService(db, clock, new NullLearnNotifier());
            foreach (var l in people.Where(l => !done.Contains(l.Id)))
                await notices.AddAsync(l.Id, NotificationType.Reminder, new($"Bài \"{title}\" sắp đến hạn nộp (còn dưới 24 giờ).", $"/Learn/Assignment/{a.Id}", AssignmentId: a.Id));
            await db.SaveChangesAsync();
        });
    }

    private void CardReminders(List<Learner> people, int fromDay, int toDay)
    {
        for (var day = fromDay; day <= toDay; day += 2)
            foreach (var l in people.Where(_ => rng.NextDouble() < 0.5))
                On(T(day, 7), async () =>
                {
                    await new NotificationService(db, clock, new NullLearnNotifier()).AddAsync(l.Id, NotificationType.Reminder,
                        new($"Hôm nay bạn có {3 + rng.Next(0, 9)} thẻ đến hạn ôn", "/StudySets"));
                    await db.SaveChangesAsync();
                });
    }

    // Thông báo trước hôm qua: người chăm chỉ đã đọc gần hết, người ít vào thì còn nhiều thông báo chưa đọc.
    private async Task MarkOldNotificationsReadAsync(List<Learner> learners, string lecturerId)
    {
        var ids = learners.Select(l => l.Id).Append(lecturerId).ToList();
        var rows = await db.Notifications.Where(n => ids.Contains(n.UserId) && n.ReadAt == null && n.CreatedAt < realNow.AddDays(-1)).ToListAsync();
        foreach (var n in rows)
        {
            var diligence = learners.FirstOrDefault(l => l.Id == n.UserId)?.Diligence ?? 0.95;
            if (rng.NextDouble() < diligence) n.ReadAt = n.CreatedAt.AddHours(1 + rng.Next(0, 20));
        }
        await db.SaveChangesAsync();
    }

    private const string WebSource =
        "Bài 1. Mô hình MVC trong ASP.NET Core\n"
        + "MVC chia ứng dụng thành ba phần. Model giữ dữ liệu và quy tắc nghiệp vụ. View chỉ lo hiển thị giao diện. Controller nhận yêu cầu từ trình duyệt, gọi Model xử lý rồi chọn View để trả về.\n"
        + "Nhờ tách như vậy, có thể sửa giao diện mà không đụng tới phần xử lý, và kiểm thử Controller riêng.\n\n"
        + "Bài 2. Razor và Tag Helper\n"
        + "Tệp giao diện Razor có đuôi .cshtml. Ký tự @ chuyển từ HTML sang C#. Tag Helper như asp-action, asp-controller tạo đường dẫn đúng với cấu hình định tuyến; asp-for gắn ô nhập với thuộc tính của Model để tên trường và kiểm tra dữ liệu luôn khớp.\n\n"
        + "Bài 3. Dependency Injection\n"
        + "Service được đăng ký một lần trong Program.cs rồi hệ thống tự truyền vào constructor của Controller. Có ba vòng đời: Singleton dùng chung suốt thời gian chạy ứng dụng, Scoped tạo mới cho mỗi yêu cầu, Transient tạo mới mỗi lần được yêu cầu.\n\n"
        + "Bài 4. Entity Framework Core và migration\n"
        + "Entity Framework Core là ORM: ánh xạ lớp C# thành bảng. Khi đổi Model, chạy lệnh dotnet ef migrations add để sinh migration, rồi dotnet ef database update để áp thay đổi lên cơ sở dữ liệu.\n\n"
        + "Bài 5. Chống giả mạo yêu cầu\n"
        + "Form gửi bằng POST kèm một mã ẩn gọi là anti-forgery token. Máy chủ chỉ nhận yêu cầu có mã khớp, nhờ đó trang web khác không thể giả danh người dùng để gửi form (tấn công CSRF). Sau khi xử lý xong POST, nên trả RedirectToAction để người dùng tải lại trang không gửi form lần nữa.";

    private const string SqlSource =
        "Chương 1. Khóa và quan hệ\n"
        + "Khóa chính xác định duy nhất mỗi dòng và không được NULL. Khóa ngoại tham chiếu khóa chính của bảng khác để giữ toàn vẹn dữ liệu.\n\n"
        + "Chương 2. Truy vấn\n"
        + "WHERE lọc từng dòng trước khi gom nhóm. GROUP BY gom các dòng cùng giá trị để tính COUNT, SUM, AVG. HAVING lọc nhóm sau khi đã gom. LEFT JOIN giữ mọi dòng của bảng bên trái kể cả khi không có dòng khớp bên phải.\n\n"
        + "Chương 3. Giao dịch\n"
        + "Transaction gom nhiều lệnh thành một khối: hoặc tất cả thành công với COMMIT, hoặc hủy toàn bộ với ROLLBACK, nên không bao giờ chỉ trừ tiền tài khoản này mà quên cộng cho tài khoản kia.\n\n"
        + "Chương 4. Chuẩn hóa\n"
        + "Dạng chuẩn 3 yêu cầu mọi cột không khóa chỉ phụ thuộc vào khóa chính, không phụ thuộc bắc cầu qua cột khác. Chỉ mục giúp tìm dòng nhanh hơn nhưng làm thao tác ghi chậm đi một chút.";

    private const string EngSource =
        "Unit 1. Working with code\n"
        + "A developer writes code, then compiles it. When the program does not work as expected, there is a bug, and the developer has to debug it. Code is stored in a repository so the whole team can work on it.\n\n"
        + "Unit 2. Running a system\n"
        + "To deploy an application means to put it on a server so users can access it. Latency is the delay before data starts to arrive; bandwidth is how much data can be sent per second. Scalability is the ability of a system to handle more users.";

    private static readonly Chat[] WebChats =
    [
        new(0, [("Em vẫn chưa hiểu Controller khác Model ở chỗ nào ạ?",
            "Controller là phần nhận yêu cầu từ trình duyệt và điều phối: nó gọi Model để xử lý dữ liệu rồi chọn View để trả về. Model thì giữ dữ liệu và quy tắc nghiệp vụ, không biết gì về trình duyệt.",
            "Controller nhận yêu cầu từ trình duyệt, gọi Model xử lý rồi chọn View để trả về."),
            ("Vậy View có được gọi thẳng Model không?",
            "View chỉ lo hiển thị dữ liệu được Controller truyền sang, không tự xử lý nghiệp vụ. Tách như vậy để sửa giao diện không ảnh hưởng phần xử lý.",
            "View chỉ lo hiển thị giao diện.")]),
        new(4, [("Singleton với Scoped khác nhau thế nào, khi nào dùng cái nào?",
            "Singleton tạo một lần và dùng chung suốt thời gian ứng dụng chạy; Scoped tạo mới cho mỗi yêu cầu. Thứ gắn với một yêu cầu như DbContext nên là Scoped; thứ dùng chung, không giữ trạng thái riêng của người dùng thì có thể là Singleton.",
            "Singleton dùng chung suốt thời gian chạy ứng dụng, Scoped tạo mới cho mỗi yêu cầu")]),
        new(3, [("Em đổi Model rồi mà database chưa có cột mới, em thiếu bước nào ạ?",
            "Sau khi đổi Model cần sinh migration bằng dotnet ef migrations add, rồi áp lên cơ sở dữ liệu bằng dotnet ef database update. Bạn có thể mới làm bước đầu.",
            "chạy lệnh dotnet ef migrations add để sinh migration, rồi dotnet ef database update để áp thay đổi lên cơ sở dữ liệu")]),
        new(5, [("Anti-forgery token dùng để làm gì vậy ạ?",
            "Đó là mã ẩn gửi kèm form POST. Máy chủ chỉ nhận yêu cầu có mã khớp, nên trang web khác không giả danh bạn để gửi form được. Kiểu tấn công này gọi là CSRF.",
            "Máy chủ chỉ nhận yêu cầu có mã khớp, nhờ đó trang web khác không thể giả danh người dùng để gửi form (tấn công CSRF)."),
            ("Thế sao sau khi lưu lại phải RedirectToAction ạ?",
            "Để khi người dùng bấm tải lại trang, trình duyệt không gửi lại form lần nữa (tránh lưu trùng).",
            "nên trả RedirectToAction để người dùng tải lại trang không gửi form lần nữa")]),
        new(2, [("asp-action với asp-for khác nhau sao ạ?",
            "asp-action tạo đường dẫn tới một action đúng cấu hình định tuyến. asp-for thì gắn ô nhập với một thuộc tính của Model để tên trường và kiểm tra dữ liệu luôn khớp.",
            "asp-for gắn ô nhập với thuộc tính của Model để tên trường và kiểm tra dữ liệu luôn khớp")]),
        new(null, [("Tóm tắt giúp em bài Razor trong 3 ý được không?",
            "1) Tệp giao diện Razor có đuôi .cshtml. 2) Ký tự @ chuyển từ HTML sang C#. 3) Tag Helper như asp-action, asp-for giúp sinh HTML đúng đường dẫn và tên trường.",
            "Tệp giao diện Razor có đuôi .cshtml. Ký tự @ chuyển từ HTML sang C#.")])
    ];

    private static readonly Chat[] SqlChats =
    [
        new(0, [("WHERE và HAVING khác nhau thế nào ạ?",
            "WHERE lọc từng dòng trước khi gom nhóm; HAVING lọc các nhóm sau khi đã GROUP BY. Ví dụ lọc lớp có trên 30 học viên thì dùng HAVING COUNT(*) > 30.",
            "WHERE lọc từng dòng trước khi gom nhóm.")]),
        new(1, [("LEFT JOIN thì dòng không khớp sẽ ra sao ạ?",
            "LEFT JOIN giữ mọi dòng của bảng bên trái; dòng không có bản ghi khớp bên phải vẫn hiện, các cột của bảng phải nhận giá trị NULL.",
            "LEFT JOIN giữ mọi dòng của bảng bên trái kể cả khi không có dòng khớp bên phải.")]),
        new(4, [("Vì sao chuyển tiền phải dùng transaction ạ?",
            "Vì cần trừ tài khoản này và cộng tài khoản kia cùng thành công. Transaction đảm bảo hoặc tất cả được COMMIT, hoặc hủy toàn bộ bằng ROLLBACK.",
            "nên không bao giờ chỉ trừ tiền tài khoản này mà quên cộng cho tài khoản kia")]),
        new(null, [("Dạng chuẩn 3 nói dễ hiểu là gì ạ?",
            "Mỗi cột không phải khóa chỉ được phụ thuộc trực tiếp vào khóa chính, không đi vòng qua một cột khác. Nếu có, tách cột đó ra bảng riêng.",
            "Dạng chuẩn 3 yêu cầu mọi cột không khóa chỉ phụ thuộc vào khóa chính, không phụ thuộc bắc cầu qua cột khác.")])
    ];

    private static readonly Chat[] EngChats =
    [
        new(0, [("\"deploy\" dịch sát nghĩa là gì ạ?",
            "\"Deploy\" là triển khai: đưa ứng dụng lên máy phục vụ để người dùng truy cập được.",
            "To deploy an application means to put it on a server so users can access it.")]),
        new(1, [("latency với bandwidth khác nhau thế nào ạ?",
            "Latency (độ trễ) là khoảng chờ trước khi dữ liệu bắt đầu tới; bandwidth (băng thông) là lượng dữ liệu gửi được mỗi giây.",
            "Latency is the delay before data starts to arrive; bandwidth is how much data can be sent per second.")]),
        new(3, [("debug và bug liên quan gì nhau ạ?",
            "Bug là lỗi khiến chương trình chạy sai; debug là việc tìm và sửa lỗi đó.",
            "there is a bug, and the developer has to debug it")])
    ];

    private static readonly (string Title, string SourceTitle, string Source, (string Title, string Content)[] Notes)[] NotebookSamples =
    [
        ("Ôn thi Lập trình Web", "Ghi chép buổi 3 — MVC và Razor",
            "Buổi 3: thầy cô giải thích luồng một yêu cầu: trình duyệt gửi yêu cầu → định tuyến chọn Controller và action → action lấy dữ liệu từ Model → trả View. "
            + "Razor dùng @ để chèn C#. Tag Helper asp-action tạo link, asp-for gắn ô nhập với thuộc tính Model. Bài tập về nhà: làm trang danh sách sách có tìm kiếm.",
            [("Cần nhớ", "Controller điều phối · Model giữ dữ liệu · View hiển thị"),
             ("Lưu từ Sổ tay AI", "Sau khi POST thành công nên RedirectToAction để tải lại trang không gửi form lần nữa."),
             ("Câu hay sai", "Nhầm Scoped với Transient: Scoped là mỗi yêu cầu một bản.")]),
        ("Cơ sở dữ liệu — tự học", "Tóm tắt chương 2: Truy vấn",
            "SELECT chọn cột, FROM chọn bảng, WHERE lọc dòng, GROUP BY gom nhóm, HAVING lọc nhóm, ORDER BY sắp xếp. Thứ tự thực hiện thật: FROM → WHERE → GROUP BY → HAVING → SELECT → ORDER BY. "
            + "LEFT JOIN giữ dòng bảng trái. COUNT(*) đếm cả dòng có NULL, COUNT(cột) thì không.",
            [("Mẹo", "Lọc trước khi gom thì dùng WHERE, lọc kết quả gom thì dùng HAVING."),
             ("Lưu từ Sổ tay AI", "Transaction: COMMIT khi tất cả thành công, ROLLBACK khi có một lệnh lỗi.")]),
        ("Từ vựng CNTT mỗi ngày", "Danh sách từ tuần 2",
            "deploy — triển khai; latency — độ trễ; bandwidth — băng thông; repository — kho mã nguồn; scalability — khả năng mở rộng; requirement — yêu cầu.",
            [("Cách nhớ", "latency = \"late\" → chậm trễ"),
             ("Câu ví dụ", "We deploy the new version every Friday.")])
    ];

    // ---------- Xóa dữ liệu mẫu ----------
    // Xóa như người dùng tự xóa: lớp và bộ học vào trạng thái đã xóa, tài khoản mẫu bị xóa (ẩn danh + khóa) bằng đúng luồng quản trị.
    public async Task<int> RemoveAsync(string lecturerId, AccountDeletionService deletion, ClassroomService classrooms, AssignmentService assignments, StudyLibraryService library)
    {
        // Lớp mẫu của người bấm và của giảng viên mẫu (gv02); xóa lớp phải do chính giảng viên chính của lớp đó.
        var mains = await db.ClassMembers.Where(m => m.Role == ClassRole.MainLecturer && m.Status == ClassMemberStatus.Active
                && m.Classroom.DeletedAt == null && m.Classroom.Name.EndsWith(ClassSuffix)
                && (m.UserId == lecturerId || (m.User.Email != null && m.User.Email.EndsWith("@" + Domain))))
            .Select(m => new { m.ClassroomId, m.UserId }).ToListAsync();
        var classIds = mains.Select(m => m.ClassroomId).ToList();
        // Đề nghị chuyển lớp mẫu còn chờ thì hủy trước.
        foreach (var t in await db.ClassTransfers.Where(t => classIds.Contains(t.ClassroomId) && t.Status == ClassTransferStatus.Pending).ToListAsync()) t.Status = ClassTransferStatus.Cancelled;
        foreach (var i in await db.Invitations.Where(i => classIds.Contains(i.ClassroomId) && i.Status == InvitationStatus.Pending).ToListAsync()) i.Status = InvitationStatus.Revoked;
        await db.SaveChangesAsync();
        // Lớp còn bài mở thì không xóa được (luật bảo vệ dữ liệu thật): đóng ngay các bài mẫu trước, lượt đang làm được nộp như khi GV bấm Đóng ngay.
        var open = await db.Assignments.Where(a => a.ClassroomId != null && classIds.Contains(a.ClassroomId.Value) && a.Status != AssignmentStatus.Closed).Select(a => a.Id).ToListAsync();
        foreach (var id in open)
        {
            var owner = mains.Single(m => m.ClassroomId == db.Assignments.Where(a => a.Id == id).Select(a => a.ClassroomId).Single()).UserId;
            await assignments.CloseNowAsync(id, owner);
        }
        foreach (var m in mains) await classrooms.DeleteAsync(m.ClassroomId, m.UserId);
        var demoUsers = await db.Users.Where(u => u.Email != null && u.Email.EndsWith("@" + Domain)).Select(u => u.Id).ToListAsync();
        foreach (var id in demoUsers) await deletion.DeleteByAdminAsync(id, "Xóa dữ liệu mẫu lớp học", lecturerId);
        var sets = await db.StudySets.Where(s => s.OwnerId == lecturerId && s.DeletedAt == null && s.Title.StartsWith(SetPrefix)).Select(s => s.Id).ToListAsync();
        foreach (var id in sets) await library.DeleteStudySetAsync(id, lecturerId);
        var file = Path.Combine(env.ContentRootPath, "App_Data", AccountsFile);
        if (File.Exists(file)) File.Delete(file);
        return demoUsers.Count;
    }

    // ---------- Nội dung bộ học (tự viết) ----------
    private static readonly (string, string)[] WebCards =
    [
        ("MVC", "Mô hình chia ứng dụng thành Model (dữ liệu), View (giao diện) và Controller (xử lý yêu cầu)."),
        ("Controller", "Lớp nhận yêu cầu HTTP, gọi xử lý nghiệp vụ và trả về kết quả (thường là một View)."),
        ("Razor", "Cú pháp trộn C# vào HTML trong tệp .cshtml, bắt đầu bằng ký tự @."),
        ("Tag Helper", "Thuộc tính đặc biệt trong Razor như asp-action, asp-for giúp sinh HTML đúng đường dẫn và tên trường."),
        ("Model binding", "Cơ chế tự gán dữ liệu từ form, URL, query string vào tham số của action."),
        ("Dependency Injection", "Hệ thống tự tạo và truyền các đối tượng phụ thuộc (service) vào constructor."),
        ("Middleware", "Từng bước trong đường ống xử lý yêu cầu, ví dụ xác thực, định tuyến, tệp tĩnh."),
        ("Entity Framework Core", "Thư viện ORM ánh xạ lớp C# thành bảng trong cơ sở dữ liệu."),
        ("Migration", "Bản ghi thay đổi cấu trúc CSDL sinh từ model, áp lên database bằng lệnh update."),
        ("Anti-forgery token", "Mã ẩn trong form chống tấn công giả mạo yêu cầu từ trang khác (CSRF)."),
        ("ViewData", "Từ điển chuyển dữ liệu phụ từ controller sang view trong một lần yêu cầu."),
        ("Partial View", "Phần giao diện dùng lại được, nhúng vào nhiều trang bằng thẻ <partial>.")
    ];
    private static readonly (string, string[], int[])[] WebQuestions =
    [
        ("Thành phần nào trong MVC nhận yêu cầu từ trình duyệt?", ["Model", "View", "Controller", "Middleware"], [3]),
        ("Tệp giao diện Razor có đuôi gì?", [".html", ".cshtml", ".razor.cs", ".aspx"], [2]),
        ("Tag Helper nào tạo đường dẫn tới một action?", ["asp-for", "asp-action", "asp-validation-for", "asp-append-version"], [2]),
        ("Lệnh nào áp các migration lên database?", ["dotnet ef migrations add", "dotnet ef database update", "dotnet build", "dotnet run"], [2]),
        ("Những thứ nào là vòng đời đăng ký service trong DI?", ["Singleton", "Scoped", "Transient", "Static"], [1, 2, 3]),
        ("Anti-forgery token chống lại kiểu tấn công nào?", ["SQL Injection", "CSRF", "XSS", "DDoS"], [2]),
        ("Kết quả nào dùng để chuyển sang action khác?", ["View()", "RedirectToAction()", "Json()", "Content()"], [2]),
        ("Thuộc tính nào đánh dấu action chỉ nhận POST?", ["[HttpGet]", "[HttpPost]", "[Route]", "[Authorize]"], [2]),
        ("EF Core thuộc loại công cụ nào?", ["ORM", "Template engine", "Web server", "Trình biên dịch"], [1]),
        ("Những tệp nào thường nằm trong thư mục wwwroot?", ["CSS", "JavaScript", "Hình ảnh", "Controller"], [1, 2, 3])
    ];
    private static readonly (string, string)[] OopCards =
    [
        ("Lớp (class)", "Bản thiết kế mô tả dữ liệu và hành vi chung của các đối tượng cùng loại."),
        ("Đối tượng (object)", "Một thể hiện cụ thể được tạo từ lớp."),
        ("Đóng gói", "Giấu dữ liệu bên trong, chỉ cho truy cập qua phương thức/thuộc tính công khai."),
        ("Kế thừa", "Lớp con dùng lại và mở rộng dữ liệu, hành vi của lớp cha."),
        ("Đa hình", "Cùng một lời gọi nhưng mỗi lớp con xử lý theo cách riêng (override)."),
        ("Interface", "Bản cam kết các phương thức mà lớp phải cài đặt, không chứa dữ liệu.")
    ];
    private static readonly (string, string[], int[])[] OopQuestions =
    [
        ("Tính chất nào cho phép lớp con dùng lại mã của lớp cha?", ["Đóng gói", "Kế thừa", "Đa hình", "Trừu tượng"], [2]),
        ("Từ khóa C# nào ghi đè phương thức virtual của lớp cha?", ["new", "override", "base", "sealed"], [2]),
        ("Phạm vi truy cập nào chỉ dùng được trong chính lớp đó?", ["public", "private", "protected", "internal"], [2]),
        ("Một lớp C# có thể kế thừa bao nhiêu lớp cha?", ["Một", "Hai", "Không giới hạn", "Không kế thừa được"], [1]),
        ("Những gì là 4 tính chất của lập trình hướng đối tượng?", ["Đóng gói", "Kế thừa", "Đa hình", "Biên dịch"], [1, 2, 3])
    ];
    private static readonly (string, string)[] SqlCards =
    [
        ("Khóa chính", "Cột (hoặc nhóm cột) xác định duy nhất mỗi dòng, không được NULL."),
        ("Khóa ngoại", "Cột tham chiếu khóa chính của bảng khác để giữ toàn vẹn quan hệ."),
        ("SELECT", "Lệnh truy vấn dữ liệu từ một hoặc nhiều bảng."),
        ("WHERE", "Mệnh đề lọc các dòng thỏa điều kiện."),
        ("JOIN", "Ghép dòng của hai bảng theo điều kiện liên quan."),
        ("GROUP BY", "Gom các dòng có cùng giá trị để tính hàm gộp như COUNT, SUM."),
        ("HAVING", "Lọc nhóm sau khi GROUP BY (WHERE lọc trước khi gom)."),
        ("Chỉ mục (Index)", "Cấu trúc giúp tìm dòng nhanh hơn, đổi lại ghi chậm hơn một chút."),
        ("Chuẩn 3NF", "Không có phụ thuộc bắc cầu: cột không khóa chỉ phụ thuộc vào khóa chính."),
        ("Transaction", "Nhóm lệnh thực hiện trọn vẹn hoặc không gì cả (COMMIT / ROLLBACK).")
    ];
    private static readonly (string, string[], int[])[] SqlQuestions =
    [
        ("Mệnh đề nào lọc nhóm sau GROUP BY?", ["WHERE", "HAVING", "ORDER BY", "LIMIT"], [2]),
        ("Kiểu JOIN nào giữ mọi dòng của bảng bên trái?", ["INNER JOIN", "LEFT JOIN", "RIGHT JOIN", "CROSS JOIN"], [2]),
        ("Hàm nào đếm số dòng?", ["SUM()", "COUNT()", "AVG()", "MAX()"], [2]),
        ("Khóa chính có thể chứa NULL không?", ["Có", "Không", "Chỉ khi có chỉ mục", "Chỉ ở SQL Server"], [2]),
        ("Lệnh nào hủy các thay đổi trong transaction?", ["COMMIT", "ROLLBACK", "SAVE", "UNDO"], [2]),
        ("Những lệnh nào thuộc nhóm DML?", ["INSERT", "UPDATE", "DELETE", "CREATE TABLE"], [1, 2, 3]),
        ("Toán tử nào tìm chuỗi theo mẫu?", ["=", "LIKE", "IN", "BETWEEN"], [2]),
        ("Mục đích chính của chỉ mục là gì?", ["Tăng tốc truy vấn", "Mã hóa dữ liệu", "Sao lưu", "Phân quyền"], [1])
    ];
    private static readonly (string, string)[] EngCards =
    [
        ("algorithm", "thuật toán"), ("bandwidth", "băng thông"), ("bug", "lỗi phần mềm"), ("compile", "biên dịch"),
        ("database", "cơ sở dữ liệu"), ("debug", "gỡ lỗi"), ("deploy", "triển khai"), ("framework", "khung phát triển"),
        ("interface", "giao diện (lập trình)"), ("latency", "độ trễ"), ("repository", "kho mã nguồn"), ("requirement", "yêu cầu"),
        ("scalability", "khả năng mở rộng"), ("security", "bảo mật"), ("server", "máy phục vụ"), ("variable", "biến")
    ];
    private static readonly (string, string[], int[])[] EngQuestions =
    [
        ("\"deploy\" nghĩa là gì?", ["gỡ lỗi", "triển khai", "biên dịch", "kiểm thử"], [2]),
        ("Từ nào nghĩa là \"độ trễ\"?", ["bandwidth", "latency", "throughput", "cache"], [2]),
        ("\"repository\" thường dùng để chỉ gì?", ["kho mã nguồn", "máy chủ", "biến", "giao diện"], [1]),
        ("Từ nào nghĩa là \"gỡ lỗi\"?", ["debug", "deploy", "compile", "commit"], [1]),
        ("\"scalability\" là gì?", ["khả năng mở rộng", "bảo mật", "băng thông", "thuật toán"], [1]),
        ("Những từ nào là danh từ chỉ người/vai trò? ", ["developer", "tester", "compile", "designer"], [1, 2, 4])
    ];
}
