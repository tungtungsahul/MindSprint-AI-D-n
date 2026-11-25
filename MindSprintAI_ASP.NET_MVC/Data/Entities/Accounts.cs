using Microsoft.AspNetCore.Identity;

namespace MindSprintAI_ASP.NET_MVC.Data.Entities;

public enum AvatarSource { None = 0, Google = 1, Upload = 2 }

public class ApplicationUser : IdentityUser
{
    public string DisplayName { get; set; } = "";
    public string? AvatarUrl { get; set; }
    public AvatarSource AvatarSource { get; set; }
    public bool AvatarHidden { get; set; }
    public DateTime? AvatarUpdatedAt { get; set; }
    // Lần tự đổi tên hiển thị gần nhất (ProfileService: mỗi lần cách nhau ít nhất 7 ngày).
    public DateTime? DisplayNameChangedAt { get; set; }

    // Chỉ đổi màn hình chính, không đổi quyền (37B, 38A).
    public UserMode CurrentMode { get; set; } = UserMode.Learner;

    public bool IsSystemAdmin { get; set; }
    public bool IsGuest { get; set; }
    public bool EmailRemindersEnabled { get; set; } = true; // (N5)
    public bool SrsBellEnabled { get; set; } = true; // (D5c) độc lập với email nhắc việc.
    public bool DeviceChangeBellEnabled { get; set; } // (F11) chỉ bật chuông khi GV chọn.
    public bool ScreenLeaveBellEnabled { get; set; } // (F11) độc lập với chuông xin chuyển máy.

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Pháp lý 2026-11-12: bằng chứng đồng ý Điều khoản + Chính sách (phiên bản nào, lúc nào); khách cũng ghi ở đây.
    public DateTime? PolicyAcceptedAt { get; set; }
    public int? PolicyVersion { get; set; }
    // Đã xóa tài khoản: hàng giữ lại (ẩn danh) để bảng điểm của giảng viên không bị thủng.
    public DateTime? DeletedAt { get; set; }
}
