using Microsoft.AspNetCore.Http;
using MindSprintAI_ASP.NET_MVC.Services;
using Xunit;

namespace MindSprintAI_ASP.NET_MVC.Tests;

// (NK1) Trang lỗi phải hiện được cả khi cơ sở dữ liệu sập: không dùng DI, DB hay layout.
public class Tang8ErrorPageTests
{
    [Fact]
    public async Task TrangLoiVietTrucTiepCoMaTraCuuVaKhongCanDichVu()
    {
        var context = new DefaultHttpContext(); // không có RequestServices: chạm DB/DI là ném lỗi
        context.TraceIdentifier = "ma-<thu>";
        context.Response.Body = new MemoryStream();
        await ErrorPage.WriteAsync(context);
        context.Response.Body.Position = 0;
        var html = await new StreamReader(context.Response.Body).ReadToEndAsync();
        Assert.Equal(500, context.Response.StatusCode);
        Assert.Contains("Mã tra cứu lỗi", html);
        Assert.Contains("ma-&lt;thu&gt;", html); // mã được mã hóa HTML
        Assert.Contains("gửi mã tra cứu lỗi ở trên cho quản trị viên", html);
        Assert.False(PlainLanguageAudit.HasJargon(html));
    }

    // 404 khi mở trang: có trang báo (không DB/DI); tệp tĩnh, fetch/JSON, POST giữ 404 trơn.
    [Theory]
    [InlineData("GET", "/khong-co", "text/html,application/xhtml+xml", true)]
    [InlineData("GET", "/khong-co", "application/json", false)]
    [InlineData("GET", "/khong-co", "*/*", false)]
    [InlineData("POST", "/khong-co", "text/html", false)]
    [InlineData("GET", "/css/khong-co.css", "text/html", false)]
    [InlineData("GET", "/lib/a/b.js", "text/html", false)]
    public async Task TrangKhongTimThayChiChoTrinhDuyetMoTrang(string method, string path, string accept, bool page)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method; context.Request.Path = path; context.Request.Headers.Accept = accept;
        context.Response.StatusCode = 404;
        context.Response.Body = new MemoryStream();
        Assert.Equal(page, ErrorPage.WantsStatusPage(context));
        await ErrorPage.WriteStatusPageAsync(context);
        context.Response.Body.Position = 0;
        var html = await new StreamReader(context.Response.Body).ReadToEndAsync();
        Assert.Equal(404, context.Response.StatusCode);
        Assert.Contains("Không tìm thấy trang này.", html);
        Assert.StartsWith("text/html", context.Response.ContentType);
        Assert.False(PlainLanguageAudit.HasJargon(html));
    }

    // Kiểm thử 19/11: học viên mở /Admin nhận 403 rỗng (Chrome tưởng tệp tải về). Giờ có trang báo; fetch/JSON vẫn 403 trơn.
    [Theory]
    [InlineData("text/html,application/xhtml+xml", true)]
    [InlineData("application/json", false)]
    public async Task KhongCoQuyenCoTrangBaoChoTrinhDuyet(string accept, bool page)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "GET"; context.Request.Path = "/Admin"; context.Request.Headers.Accept = accept;
        context.Response.StatusCode = 403;
        context.Response.Body = new MemoryStream();
        Assert.Equal(page, ErrorPage.WantsStatusPage(context));
        await ErrorPage.WriteStatusPageAsync(context);
        context.Response.Body.Position = 0;
        var html = await new StreamReader(context.Response.Body).ReadToEndAsync();
        Assert.Equal(403, context.Response.StatusCode);
        Assert.Contains("Bạn không có quyền xem trang này.", html);
        Assert.DoesNotContain("Không tìm thấy", html);
        Assert.False(PlainLanguageAudit.HasJargon(html));
    }
}
