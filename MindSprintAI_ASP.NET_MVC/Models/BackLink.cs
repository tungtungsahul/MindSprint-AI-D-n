using Microsoft.AspNetCore.Http;

namespace MindSprintAI_ASP.NET_MVC.Models;

// Trang mở được từ nhiều nơi (lời mời, báo cáo vi phạm) quay về đúng trang vừa đứng.
// Chỉ nhận trang trước cùng máy chủ MindSprint AI; khác máy chủ, trùng chính trang này (gửi form lỗi) hoặc không có thì dùng trang mặc định.
public static class BackLink
{
    public static string Url(HttpRequest request, string fallback)
    {
        if (!Uri.TryCreate(request.Headers.Referer.ToString(), UriKind.Absolute, out var referer)) return fallback;
        var sameHost = string.Equals(referer.Scheme, request.Scheme, StringComparison.OrdinalIgnoreCase)
            && string.Equals(referer.Authority, request.Host.Value, StringComparison.OrdinalIgnoreCase);
        var path = referer.PathAndQuery;
        var samePage = string.Equals(referer.AbsolutePath, (request.PathBase + request.Path).Value, StringComparison.OrdinalIgnoreCase);
        return sameHost && !samePage && path.StartsWith('/') && !path.StartsWith("//") ? path : fallback;
    }
}
