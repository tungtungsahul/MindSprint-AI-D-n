using Microsoft.EntityFrameworkCore;
using MindSprintAI_ASP.NET_MVC.Data;
using MindSprintAI_ASP.NET_MVC.Data.Entities;
using MindSprintAI_ASP.NET_MVC.Models;

namespace MindSprintAI_ASP.NET_MVC.Services;

public sealed class SourceTextService(AppDbContext db, StudyLibraryService library)
{
    public async Task<SourceEditModel> EditorAsync(int id, string userId)
    {
        var set = await library.GetOwnedStudySetAsync(id, userId);
        var source = await db.SourceTexts.AsNoTracking().SingleOrDefaultAsync(s => s.StudySetId == id);
        return new(id, set.Title, source?.Content ?? "", source?.Version ?? 0);
    }

    public async Task SaveAsync(int id, string? content, int version, string userId)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT [Id] FROM [StudySets] WITH (UPDLOCK,HOLDLOCK) WHERE [Id] = {id}");
        await library.GetOwnedStudySetAsync(id, userId);
        // Trình duyệt gửi xuống dòng là \r\n (2 ký tự) nhưng bộ đếm trên trang tính 1; chuẩn về \n để hai bên đếm như nhau.
        content = content?.Replace("\r\n", "\n").Trim() ?? "";
        if (content.Length > 50000) throw new BusinessException("Văn bản nguồn tối đa 50.000 ký tự.");
        var row = await db.SourceTexts.SingleOrDefaultAsync(s => s.StudySetId == id);
        if (row != null) await db.Entry(row).ReloadAsync();
        if ((row?.Version ?? 0) != version) throw new BusinessException("Văn bản nguồn vừa được sửa ở nơi khác. Hãy tải lại.");
        if (row == null) { row = new() { StudySetId = id }; db.SourceTexts.Add(row); }
        else db.Entry(row).Property(s => s.Content).IsModified = true; // Mỗi lần lưu đều tăng Version, kể cả giữ nguyên chữ.
        row.Content = content;
        await db.SaveChangesAsync(); await tx.CommitAsync();
    }
}
