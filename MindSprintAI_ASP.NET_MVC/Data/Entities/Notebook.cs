namespace MindSprintAI_ASP.NET_MVC.Data.Entities;

// Sổ tay AI kiểu NotebookLM chuyển từ MindSprint-AI-D-n (Notebook/NotebookSource/NotebookNote + NotebooksController):
// gom nhiều nguồn tài liệu để hỏi đáp có trích dẫn, ghi chú và tạo bộ học. Giới hạn nhóm: 20 nguồn/sổ, 200.000 ký tự/nguồn.
public enum NotebookSourceKind { Text = 0, Pdf = 1, File = 2, Image = 3, Url = 4 } // Image: chữ đọc từ ảnh bằng Gemini (Gia sư OCR của nhóm)

public class Notebook
{
    public int Id { get; set; }
    public string UserId { get; set; } = "";
    public string Title { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<NotebookSource> Sources { get; set; } = [];
    public List<NotebookNote> Notes { get; set; } = [];
}

public class NotebookSource
{
    public int Id { get; set; }
    public int NotebookId { get; set; }
    public Notebook Notebook { get; set; } = null!;
    public string Title { get; set; } = "";
    public NotebookSourceKind Kind { get; set; }
    public string? Origin { get; set; } // tên tệp gốc
    public string Content { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class NotebookNote
{
    public int Id { get; set; }
    public int NotebookId { get; set; }
    public Notebook Notebook { get; set; } = null!;
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
