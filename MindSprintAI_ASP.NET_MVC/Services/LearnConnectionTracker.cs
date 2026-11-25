namespace MindSprintAI_ASP.NET_MVC.Services;

public record LearnConnection(long AttemptId, string DeviceId);

// shortcut: in-memory, one server only; move to a backplane/DB if scaling out.
public sealed class LearnConnectionTracker
{
    private readonly object gate = new();
    private readonly Dictionary<string, LearnConnection> connections = [];

    public LearnConnection? Find(string connectionId)
    {
        lock (gate) return connections.GetValueOrDefault(connectionId);
    }

    public void Add(string connectionId, long attemptId, string deviceId)
    {
        lock (gate)
        {
            if (connections.TryGetValue(connectionId, out var old) && old != new LearnConnection(attemptId, deviceId))
                throw new BusinessException("Kết nối này đã mở lượt khác. Hãy tải lại trang.");
            connections[connectionId] = new(attemptId, deviceId);
        }
    }

    public void Remove(string connectionId)
    {
        lock (gate) connections.Remove(connectionId);
    }

    public string[] ForDevice(long attemptId, string deviceId)
    {
        lock (gate) return connections.Where(x => x.Value == new LearnConnection(attemptId, deviceId)).Select(x => x.Key).ToArray();
    }
}
