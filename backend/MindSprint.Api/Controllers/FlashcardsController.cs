using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MindSprint.Api.Data;
using MindSprint.Api.Models;

namespace MindSprint.Api.Controllers;

public record CardDto(string? ExternalId, string Category, string? SubCategory, string Question, string Answer, string? Example);

[Authorize, Route("api/flashcards")]
public class FlashcardsController(AppDbContext db) : ApiBase
{
    // Thẻ hệ thống + thẻ của tôi, kèm trạng thái SRS (nếu đã học). Định dạng khớp frontend hiện tại.
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string? category, [FromQuery] string? subCategory)
    {
        var uid = UserId;
        var q = db.Flashcards.AsNoTracking().Where(f => f.OwnerId == null || f.OwnerId == uid);
        if (!string.IsNullOrEmpty(category) && category != "all") q = q.Where(f => f.Category == category);
        if (!string.IsNullOrEmpty(subCategory) && subCategory != "all") q = q.Where(f => f.SubCategory == subCategory);

        var rows = await q.GroupJoin(db.CardProgresses.Where(p => p.UserId == uid), f => f.Id, p => p.FlashcardId,
                (f, ps) => new { f, p = ps.FirstOrDefault() })
            .Select(x => new
            {
                id = x.f.ExternalId, category = x.f.Category, subCategory = x.f.SubCategory,
                question = x.f.Question, answer = x.f.Answer, example = x.f.Example,
                status = x.p != null ? x.p.Status : "new",
                repetition = x.p != null ? x.p.Repetition : 0,
                interval = x.p != null ? x.p.IntervalDays : 1,
                efactor = x.p != null ? x.p.EFactor : 2.5,
                nextReviewDate = x.p != null && x.p.NextReviewDate != null
                    ? EF.Functions.DateDiffSecond(new DateTime(1970, 1, 1), x.p.NextReviewDate.Value) * 1000L : 0L
            }).ToListAsync();
        return Ok(rows);
    }

    [HttpPost]
    public async Task<IActionResult> Create(CardDto dto)
    {
        var card = new Flashcard
        {
            Category = dto.Category, SubCategory = dto.SubCategory, Question = dto.Question,
            Answer = dto.Answer, Example = dto.Example, OwnerId = UserId
        };
        if (!string.IsNullOrWhiteSpace(dto.ExternalId)) card.ExternalId = dto.ExternalId;
        db.Flashcards.Add(card);
        await db.SaveChangesAsync();
        return Ok(new { id = card.ExternalId });
    }

    [HttpPut("{externalId}")]
    public async Task<IActionResult> Update(string externalId, CardDto dto)
    {
        var card = await db.Flashcards.FirstOrDefaultAsync(f => f.ExternalId == externalId && f.OwnerId == UserId);
        if (card is null) return NotFound();
        (card.Category, card.SubCategory, card.Question, card.Answer, card.Example) =
            (dto.Category, dto.SubCategory, dto.Question, dto.Answer, dto.Example);
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{externalId}")]
    public async Task<IActionResult> Delete(string externalId)
    {
        var card = await db.Flashcards.FirstOrDefaultAsync(f => f.ExternalId == externalId && f.OwnerId == UserId);
        if (card is null) return NotFound();
        db.Flashcards.Remove(card);
        await db.SaveChangesAsync();
        return NoContent();
    }
}
