namespace MindSprintAI_ASP.NET_MVC.Data.Entities;

// (46A) chỉ nhận dạng bằng bí mật ngẫu nhiên trên trình duyệt, không IP/vân tay.
public class Guest
{
    public int Id { get; set; }
    public string UserId { get; set; } = "";
    public ApplicationUser User { get; set; } = null!;
    public string DisplayName { get; set; } = "";
    public string BrowserTokenHash { get; set; } = "";
    public string? MergedIntoUserId { get; set; }
    public ApplicationUser? MergedIntoUser { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
