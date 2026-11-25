using Microsoft.EntityFrameworkCore;
using MindSprintAI_ASP.NET_MVC.Data;
using MindSprintAI_ASP.NET_MVC.Data.Entities;

namespace MindSprintAI_ASP.NET_MVC.Services;

public record PaperSettings(string? Title, DateTime? OpensAt, int DurationMinutes,
    bool DeviceLock = false, bool MonitorScreen = false, int? AutoSubmitLeaves = null,
    AudiencePolicy AudiencePolicy = AudiencePolicy.IncludeLateJoiners);

public record PaperRow(string? UserId, string Name, long? AttemptId, AttemptStatus? Status, DateTime? EndedAt,
    int TextLength, int FileCount, decimal? Score, string? Comment, bool IsGuest);

// Bài làm theo đề tài liệu (người dùng chốt 16/11/2026): GV tải đề PDF/Word, học viên xem đề ngay trên trang, viết bài + đính kèm
// tệp trong giờ làm; GV chấm tay thang 10 (2 chữ số lẻ) kèm nhận xét rồi công bố cả lớp. Là một bài Kiểm tra (Mode = Test,
// Form = Document): dùng lại toàn bộ khung giờ, GV bấm Bắt đầu, khóa thiết bị, giám sát rời màn hình, tự nộp, cộng giờ, làm lại.
public sealed class PaperTestService(AppDbContext db, AssignmentService assignments, AttemptService attempts,
    ClassroomService classrooms, JoinCodeService codes, PaperFileStore files, TimeProvider clock)
{
    public const int MaxText = 100_000;
    public const int MaxComment = 2_000;
    public const int MaxFiles = 5;
    public const decimal MaxScore = 10m;
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<Assignment> CreateAsync(int classroomId, PaperSettings s, IFormFile? paper, string userId)
    {
        await classrooms.EnsureLecturerAsync(classroomId, userId);
        var title = s.Title?.Trim();
        if (string.IsNullOrEmpty(title) || title.Length > 200) throw new BusinessException("Tên bài cần từ 1 đến 200 ký tự.");
        if (s.DurationMinutes is < 1 or > 180) throw new BusinessException("Thời lượng làm bài phải từ 1 đến 180 phút.");
        if (!Enum.IsDefined(s.AudiencePolicy)) throw new BusinessException("Đối tượng làm bài không hợp lệ.");
        var bytes = await PaperFileStore.ReadUploadAsync(paper);
        await using var tx = await db.Database.BeginTransactionAsync();
        await ClassroomLock.RoomAsync(db, classroomId);
        var a = new Assignment
        {
            ClassroomId = classroomId, CreatedById = userId, Mode = AssignmentMode.Test, Form = AssignmentForm.Document,
            RequiredParts = RequiredParts.Quiz, DurationMinutes = s.DurationMinutes, OpensAt = s.OpensAt,
            ScoreSelection = ScoreSelection.Latest, ScoreScale = ScoreScale.Ten, Rounding = RoundingMode.TwoDecimals,
            AudiencePolicy = s.AudiencePolicy, AllowAiAssistant = false, PaperTitle = title,
            PaperFileName = Path.GetFileName(paper!.FileName) is { Length: > 0 and <= 255 } n ? n : "de-bai", CreatedAt = Now
        };
        AssignmentService.ApplyDeviceSettings(a, s.DeviceLock, s.MonitorScreen, s.AutoSubmitLeaves);
        db.Assignments.Add(a);
        await db.SaveChangesAsync();
        string? stored = null;
        try
        {
            stored = await files.SavePaperAsync(a.Id, bytes);   // Word đổi sang PDF ở đây
            a.PaperStoredName = stored;
            a.PaperUploadedAt = Now;
            await codes.IssueAsync(JoinCodeTarget.Assignment, a.Id);
            db.Audit?.Add(AuditAction.AssignmentCreated, "Assignment", a.Id, userId, resolveTarget: () => a.Id.ToString());
            await db.SaveChangesAsync();
            await tx.CommitAsync();
        }
        catch
        {
            files.DeletePaper(a.Id, stored);   // không để lại tệp mồ côi khi tạo bài thất bại
            throw;
        }
        return a;
    }

    // Đổi đề: chỉ trước khi GV bấm Bắt đầu (đã có người xem đề thì không đổi được).
    public async Task ReplacePaperAsync(int assignmentId, IFormFile? paper, string userId)
    {
        var bytes = await PaperFileStore.ReadUploadAsync(paper);
        await using var tx = await db.Database.BeginTransactionAsync();
        await AttemptLock.AssignmentAsync(db, assignmentId);
        var a = await LecturerPaperAsync(assignmentId, userId);
        if (a.TestStartedAt != null) throw new BusinessException("Bài đã bắt đầu nên không đổi đề được.");
        var old = a.PaperStoredName;
        var stored = await files.SavePaperAsync(a.Id, bytes);
        a.PaperStoredName = stored;
        a.PaperFileName = Path.GetFileName(paper!.FileName) is { Length: > 0 and <= 255 } n ? n : "de-bai";
        a.PaperUploadedAt = Now;
        try { await db.SaveChangesAsync(); await tx.CommitAsync(); }
        catch { files.DeletePaper(a.Id, stored); throw; }
        files.DeletePaper(a.Id, old);
    }

    // Đề: GV của lớp xem lúc nào cũng được; học viên chỉ xem khi có lượt của bài này (đang làm hoặc đã nộp).
    public async Task<(Stream File, string Name)> OpenPaperAsync(int assignmentId, string userId)
    {
        var a = await db.Assignments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == assignmentId && x.Form == AssignmentForm.Document)
            ?? throw new BusinessException("Không tìm thấy đề.");
        var lecturer = a.ClassroomId != null && await classrooms.IsLecturerAsync(a.ClassroomId.Value, userId);
        if (!lecturer && !await db.Attempts.AnyAsync(x => x.Participant.AssignmentId == assignmentId && x.Participant.UserId == userId
                && x.Status != AttemptStatus.Cancelled))
            throw new ForbiddenException();
        var stream = files.OpenPaper(a.Id, a.PaperStoredName) ?? throw new BusinessException("Không tìm thấy tệp đề.");
        return (stream, Path.ChangeExtension(a.PaperFileName ?? "de-bai", ".pdf"));
    }

    public async Task<PaperSubmission> GetSubmissionAsync(long attemptId) =>
        await db.PaperSubmissions.Include(s => s.Files).SingleOrDefaultAsync(s => s.AttemptId == attemptId)
        ?? new PaperSubmission { AttemptId = attemptId };

    // Lưu bài viết (tự lưu nháp mỗi vài giây và lúc nộp). Đi qua đúng các kiểm tra của lượt làm: chủ lượt, thiết bị, còn giờ.
    public async Task<DateTime> SaveTextAsync(long attemptId, string? text, string userId)
    {
        // Trình duyệt đếm xuống dòng là 1 ký tự (maxlength) nhưng gửi lên thành \r\n: quy về \n trước khi đếm, không thì
        // bài dài nhiều dòng qua được ô viết mà bị từ chối mãi (lỗi trên server 16/11).
        text = (text ?? "").ReplaceLineEndings("\n");
        if (text.Length > MaxText) throw new BusinessException($"Bài viết tối đa {MaxText:N0} ký tự.");
        await using var tx = await db.Database.BeginTransactionAsync();
        var (_, submission) = await OpenForWriteAsync(attemptId, userId);
        submission.Text = text;
        submission.SavedAt = Now;
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        await attempts.NotifyLiveChangesAsync();
        return submission.SavedAt.Value;
    }

    public async Task<PaperFile> AddFileAsync(long attemptId, IFormFile? file, string userId)
    {
        var bytes = await PaperFileStore.ReadUploadAsync(file);
        await using var tx = await db.Database.BeginTransactionAsync();
        var (attempt, submission) = await OpenForWriteAsync(attemptId, userId);
        if (submission.Files.Count >= MaxFiles) throw new BusinessException($"Mỗi bài tối đa {MaxFiles} tệp. Hãy xóa bớt tệp cũ.");
        var (stored, contentType) = await files.SaveAnswerAsync(attempt.Participant.AssignmentId, attemptId, bytes);
        var name = CleanName(file!.FileName, stored);
        var row = new PaperFile { AttemptId = attemptId, FileName = name, ContentType = contentType, SizeBytes = bytes.Length, StoredName = stored, UploadedAt = Now };
        submission.Files.Add(row);
        try { await db.SaveChangesAsync(); await tx.CommitAsync(); }
        catch { files.DeleteAnswer(attempt.Participant.AssignmentId, attemptId, stored); throw; }
        return row;
    }

    public async Task DeleteFileAsync(long attemptId, long fileId, string userId)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        var (attempt, submission) = await OpenForWriteAsync(attemptId, userId);
        var row = submission.Files.SingleOrDefault(f => f.Id == fileId) ?? throw new BusinessException("Không tìm thấy tệp.");
        db.PaperFiles.Remove(row);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        files.DeleteAnswer(attempt.Participant.AssignmentId, attemptId, row.StoredName);
    }

    // Tệp bài nộp: chủ bài hoặc GV của lớp.
    public async Task<(Stream File, PaperFile Info)> OpenFileAsync(long fileId, string userId)
    {
        var row = await db.PaperFiles.AsNoTracking().Where(f => f.Id == fileId)
            .Select(f => new { File = f, f.Submission.Attempt.Participant.UserId, f.Submission.Attempt.Participant.AssignmentId,
                ClassroomId = f.Submission.Attempt.Participant.Assignment.ClassroomId })
            .SingleOrDefaultAsync() ?? throw new BusinessException("Không tìm thấy tệp.");
        if (row.UserId != userId && !(row.ClassroomId != null && await classrooms.IsLecturerAsync(row.ClassroomId.Value, userId)))
            throw new ForbiddenException();
        var stream = files.OpenAnswer(row.AssignmentId, row.File.AttemptId, row.File.StoredName) ?? throw new BusinessException("Không tìm thấy tệp.");
        return (stream, row.File);
    }

    // GV chấm: chỉ lượt đã nộp (không phải lượt hủy, lượt tự ôn). Điểm chép sang lượt làm cho bảng điểm/công bố.
    public async Task GradeAsync(long attemptId, decimal? score, string? comment, string userId)
    {
        if (score is null or < 0 or > MaxScore || decimal.Round(score.Value, 2) != score.Value)
            throw new BusinessException("Điểm phải từ 0 đến 10, tối đa 2 chữ số lẻ.");
        comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
        if (comment?.Length > MaxComment) throw new BusinessException($"Nhận xét tối đa {MaxComment:N0} ký tự.");
        await using var tx = await db.Database.BeginTransactionAsync();
        var assignmentId = await db.Attempts.Where(x => x.Id == attemptId).Select(x => (int?)x.Participant.AssignmentId).SingleOrDefaultAsync()
            ?? throw new BusinessException("Không tìm thấy bài làm.");
        await AttemptLock.AssignmentAsync(db, assignmentId);
        await LecturerPaperAsync(assignmentId, userId);
        var attempt = await AttemptLock.LoadAsync(db, attemptId);
        if (attempt.Status != AttemptStatus.Ended || attempt.Kind == AttemptKind.SelfReview)
            throw new BusinessException("Chỉ chấm được bài đã nộp.");
        var submission = await db.PaperSubmissions.SingleOrDefaultAsync(s => s.AttemptId == attemptId);
        if (submission == null) db.PaperSubmissions.Add(submission = new PaperSubmission { AttemptId = attemptId });
        submission.Score = score;
        submission.Comment = comment;
        submission.GradedById = userId;
        submission.GradedAt = Now;
        attempt.FirstScore = score.Value;
        attempt.MaxScore = MaxScore;
        db.Audit?.Add(AuditAction.PaperGraded, "Attempt", attemptId, userId);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    public async Task<(Assignment Assignment, List<PaperRow> Rows)> ListAsync(int assignmentId, string userId)
    {
        var a = await LecturerPaperAsync(assignmentId, userId);
        var participants = await db.Participants.AsNoTracking().Include(p => p.User).Include(p => p.Attempts)
            .Where(p => p.AssignmentId == assignmentId).ToListAsync();
        var ids = participants.SelectMany(p => p.Attempts).Select(x => x.Id).ToList();
        var subs = await db.PaperSubmissions.AsNoTracking().Include(s => s.Files).Where(s => ids.Contains(s.AttemptId)).ToDictionaryAsync(s => s.AttemptId);
        var rows = participants.Select(p =>
        {
            var latest = p.Attempts.Where(x => x.Status != AttemptStatus.Cancelled && x.Kind != AttemptKind.SelfReview)
                .OrderByDescending(x => x.AttemptNumber).FirstOrDefault();
            subs.TryGetValue(latest?.Id ?? 0, out var s);
            return new PaperRow(p.UserId, p.User?.DisplayName ?? p.GuestDisplayName ?? "Khách", latest?.Id, latest?.Status, latest?.EndedAt,
                s?.Text.Length ?? 0, s?.Files.Count ?? 0, s?.Score, s?.Comment, p.User?.IsGuest == true || p.GuestId != null);
        }).OrderBy(r => r.Name).ToList();
        return (a, rows);
    }

    public async Task<(Attempt Attempt, PaperSubmission Submission, Assignment Assignment)> GetForGradingAsync(long attemptId, string userId)
    {
        var assignmentId = await AssignmentIdOfAsync(attemptId);
        var a = await LecturerPaperAsync(assignmentId, userId);
        var attempt = await db.Attempts.AsNoTracking().Include(x => x.Participant).ThenInclude(p => p.User)
            .Include(x => x.ScreenLeaves).SingleAsync(x => x.Id == attemptId);
        return (attempt, await GetSubmissionAsync(attemptId), a);
    }

    public async Task<int> AssignmentIdOfAsync(long attemptId) =>
        await db.Attempts.Where(x => x.Id == attemptId).Select(x => (int?)x.Participant.AssignmentId).SingleOrDefaultAsync()
        ?? throw new BusinessException("Không tìm thấy bài làm.");

    public Task<int> CountUngradedAsync(int assignmentId) =>
        db.Attempts.CountAsync(x => x.Participant.AssignmentId == assignmentId && x.Status == AttemptStatus.Ended
            && x.Kind != AttemptKind.SelfReview && !x.ExcludeFromGradebook && x.MaxScore == 0);

    private async Task<(Attempt Attempt, PaperSubmission Submission)> OpenForWriteAsync(long attemptId, string userId)
    {
        var attempt = await attempts.LoadForLearnerAsync(attemptId, userId, writing: true);
        if (attempt.Participant.Assignment.Form != AssignmentForm.Document) throw new BusinessException("Lượt này không phải bài làm theo đề.");
        if (!AttemptRules.IsOpen(attempt)) throw new BusinessException("Bài đã nộp hoặc đã hết giờ nên không sửa được nữa.");
        var submission = await db.PaperSubmissions.Include(s => s.Files).SingleOrDefaultAsync(s => s.AttemptId == attemptId);
        if (submission == null) db.PaperSubmissions.Add(submission = new PaperSubmission { AttemptId = attemptId });
        return (attempt, submission);
    }

    private async Task<Assignment> LecturerPaperAsync(int assignmentId, string userId)
    {
        var a = await assignments.GetForLecturerAsync(assignmentId, userId);
        if (a.Form != AssignmentForm.Document) throw new BusinessException("Bài này không phải bài làm theo đề.");
        return a;
    }

    // Tên hiển thị: bỏ đường dẫn và ký tự điều khiển; đuôi theo loại thật của tệp.
    private static string CleanName(string? original, string stored)
    {
        var name = new string(Path.GetFileNameWithoutExtension(original ?? "").Where(c => !char.IsControl(c) && c is not ('"' or '\\' or '/')).ToArray()).Trim();
        if (name.Length == 0) name = "tep";
        if (name.Length > 200) name = name[..200];
        return name + Path.GetExtension(stored);
    }
}
