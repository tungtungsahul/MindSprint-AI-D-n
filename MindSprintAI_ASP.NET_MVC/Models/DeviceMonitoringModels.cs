using MindSprintAI_ASP.NET_MVC.Data.Entities;
using MindSprintAI_ASP.NET_MVC.Services;

namespace MindSprintAI_ASP.NET_MVC.Models;

public record DeviceConsentModel(Assignment Assignment, long? AttemptId = null, int? RetakeId = null);
public record DeviceChangeCard(long Id, long AttemptId, string? Reason, DeviceHintNote? Note)
{
    public DateTime? RequestedAt { get; init; }
}
