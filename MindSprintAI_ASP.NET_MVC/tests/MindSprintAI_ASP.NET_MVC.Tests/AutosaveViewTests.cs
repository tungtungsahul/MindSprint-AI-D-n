using System.Text.RegularExpressions;

namespace MindSprintAI_ASP.NET_MVC.Tests;

public class AutosaveViewTests
{
    private static string Read(string path) => File.ReadAllText(Path.Combine(Tang8PureTests.Root, path));

    [Fact]
    public void MoiFormTuLuuChiCoLuaChonAnToanVaConNutDuPhong()
    {
        var forms = Directory.GetFiles(Path.Combine(Tang8PureTests.Root, "Views"), "*.cshtml", SearchOption.AllDirectories)
            .SelectMany(file => Regex.Matches(File.ReadAllText(file), @"<form\b[^>]*>[\s\S]*?</form>").Select(m => (file, html: m.Value)))
            .Where(f => Regex.IsMatch(f.html.Split('>')[0], @"\bdata-autosave(?:\s|=|$)")).ToList();
        Assert.Equal(13, forms.Count); // +1: "Hiện ảnh Google" (09/11); +1: công tắc tự ôn sau hạn (10/11); +1: ôn thẻ khi mất mạng (15/11); +1: bật/tắt xếp hạng lớp (17/11)
        foreach (var (file, html) in forms)
        {
            var expanded = html;
            foreach (Match partial in Regex.Matches(html, "<partial\\b[^>]*name=\"([^\"]+)\""))
            {
                var name = partial.Groups[1].Value + ".cshtml";
                var local = Path.Combine(Path.GetDirectoryName(file)!, name);
                expanded += File.ReadAllText(File.Exists(local) ? local : Path.Combine(Tang8PureTests.Root, "Views/Shared", name));
            }
            Assert.DoesNotContain("data-confirm", expanded);
            Assert.DoesNotContain("<textarea", expanded);
            foreach (Match input in Regex.Matches(expanded, @"<input\b[^>]*>"))
                Assert.Matches("type=\"(?:hidden|checkbox|radio)\"", input.Value);
            Assert.Contains("method=\"post\"", html);
            Assert.Contains("<button", html);
            Assert.DoesNotMatch(@"<button\b[^>]*\bhidden(?:\s|>|=)", html);
        }
    }

    [Fact]
    public void FormTronVaFormNguyHiemGiuNut()
    {
        var view = Read("Views/Assignments/Details.cshtml");
        foreach (var action in new[] { "ChangeDeviceSettings", "ChangeMastery", "ChangeCriticalSettings", "PublishOpinion" })
        {
            var form = Assert.Single(Regex.Matches(view, @"<form\b[^>]*>[\s\S]*?</form>").Select(m => m.Value), f => f.Contains($"asp-action=\"{action}\""));
            Assert.DoesNotContain("data-autosave", form);
            Assert.Contains("<button", form);
        }
        Assert.Contains("data-confirm", Assert.Single(Regex.Matches(view, @"<form\b[^>]*>[\s\S]*?</form>").Select(m => m.Value), f => f.Contains("asp-action=\"PublishOpinion\"")));
    }

    [Fact]
    public void ScriptCoPhanHoiLoiKhoiPhucGiaTriVaHoTroDocManHinh()
    {
        var script = Read("wwwroot/js/autosave.js");
        foreach (var text in new[] { "aria-live", "polite", "role", "status", "catch", "restore(saved)", "new FormData(form)", "same-origin", "response.ok", ".alert-danger", ".validation-summary-errors", ".field-validation-error", "textContent", "Thử lại", "400", "2000", "data-confirm", "submitter.hidden = true" })
            Assert.Contains(text, script);
        Assert.Matches("<script[^>]*src=\"~/js/autosave.js\"[^>]*defer", Read("Views/Shared/_Layout.cshtml"));
    }
}
