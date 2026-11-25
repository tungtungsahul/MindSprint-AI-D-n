using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MindSprintAI_ASP.NET_MVC.Data;
using MindSprintAI_ASP.NET_MVC.Data.Entities;

namespace MindSprintAI_ASP.NET_MVC.Services;

public record NotificationPayload(string Text, string Url, int? ClassroomId = null, int? TransferId = null,
    int? AssignmentId = null, long? AttemptId = null);
public record NotificationNotice(int Unread);
public record NotificationPage(List<Notification> Items, int Page, bool HasNext);

public class NotificationService(AppDbContext db, TimeProvider clock, ILearnNotifier notifier)
{
    public static void Validate(NotificationPayload payload)
    {
        if (string.IsNullOrWhiteSpace(payload.Text) || payload.Text.Length > 1000
            || !payload.Url.StartsWith('/') || payload.Url.StartsWith("//") || payload.Url.Contains('\\')
            || payload.Url.Any(char.IsControl)) throw new BusinessException("Thông báo không hợp lệ.");
    }

    // (14) chỉ thêm vào DbContext; bên nghiệp vụ lưu cùng giao dịch, rồi mới đẩy số chưa đọc.
    public Task AddAsync(string userId, NotificationType type, NotificationPayload payload)
    {
        Validate(payload);
        db.Notifications.Add(new() { UserId = userId, Type = type, PayloadJson = JsonSerializer.Serialize(payload),
            CreatedAt = clock.GetUtcNow().UtcDateTime });
        return Task.CompletedTask;
    }

    public static NotificationPayload Payload(Notification row) => JsonSerializer.Deserialize<NotificationPayload>(row.PayloadJson)!;
    public Task<int> UnreadAsync(string userId) => db.Notifications.CountAsync(n => n.UserId == userId && n.ReadAt == null);
    public async Task PushAsync(params string[] users)
    {
        foreach (var id in users.Distinct()) await notifier.NotificationAsync(id, new(await UnreadAsync(id)));
    }

    public async Task<NotificationPage> ListAsync(string userId, int page = 1)
    {
        page = Math.Clamp(page, 1, 100000);
        var rows = await db.Notifications.AsNoTracking().Where(n => n.UserId == userId)
            .OrderByDescending(n => n.Id).Skip((page - 1) * 20).Take(21).ToListAsync();
        return new(rows.Take(20).ToList(), page, rows.Count > 20);
    }

    public async Task<Notification> ReadAsync(long id, string userId)
    {
        var row = await db.Notifications.AsNoTracking().SingleOrDefaultAsync(n => n.Id == id);
        if (row == null || row.UserId != userId) throw new ForbiddenException();
        return row;
    }

    public async Task<string> MarkReadAsync(long id, string userId)
    {
        var row = await ReadAsync(id, userId);
        var now = clock.GetUtcNow().UtcDateTime;
        await db.Notifications.Where(n => n.Id == id && n.UserId == userId && n.ReadAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, now));
        await PushAsync(userId);
        var payload = Payload(row);
        Validate(payload);
        return payload.Url;
    }

    public async Task ReadAllAsync(string userId)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        await db.Notifications.Where(n => n.UserId == userId && n.ReadAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, now));
        await PushAsync(userId);
    }
}
