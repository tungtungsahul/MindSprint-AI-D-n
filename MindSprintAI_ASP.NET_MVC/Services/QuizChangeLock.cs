using Microsoft.EntityFrameworkCore;
using MindSprintAI_ASP.NET_MVC.Data;
using MindSprintAI_ASP.NET_MVC.Data.Entities;

namespace MindSprintAI_ASP.NET_MVC.Services;

internal static class QuizChangeLock
{
    // (C3) khóa logic không khóa đọc quiz: tránh vòng chờ với học viên đang giữ khóa bài giao.
    internal static Task AcquireAsync(AppDbContext db, int quizId) => db.Database.ExecuteSqlInterpolatedAsync(
        $"DECLARE @result int; EXEC @result = sp_getapplock @Resource = {"quiz-edit-" + quizId}, @LockMode = 'Exclusive', @LockOwner = 'Transaction'; IF @result < 0 THROW 50000, N'Không lấy được khóa quiz, hãy thử lại.', 1;");

    internal static async Task<List<Attempt>> AttemptsAsync(AppDbContext db, Assignment assignment)
    {
        var headers = await db.Participants.Where(p => p.AssignmentId == assignment.Id).OrderBy(p => p.Id)
            .Select(p => new { p.Id, p.UserId }).ToListAsync();
        foreach (var p in headers.Where(p => p.UserId != null).OrderBy(p => p.UserId))
            await AttemptLock.MemberAsync(db, assignment.ClassroomId, p.UserId!);
        foreach (var p in headers) await AttemptLock.ParticipantAsync(db, p.Id);
        var ids = await db.Attempts.Where(a => a.Participant.AssignmentId == assignment.Id).OrderBy(a => a.Id).Select(a => a.Id).ToListAsync();
        var attempts = new List<Attempt>();
        foreach (var id in ids) attempts.Add(await AttemptLock.LoadAsync(db, id));
        return attempts;
    }
}
