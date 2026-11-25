using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using MindSprintAI_ASP.NET_MVC.Data.Entities;
using MindSprintAI_ASP.NET_MVC.Prompts;

namespace MindSprintAI_ASP.NET_MVC.Services;

public record TutorTurn(string Role, string Text);
public record TutorInput(string? Question, string Action = "solve", string Style = "steps", int? HintLevel = null, string? Submission = null,
    string? Language = null, string? Code = null, string? Error = null, List<TutorTurn>? History = null);

// Studio (8 loại tài liệu), gợi ý câu hỏi và Gia sư của Sổ tay AI — chuyển từ MindSprint-AI-D-n NotebookAi + TutorService.
// Khác nhóm: dùng API key Gemini của chính người dùng (như mọi chức năng AI của QuizEdu); Flashcard/Trắc nghiệm của Studio
// đi qua trang xem trước nhập (NotebookService.CreateStudySetAsync) thay vì lưu thẳng.
// Studio "Flashcard"/"Trắc nghiệm" làm thử tại chỗ như nhóm (notebook.js quizHtml/pickAnswer): chỉ các dòng bản nháp đã qua
// ExcelTemplate.ValidateRows (không lỗi). Correct là chỉ số 0-based trong Options. "Lưu thành bộ học" gửi lại đúng các dòng này
// tới Import/Recheck → máy chủ kiểm tra lại toàn bộ → trang xem trước → Lưu (không gọi AI lần hai).
public record StudioItem(string Front, string Back, IReadOnlyList<string> Options, IReadOnlyList<int> Correct, string? Explanation);

public static class StudioItems
{
    public static List<StudioItem> From(ImportPreview preview, bool cards) => preview.Rows.Where(r => r.Include && r.Error == null).Select(r =>
    {
        string At(int i) => i < r.Values.Count ? r.Values[i].Trim() : "";
        if (cards) return new StudioItem(At(0), At(1), [], [], null);
        var options = Enumerable.Range(ExcelTemplate.FirstOption, ExcelTemplate.OptionCount).Select(At).ToList();
        var correct = At(ExcelTemplate.Correct).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => int.TryParse(x, out var n) ? n - 1 : char.ToUpperInvariant(x[0]) - 'A').Where(i => i >= 0 && i < options.Count && options[i] != "").ToList();
        // Bỏ ô đáp án trống ở cuối (AI tạo 4 đáp án trong 6 cột); chỉ số đáp án đúng không đổi vì chỉ cắt đuôi.
        var last = options.FindLastIndex(o => o != "");
        return new StudioItem(At(ExcelTemplate.Prompt), "", options.Take(last + 1).ToList(), correct, At(ExcelTemplate.Explain) is { Length: > 0 } e ? e : null);
    }).ToList();
}

