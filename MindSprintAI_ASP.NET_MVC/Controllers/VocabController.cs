using Microsoft.AspNetCore.Mvc;
using MindSprintAI_ASP.NET_MVC.Services;

namespace MindSprintAI_ASP.NET_MVC.Controllers;

public record VocabIndexPage(IReadOnlyDictionary<string, string> Categories, IReadOnlyList<VocabTopic> Topics, string? Category);
public record VocabTopicPage(string Category, string CategoryName, string Topic, IReadOnlyList<VocabCard> Cards);
public record VocabQuestion(string Word, string Meaning, string? Hint, string Topic, List<string> Options);
public record VocabPracticePage(IReadOnlyDictionary<string, string> Categories, IReadOnlyList<VocabTopic> Topics, string? Category, string? Topic,
    string Mode, int Count, List<VocabQuestion>? Deck);

// Thư viện từ vựng hệ thống + game ghép thẻ của nhóm (cần đăng nhập như mọi trang ứng dụng; khách vào bằng mã bị NotGuestFilter chặn).
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class VocabController(VocabCatalog catalog, VocabLibraryService library) : AppController
{
    public IActionResult Index(string? category) =>
        View(new VocabIndexPage(catalog.Categories, catalog.Topics.Where(t => category == null || t.Category == category).ToList(),
            category != null && catalog.Categories.ContainsKey(category) ? category : null));

    public IActionResult Topic(string category, string topic) => View(Page(category, topic));

    // pick = "danh mục|chủ đề" từ ô chọn của trang game (form GET, chạy cả khi không có JS).
    public IActionResult Game(string? category, string? topic, string? pick)
    {
        if (pick?.Split('|', 2) is [var c, var t]) (category, topic) = (c, t);
        return View(category == null || topic == null ? null : Page(category, topic));
    }

    // "Trắc nghiệm & Tự luận" ở trang Luyện tập của nhóm (startQuiz): chọn danh mục, chủ đề, chế độ, số câu → bộ câu ngẫu nhiên.
    // Máy chủ chọn câu + 3 đáp án nhiễu cùng danh mục; trình duyệt chấm (ôn tập tự do, không lưu điểm như nhóm).
    public IActionResult Practice(string? category, string? pick, string mode = "choice", int count = 10)
    {
        string? topic = null;
        if (pick?.Split('|', 2) is [var c, var t]) (category, topic) = (c, string.IsNullOrEmpty(t) ? null : t);
        if (category == null) return View(new VocabPracticePage(catalog.Categories, catalog.Topics, null, null, mode, count, null));
        var pool = catalog.Cards.Where(x => (category == "all" || x.Category == category) && (topic == null || x.Topic == topic)).ToList();
        if (pool.Count == 0) throw new BusinessException("Không tìm thấy từ vựng nào phù hợp trong danh mục này để ôn tập.");
        var deck = pool.OrderBy(_ => Random.Shared.Next()).Take(Math.Clamp(count, 1, 50)).Select(card =>
        {
            var wrong = catalog.Cards.Where(x => x.Answer != card.Answer).OrderBy(x => x.Category == card.Category ? 0 : 1).ThenBy(_ => Random.Shared.Next())
                .Select(x => x.Answer).Distinct().Take(3); // nhiễu cùng danh mục trước (khó hơn), thiếu thì lấy danh mục khác

            return new VocabQuestion(card.Question, card.Answer, card.Example, card.Topic, wrong.Append(card.Answer).OrderBy(_ => Random.Shared.Next()).ToList());
        }).ToList();
        return View(new VocabPracticePage(catalog.Categories, catalog.Topics, category, topic, mode == "write" ? "write" : "choice", count, deck));
    }

    [HttpPost]
    public async Task<IActionResult> Copy(string category, string topic)
    {
        var set = await library.CopyTopicAsync(category, topic, UserId);
        Success($"Đã lưu \"{set.Title}\" vào Thư viện của bạn. Bạn có thể ôn thẻ, tạo quiz hoặc giao cho lớp.");
        return RedirectToAction("Details", "StudySets", new { id = set.Id });
    }

    private VocabTopicPage Page(string category, string topic) =>
        new(category, catalog.Categories.GetValueOrDefault(category) ?? "", topic, catalog.TopicCards(category, topic));
}
