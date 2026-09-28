using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MindSprint.Api.Services;

namespace MindSprint.Api.Controllers;

public record GenerateDto(string Text, int Count = 10);

/// <summary>Sinh Flashcard / trắc nghiệm từ văn bản người dùng gửi lên bằng Gemini.</summary>
[Authorize, Route("api/ai")]
public class AiController(GeminiService gemini) : ApiBase
{
    private const int MaxChars = 30_000;

    [HttpPost("flashcards")]
    public async Task<IActionResult> Flashcards(GenerateDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Text)) return BadRequest(new { message = "Nội dung trống." });
        try { return Ok(await gemini.GenerateFlashcardsAsync(Trim(dto.Text), Math.Clamp(dto.Count, 1, 30))); }
        catch (Exception ex) { return StatusCode(502, new { message = "Gemini lỗi: " + ex.Message }); }
    }

    [HttpPost("quiz")]
    public async Task<IActionResult> Quiz(GenerateDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Text)) return BadRequest(new { message = "Nội dung trống." });
        try { return Ok(await gemini.GenerateQuizAsync(Trim(dto.Text), Math.Clamp(dto.Count, 1, 30))); }
        catch (Exception ex) { return StatusCode(502, new { message = "Gemini lỗi: " + ex.Message }); }
    }

    private static string Trim(string s) => s.Length > MaxChars ? s[..MaxChars] : s;
}
