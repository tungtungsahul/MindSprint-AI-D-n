namespace MindSprintAI_ASP.NET_MVC.Data.Entities;

public enum UnhideRequestStatus { Pending = 1, Approved = 2, Rejected = 3 }

public class UnhideRequest
{
    public int Id { get; set; }
    public int StudySetId { get; set; }
    public StudySet StudySet { get; set; } = null!;
    public string RequesterId { get; set; } = "";
    public ApplicationUser Requester { get; set; } = null!;
    public string Message { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public UnhideRequestStatus Status { get; set; } = UnhideRequestStatus.Pending;
    public string? DecidedById { get; set; }
    public DateTime? DecidedAt { get; set; }
    public string? DecisionNote { get; set; }
}
