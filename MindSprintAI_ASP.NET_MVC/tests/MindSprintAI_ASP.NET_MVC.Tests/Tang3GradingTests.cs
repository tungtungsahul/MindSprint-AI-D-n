using System.Text.Json;
using MindSprintAI_ASP.NET_MVC.Data.Entities;
using MindSprintAI_ASP.NET_MVC.Services;

namespace MindSprintAI_ASP.NET_MVC.Tests;

// Không dùng fixture SQL: các quy tắc chấm phải kiểm được độc lập.
public class Tang3GradingTests
{
    [Theory]
    [InlineData("2.5", "2.5"), InlineData("2,5", "2.5"), InlineData("1,234", "1.234")]
    [InlineData("-3", "-3"), InlineData("+5", "5"), InlineData("007", "7")]
    [InlineData(".5", "0.5"), InlineData("5.", "5"), InlineData("0", "0"), InlineData(" −3 ", "-3")]
    public void DocSo(string input, string expected) =>
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), NumberParser.Parse(input));

    [Theory]
    [InlineData("1.234,5", "Số không hợp lệ"), InlineData("1 000", "Số không hợp lệ")]
    [InlineData("1.2.3", "Số không hợp lệ"), InlineData("2.5 m", "Chỉ nhập số")]
    [InlineData("5/2", "Chỉ nhập số"), InlineData("1e3", "Chỉ nhập số"), InlineData("2,5%", "Chỉ nhập số")]
    public void LoiDocSo(string input, string error) => Assert.Equal(error, Assert.Throws<BusinessException>(() => NumberParser.Parse(input)).Message);

    [Theory]
    [InlineData("3,13", true), InlineData("3.15", true), InlineData("3.12", false)]
    public void DungSaiTinhCaBien(string value, bool correct)
    {
        var q = TextQuestion() with { Type = QuestionType.NumberInput, NumberAnswer = 3.14m, NumberTolerance = .01m };
        Assert.Equal(correct, new NumberInputGrader().Grade(q, JsonSerializer.Serialize(new NumberAnswer(value))).IsCorrect);
    }

    internal static QuestionSnapshot TextQuestion(params string[][] answers) => new(1, 0, QuestionType.TextInput, "Đề", "Giải thích", 2, [])
    { Blanks = answers.Select(a => new BlankSnapshot(a)).ToList() };

    private static GradeResult Grade(QuestionSnapshot q, params string[] values) => new TextInputGrader().Grade(q, JsonSerializer.Serialize(new TextAnswer(values)));

    [Fact]
    public void ChuanHoaVaDapAnThayThe()
    {
        var q = TextQuestion(["Hà Nội", "Thủ đô"]);
        Assert.True(Grade(q, "  ha\u0300\u00a0\tNội  ").IsCorrect);
        Assert.True(Grade(q, "thủ đô").IsCorrect);
        Assert.False(Grade(q with { CaseSensitive = true }, "hà nội").IsCorrect);
        Assert.False(Grade(q, "Ha Noi").IsCorrect);
        Assert.True(Grade(TextQuestion(["Đà Nẵng"]) with { AcceptNoDiacritics = true }, "da nang").IsCorrect);
    }

    [Fact]
    public void DauCauKhongXoaKyHieu()
    {
        var q = TextQuestion(["C#"], ["TP HCM"]) with { IgnorePunctuation = true };
        Assert.True(Grade(q, "C#", "TP.HCM!").IsCorrect);
        Assert.False(Grade(q, "C", "TP HCM").IsCorrect);
        Assert.False(Grade(q with { IgnorePunctuation = false }, "C#", "TP.HCM").IsCorrect);
    }

    [Fact]
    public void GhepCapToiDaKhongThamLamVaKhongDungHaiLan()
    {
        var q = TextQuestion(["Lào", "Thái"], ["Lào"]) with { AcceptAnyBlankOrder = true, BlankScoring = BlankScoring.PerBlank };
        Assert.True(Grade(q, "Lào", "Thái").IsCorrect);
        var countries = TextQuestion(["Lào"], ["Thái"]) with { AcceptAnyBlankOrder = true, BlankScoring = BlankScoring.PerBlank };
        Assert.Equal(1m, Grade(countries, "Lào", "Lào").Score);
        Assert.False(Grade(countries with { AcceptAnyBlankOrder = false }, "Thái", "Lào").IsCorrect);
    }

    [Fact]
    public void ChiaDiemKhongLamTronVaChanOTrong()
    {
        var q = TextQuestion(["a"], ["b"], ["c"]) with { Points = 1, BlankScoring = BlankScoring.PerBlank };
        Assert.Equal(1m / 3, Grade(q, "a", "x", "x").Score);
        Assert.Equal(0, Grade(q with { BlankScoring = BlankScoring.AllOrNothing }, "a", "x", "x").Score);
        Assert.Contains("Chỗ trống 2 chưa điền.", Assert.Throws<BusinessException>(() => Grade(q, "a", " \t", "c")).Message);
    }
}
