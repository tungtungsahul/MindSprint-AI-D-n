namespace MindSprintAI_ASP.NET_MVC.Models;

public record PasskeyItem(string Id, string Name, DateTime CreatedAt, DateTime? LastUsedAt);
public record PasskeyPage(IReadOnlyList<PasskeyItem> Items, bool HasPassword);
