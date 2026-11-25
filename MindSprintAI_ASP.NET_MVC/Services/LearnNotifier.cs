using Microsoft.AspNetCore.SignalR;
using MindSprintAI_ASP.NET_MVC.Hubs;

namespace MindSprintAI_ASP.NET_MVC.Services;

// (66, B4a) hub chỉ gửi định danh, giờ VN và URL; hạn máy đọc lấy qua State.
public record LearnNotice(long? AttemptId = null, int? AssignmentId = null, string? Time = null, string? Url = null);

// (69, E) chỉ báo bảng đã đổi; dữ liệu học viên phải qua endpoint kiểm quyền.
public record LecturerNotice(int AssignmentId, int? RecheckAfterSeconds = null);

public interface ILearnNotifier
{
    Task AiSettingsAsync(int assignmentId) => Task.CompletedTask;
    Task StrangerAsync(string userId, int assignmentId) => Task.CompletedTask;
    Task NotificationAsync(string userId, NotificationNotice notice) => Task.CompletedTask;
    Task AttemptAsync(long attemptId, string eventName, LearnNotice notice);
    Task TestStartedAsync(int assignmentId);
    Task LecturersAsync(int assignmentId, int? recheckAfterSeconds = null);
    Task MovedAsync(long attemptId, IReadOnlyList<string> oldConnections);
}

public sealed class NullLearnNotifier : ILearnNotifier
{
    public Task AttemptAsync(long attemptId, string eventName, LearnNotice notice) => Task.CompletedTask;
    public Task TestStartedAsync(int assignmentId) => Task.CompletedTask;
    public Task LecturersAsync(int assignmentId, int? recheckAfterSeconds = null) => Task.CompletedTask;
    public Task MovedAsync(long attemptId, IReadOnlyList<string> oldConnections) => Task.CompletedTask;
}

public sealed class SignalRLearnNotifier(IHubContext<LearnHub> hub) : ILearnNotifier
{
    public static string AiGroup(int id) => $"assignment-{id}-ai";
    public Task AiSettingsAsync(int assignmentId) => hub.Clients.Group(AiGroup(assignmentId))
        .SendAsync("ai-settings-changed", new LearnNotice(AssignmentId: assignmentId));
    public Task StrangerAsync(string userId, int assignmentId) => hub.Clients.User(userId).SendAsync("stranger-changed",
        new LearnNotice(AssignmentId: assignmentId, Url: $"/Learn/Assignment/{assignmentId}"));
    public Task NotificationAsync(string userId, NotificationNotice notice) =>
        hub.Clients.User(userId).SendAsync("notification", notice);
    public static string AttemptGroup(long id) => $"attempt-{id}";
    public static string WaitingGroup(int id) => $"assignment-{id}-waiting";
    public static string LecturersGroup(int id) => $"assignment-{id}-lecturers";

    public Task LecturersAsync(int assignmentId, int? recheckAfterSeconds = null) =>
        hub.Clients.Group(LecturersGroup(assignmentId)).SendAsync("lecturers-changed", new LecturerNotice(assignmentId, recheckAfterSeconds));

    public Task AttemptAsync(long attemptId, string eventName, LearnNotice notice) =>
        hub.Clients.Group(AttemptGroup(attemptId)).SendAsync(eventName, notice);

    public Task TestStartedAsync(int assignmentId) => hub.Clients.Group(WaitingGroup(assignmentId))
        .SendAsync("test-started", new LearnNotice(AssignmentId: assignmentId, Url: $"/Learn/Assignment/{assignmentId}"));

    public async Task MovedAsync(long attemptId, IReadOnlyList<string> oldConnections)
    {
        if (oldConnections.Count == 0) return;
        foreach (var id in oldConnections) await hub.Groups.RemoveFromGroupAsync(id, AttemptGroup(attemptId));
        await hub.Clients.Clients(oldConnections).SendAsync("moved", new LearnNotice(AttemptId: attemptId));
    }
}
