namespace MindSprintAI_ASP.NET_MVC.Data.Entities;

public class UserSettingPreset
{
    public string UserId { get; set; } = "";
    public string Scope { get; set; } = "";
    public string ValuesJson { get; set; } = "{}";
    public DateTime UpdatedAt { get; set; }
}
