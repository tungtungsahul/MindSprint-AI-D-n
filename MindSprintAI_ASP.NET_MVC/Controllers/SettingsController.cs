using Microsoft.AspNetCore.Mvc;

namespace MindSprintAI_ASP.NET_MVC.Controllers;

// Trang "Cài đặt" của nhóm (docs/giao-dien-nhom/11): cài ứng dụng lên máy, giao diện, chuông báo. Lựa chọn lưu ở trình duyệt này.
public class SettingsController : AppController
{
    public IActionResult Index() => View();
}
