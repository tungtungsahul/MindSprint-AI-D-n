using Microsoft.EntityFrameworkCore;
using MindSprintAI_ASP.NET_MVC.Data;
using MindSprintAI_ASP.NET_MVC.Data.Entities;

namespace MindSprintAI_ASP.NET_MVC.Services;

// (33A, 41A, 61, C2) mọi bên lấy khóa theo thứ tự bài → thành viên → người làm → lượt,
// rồi đọc lại dữ liệu; Include không tự làm mới entity đã được DbContext theo dõi.
// Thử tải 13/11/2026 (650 khách): khi mọi thao tác học viên đều khóa RIÊNG dòng bài, cả lớp xếp hàng qua một dòng
// (lưu câu trả lời 35 giây, cạn kết nối DB). Người dùng duyệt cách sửa 13/11/2026:
//  - Học viên (LearnerAsync): khóa CHUNG dòng bài → nhiều học viên chạy song song; rồi khóa riêng "ngăn kéo" của chính học viên đó
//    (khóa ứng dụng theo bài + người) để hai tab/hai máy của cùng một người vẫn xếp hàng với nhau. Học viên KHÔNG BAO GIỜ ghi dòng bài.
//  - Giáo viên/Admin (AssignmentAsync): khóa ĐỘC QUYỀN (XLOCK) → chờ mọi học viên đang thao tác xong, học viên mới chờ GV xong.
//    (UPDLOCK cũ không chặn khóa chung nên không dùng được nữa.)
//  - Cấm lấy khóa độc quyền sau khóa chung trên cùng bài trong cùng giao dịch (nâng khóa → học viên chờ nhau mãi).
internal static class AttemptLock
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, HashSet<int>> SharedHeld = new(), ExclusiveHeld = new();

    internal static async Task<Assignment> AssignmentAsync(AppDbContext db, int id)
    {
        if (db.Database.CurrentTransaction is { } tx && SharedHeld.TryGetValue(tx, out var held) && held.Contains(id)
            && !(ExclusiveHeld.TryGetValue(tx, out var mine) && mine.Contains(id)))
            throw new InvalidOperationException($"Không được khóa độc quyền bài {id} sau khi đã khóa chung trong cùng giao dịch.");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT [Id] FROM [Assignments] WITH (XLOCK, ROWLOCK, HOLDLOCK) WHERE [Id] = {id}");
        if (db.Database.CurrentTransaction is { } own) ExclusiveHeld.GetOrCreateValue(own).Add(id);
        return await ReadAsync(db, id);
    }

    // Thao tác của học viên (kể cả công việc nền chạy cho một lượt cụ thể). learnerId = UserId của người làm bài.
    internal static async Task<Assignment> LearnerAsync(AppDbContext db, int id, string learnerId)
    {
        await SharedLockAsync(db, id);
        await LearnerKeyAsync(db, id, learnerId);
        return await ReadAsync(db, id);
    }

    // Khóa chung dòng bài, chưa biết người làm (khách vào bài lần đầu): gọi LearnerKeyAsync ngay khi biết UserId.
    internal static async Task<Assignment> SharedAsync(AppDbContext db, int id)
    {
        await SharedLockAsync(db, id);
        return await ReadAsync(db, id);
    }

    private static async Task SharedLockAsync(AppDbContext db, int id)
    {
        var tx = db.Database.CurrentTransaction ?? throw new InvalidOperationException("Khóa học viên cần chạy trong giao dịch.");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT [Id] FROM [Assignments] WITH (ROWLOCK, HOLDLOCK) WHERE [Id] = {id}");
        SharedHeld.GetOrCreateValue(tx).Add(id);
    }

    // Khóa như học viên nhưng không đọc dòng bài (lối nhanh tín hiệu giám sát).
    internal static async Task LearnerLockOnlyAsync(AppDbContext db, int id, string learnerId)
    {
        await SharedLockAsync(db, id);
        await LearnerKeyAsync(db, id, learnerId);
    }

    internal static Task LearnerKeyAsync(AppDbContext db, int id, string learnerId) => AppLockAsync(db, $"learner:{id}:{learnerId}");

    // Tiến độ thẻ cá nhân của một người với một bộ học (tự ôn, học bộ của mình). Tài khoản 2 rà 13/11/2026: trước đây khóa dòng StudySets
    // → cả lớp tự ôn cùng một bộ xếp hàng qua một dòng, và đi ngược thứ tự "bộ học → bài" của Admin ẩn nội dung / chuyển lớp.
    internal static Task PersonalCardsAsync(AppDbContext db, int studySetId, string userId) => AppLockAsync(db, $"cards:{studySetId}:{userId}");
    // Bảng Kanban cá nhân: chỉ người đó sửa (nhiều tab) → khóa theo người, không đụng khóa bài/lớp.
    internal static Task KanbanAsync(AppDbContext db, string userId) => AppLockAsync(db, $"kanban:{userId}");
    internal static Task StudyPlanAsync(AppDbContext db, string userId) => AppLockAsync(db, $"study-plan:{userId}");
    internal static Task NotebookAsync(AppDbContext db, int notebookId) => AppLockAsync(db, $"notebook:{notebookId}");

    // Đặt tên khách không trùng trong bài. Thử tải P2 14/11/2026: một hàng chung cho cả bài → 213/650 khách chờ quá 25 giây.
    // Người dùng duyệt: chỉ những tên có thể trùng mới xếp hàng với nhau. Khóa theo "tên gốc": bỏ hậu tố " (n)" cuối,
    // chữ thường, chuẩn hóa Unicode — "Minh", "minh", "Minh (2)" chung một hàng; "Minh" và "Lan" vào song song. Gọi sau LearnerAsync.
    internal static Task GuestNameAsync(AppDbContext db, int id, string name) => AppLockAsync(db, $"guest-name:{id}:{GuestNameKey(name)}");

    internal static string GuestNameKey(string name) =>
        System.Text.RegularExpressions.Regex.Replace(name.Trim().Normalize(System.Text.NormalizationForm.FormKC), @" \(\d+\)$", "")
            .ToLowerInvariant();

    private static Task AppLockAsync(AppDbContext db, string resource) => db.Database.ExecuteSqlInterpolatedAsync(
        $"DECLARE @r int; EXEC @r = sp_getapplock @Resource = {resource}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 25000; IF @r < 0 THROW 50000, N'Không lấy được khóa người làm bài.', 1;");

    private static async Task<Assignment> ReadAsync(AppDbContext db, int id)
    {
        // Thử tải 14/11/2026: chưa nạp trong yêu cầu này thì 1 câu (Regrades tự kèm theo AutoInclude) thay cho Find + Reload + Regrades.
        if (!db.ChangeTracker.Entries<Assignment>().Any(e => e.Entity.Id == id))
            return await db.Assignments.SingleOrDefaultAsync(a => a.Id == id) ?? throw new BusinessException("Không tìm thấy bài.");
        var a = await db.Assignments.FindAsync(id) ?? throw new BusinessException("Không tìm thấy bài.");
        await db.Entry(a).ReloadAsync();
        // (19) Reload chỉ làm mới cột; lần chấm lại mới từ DbContext khác cũng phải đi theo khóa bài.
        await db.Entry(a).Collection(a => a.Regrades).Query().LoadAsync();
        return a;
    }

    internal static async Task<ClassMember?> MemberAsync(AppDbContext db, int? classroomId, string userId)
    {
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT [Id] FROM [ClassMembers] WITH (UPDLOCK, HOLDLOCK) WHERE [ClassroomId] = {classroomId} AND [UserId] = {userId}");
        var m = await db.ClassMembers.SingleOrDefaultAsync(m => m.ClassroomId == classroomId && m.UserId == userId);
        if (m != null) await db.Entry(m).ReloadAsync();
        return m;
    }

    // Đường học viên: không khóa "khoảng trống" khi người này chưa là thành viên — khóa khoảng làm mọi khách/người lạ
    // của cùng lớp xếp hàng nhau. Thành viên thật vẫn khóa đúng dòng của mình; GV xóa thành viên / học viên rời lớp thì khóa độc quyền các bài trước; duyệt hay thêm thành viên chỉ mở thêm quyền nên không cần.
    internal static async Task<ClassMember?> MemberForLearnerAsync(AppDbContext db, int? classroomId, string userId)
    {
        var id = await db.ClassMembers.Where(m => m.ClassroomId == classroomId && m.UserId == userId).Select(m => (int?)m.Id).SingleOrDefaultAsync();
        if (id == null) return null;
        // Chưa nạp: khóa và đọc trong 1 câu (dữ liệu đọc dưới khóa nên đã mới nhất, không cần Reload).
        if (!db.ChangeTracker.Entries<ClassMember>().Any(e => e.Entity.Id == id))
            return await db.ClassMembers.FromSqlInterpolated($"SELECT * FROM [ClassMembers] WITH (UPDLOCK, ROWLOCK, HOLDLOCK) WHERE [Id] = {id}").SingleOrDefaultAsync();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT [Id] FROM [ClassMembers] WITH (UPDLOCK, ROWLOCK, HOLDLOCK) WHERE [Id] = {id}");
        var m = await db.ClassMembers.FindAsync(id.Value);
        if (m != null) await db.Entry(m).ReloadAsync();
        return m;
    }

    internal static async Task<Participant> ParticipantAsync(AppDbContext db, int id)
    {
        if (!db.ChangeTracker.Entries<Participant>().Any(e => e.Entity.Id == id) && !db.ChangeTracker.Entries<Attempt>().Any(e => e.Entity.ParticipantId == id))
        {
            var fresh = await db.Participants.FromSqlInterpolated($"SELECT * FROM [Participants] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {id}").SingleOrDefaultAsync()
                ?? throw new BusinessException("Không tìm thấy người làm bài.");
            await db.Entry(fresh).Collection(p => p.Attempts).Query().LoadAsync();
            return fresh;
        }
        // Đã có dữ liệu đang theo dõi: khóa + đọc 1 câu rồi làm mới (thay cho khóa → Find → ReloadAsync; GV gọi trong vòng lặp cả lớp).
        var lockedRow = await db.Participants.FromSqlInterpolated($"SELECT * FROM [Participants] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {id}")
            .AsNoTracking().IgnoreAutoIncludes().IgnoreQueryFilters().SingleOrDefaultAsync()
            ?? throw new BusinessException("Không tìm thấy người làm bài.");
        var p = db.ChangeTracker.Entries<Participant>().FirstOrDefault(e => e.Entity.Id == id)?.Entity;
        if (p == null) p = db.Participants.Attach(lockedRow).Entity;
        else Refresh(db.Entry(p), lockedRow);
        var attempts = db.ChangeTracker.Entries<Attempt>().Where(e => e.Entity.ParticipantId == id).ToList();
        if (attempts.Count > 0)
        {
            var ids = attempts.Select(e => e.Entity.Id).ToList();
            Apply(attempts, await Fresh(db.Attempts).Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id), x => x.Id);
        }
        await db.Entry(p).Collection(p => p.Attempts).Query().LoadAsync();
        return p;
    }

    // Đo CPU khi thử tải 14/11/2026: ReloadAsync từng dòng (mỗi lần EF dựng + dịch lại một câu truy vấn) chiếm ~26% CPU máy chủ.
    // Nay: khóa + đọc lượt trong 1 câu, các dòng đang theo dõi được làm mới theo nhóm (1 câu/bảng). Kết quả như ReloadAsync:
    // giá trị hiện tại = giá trị gốc = DB, trạng thái Unchanged; dòng đã bị xóa trong DB thì thôi theo dõi.
    internal static async Task<Attempt> LoadAsync(AppDbContext db, long id)
    {
        var lockedRow = await db.Attempts.FromSqlInterpolated($"SELECT * FROM [Attempts] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {id}")
            .AsNoTracking().IgnoreAutoIncludes().IgnoreQueryFilters().SingleOrDefaultAsync();
        foreach (var entry in db.ChangeTracker.Entries<Attempt>().Where(e => e.Entity.Id == id).ToList()) Refresh(entry, lockedRow);
        var questions = db.ChangeTracker.Entries<AttemptQuestion>().Where(e => e.Entity.AttemptId == id).ToList();
        var questionIds = questions.Select(e => e.Entity.Id).ToList();
        var questionSet = questionIds.ToHashSet(); // lọc trong bộ nhớ; List chỉ để EF dịch thành câu truy vấn
        if (questions.Count > 0)
            Apply(questions, await Fresh(db.AttemptQuestions).Where(x => questionIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id), x => x.Id);
        var submissions = db.ChangeTracker.Entries<AnswerSubmission>().Where(e => questionSet.Contains(e.Entity.AttemptQuestionId)).ToList();
        if (submissions.Count > 0)
        {
            var ids = submissions.Select(e => e.Entity.Id).ToList();
            Apply(submissions, await Fresh(db.AnswerSubmissions).Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id), x => x.Id);
        }
        var changes = db.ChangeTracker.Entries<DeviceChangeRequest>().Where(e => e.Entity.AttemptId == id).ToList();
        if (changes.Count > 0)
        {
            var ids = changes.Select(e => e.Entity.Id).ToList();
            Apply(changes, await Fresh(db.DeviceChangeRequests).Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id), x => x.Id);
        }
        var leaves = db.ChangeTracker.Entries<ScreenLeave>().Where(e => e.Entity.AttemptId == id).ToList();
        if (leaves.Count > 0)
        {
            var ids = leaves.Select(e => e.Entity.Id).ToList();
            Apply(leaves, await Fresh(db.ScreenLeaves).Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id), x => x.Id);
        }
        return await db.Attempts.AsSplitQuery().Include(a => a.Participant).ThenInclude(p => p.Assignment)
            .Include(a => a.DeviceChanges).Include(a => a.ScreenLeaves)
            .Include(a => a.RetakeRequest)
            .Include(a => a.Questions).ThenInclude(q => q.Submissions).SingleOrDefaultAsync(a => a.Id == id)
            ?? throw new BusinessException("Không tìm thấy lượt làm.");
    }

    // Làm mới theo nhóm: 1 câu đọc theo đúng mã các dòng đang theo dõi (như ReloadAsync, đã dưới khóa của người gọi).
    private static IQueryable<T> Fresh<T>(IQueryable<T> set) where T : class => set.AsNoTracking().IgnoreAutoIncludes().IgnoreQueryFilters();
    private static void Apply<T, TKey>(List<Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<T>> entries, Dictionary<TKey, T> rows, Func<T, TKey> key)
        where T : class where TKey : notnull
    {
        foreach (var entry in entries) Refresh(entry, rows.GetValueOrDefault(key(entry.Entity)));
    }

    // Giống ReloadAsync: lấy đúng giá trị trong DB, bỏ thay đổi chưa lưu; dòng không còn trong DB thì thôi theo dõi.
    private static void Refresh(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry, object? row)
    {
        // Như ReloadAsync: dòng mới thêm (Added) chưa có trong DB thì giữ nguyên, không bỏ mất (tài khoản 2 rà 14/11/2026).
        if (row == null) { if (entry.State != EntityState.Added) entry.State = EntityState.Detached; return; }
        entry.OriginalValues.SetValues(row);
        entry.CurrentValues.SetValues(row);
        entry.State = EntityState.Unchanged;
    }
}
