using Microsoft.EntityFrameworkCore;
using MindSprintAI_ASP.NET_MVC.Data;

namespace MindSprintAI_ASP.NET_MVC.Services;

public static class ContentVisibility
{
    // Đọc mới từ DB; entity trong request có thể còn trạng thái trước khi Admin ẩn.
    public static Task<string?> HiddenReasonAsync(AppDbContext db, int? studySetId) =>
        db.StudySets.AsNoTracking().Where(s => s.Id == studySetId && s.HiddenByAdminAt != null)
            .Select(s => s.HiddenReason ?? "").SingleOrDefaultAsync();

    public static string? LearnerNotice(string? reason) =>
        reason == null ? null : "Nội dung bài đã bị quản trị viên ẩn: " + reason;
}
