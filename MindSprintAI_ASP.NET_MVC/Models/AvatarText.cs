using System.Text;

namespace MindSprintAI_ASP.NET_MVC.Models;

public static class AvatarText
{
    public static string Initials(string? name)
    {
        var words = (name ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return "K";
        string First(string word) => char.ToUpperInvariant(word.Normalize(NormalizationForm.FormD)[0]) switch
        {
            'Đ' => "D",
            var letter => letter.ToString()
        };
        return First(words[0]) + (words.Length > 1 ? First(words[^1]) : "");
    }

    public static string Tint(string? name)
    {
        uint hash = 0;
        foreach (var letter in (name ?? "").Trim()) hash = unchecked(hash * 31 + letter);
        return (hash % 4) switch
        {
            0 => "var(--acid)", 1 => "var(--pink)", 2 => "var(--blue)", _ => "var(--warn)"
        };
    }
}
