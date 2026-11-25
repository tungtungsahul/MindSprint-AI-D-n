using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using MindSprintAI_ASP.NET_MVC.Services;

namespace MindSprintAI_ASP.NET_MVC.Tests;

// Thử tải A4 (14/11/2026): trang làm bài có mã chống giả mạo đã cũ → mọi thao tác bị 400 trống, trang chỉ báo "hãy thử lại".
public class TrangCuTests
{
    private static ResultExecutingContext Run(IActionResult result, string? header = null, string? accept = null)
    {
        var http = new DefaultHttpContext();
        http.Request.Method = "POST"; http.Request.Path = "/Learn/Flag/1";
        if (header != null) http.Request.Headers["RequestVerificationToken"] = header;
        if (accept != null) http.Request.Headers.Accept = accept;
        var context = new ResultExecutingContext(new ActionContext(http, new RouteData(), new ActionDescriptor()), [], result, new object());
        new StaleFormFilter().OnResultExecuting(context);
        return context;
    }

    [Theory]
    [InlineData("cu", null)]                 // play.js: fetch kèm header mã
    [InlineData(null, "application/json")]   // screen-monitor.js: gửi form, đòi JSON
    public void MaCuGuiTuTrang_NhanJsonTaiLai(string? header, string? accept)
    {
        var context = Run(new AntiforgeryValidationFailedResult(), header, accept);
        var json = Assert.IsType<BadRequestObjectResult>(context.Result);
        var body = System.Text.Json.JsonSerializer.Serialize(json.Value,
            new System.Text.Json.JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        Assert.Contains("\"reload\":true", body);
        Assert.Contains("tải lại trang", body);
    }

    // "Mở câu" xin HTML: lỗi nghiệp vụ (kể cả "Hệ thống đang bận") phải về JSON, không chuyển hướng về trang trước.
    [Fact]
    public void LoiNghiepVuTuFetchCoHeaderMa_TraJsonDuXinHtml()
    {
        var http = new DefaultHttpContext();
        http.Request.Method = "POST"; http.Request.Headers.Accept = "text/html"; http.Request.Headers["RequestVerificationToken"] = "x";
        var context = new ExceptionContext(new ActionContext(http, new RouteData(), new ActionDescriptor()), [])
            { Exception = new BusinessException(BusinessExceptionFilter.BusyMessage) };
        new BusinessExceptionFilter(null!).OnException(context);
        var json = Assert.IsType<BadRequestObjectResult>(context.Result);
        Assert.Contains("bận", System.Text.Json.JsonSerializer.Serialize(json.Value,
            new System.Text.Json.JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        Assert.False(BusinessExceptionFilter.IsBusy(new InvalidOperationException("khác")));
    }

    [Fact]
    public void FormThuongVaKetQuaKhac_GiuNguyen()
    {
        Assert.IsType<AntiforgeryValidationFailedResult>(Run(new AntiforgeryValidationFailedResult()).Result);
        var ok = new OkResult();
        Assert.Same(ok, Run(ok, header: "x").Result);
    }
}
