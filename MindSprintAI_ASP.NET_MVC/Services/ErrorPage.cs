using System.Net;

namespace MindSprintAI_ASP.NET_MVC.Services;

// (NK1, G0) Trang lỗi khi có lỗi chưa xử lý: viết thẳng ra, KHÔNG chạy lại pipeline.
// Chạy lại /Home/Error cần cơ sở dữ liệu (kiểm tra phiên, menu, số thông báo); khi chính
// cơ sở dữ liệu sập thì trang lỗi cũng sập và người dùng mất mã tra cứu.
public static class ErrorPage
{
    public static string Html(string requestId) =>
        "<!DOCTYPE html><html lang=\"vi\"><head><meta charset=\"utf-8\" />"
        + "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\" />"
        + $"<title>Có lỗi xảy ra - {WebUtility.HtmlEncode(Brand.Name)}</title>"
        + "<link rel=\"stylesheet\" href=\"/lib/bootstrap/dist/css/bootstrap.min.css\" /></head>"
        + "<body><main class=\"container py-4\" style=\"max-width:40rem\">"
        + $"<p><a href=\"/\" class=\"fw-bold text-decoration-none\">{WebUtility.HtmlEncode(Brand.Name)}</a></p>"
        + "<h1 class=\"h3 text-danger\">Có lỗi xảy ra.</h1>"
        + "<p class=\"text-danger\">Chưa thực hiện được thao tác của bạn.</p>"
        + $"<p><strong>Mã tra cứu lỗi:</strong> <code>{WebUtility.HtmlEncode(requestId)}</code></p>"
        + "<p>Hãy thử lại. Nếu vẫn lỗi, gửi mã tra cứu lỗi ở trên cho quản trị viên.</p>"
        + "<p><a href=\"/\">Về trang chủ</a></p></main></body></html>";

    // Gõ sai địa chỉ trang (issue #108 của nhóm: đường dẫn thiếu trả lỗi phù hợp). Trước đây là 404 rỗng: trình duyệt hiện
    // trang trắng, Chrome có lúc tưởng là tệp tải về. Cũng viết thẳng, không cần DB như trang lỗi ở trên.
    public static readonly string NotFoundHtml =
        "<!DOCTYPE html><html lang=\"vi\"><head><meta charset=\"utf-8\" />"
        + "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\" />"
        + $"<title>Không tìm thấy trang - {WebUtility.HtmlEncode(Brand.Name)}</title>"
        + "<link rel=\"stylesheet\" href=\"/lib/bootstrap/dist/css/bootstrap.min.css\" /></head>"
        + "<body><main class=\"container py-4\" style=\"max-width:40rem\">"
        + $"<p><a href=\"/\" class=\"fw-bold text-decoration-none\">{WebUtility.HtmlEncode(Brand.Name)}</a></p>"
        + "<h1 class=\"h3\">Không tìm thấy trang này.</h1>"
        + "<p>Địa chỉ có thể đã gõ sai, hoặc trang đã bị xóa.</p>"
        + "<p><a href=\"/\">Về trang chủ</a></p></main></body></html>";

    // Kiểm thử 19/11: khu quản trị trả 403 rỗng cho người không phải quản trị (Chrome coi là tệp tải về) → cùng cách như 404.
    public static readonly string ForbiddenHtml =
        "<!DOCTYPE html><html lang=\"vi\"><head><meta charset=\"utf-8\" />"
        + "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\" />"
        + $"<title>Không có quyền - {WebUtility.HtmlEncode(Brand.Name)}</title>"
        + "<link rel=\"stylesheet\" href=\"/lib/bootstrap/dist/css/bootstrap.min.css\" /></head>"
        + "<body><main class=\"container py-4\" style=\"max-width:40rem\">"
        + $"<p><a href=\"/\" class=\"fw-bold text-decoration-none\">{WebUtility.HtmlEncode(Brand.Name)}</a></p>"
        + "<h1 class=\"h3\">Bạn không có quyền xem trang này.</h1>"
        + "<p>Trang này chỉ dành cho người có vai trò phù hợp, ví dụ quản trị viên hoặc giảng viên của lớp.</p>"
        + "<p><a href=\"/\">Về trang chủ</a></p></main></body></html>";

    // Chỉ cho trình duyệt mở trang (GET, nhận HTML), không cho tệp tĩnh hay lời gọi fetch/JSON: những thứ đó giữ 403/404 trơn.
    public static bool WantsStatusPage(HttpContext context) =>
        context.Response.StatusCode is StatusCodes.Status404NotFound or StatusCodes.Status403Forbidden
        && (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method))
        && context.Request.Headers.Accept.ToString().Contains("text/html", StringComparison.OrdinalIgnoreCase)
        && !new[] { "/css", "/js", "/lib", "/img", "/hubs" }.Any(p => context.Request.Path.StartsWithSegments(p));

    public static async Task WriteStatusPageAsync(HttpContext context)
    {
        context.Response.ContentType = "text/html; charset=utf-8";
        await context.Response.WriteAsync(context.Response.StatusCode == StatusCodes.Status403Forbidden ? ForbiddenHtml : NotFoundHtml);
    }

    public static async Task WriteAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.Headers.CacheControl = "no-store";
        await context.Response.WriteAsync(Html(AuditLog.RequestId(context)));
    }
}
