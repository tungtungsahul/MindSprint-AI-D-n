using MindSprintAI_ASP.NET_MVC.Services;
using Xunit;

namespace MindSprintAI_ASP.NET_MVC.Tests;

public class EmailLayoutTests
{
    [Fact]
    public void KhungEmailCoThuongHieuMauRekalVaKhongTaiTaiNguyenNgoai()
    {
        var html = EmailLayout.Wrap("Xác nhận <khóa> · MindSprint AI", "<p>Chào bạn</p>");
        Assert.Contains("Chào bạn", html);
        Assert.Contains(Brand.Name, html);
        Assert.Contains("#D2E823", html); // Acid
        Assert.Contains("#F3F7DC", html); // nền
        Assert.Contains("<title>Xác nhận &lt;khóa&gt; · MindSprint AI</title>", html); // tiêu đề được mã hóa
        foreach (var banned in new[] { "fonts.googleapis", "fonts.gstatic", "<link", "<script", "url(http", "<img" })
            Assert.DoesNotContain(banned, html);
    }

    [Fact]
    public void LienKetDonLeThanhNutVaGiuNguyenDiaChi()
    {
        var url = "http://localhost:5216/Account/ConfirmEmail?userId=1&code=abc";
        var html = EmailLayout.Wrap("Xác minh", $"<p>Bấm:</p><p><a href=\"{url}\">{url}</a></p><p><a href=\"{url}\">Xác nhận khóa</a></p>");
        Assert.Contains($"href=\"{url}\"", html);
        Assert.Contains("Mở liên kết", html);          // địa chỉ dài thành chữ dễ hiểu
        Assert.Contains("Nếu nút không bấm được", html); // vẫn có địa chỉ để sao chép
        Assert.Contains(">Xác nhận khóa</a>", html);
    }

    [Fact]
    public void MaDangNhapVaLienKetTrongDongGiuNoiDung()
    {
        var html = EmailLayout.Wrap("Mã", "<p>Mã đăng nhập: <b>123456</b>. Hết hạn sau 5 phút.</p><p><a href=\"/a\">Tài khoản</a> · <a href=\"/b\">Khóa</a></p>");
        Assert.Contains(">123456</b>", html);
        Assert.Contains("Hết hạn sau 5 phút", html);
        Assert.Contains("href=\"/a\"", html);
        Assert.Contains("href=\"/b\"", html);
    }

    [Fact]
    public void CoGiaoDienToiTheoCaiDatNguoiDoc()
    {
        var html = EmailLayout.Wrap("Mã", "<p>Mã: <b>123456</b></p><p><a href=\"/x\">Mở trang</a></p><p><a href=\"/a\">Tài khoản</a> · <a href=\"/b\">Khóa</a></p>");
        Assert.Contains("<meta name=\"color-scheme\" content=\"light dark\">", html);
        Assert.DoesNotContain("light only", html);
        Assert.Contains("@media (prefers-color-scheme:dark)", html);
        Assert.Contains("[data-ogsc] .rk-text", html); // Outlook.com
        Assert.Contains("#131317", html); // nền tối giống web
        // Mỗi phần có màu đều gắn lớp để bản tối ghi đè được.
        foreach (var css in new[] { "rk-bg", "rk-logo", "rk-card", "rk-band", "rk-text", "rk-mark", "rk-muted", "rk-link", "rk-btn", "rk-on-acid", "rk-code" })
            Assert.Contains($"class=\"{css}\"", html);
        // Bản sáng giữ nguyên màu inline (Gmail chỉ dùng bản này).
        Assert.Contains("background:#F3F7DC", html);
    }

    [Fact]
    public void BocHaiLanKhongLongNhau()
    {
        var once = EmailLayout.Wrap("S", "<p>x</p>");
        Assert.Equal(once, EmailLayout.Wrap("S", once));
    }

    [Fact]
    public void ThuGuiThatDungBodyDaBoc()
    {
        var settings = new SmtpSettings { Host = "h", UserName = "he.thong@example.test", Password = "p" };
        using var message = SmtpEmailWorker.Build(settings, new("hv@example.test", "Lời mời",
            "<p>Chào bạn, bấm <a href=\"https://mindsprint.example/x?a=1&amp;b=2\">vào đây</a>.</p><p><a href=\"https://mindsprint.example/y\">https://mindsprint.example/y</a></p>"));
        var html = Assert.Single(message.AlternateViews);
        Assert.Equal("text/html", html.ContentType.MediaType);
        var htmlText = new StreamReader(html.ContentStream).ReadToEnd();
        Assert.Contains("Chào bạn", htmlText);
        Assert.Contains("#D2E823", htmlText);
        // Phần chữ thường (chống bị xếp vào thư rác vì chỉ có HTML): không còn thẻ, liên kết in rõ địa chỉ.
        Assert.False(message.IsBodyHtml);
        Assert.DoesNotContain("<", message.Body);
        Assert.Contains("vào đây: https://mindsprint.example/x?a=1&b=2", message.Body);
        Assert.Contains("\nhttps://mindsprint.example/y\n", message.Body);
    }

    [Fact]
    public void GhiMauXemTruocRaThuMucTestResults()
    {
        var dir = Path.Combine(Tang8PureTests.Root, "App_Data", "TestResults", "mindsprint-preview");
        Directory.CreateDirectory(dir);
        var url = "http://localhost:5216/Account/ConfirmEmail?userId=db6bb14b&code=Q2ZESjhCcnBzeEJu";
        File.WriteAllText(Path.Combine(dir, "email-xac-minh.html"), EmailLayout.Wrap("Xác minh email MindSprint AI", $"<p>Chào Cô Lan, bấm nút dưới để xác minh email:</p><p><a href=\"{url}\">{url}</a></p><p>Liên kết dùng được trong 30 phút.</p>"));
        File.WriteAllText(Path.Combine(dir, "email-thiet-bi-moi.html"), EmailLayout.Wrap("Đăng nhập mới vào MindSprint AI", "<p>Chrome trên Windows, lúc 23:30 09/11/2026 (giờ Việt Nam), mạng Internet 203.0.113.7, vị trí ước tính Hà Nội. Nếu không phải bạn, hãy đổi mật khẩu.</p><p><a href=\"https://db-ip.com\">Vị trí ước tính theo DB-IP.com</a></p><p><a href=\"http://localhost:5216/Account\">Tài khoản</a> · <a href=\"http://localhost:5216/Account#protective-lock\">Khóa tài khoản</a></p>"));
        File.WriteAllText(Path.Combine(dir, "email-ma-dang-nhap.html"), EmailLayout.Wrap("Mã đăng nhập MindSprint AI", "<p>Mã đăng nhập: <b>482913</b>. Mã hết hạn sau 5 phút, chỉ dùng một lần.</p><p>Nếu không phải bạn, hãy bỏ qua email này.</p>"));
        Assert.True(File.Exists(Path.Combine(dir, "email-xac-minh.html")));
    }
}
