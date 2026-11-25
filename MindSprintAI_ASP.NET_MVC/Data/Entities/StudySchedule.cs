namespace MindSprintAI_ASP.NET_MVC.Data.Entities;

// Thời khóa biểu cá nhân chuyển từ MindSprint-AI-D-n (StudySchedule + frontend timetable). Giữ quy tắc nhóm:
// giờ học trong 06:00–23:00, kết thúc sau bắt đầu, 4 chủ đề, ghi chú ≤ 40 ký tự, được phép chồng giờ.
public enum StudyTopic { Mixed = 0, English = 1, Programming = 2, General = 3 }

public class StudySlot
{
    public int Id { get; set; }
    public string UserId { get; set; } = "";
    /// <summary>0 = Chủ nhật … 6 = Thứ Bảy (như System.DayOfWeek và JS getDay()).</summary>
    public DayOfWeek DayOfWeek { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public StudyTopic Topic { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
