using Microsoft.EntityFrameworkCore;
using MindSprintAI_ASP.NET_MVC.Data.Entities;

namespace MindSprintAI_ASP.NET_MVC.Services;

public record CardHistoryPage(int StudySetId, int? AssignmentId, int? ParticipantId, string Title,
    List<FlashcardReview> Reviews, int Page, bool HasNext);

public partial class FlashcardService
{
    public async Task<int> PersonalDueCountAsync(int studySetId, string userId)
    {
        var set = await library.GetOwnedStudySetAsync(studySetId, userId); // (D5b) trước khi trả bộ đếm cá nhân.
        return set.FlashcardMode == FlashcardMode.SpacedRepetition
            ? await new SrsDueService(db, clock).CountAsync(studySetId, userId) : 0;
    }

    public static FlashcardRating? TraditionalRating(FlashcardProgress? p) => p == null || p.HasTraditionalRating == false ? null : p.LastRating;

    private static FlashcardMode ResolveMode(Assignment? a, Participant? p, StudySet? set, FlashcardMode? requested)
    {
        var active = a == null ? set!.FlashcardMode : a.FlashcardMode == FlashcardMode.LearnerChoice ? p!.FlashcardMode : a.FlashcardMode;
        if (requested != null && (requested == FlashcardMode.LearnerChoice || !Enum.IsDefined(requested.Value) || requested != active))
            throw new BusinessException("Chế độ học đã thay đổi hoặc không được bài này cho phép. Hãy tải lại trang.");
        return active;
    }

    public async Task ChangeModeAsync(int studySetId, int? assignmentId, FlashcardMode mode, string userId)
    {
        if (mode is not (FlashcardMode.Traditional or FlashcardMode.SpacedRepetition)) throw new BusinessException("Chế độ không hợp lệ.");
        await EnsureNotTakingTestAsync(studySetId, userId);
        await using var tx = await db.Database.BeginTransactionAsync();
        if (assignmentId != null)
        {
            var (a, p) = await LearnerAsync(assignmentId.Value, userId);
            if (a.StudySetId != studySetId) throw new ForbiddenException();
            if (a.FlashcardMode != FlashcardMode.LearnerChoice) throw new BusinessException("Giảng viên đã cố định chế độ học.");
            p.FlashcardMode = mode;
        }
        else
        {
            if (await db.Users.AnyAsync(u => u.Id == userId && u.IsGuest)) throw new ForbiddenException();
            var set = await library.GetOwnedStudySetAsync(studySetId, userId);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT [Id] FROM [StudySets] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {studySetId}");
            await db.Entry(set).ReloadAsync();
            set.FlashcardMode = mode;
        }
        db.Audit?.Add(AuditAction.StudySetEdited, "StudySet", studySetId, userId); // (NK3)
        await db.SaveChangesAsync(); await tx.CommitAsync();
    }

    public async Task<CardHistoryPage> HistoryAsync(int studySetId, int? assignmentId, string userId,
        int? participantId = null, int page = 1)
    {
        await EnsureNotTakingTestAsync(studySetId, userId);
        var query = db.FlashcardReviews.AsNoTracking().Include(r => r.Flashcard).Where(r => r.StudySetId == studySetId);
        string title;
        if (participantId != null)
        {
            // (D3b) cùng quyền GV đang hoạt động trong lớp với các trang bảng điểm.
            var p = await db.Participants.Include(p => p.Assignment).Include(p => p.User).SingleOrDefaultAsync(p => p.Id == participantId && p.AssignmentId == assignmentId);
            if (p == null || p.Assignment.StudySetId != studySetId || p.Assignment.ClassroomId == null || !await db.ClassMembers.AnyAsync(m =>
                m.ClassroomId == p.Assignment.ClassroomId && m.UserId == userId && m.Status == ClassMemberStatus.Active
                && m.Classroom.DeletedAt == null
                && (m.Role == ClassRole.MainLecturer || m.Role == ClassRole.AssistantLecturer))) throw new ForbiddenException();
            query = query.Where(r => r.ParticipantId == participantId);
            title = "học viên " + (p.User?.DisplayName ?? "");
        }
        else if (assignmentId != null)
        {
            // Xem lịch sử đã học sau hạn vẫn được; không tạo Participant cho người chưa từng tham gia.
            var p = await db.Participants.Include(p => p.Assignment).SingleOrDefaultAsync(p => p.AssignmentId == assignmentId && p.UserId == userId)
                ?? throw new ForbiddenException();
            if (p.Assignment.StudySetId != studySetId) throw new ForbiddenException();
            query = query.Where(r => r.ParticipantId == p.Id && r.UserId == userId);
            title = "của bạn";
        }
        else
        {
            if (await db.Users.AnyAsync(u => u.Id == userId && u.IsGuest)) throw new ForbiddenException();
            title = (await library.GetOwnedStudySetAsync(studySetId, userId)).Title;
            query = query.Where(r => r.ParticipantId == null && r.UserId == userId);
        }
        page = Math.Clamp(page, 1, 100000);
        var rows = await query.OrderByDescending(r => r.ReviewedAt).ThenByDescending(r => r.Id).Skip((page - 1) * 50).Take(51).ToListAsync();
        return new(studySetId, assignmentId, participantId, title, rows.Take(50).ToList(), page, rows.Count > 50);
    }
}
