using System.Text;
using System.Text.Json;

namespace MindSprint.Api.Services;

public record GeneratedCard(string Question, string Answer, string? Example);
public record GeneratedQuiz(string Question, List<string> Options, int CorrectIndex, string? Explanation);

/// <summary>Gọi Google Gemini: chế độ JSON thuần (responseMimeType) hoặc văn bản tự do.</summary>
public class GeminiService(HttpClient http, IConfiguration cfg)
{
    private static readonly JsonSerializerOptions J = new() { PropertyNameCaseInsensitive = true };

    public Task<List<GeneratedCard>> GenerateFlashcardsAsync(string text, int count) => JsonAsync<List<GeneratedCard>>(
        $$"""
        Bạn là trợ lý học tập. Từ tài liệu dưới đây, tạo đúng {{count}} flashcard bằng tiếng Việt.
        Chỉ trả về JSON hợp lệ, KHÔNG kèm giải thích hay markdown, theo dạng:
        [{"question":"...","answer":"...","example":"..."}]

        TÀI LIỆU:
        {{text}}
        """);

    public Task<List<GeneratedQuiz>> GenerateQuizAsync(string text, int count) => JsonAsync<List<GeneratedQuiz>>(
        $$"""
        Bạn là trợ lý học tập. Từ tài liệu dưới đây, tạo đúng {{count}} câu hỏi trắc nghiệm 4 đáp án bằng tiếng Việt.
        Chỉ trả về JSON hợp lệ, KHÔNG kèm giải thích hay markdown, theo dạng:
        [{"question":"...","options":["A","B","C","D"],"correctIndex":0,"explanation":"..."}]

        TÀI LIỆU:
        {{text}}
        """);

    public async Task<T> JsonAsync<T>(string prompt)
    {
        var raw = (await CallAsync(prompt, json: true)).Trim();
        if (raw.StartsWith("```")) raw = raw.Trim('`').Replace("json", "", StringComparison.OrdinalIgnoreCase).Trim();
        return JsonSerializer.Deserialize<T>(raw, J) ?? throw new InvalidOperationException("Gemini trả về JSON rỗng.");
    }

    public Task<string> TextAsync(string prompt) => CallAsync(prompt, json: false);

    private async Task<string> CallAsync(string prompt, bool json)
    {
        var key = cfg["Gemini:ApiKey"];
        if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("Chưa cấu hình Gemini:ApiKey.");

        var models = new List<string>();
        var primary = cfg["Gemini:Model"];
        if (!string.IsNullOrWhiteSpace(primary)) models.Add(primary.Trim());
        if (models.Count == 0) models.Add("gemini-flash-latest");

        var fallback = cfg.GetSection("Gemini:FallbackModels").Get<string[]>() ?? [];
        foreach (var item in fallback)
        {
            var value = item?.Trim();
            if (!string.IsNullOrWhiteSpace(value) && !models.Contains(value, StringComparer.OrdinalIgnoreCase))
                models.Add(value);
        }

        foreach (var model in models)
        {
            try
            {
                return await TrySingleModelAsync(prompt, model, key, json);
            }
            catch (InvalidOperationException ex) when (ShouldRetryOrFallback(ex.Message))
            {
                continue;
            }
        }

        throw new InvalidOperationException("Google Gemini đang không phản hồi. Thử lại sau vài phút hoặc đổi model dự phòng.");
    }

    private async Task<string> TrySingleModelAsync(string prompt, string model, string key, bool json)
    {
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={Uri.EscapeDataString(key)}";

        object gen = json ? new { responseMimeType = "application/json", temperature = 0.3 } : new { temperature = 0.5 };
        var body = new { contents = new[] { new { parts = new[] { new { text = prompt } } } }, generationConfig = gen };

        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
        req.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

        try
        {
            using var res = await http.SendAsync(req);
            var raw = await res.Content.ReadAsStringAsync();
            if (!res.IsSuccessStatusCode)
            {
                var detail = TryExtractError(raw);
                var message = $"Gemini {(int)res.StatusCode} ({res.StatusCode}) model '{model}': {detail}";

                if (IsTransient(res.StatusCode))
                {
                    await Task.Delay(1500);
                    throw new InvalidOperationException(message);
                }

                throw new InvalidOperationException(message);
            }

            using var doc = JsonDocument.Parse(raw);
            return doc.RootElement.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString() ?? "";
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"Gemini request failed for model '{model}': {ex.Message}", ex);
        }
    }

    private static bool ShouldRetryOrFallback(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return false;
        return message.Contains("503", StringComparison.OrdinalIgnoreCase)
            || message.Contains("429", StringComparison.OrdinalIgnoreCase)
            || message.Contains("500", StringComparison.OrdinalIgnoreCase)
            || message.Contains("timeout", StringComparison.OrdinalIgnoreCase)
            || message.Contains("too many requests", StringComparison.OrdinalIgnoreCase)
            || message.Contains("overloaded", StringComparison.OrdinalIgnoreCase)
            || message.Contains("404", StringComparison.OrdinalIgnoreCase)
            || message.Contains("401", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTransient(System.Net.HttpStatusCode statusCode) => statusCode == System.Net.HttpStatusCode.ServiceUnavailable || statusCode == System.Net.HttpStatusCode.TooManyRequests || statusCode == System.Net.HttpStatusCode.InternalServerError;

    private static string TryExtractError(string raw)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.TryGetProperty("error", out var error))
            {
                if (error.TryGetProperty("message", out var message)) return message.GetString() ?? "Google trả về lỗi không rõ.";
                if (error.TryGetProperty("status", out var status)) return status.GetString() ?? "Google trả về lỗi không rõ.";
            }
        }
        catch
        {
            // Bỏ qua nếu không phải JSON; trả raw nguyên văn.
        }

        return string.IsNullOrWhiteSpace(raw) ? "Google trả về lỗi không rõ." : raw.Trim();
    }
}
