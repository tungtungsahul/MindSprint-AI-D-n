namespace MindSprintAI_ASP.NET_MVC.Data.Entities;

// Bảng công việc cá nhân (Kanban) chuyển từ MindSprint-AI-D-n (backend/MindSprint.Api, KanbanTask + TasksController).
// Quy tắc giữ nguyên bản nhóm: tiêu đề 1–200 ký tự, mô tả ≤ 5000, ưu tiên Thấp/Trung bình/Cao (mặc định Trung bình),
// 3 cột, thứ tự trong cột theo Position (0..n-1) của từng người.
public enum KanbanStatus { Todo = 0, Doing = 1, Done = 2 }

public enum KanbanPriority { Low = 0, Medium = 1, High = 2 }

public class KanbanTask
{
    public int Id { get; set; }
    public string UserId { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public KanbanPriority Priority { get; set; } = KanbanPriority.Medium;
    public KanbanStatus Status { get; set; } = KanbanStatus.Todo;
    public int Position { get; set; }
    public DateTime? DueDate { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
