using Microsoft.AspNetCore.Razor.TagHelpers;

namespace MindSprintAI_ASP.NET_MVC.TagHelpers;

/// <summary>&lt;icon name="plus" /&gt; → icon Lucide trong Views/Shared/_Icons.cshtml. Icon chỉ để trang trí (aria-hidden); chữ của nút vẫn là tên nút.</summary>
[HtmlTargetElement("icon", TagStructure = TagStructure.WithoutEndTag)]
public class IconTagHelper : TagHelper
{
    public string Name { get; set; } = "";

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "svg";
        output.TagMode = TagMode.StartTagAndEndTag;
        var extra = output.Attributes.TryGetAttribute("class", out var c) ? " " + c.Value : "";
        output.Attributes.SetAttribute("class", "icon" + extra);
        output.Attributes.SetAttribute("aria-hidden", "true");
        output.Attributes.SetAttribute("focusable", "false");
        // Cỡ mặc định khi CSS chưa nạp (mạng chậm, lỗi tải); CSS .icon vẫn quyết định cỡ thật.
        output.Attributes.SetAttribute("width", "20");
        output.Attributes.SetAttribute("height", "20");
        output.Content.SetHtmlContent($"<use href=\"#i-{System.Net.WebUtility.HtmlEncode(Name)}\"></use>");
    }
}
