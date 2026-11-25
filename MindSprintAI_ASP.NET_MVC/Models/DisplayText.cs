using MindSprintAI_ASP.NET_MVC.Data.Entities;

namespace MindSprintAI_ASP.NET_MVC.Models;

// Chữ tiếng Việt cho các giá trị enum hiện trên giao diện.
public static class DisplayText
{
    // Thời lượng dễ đọc: "45 giây", "3 phút 5 giây", "46 giờ 12 phút" (thay cho "166348 giây").
    public static string Duration(int seconds)
    {
        if (seconds < 60) return $"{Math.Max(0, seconds)} giây";
        if (seconds < 3600) return seconds % 60 == 0 ? $"{seconds / 60} phút" : $"{seconds / 60} phút {seconds % 60} giây";
        var minutes = seconds / 60 % 60;
        return minutes == 0 ? $"{seconds / 3600} giờ" : $"{seconds / 3600} giờ {minutes} phút";
    }

    public static string Of(ClassRole role) => role switch
    {
        ClassRole.MainLecturer => "Giảng viên chính", ClassRole.AssistantLecturer => "Giảng viên phụ", _ => "Học viên"
    };
    public static string Of(ReportTarget target) => target switch
    {
        ReportTarget.StudySet => "Bộ học", ReportTarget.Quiz => "Quiz", ReportTarget.Question => "Câu hỏi", ReportTarget.User => "Tên hiển thị", ReportTarget.AiConversation => "Hội thoại AI", _ => "Nội dung"
    };
    public static string Of(ReportStatus status) => status switch
    {
        ReportStatus.Pending => "Chờ xử lý", ReportStatus.Handled => "Đã xử lý", ReportStatus.Dismissed => "Bác bỏ", _ => ""
    };
    public static string Of(CompletionStatus s) => s switch
    {
        CompletionStatus.NotStarted => "Chưa làm",
        CompletionStatus.InProgress => "Đang làm",
        CompletionStatus.Paused => "Tạm dừng",
        CompletionStatus.SubmittedAwaitingScore => "Đã nộp, chờ công bố điểm",
        CompletionStatus.Completed => "Hoàn thành",
        CompletionStatus.Incomplete => "Chưa hoàn thành",
        _ => s.ToString()
    };

    public static IEnumerable<string> Of(CompletionLabels labels)
    {
        if (labels.HasFlag(CompletionLabels.Expired)) yield return "Kết thúc do hết hạn";
        if (labels.HasFlag(CompletionLabels.ClosedByLecturer)) yield return "Kết thúc do giảng viên đóng bài";
        if (labels.HasFlag(CompletionLabels.HiddenByAdmin)) yield return "Kết thúc do quản trị viên ẩn nội dung";
        // (B4b) bị xóa khi đang làm có nhãn riêng, không gộp vào "Đã rời lớp" (67).
        if (labels.HasFlag(CompletionLabels.RemovedFromClass))
            yield return labels.HasFlag(CompletionLabels.LeftClass) ? "Bị dừng do rời lớp" : "Bị dừng do bị xóa khỏi lớp";
        else if (labels.HasFlag(CompletionLabels.LeftClass)) yield return "Đã rời lớp";
        if (labels.HasFlag(CompletionLabels.StoppedByLock)) yield return "Bị dừng do khóa tài khoản";
        if (labels.HasFlag(CompletionLabels.MadeAsGuest)) yield return "Làm khi là khách";
        if (labels.HasFlag(CompletionLabels.RetakeNotUsed)) yield return "Được làm lại nhưng không làm";
        if (labels.HasFlag(CompletionLabels.CompletedBeforeCardsRequired)) yield return "Hoàn thành trước khi bắt buộc học thẻ";
        if (labels.HasFlag(CompletionLabels.MissingRequiredSurvey)) yield return "Chưa phản hồi khảo sát bắt buộc";
    }

