using Microsoft.EntityFrameworkCore;
using MindSprintAI_ASP.NET_MVC.Data.Entities;
using MindSprintAI_ASP.NET_MVC.Services;

namespace MindSprintAI_ASP.NET_MVC.Tests;

// Thư viện từ vựng hệ thống chuyển từ MindSprint-AI-D-n (vocab_data.js + defaultFlashcards).
[Collection("db")]
public class TuVungTests
{
    private static readonly VocabCatalog Catalog = new(File.ReadAllText(Path.Combine(Tang8PureTests.Root, "Data/Vocab/vocab.json")));

    [Fact]
    public void DuDuLieuNhomVaChuDe()
    {
        Assert.Equal(2572, Catalog.Cards.Count);
        Assert.Equal(["english", "programming", "general"], Catalog.Categories.Keys);
        Assert.Contains(Catalog.Topics, t => t.Topic == "Đồ dùng học tập" && t.CategoryName == "Tiếng Anh" && t.Count > 0);
        Assert.All(Catalog.Cards, c => { Assert.False(string.IsNullOrWhiteSpace(c.Question)); Assert.False(string.IsNullOrWhiteSpace(c.Answer)); });
        Assert.Equal(Catalog.Cards.Count, Catalog.Topics.Sum(t => t.Count));
        Assert.Throws<BusinessException>(() => Catalog.TopicCards("english", "Không có chủ đề này"));
    }

    [Theory]
    [InlineData("english|Đồ dùng học tập", 10)]
    [InlineData("all|", 5)]
    [InlineData("general|", 3)] // danh mục chỉ 1 thẻ: vẫn đủ 4 đáp án nhờ lấy nhiễu danh mục khác
    public void TracNghiemTuVungDuBonDapAnCoDapAnDung(string pick, int count)
    {
        var controller = new MindSprintAI_ASP.NET_MVC.Controllers.VocabController(Catalog, null!);
        var page = Assert.IsType<MindSprintAI_ASP.NET_MVC.Controllers.VocabPracticePage>(
            Assert.IsType<Microsoft.AspNetCore.Mvc.ViewResult>(controller.Practice(null, pick, "choice", count)).Model);
        Assert.InRange(page.Deck!.Count, 1, count);
        Assert.All(page.Deck, q => { Assert.Equal(4, q.Options.Distinct().Count()); Assert.Contains(q.Meaning, q.Options); });
        Assert.Null(Assert.IsType<Microsoft.AspNetCore.Mvc.ViewResult>(controller.Practice(null, null)).Model is MindSprintAI_ASP.NET_MVC.Controllers.VocabPracticePage p ? p.Deck : null);
    }

    [Fact]
    public async Task LuuChuDeThanhBoHocRiengOnLapLaiNganQuang()
    {
        using var h = new Harness();
        var u = await h.UserAsync("Minh");
        var service = new VocabLibraryService(h.Db, Catalog, h.Library, h.Clock);
        var set = await service.CopyTopicAsync("english", "Đồ dùng học tập", u);
        var expected = Catalog.TopicCards("english", "Đồ dùng học tập");
        Assert.Equal(("Đồ dùng học tập (Tiếng Anh)", u, FlashcardMode.SpacedRepetition), (set.Title, set.OwnerId, set.FlashcardMode));
        var cards = await h.Db.Flashcards.Where(c => c.StudySetId == set.Id).OrderBy(c => c.OrderIndex).ToListAsync();
        Assert.Equal(expected.Select(c => c.Question), cards.Select(c => c.Front));
        Assert.StartsWith(expected[0].Answer, cards[0].Back);
        Assert.Equal(Enumerable.Range(0, cards.Count), cards.Select(c => c.OrderIndex));
        // Lưu hai lần = hai bộ riêng (người dùng có thể đã sửa bộ đầu); chủ đề sai không tạo bộ rỗng.
        await service.CopyTopicAsync("english", "Đồ dùng học tập", u);
        await Assert.ThrowsAsync<BusinessException>(() => service.CopyTopicAsync("english", "Không có", u));
        Assert.Equal(2, await h.Db.StudySets.CountAsync(s => s.OwnerId == u));
    }
}