public sealed class NotebookAiService(NotebookService notebooks, UserApiKeyService keys, GeminiClient gemini)
{
    public static readonly string[] StudioTypes = ["summary", "studyguide", "faq", "timeline", "mindmap", "audio"];
    public const int MaxContextChars = 120_000, MaxTutorChars = 10_000, MaxHistoryTurns = 12, MaxHistoryChars = 40_000;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public async Task<List<string>> SuggestAsync(int id, string userId)
    {
        var notebook = await notebooks.GetAsync(id, userId);
        if (notebook.Sources.Count == 0) return [];
        var result = await gemini.PromptJsonAsync(await keys.UseAsync(userId), NotebookPrompts.Suggestions(Context(notebook)), NotebookPrompts.SuggestionSchema, 2048, quick: true);
        return result.TryGetProperty("questions", out var q) && q.ValueKind == JsonValueKind.Array
            ? q.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!.Trim()).Where(x => x.Length is > 0 and <= 300).Take(5).ToList()
            : [];
    }

    public async Task<JsonElement> StudioAsync(int id, string? type, string? focus, string userId)
    {
        if (type == null || !StudioTypes.Contains(type)) throw new BusinessException("Loại tài liệu không hợp lệ.");
        if (focus?.Length > 500) throw new BusinessException("Trọng tâm tối đa 500 ký tự.");
        var notebook = await notebooks.GetAsync(id, userId);
        NotebookService.Combined(notebook); // báo "Hãy thêm ít nhất một nguồn trước." khi sổ trống
        return CheckOutput(await gemini.PromptJsonAsync(await keys.UseAsync(userId), NotebookPrompts.Studio(type, focus, Context(notebook)), NotebookPrompts.StudioSchema(type)), type);
    }

    // NFR-14 của nhóm (NotebookOutputValidation): kết quả AI quá dài hoặc sơ đồ tư duy vượt khuôn (quá 3 cấp, quá 100 nút, nút quá 12 con,
    // tiêu đề quá 100 ký tự) thì báo lỗi thay vì hiện. Bỏ luật "tiêu đề dưới 8 từ" của nhóm: một tiêu đề dài không đáng bỏ cả kết quả
    // (người dùng mất thêm một lượt Gemini); lời nhắc vẫn yêu cầu dưới 8 từ.
    public static JsonElement CheckOutput(JsonElement value, string type)
    {
        var total = 0;
        if (value.GetRawText().Length > 200_000 || (type == "mindmap" && !Node(value, 0, ref total)))
            throw new BusinessException("Gemini trả về dữ liệu không hợp lệ. Vui lòng thử lại.");
        return value;
    }
    private static bool Node(JsonElement node, int depth, ref int total)
    {
        if (node.ValueKind != JsonValueKind.Object || depth > 3 || ++total > 100) return false;
        if (!node.TryGetProperty("title", out var title) || title.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(title.GetString()) || title.GetString()!.Length > 100) return false;
        if (!node.TryGetProperty("children", out var children)) return true;
        if (children.ValueKind != JsonValueKind.Array || children.GetArrayLength() > 12) return false;
        foreach (var child in children.EnumerateArray()) if (!Node(child, depth + 1, ref total)) return false;
        return true;
    }

    public async Task<JsonElement> TutorAsync(int id, TutorInput input, string userId)
    {
        Validate(input);
        var notebook = await notebooks.GetAsync(id, userId);
        var data = JsonSerializer.Serialize(new
        {
            question = input.Question?.Trim() ?? "", submission = input.Submission, language = input.Language, code = input.Code, error = input.Error,
            history = input.History ?? [], sources = notebook.Sources.Count > 0 ? Context(notebook) : ""
        }, Json);
        return CheckOutput(await gemini.PromptJsonAsync(await keys.UseAsync(userId), NotebookPrompts.Tutor(input.Action, input.Style, input.HintLevel, data), NotebookPrompts.TutorSchema(input.Action)), "tutor");
    }

    // Kiểm tra đầu vào như TutorService.Validate của nhóm (câu báo lỗi giữ nguyên).
    public static void Validate(TutorInput r)
    {
        if (r.Action is not ("solve" or "check" or "hint" or "code")) throw new BusinessException("Chế độ Gia sư không hợp lệ.");
        if (r.Style is not ("steps" or "brief")) throw new BusinessException("Chọn cách giải Từng bước hoặc Ngắn gọn.");
        if (r.Action != "code" && string.IsNullOrWhiteSpace(r.Question)) throw new BusinessException("Hãy nhập đề bài hoặc câu hỏi.");
        if (r.Question?.Length > MaxTutorChars) throw new BusinessException("Đề bài tối đa 10.000 ký tự.");
        if (r.Action == "code")
        {
            if (r.Language is not ("python" or "javascript")) throw new BusinessException("Chỉ hỗ trợ Python và JS.");
            if (string.IsNullOrWhiteSpace(r.Code)) throw new BusinessException("Hãy nhập đoạn code cần phân tích.");
            if (r.Code.Length > MaxTutorChars || r.Error?.Length > MaxTutorChars) throw new BusinessException("Code và thông báo lỗi mỗi phần tối đa 10.000 ký tự.");
        }
        else if (r.Language != null || r.Code != null || r.Error != null) throw new BusinessException("Thông tin code chỉ dùng trong hoạt động Lập trình.");
        if (r.Action == "hint" && r.HintLevel is not (>= 1 and <= 3)) throw new BusinessException("Mức gợi ý phải từ 1 đến 3.");
        if (r.Action != "hint" && r.HintLevel != null) throw new BusinessException("Mức gợi ý chỉ dùng trong Tự luyện.");
        if (r.Action == "check" && string.IsNullOrWhiteSpace(r.Submission)) throw new BusinessException("Hãy nhập bài làm cần kiểm tra.");
        if (r.Submission?.Length > MaxTutorChars) throw new BusinessException("Bài làm tối đa 10.000 ký tự.");
        if (r.History is not { } history) return;
        if (history.Count > MaxHistoryTurns) throw new BusinessException("Lịch sử gửi kèm tối đa 12 lượt.");
        if (history.Any(t => t is null || t.Role is not ("user" or "assistant") || string.IsNullOrWhiteSpace(t.Text)))
            throw new BusinessException("Lịch sử hội thoại không hợp lệ.");
        if (history.Any(t => t.Text.Length > MaxTutorChars) || history.Sum(t => (long)t.Text.Length) > MaxHistoryChars)
            throw new BusinessException("Ngữ cảnh bài tập quá dài. Hãy bắt đầu bài mới.");
    }

    // Ngữ cảnh nguồn như NotebookAi.BuildContext: nhãn [Sn] + tiêu đề, chia đều ngân sách ký tự cho từng nguồn.
    public static string Context(Notebook notebook)
    {
        var per = Math.Max(2_000, MaxContextChars / Math.Max(1, notebook.Sources.Count));
        var sb = new StringBuilder();
        for (var i = 0; i < notebook.Sources.Count; i++)
        {
            var content = notebook.Sources[i].Content;
            sb.AppendLine($"[S{i + 1}] Tiêu đề: {notebook.Sources[i].Title}");
            sb.AppendLine(content.Length > per ? content[..per] : content);
            if (content.Length > per) sb.AppendLine("[Phần văn bản vượt giới hạn ngữ cảnh chưa được đưa vào; không suy đoán phần thiếu.]");
            sb.AppendLine();
        }
        return sb.ToString();
    }
}
