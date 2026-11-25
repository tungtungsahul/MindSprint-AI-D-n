using System.Globalization;
using MindSprintAI_ASP.NET_MVC.Data.Entities;

namespace MindSprintAI_ASP.NET_MVC.Services;

// Lưu điểm gốc, chỉ quy đổi và làm tròn một lần khi hiển thị (64C, 65C).
public static class ScoreFormatter
{
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

    public static decimal Scale(decimal raw, decimal max, ScoreScale scale) => scale switch
    {
        ScoreScale.Original => raw,
        _ when max <= 0 => 0,
        ScoreScale.Hundred => raw / max * 100,
        _ => raw / max * 10
    };

    public static decimal Round(decimal value, RoundingMode mode) => mode switch
    {
        RoundingMode.OneDecimal => Math.Round(value, 1, MidpointRounding.AwayFromZero),
        RoundingMode.Quarter => Math.Round(value * 4, MidpointRounding.AwayFromZero) / 4,
        RoundingMode.Half => Math.Round(value * 2, MidpointRounding.AwayFromZero) / 2,
        _ => Math.Round(value, 2, MidpointRounding.AwayFromZero)
    };

    // Ví dụ 11/15, thang 10, 2 chữ số → "7,33 (11/15)".
    public static string Format(decimal raw, decimal max, ScoreScale scale, RoundingMode mode)
    {
        if (max <= 0) return "Không chấm điểm";
        var digits = mode is RoundingMode.OneDecimal or RoundingMode.Half ? "0.0" : "0.00";
        var rawText = $"{Round(raw, RoundingMode.TwoDecimals).ToString("0.##", Vi)}/{max.ToString("0.##", Vi)}";
        if (scale == ScoreScale.Original) return rawText;
        return $"{Round(Scale(raw, max, scale), mode).ToString(digits, Vi)} ({rawText})";
    }
}
