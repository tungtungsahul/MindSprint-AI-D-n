namespace MindSprintAI_ASP.NET_MVC.Data.Entities;

// Bài làm theo đề tài liệu (AssignmentForm.Document): mỗi lượt làm có một bài viết + các tệp đính kèm; GV chấm tay.
// Điểm chấm tay được chép sang Attempt.FirstScore/MaxScore để bảng điểm, ma trận, công bố điểm dùng chung một đường.
// Quy ước: lượt bài theo đề có Attempt.MaxScore = 0 cho tới khi GV chấm (bảng điểm hiểu là "chưa chấm", không phải 0 điểm).
public class PaperSubmission
{
    public long AttemptId { get; set; }
    public Attempt Attempt { get; set; } = null!;
    public string Text { get; set; } = "";
    public DateTime? SavedAt { get; set; }
    public decimal? Score { get; set; }          // thang 10, tối đa 2 chữ số lẻ
    public string? Comment { get; set; }
    public string? GradedById { get; set; }
    public DateTime? GradedAt { get; set; }
    public List<PaperFile> Files { get; set; } = [];
}

public class PaperFile
{
    public long Id { get; set; }
    public long AttemptId { get; set; }
    public PaperSubmission Submission { get; set; } = null!;
    public string FileName { get; set; } = "";     // tên gốc (đã làm sạch) để hiển thị/tải về
    public string ContentType { get; set; } = "";
    public long SizeBytes { get; set; }
    public string StoredName { get; set; } = "";   // tên ngẫu nhiên trên đĩa
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
}
