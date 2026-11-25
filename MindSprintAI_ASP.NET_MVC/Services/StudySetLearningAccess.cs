using Microsoft.EntityFrameworkCore;
using MindSprintAI_ASP.NET_MVC.Data;
using MindSprintAI_ASP.NET_MVC.Data.Entities;

namespace MindSprintAI_ASP.NET_MVC.Services;

public static class StudySetLearningAccess
{
    public static async Task EnsureNotTakingTestAsync(AppDbContext db, int studySetId, string userId)
    {
        // (71, ý Codex 3) khóa thắng quyền của mọi bài giao khác cùng bộ, kể cả lịch sử AI.
        if (await db.Attempts.AnyAsync(a => a.Participant.UserId == userId
            && a.Participant.Assignment.Mode == AssignmentMode.Test
            && (a.Participant.Assignment.StudySetId == studySetId || a.Participant.Assignment.Quiz!.StudySetId == studySetId)
            && (a.Status == AttemptStatus.InProgress || a.Status == AttemptStatus.Paused)))
            throw new BusinessException("Đang làm Kiểm tra: văn bản nguồn, thẻ ghi nhớ và trợ lý AI của bộ này đang bị khóa cho đến khi làm xong.");
    }
}
