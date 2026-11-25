using MindSprintAI_ASP.NET_MVC.Data.Entities;

namespace MindSprintAI_ASP.NET_MVC.Models;

public sealed class AuditFilter
{
    public string? Email { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public AuditAction? Action { get; set; }
    public AuditResult? Result { get; set; }
    public string? TargetType { get; set; }
    public string? TargetId { get; set; }
    public string? RequestId { get; set; }
    public int Page { get; set; } = 1;
    public string PageUrl(string path, int page) => Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString(path,
        new Dictionary<string, string?> { [nameof(Email)] = Email, [nameof(From)] = From?.ToString("yyyy-MM-dd"),
            [nameof(To)] = To?.ToString("yyyy-MM-dd"), [nameof(Action)] = ((int?)Action)?.ToString(),
            [nameof(Result)] = ((int?)Result)?.ToString(), [nameof(TargetType)] = TargetType, [nameof(TargetId)] = TargetId,
            [nameof(RequestId)] = RequestId, [nameof(Page)] = page.ToString() });
}
public record AuditRow(AuditEntry Entry, string Actor, string Target, string? Url);
public record AuditPage(AuditFilter Filter, List<AuditRow> Rows, int Total, int Page)
{
    public int Pages => Math.Max(1, (int)Math.Ceiling(Total / 50d));
}

// (NK3, NK5) Tên hiển thị cố định, không lấy tên enum hoặc tên controller làm chữ giao diện.
public static class AuditDisplay
{
    private static readonly string[] Actions = ["",
        "Đăng ký tài khoản", "Xác minh email", "Đăng nhập", "Đăng nhập thất bại", "Đăng xuất",
        "Xin đặt lại mật khẩu", "Đặt lại mật khẩu", "Đổi mật khẩu", "Đặt mật khẩu",
        "Bật xác thực 2 lớp", "Tắt xác thực 2 lớp", "Tạo lại mã dự phòng", "Đặt lại ứng dụng xác thực",
        "Thêm Passkey", "Xóa Passkey", "Đổi tên Passkey", "Nối tài khoản Google hoặc Microsoft", "Gỡ tài khoản Google hoặc Microsoft",
        "Xóa thiết bị", "Đăng xuất các thiết bị khác", "Tự khóa tài khoản", "Lưu API key Gemini", "Xóa API key Gemini", "Đổi tùy chọn thông báo",
        "Tạo lớp", "Đổi mã vào lớp", "Xóa lớp", "Khôi phục lớp", "Duyệt thành viên", "Từ chối thành viên", "Xóa thành viên", "Tham gia lớp", "Rời lớp",
        "Mời giảng viên phụ", "Thu hồi lời mời", "Nhận lời mời giảng viên phụ", "Từ chối lời mời giảng viên phụ", "Gỡ giảng viên phụ",
        "Đề nghị chuyển lớp", "Nhận chuyển lớp", "Từ chối chuyển lớp", "Hủy chuyển lớp",
        "Tạo bộ học", "Sửa bộ học", "Xóa bộ học", "Khôi phục bộ học", "Tạo quiz", "Sửa quiz", "Xóa quiz", "Khôi phục quiz",
        "Tạo câu hỏi", "Sửa câu hỏi", "Xóa câu hỏi", "Khôi phục câu hỏi", "Tạo thẻ ghi nhớ", "Sửa thẻ ghi nhớ", "Xóa thẻ ghi nhớ", "Khôi phục thẻ ghi nhớ",
        "Nhập từ Excel", "Nhân bản bộ học", "Giao bài", "Đổi cài đặt bài giao", "Đổi hạn nộp", "Đóng bài giao", "Bắt đầu kiểm tra",
        "Công bố điểm", "Công bố đáp án", "Chấm lại", "Cộng giờ riêng", "Cộng lượt riêng", "Duyệt người ngoài lớp", "Từ chối người ngoài lớp",
        "Xin làm lại", "Duyệt làm lại", "Từ chối làm lại", "Hủy lượt làm lại", "Duyệt chuyển thiết bị", "Từ chối chuyển thiết bị",
        "Bắt đầu lượt làm bài", "Đồng ý trước khi làm bài", "Nộp bài", "Tự nộp vì hết giờ", "Tự nộp vì rời màn hình", "Xin chuyển thiết bị",
        "Quản trị viên khóa tài khoản", "Quản trị viên mở khóa tài khoản", "Cấp quyền quản trị viên", "Gỡ quyền quản trị viên", "Ẩn nội dung", "Bác bỏ báo cáo", "Báo cáo nội dung",
        "Quản trị viên đề nghị chuyển lớp", "Xem nhật ký hoạt động", "Thao tác không hoàn tất", "Hủy xin chuyển thiết bị", "Xóa báo cáo bài giao", "Nâng giảng viên chính", "Xem báo cáo nội dung",
        "Xem danh sách tài khoản", "Xem tài khoản", "Xem lịch sử quyền quản trị viên", "Xem danh sách báo cáo", "Xem danh sách chuyển lớp",
        "Xử lý báo cáo nội dung", "Bật mã xác thực qua email", "Tắt mã xác thực qua email", "Dừng lượt làm bài", "Hoàn thành lượt làm bài",
        "Xin bỏ ẩn bộ học", "Bỏ ẩn bộ học", "Từ chối bỏ ẩn", "Đổi cách vào lớp", "Xóa tài khoản",
        "Chấm bài theo đề", "Đổi tên hiển thị"
    ];
    public static string Text(this AuditAction action) => (int)action > 0 && (int)action < Actions.Length ? Actions[(int)action] : "Thao tác khác";
    public static string Text(this AuditResult result) => result switch
    { AuditResult.Success => "Thành công", AuditResult.Denied => "Bị từ chối", _ => "Thất bại" };
    public static string Target(string type) => type switch
    {
        "User" => "Tài khoản", "Classroom" => "Lớp", "ClassMember" => "Thành viên", "Invitation" => "Lời mời",
        "ClassTransfer" => "Chuyển lớp", "StudySet" => "Bộ học", "Quiz" => "Quiz", "Question" => "Câu hỏi",
        "Flashcard" => "Thẻ ghi nhớ", "Assignment" => "Bài giao", "Attempt" => "Lượt làm bài", "Participant" => "Người làm bài",
        "RetakeRequest" => "Đề nghị làm lại", "ContentReport" => "Báo cáo nội dung", "Audit" => "Nhật ký hoạt động",
        "UnhideRequest" => "Yêu cầu bỏ ẩn bộ học",
        _ => "Chức năng"
    };
}
