using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using ClosedXML.Excel;

namespace MindSprintAI_ASP.NET_MVC.Services;

// Lấy chữ từ tệp Word/PowerPoint/Excel như NotebookDocumentExtractor của nhóm (DOCX, PPTX, XLSX), không thêm thư viện:
// DOCX/PPTX là tệp zip chứa XML (đoạn văn w:p / a:p, chữ w:t / a:t); XLSX đọc bằng ClosedXML đã có cho trang Nhập.
public static class OfficeText
{
    public static readonly string[] Types = [".docx", ".pptx", ".xlsx"];
    private const long MaxXmlBytes = 30 * 1024 * 1024; // chặn tệp zip "nở" bất thường

    public static string Extract(byte[] bytes, string ext)
    {
        try
        {
            if (ext == ".xlsx") return Sheets(bytes);
            using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
            var parts = ext == ".docx"
                ? zip.Entries.Where(e => e.FullName == "word/document.xml")
                : zip.Entries.Where(e => Regex.IsMatch(e.FullName, @"^ppt/slides/slide\d+\.xml$"))
                    .OrderBy(e => int.Parse(Regex.Match(e.FullName, @"\d+").Value));
            return string.Join("\n\n", parts.Select(e => Paragraphs(e, ext == ".docx" ? "w" : "a")));
        }
        catch (Exception ex) when (ex is not BusinessException) { throw new BusinessException("Không đọc được tệp này."); }
    }

    private static string Paragraphs(ZipArchiveEntry entry, string prefix)
    {
        if (entry.Length > MaxXmlBytes) throw new BusinessException("Tệp quá lớn để đọc.");
        var text = new StringBuilder();
        using var reader = XmlReader.Create(entry.Open(), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        var more = reader.Read();
        while (more)
        {
            var mine = reader.Prefix == prefix;
            if (mine && reader.NodeType == XmlNodeType.Element && reader.LocalName == "t")
            {
                text.Append(reader.ReadElementContentAsString()); // đã tự sang nút kế tiếp
                more = !reader.EOF;
                continue;
            }
            if (mine && reader.NodeType == XmlNodeType.Element && reader.LocalName == "tab") text.Append('\t');
            if (mine && reader.NodeType == XmlNodeType.EndElement && reader.LocalName == "p") text.Append('\n');
            more = reader.Read();
        }
        return text.ToString().Trim();
    }

    private static string Sheets(byte[] bytes)
    {
        using var book = new XLWorkbook(new MemoryStream(bytes));
        var text = new StringBuilder();
        foreach (var sheet in book.Worksheets)
        {
            text.AppendLine(sheet.Name);
            foreach (var row in sheet.RowsUsed())
                text.AppendLine(string.Join(" | ", row.CellsUsed().Select(c => c.GetFormattedString())));
            text.AppendLine();
        }
        return text.ToString().Trim();
    }
}
