using System.Net;
using System.Text.Json;
using MindSprintAI_ASP.NET_MVC.Controllers;

namespace MindSprintAI_ASP.NET_MVC.Tests;

// Trang Cài đặt + PWA + đăng nhập demo chuyển từ MindSprint-AI-D-n.
public class CaiDatPwaDemoTests
{
    private static string Read(string path) => File.ReadAllText(Path.Combine(Tang8PureTests.Root, path));

    [Theory]
    [InlineData(true, "127.0.0.1", true)]
    [InlineData(true, "::1", true)]
    [InlineData(true, "203.0.113.5", false)]   // máy khác dù đang chạy thử
    [InlineData(false, "127.0.0.1", false)]    // bản thật không bao giờ có demo
    public void DemoChiTrenMayDangChayThu(bool development, string address, bool allowed) =>
        Assert.Equal(allowed, AccountController.CanUseDemo(development, IPAddress.Parse(address)));

    [Fact]
    public void ServiceWorkerKhongLuuTrangCaNhan()
    {
        var sw = Read("wwwroot/sw.js");
        Assert.Contains("request.mode === 'navigate'", sw);
        Assert.Contains("fetch(request).catch(() => caches.match(OFFLINE))", sw); // trang luôn lấy từ mạng
        Assert.Matches(@"\^\\/\(css\|js\|lib\|img\)\\/", sw);                       // chỉ lưu đệm tệp tĩnh
        Assert.Contains("if (request.method !== 'GET') return;", sw);
        using var manifest = JsonDocument.Parse(Read("wwwroot/manifest.webmanifest"));
        foreach (var icon in manifest.RootElement.GetProperty("icons").EnumerateArray())
        {
            var file = Path.Combine(Tang8PureTests.Root, "wwwroot", icon.GetProperty("src").GetString()!.TrimStart('/'));
            Assert.InRange(new FileInfo(file).Length, 1, 100 * 1024);
        }
        Assert.Contains("manifest.webmanifest", Read("Views/Shared/_Layout.cshtml"));
    }

    [Fact]
    public void CaiDatCoBaMucNhuNhom()
    {
        var view = Read("Views/Settings/Index.cshtml");
        foreach (var text in new[] { "Cài ứng dụng lên máy", "Giao diện sáng / tối", "Chuông báo giờ học", "id=\"alarm-style\"", "id=\"alarm-toggle\"", "data-theme-toggle" })
            Assert.Contains(text, view);
    }
}
