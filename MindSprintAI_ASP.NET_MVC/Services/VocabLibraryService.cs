using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using MindSprintAI_ASP.NET_MVC.Data;
using MindSprintAI_ASP.NET_MVC.Data.Entities;

namespace MindSprintAI_ASP.NET_MVC.Services;

public record VocabCard([property: JsonPropertyName("c")] string Category, [property: JsonPropertyName("s")] string Topic,
    [property: JsonPropertyName("q")] string Question, [property: JsonPropertyName("a")] string Answer, [property: JsonPropertyName("e")] string? Example);
public record VocabTopic(string Category, string CategoryName, string Topic, int Count);

// Thư viện từ vựng hệ thống của nhóm (frontend/vocab_data.js: 3000 từ Oxford theo chủ đề + thẻ mặc định), chỉ đọc.
// Data/Vocab/vocab.json trích bằng tools/vocab/extract-vocab.cjs. Nạp một lần khi khởi động (≈ 270 KB).
public sealed class VocabCatalog
{
    private sealed record File(Dictionary<string, string> Categories, List<VocabCard> Cards);
    public IReadOnlyDictionary<string, string> Categories { get; }
    public IReadOnlyList<VocabCard> Cards { get; }
    public IReadOnlyList<VocabTopic> Topics { get; }

    public VocabCatalog(IWebHostEnvironment env) : this(System.IO.File.ReadAllText(Path.Combine(env.ContentRootPath, "Data", "Vocab", "vocab.json"))) { }

    public VocabCatalog(string json)
    {
        var file = JsonSerializer.Deserialize<File>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Categories = file.Categories;
        Cards = file.Cards;
        Topics = Cards.GroupBy(c => (c.Category, c.Topic)).Select(g => new VocabTopic(g.Key.Category, Categories[g.Key.Category], g.Key.Topic, g.Count()))
            .OrderBy(t => t.Category == "english" ? 0 : t.Category == "programming" ? 1 : 2).ThenBy(t => t.Topic, StringComparer.Create(new("vi"), true)).ToList();
    }

    public IReadOnlyList<VocabCard> TopicCards(string category, string topic) =>
        Cards.Where(c => c.Category == category && c.Topic == topic).ToList() is { Count: > 0 } list ? list : throw new BusinessException("Không tìm thấy chủ đề từ vựng.");
}

// "Lưu vào Thư viện của tôi": chép một chủ đề thành bộ học riêng (thẻ ghi nhớ) để dùng lại ôn lặp lại ngắt quãng, quiz, giao bài của QuizEdu.
public sealed class VocabLibraryService(AppDbContext db, VocabCatalog catalog, StudyLibraryService library, TimeProvider clock)
{
    public async Task<StudySet> CopyTopicAsync(string category, string topic, string userId)
    {
        var cards = catalog.TopicCards(category, topic);
        await using var tx = await db.Database.BeginTransactionAsync();
        var set = await library.CreateStudySetAsync($"{topic} ({catalog.Categories[category]})", userId);
        set.FlashcardMode = FlashcardMode.SpacedRepetition; // từ vựng tự học: ôn lặp lại ngắt quãng như thẻ của nhóm (SM-2)
        var now = clock.GetUtcNow().UtcDateTime;
        db.Flashcards.AddRange(cards.Select((c, i) => new Flashcard
        {
            StudySetId = set.Id, OrderIndex = i, CreatedAt = now, Front = c.Question, Back = c.Answer, Example = c.Example
        }));
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return set;
    }
}
