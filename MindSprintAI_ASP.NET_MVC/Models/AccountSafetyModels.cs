using MindSprintAI_ASP.NET_MVC.Data.Entities;
using MindSprintAI_ASP.NET_MVC.Services;

namespace MindSprintAI_ASP.NET_MVC.Models;

public record LockConfirmationModel(string UserId, string Code, string Nonce);
public class AdminLockModel
{
    public string UserId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public AccountLockKind Kind { get; set; } = AccountLockKind.Protective;
    public string? Reason { get; set; }
    public DateTime? EndsAt { get; set; }
    public int? ReportId { get; set; }
}
public class ReportForm
{
    public ReportContext Context { get; set; } = new(ReportTarget.StudySet, "");
    public string? Reason { get; set; }
    public string? Note { get; set; }
}

public class AdminDeleteUserModel
{
    public string UserId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? Email { get; set; }
    public string? Reason { get; set; }
}
