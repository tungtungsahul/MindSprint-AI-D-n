using Microsoft.EntityFrameworkCore;
using MindSprintAI_ASP.NET_MVC.Data;
using MindSprintAI_ASP.NET_MVC.Data.Entities;

namespace MindSprintAI_ASP.NET_MVC.Services;

public sealed class SrsDueCard
{
    public string UserId { get; set; } = "";
    public int StudySetId { get; set; }
    public int? ParticipantId { get; set; }
    public int CardId { get; set; }
}

public class SrsDueService(AppDbContext db, TimeProvider clock)
{
    public Task<int> CountAsync(int studySetId, string userId, int? participantId = null)
    {
        var today = VnTime.Date(clock.GetUtcNow().UtcDateTime);
        return db.Flashcards.CountAsync(c => c.StudySetId == studySetId && c.DeletedAt == null
            && !db.FlashcardProgresses.Any(p => p.FlashcardId == c.Id && p.UserId == userId && p.ParticipantId == participantId
                && p.SrsBox != null && p.SrsDueDate != null && p.SrsDueDate > today));
    }

    // (D5b, D5c) cùng điều kiện Leitner.IsDue: chưa có hộp/hạn cũng được tính đến hạn, theo ngày VN.
    public IQueryable<SrsDueCard> PersonalCards()
    {
        var today = VnTime.Date(clock.GetUtcNow().UtcDateTime);
        return db.Flashcards.Where(c => c.DeletedAt == null && c.StudySet.DeletedAt == null
            && c.StudySet.ArchivedAt == null && c.StudySet.HiddenByAdminAt == null
            && c.StudySet.FlashcardMode == FlashcardMode.SpacedRepetition
            && !db.FlashcardProgresses.Any(p => p.FlashcardId == c.Id && p.UserId == c.StudySet.OwnerId && p.ParticipantId == null
                && p.SrsBox != null && p.SrsDueDate != null && p.SrsDueDate > today))
            .Select(c => new SrsDueCard { UserId = c.StudySet.OwnerId, StudySetId = c.StudySetId, ParticipantId = null, CardId = c.Id });
    }

    public Task<Dictionary<int, int>> PersonalCountsAsync(string userId) => PersonalCards().Where(c => c.UserId == userId)
        .GroupBy(c => c.StudySetId).Select(g => new { StudySetId = g.Key, Count = g.Count() })
        .ToDictionaryAsync(g => g.StudySetId, g => g.Count);

    // (D5c) chỉ các lượt tham gia được phép học; mỗi bài là một ngữ cảnh SRS riêng.
    public IQueryable<SrsDueCard> AssignmentCards()
    {
        var now = clock.GetUtcNow().UtcDateTime; var today = VnTime.Date(now);
        return from p in db.Participants
            from set in db.StudySets.Where(s => s.Id == p.Assignment.StudySetId && s.DeletedAt == null
                && s.ArchivedAt == null && s.HiddenByAdminAt == null)
            where p.UserId != null && p.GuestId == null
                && (p.ApprovalStatus == ApprovalStatus.NotRequired || p.ApprovalStatus == ApprovalStatus.Approved)
                && p.Assignment.Status == AssignmentStatus.Open && (p.Assignment.OpensAt == null || p.Assignment.OpensAt <= now)
                && (p.Assignment.DueAt == null || p.Assignment.DueAt > now)
                && p.Assignment.Mode == AssignmentMode.Practice && p.Assignment.Form != AssignmentForm.QuizOnly
                && (p.Assignment.ClassroomId == null || p.Assignment.Classroom!.DeletedAt == null)
                && (p.Assignment.FlashcardMode == FlashcardMode.SpacedRepetition
                    || p.Assignment.FlashcardMode == FlashcardMode.LearnerChoice && p.FlashcardMode == FlashcardMode.SpacedRepetition)
                && !db.ClassMembers.Any(m => m.ClassroomId == p.Assignment.ClassroomId && m.UserId == p.UserId
                    && (m.Status == ClassMemberStatus.Left || m.Status == ClassMemberStatus.Removed))
                && (p.IsStranger || db.ClassMembers.Any(m => m.ClassroomId == p.Assignment.ClassroomId && m.UserId == p.UserId
                    && m.Status == ClassMemberStatus.Active && m.Role == ClassRole.Student && m.JoinedAt != null
                    && (p.Assignment.AudiencePolicy == AudiencePolicy.IncludeLateJoiners || m.JoinedAt <= p.Assignment.CreatedAt)))
            from c in db.Flashcards.Where(c => c.StudySetId == p.Assignment.StudySetId && c.DeletedAt == null
                && !db.FlashcardProgresses.Any(s => s.ParticipantId == p.Id && s.UserId == p.UserId && s.FlashcardId == c.Id
                    && s.SrsBox != null && s.SrsDueDate != null && s.SrsDueDate > today))
            select new SrsDueCard { UserId = p.UserId!, StudySetId = c.StudySetId, ParticipantId = p.Id, CardId = c.Id };
    }
}
