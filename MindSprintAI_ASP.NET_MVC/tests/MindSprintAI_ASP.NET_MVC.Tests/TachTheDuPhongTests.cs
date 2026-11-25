using Microsoft.AspNetCore.DataProtection;
using MindSprintAI_ASP.NET_MVC.Services;

namespace MindSprintAI_ASP.NET_MVC.Tests;

// Tách thẻ dự phòng khi AI không dùng được — quy tắc của MindSprint-AI-D-n extractFallbackCards.
[Collection("db")]
public class TachTheDuPhongTests
{
    [Fact]
    public void DongHaiChamVaGachNgang_BoNhieu_BoTrung()
    {
        var cards = FallbackCards.Extract("""
            Bài tập 1: tính diện tích hình tròn
            - Ti thể: bào quan tạo năng lượng cho tế bào
            2. Lục lạp – nơi diễn ra quang hợp
            Ti thể: lặp lại thì bỏ qua
            Trang 3: mục lục
            """, 10);
        Assert.Equal([new("Ti thể", "bào quan tạo năng lượng cho tế bào"), new("Lục lạp", "nơi diễn ra quang hợp")], cards.Take(2));
        Assert.DoesNotContain(cards, c => c.Front.StartsWith("Bài tập") || c.Front.StartsWith("Trang"));
        Assert.Single(cards, c => c.Front == "Ti thể");
    }

    [Fact]
    public void CauDinhNghia_CongThuc_DieuKien_VaGioiHanSoThe()
    {
        var cards = FallbackCards.Extract("Ta gọi phần tử nhỏ nhất của vật chất là nguyên tử. Ribôxôm là nơi tổng hợp prôtêin trong tế bào. " +
            "Diện tích hình vuông bằng a*a. Nếu nhiệt độ tăng, áp suất khí trong bình kín tăng theo.", 10);
        Assert.Contains(new FallbackCard("Khái niệm: nguyên tử là gì?", "phần tử nhỏ nhất của vật chất"), cards);
        Assert.Contains(cards, c => c.Front == "Ribôxôm là gì?" && c.Back.StartsWith("nơi tổng hợp prôtêin"));
        Assert.Contains(cards, c => c.Front.StartsWith("Công thức tính") && c.Back == "$$a*a$$");
        Assert.Contains(cards, c => c.Front.StartsWith("Nếu nhiệt độ tăng") && c.Back.StartsWith("áp suất khí"));
        Assert.Single(FallbackCards.Extract("A1: định nghĩa một\nA2: định nghĩa hai\nA3: định nghĩa ba", 1));
        Assert.Empty(FallbackCards.Extract("   ", 5));
    }

    [Fact]
    public async Task ChuaCoKey_TheGhiNho_TachTuDong_TracNghiemVanBaoLoi()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(1);
        var ai = new AiGenerateService(new ExcelImportService(h.Db, h.Library, h.Clock), new UserApiKeyService(h.Db, new EphemeralDataProtectionProvider(), h.Clock), null!);
        const string text = """
            Ti thể: bào quan tạo năng lượng cho tế bào
            Lục lạp: nơi diễn ra quang hợp ở tế bào thực vật
            Ribôxôm: nơi tổng hợp prôtêin
            """;
        var preview = await ai.GenerateAsync(0, text, 10, cards: true, s.Student, "Sinh học");
        Assert.Equal(("Thẻ ghi nhớ tách tự động", 3, "Sinh học"), (preview.FileName, preview.Rows.Count(r => r.Error == null), preview.NewSetTitle));
        Assert.StartsWith("Chưa dùng được AI (", preview.Notice);
        await Assert.ThrowsAsync<BusinessException>(() => ai.GenerateAsync(0, text, 10, cards: false, s.Student, "Sinh học"));
        Assert.Equal(0, h.Db.StudySets.Count(x => x.Title == "Sinh học")); // bản nháp: chưa tạo bộ học nào
    }
}
