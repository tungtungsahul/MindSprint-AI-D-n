namespace MindSprintAI_ASP.NET_MVC.Models;

public record PagerModel(int Page, int TotalPages, Func<int, string> Url, string Label)
{
    public static IReadOnlyList<int?> Numbers(int page, int last)
    {
        var numbers = new[] { 1, last, page - 1, page, page + 1 }
            .Where(n => n >= 1 && n <= last).Distinct().Order().ToList();
        var result = new List<int?>();
        var previous = 0;
        foreach (var number in numbers)
        {
            if (previous > 0 && number - previous > 1) result.Add(null);
            result.Add(number);
            previous = number;
        }
        return result;
    }

    // Đường dẫn trang hiện tại, giữ mọi tham số khác và thay các tham số được truyền (vd. phân trang bảng xếp hạng không làm mất trang thành viên).
    public static string WithQuery(Microsoft.AspNetCore.Http.HttpContext http, string fragment, params (string Key, string Value)[] set)
    {
        var request = http.Request;
        var pairs = request.Query.Where(q => !set.Any(s => s.Key == q.Key))
            .SelectMany(q => q.Value.Select(v => new KeyValuePair<string, string?>(q.Key, v)))
            .Concat(set.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)));
        return Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString(request.Path, pairs) + "#" + fragment;
    }
}