    public static string Of(AssignmentMode mode) => mode == AssignmentMode.Test ? "Kiểm tra" : "Ôn tập";
    // "Quay lại lớp Lớp 10A" lặp chữ: tên đã bắt đầu bằng "lớp" thì không thêm.
    public static string ClassName(string? name) => name?.StartsWith("lớp", StringComparison.CurrentCultureIgnoreCase) == true ? name : "lớp " + name;

    public static string Of(AttemptKind kind) => kind switch
    {
        AttemptKind.Retake => "Làm lại",
        AttemptKind.SelfReview => "Tự ôn sau hạn",
        _ => "Lượt đầu"
    };

    public static string Of(RetakeRequestStatus status) => status switch
    {
        RetakeRequestStatus.Pending => "Chờ duyệt",
        RetakeRequestStatus.Approved => "Đã duyệt",
        RetakeRequestStatus.Rejected => "Từ chối",
        RetakeRequestStatus.Used => "Đã dùng",
        RetakeRequestStatus.Expired => "Hết khung không làm",
        _ => status.ToString()
    };

    public static string Of(AttemptEndReason? r) => r switch
    {
        AttemptEndReason.Submitted => "Đã nộp",
        AttemptEndReason.DueDate => "Kết thúc do hết hạn",
        AttemptEndReason.ClosedByLecturer => "Kết thúc do giảng viên đóng bài",
        AttemptEndReason.HiddenByAdmin => "Kết thúc do quản trị viên ẩn nội dung",
        AttemptEndReason.FinishedAfterMastery => "Kết thúc khi đạt mốc làm chủ",
        AttemptEndReason.ScreenLeaveLimit => "Tự nộp vì rời màn hình quá N lần",
        null => "Đang làm",
        _ => r.ToString()!
    };

    public static string Of(ScoreSelection s) => s switch
    {
        ScoreSelection.Highest => "Lượt điểm cao nhất",
        ScoreSelection.First => "Lượt đầu tiên",
        ScoreSelection.Latest => "Lượt gần nhất",
        ScoreSelection.Average => "Trung bình các lượt",
        _ => s.ToString()
    };

    public static string Of(ScoreScale s) => s switch
    {
        ScoreScale.Ten => "Thang 10",
        ScoreScale.Original => "Thang điểm gốc",
        ScoreScale.Hundred => "Thang 100",
        _ => s.ToString()
    };

    public static string Of(RoundingMode r) => r switch
    {
        RoundingMode.TwoDecimals => "2 chữ số thập phân",
        RoundingMode.OneDecimal => "1 chữ số thập phân",
        RoundingMode.Quarter => "Làm tròn tới 0,25",
        RoundingMode.Half => "Làm tròn tới 0,5",
        _ => r.ToString()
    };

    public static string Of(AudiencePolicy a) => a switch
    {
        AudiencePolicy.IncludeLateJoiners => "Giao cho cả người vào lớp sau",
        AudiencePolicy.OnlyCurrentMembers => "Chỉ người đang ở lớp lúc giao",
        _ => a.ToString()
    };

    public static string Of(QuestionType t) => t switch
    {
        QuestionType.SingleChoice => "Chọn một",
        QuestionType.MultipleChoice => "Chọn nhiều",
        QuestionType.TextInput => "Điền chữ",
        QuestionType.NumberInput => "Điền số",
        QuestionType.SurveySingle => "Khảo sát chọn một",
        QuestionType.SurveyMultiple => "Khảo sát chọn nhiều",
        QuestionType.SurveyText => "Khảo sát ý kiến",
        _ => t.ToString()
    };

    public static string Of(AssignmentForm f) => f switch
    {
        AssignmentForm.QuizOnly => "Chỉ quiz", AssignmentForm.FlashcardOnly => "Chỉ thẻ ghi nhớ", AssignmentForm.Document => "Bài làm theo đề", _ => "Kết hợp"
    };

    public static string Of(RequiredParts p) => p switch
    {
        RequiredParts.Quiz => "Quiz", RequiredParts.Flashcards => "Thẻ", _ => "Cả hai"
    };
}
