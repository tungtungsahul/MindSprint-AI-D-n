using Microsoft.EntityFrameworkCore;
using MindSprintAI_ASP.NET_MVC.Data;
using MindSprintAI_ASP.NET_MVC.Data.Entities;

namespace MindSprintAI_ASP.NET_MVC.Services;

public record KanbanInput(string? Title, string? Description, DateTime? DueDate, KanbanPriority? Priority = null, KanbanStatus? Status = null);

// Quy tắc lấy từ MindSprint-AI-D-n TasksController (giữ nguyên): tiêu đề 1–200, mô tả ≤ 5000, ưu tiên Thấp/Trung bình/Cao,
// mỗi cột đánh số 0..n-1; chuyển cột/xóa thì đánh số lại. Khác bản nhóm: mọi thao tác ghi khóa theo người (sp_getapplock)
// thay cho giao dịch Serializable — cùng kết quả, không gây deadlock khi một người mở nhiều tab.
public sealed class KanbanService(AppDbContext db)
{
    public const string NotFound = "Không tìm thấy công việc.";

    public Task<List<KanbanTask>> ListAsync(string userId) => db.KanbanTasks.AsNoTracking()
        .Where(t => t.UserId == userId).OrderBy(t => t.Status).ThenBy(t => t.Position).ThenBy(t => t.Id).ToListAsync();

    public async Task<KanbanTask> CreateAsync(string userId, KanbanInput input)
    {
        var (title, description, priority) = Validate(input);
        var status = input.Status ?? KanbanStatus.Todo;
        await using var tx = await db.Database.BeginTransactionAsync();
        await AttemptLock.KanbanAsync(db, userId);
        var position = await db.KanbanTasks.CountAsync(t => t.UserId == userId && t.Status == status);
        var task = new KanbanTask { UserId = userId, Title = title, Description = description, Priority = priority ?? KanbanPriority.Medium,
            Status = status, Position = position, DueDate = DateOnly(input.DueDate) };
        db.KanbanTasks.Add(task);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return task;
    }

    public async Task<KanbanTask> UpdateAsync(string userId, int id, KanbanInput input)
    {
        var (title, description, priority) = Validate(input);
        await using var tx = await db.Database.BeginTransactionAsync();
        await AttemptLock.KanbanAsync(db, userId);
        var task = await OwnAsync(userId, id);
        (task.Title, task.Description, task.DueDate) = (title, description, DateOnly(input.DueDate));
        task.Priority = priority ?? task.Priority;
        if (input.Status is { } status && status != task.Status) await ArrangeAsync(task, status, int.MaxValue);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return task;
    }

    public async Task<KanbanTask> MoveAsync(string userId, int id, KanbanStatus status, int position)
    {
        if (!Enum.IsDefined(status) || position < 0) throw new BusinessException("Cột hoặc vị trí công việc không hợp lệ.");
        await using var tx = await db.Database.BeginTransactionAsync();
        await AttemptLock.KanbanAsync(db, userId);
        var task = await OwnAsync(userId, id);
        await ArrangeAsync(task, status, position);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return task;
    }

    public async Task DeleteAsync(string userId, int id)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        await AttemptLock.KanbanAsync(db, userId);
        var task = await OwnAsync(userId, id);
        db.KanbanTasks.Remove(task);
        var rest = await db.KanbanTasks.Where(t => t.UserId == userId && t.Status == task.Status && t.Id != id)
            .OrderBy(t => t.Position).ThenBy(t => t.Id).ToListAsync();
        for (var i = 0; i < rest.Count; i++) rest[i].Position = i;
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    // Người khác (hoặc mã sai) đều nhận cùng một câu, không lộ việc của người khác có tồn tại hay không.
    private async Task<KanbanTask> OwnAsync(string userId, int id) =>
        await db.KanbanTasks.SingleOrDefaultAsync(t => t.Id == id && t.UserId == userId) ?? throw new BusinessException(NotFound);

    private async Task ArrangeAsync(KanbanTask task, KanbanStatus status, int position)
    {
        var oldStatus = task.Status;
        var others = await db.KanbanTasks.Where(t => t.UserId == task.UserId && t.Id != task.Id && (t.Status == oldStatus || t.Status == status))
            .OrderBy(t => t.Position).ThenBy(t => t.Id).ToListAsync();
        if (oldStatus != status)
        {
            var previous = others.Where(t => t.Status == oldStatus).ToList();
            for (var i = 0; i < previous.Count; i++) previous[i].Position = i;
        }
        var target = others.Where(t => t.Status == status).ToList();
        task.Status = status;
        target.Insert(Math.Min(position, target.Count), task);
        for (var i = 0; i < target.Count; i++) target[i].Position = i;
    }

    private static (string Title, string? Description, KanbanPriority? Priority) Validate(KanbanInput input)
    {
        var title = input.Title?.Trim() ?? "";
        if (title.Length is < 1 or > 200) throw new BusinessException("Tiêu đề cần có từ 1 đến 200 ký tự.");
        var description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim();
        if (description?.Length > 5000) throw new BusinessException("Mô tả tối đa 5000 ký tự.");
        if (input.Priority is { } p && !Enum.IsDefined(p)) throw new BusinessException("Mức ưu tiên không hợp lệ.");
        if (input.Status is { } s && !Enum.IsDefined(s)) throw new BusinessException("Trạng thái không hợp lệ.");
        return (title, description, input.Priority);
    }

    // Hạn hoàn thành là một ngày (ô chọn ngày); lưu 00:00 của ngày đó, đánh dấu UTC như mọi DateTime trong DB.
    private static DateTime? DateOnly(DateTime? value) => value is { } d ? DateTime.SpecifyKind(d.Date, DateTimeKind.Utc) : null;
}
