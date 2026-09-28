using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MindSprint.Api.Data;
using MindSprint.Api.Models;
using TaskStatus = MindSprint.Api.Models.TaskStatus;

namespace MindSprint.Api.Controllers;

public record TaskDto(string Title, string? Description, DateTime? DueDate);
public record MoveDto(TaskStatus Status, int Position); // gọi khi kéo/thả thẻ sang cột khác

/// <summary>API CRUD cho bảng Kanban (Todo / Doing / Done).</summary>
[Authorize, Route("api/tasks")]
public class TasksController(AppDbContext db) : ApiBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll() =>
        Ok(await db.Tasks.AsNoTracking().Where(t => t.UserId == UserId).OrderBy(t => t.Status).ThenBy(t => t.Position).ToListAsync());

    [HttpPost]
    public async Task<IActionResult> Create(TaskDto dto)
    {
        var uid = UserId;
        var pos = await db.Tasks.Where(t => t.UserId == uid && t.Status == TaskStatus.Todo).CountAsync();
        var t = new KanbanTask { UserId = uid, Title = dto.Title, Description = dto.Description, DueDate = dto.DueDate, Position = pos };
        db.Tasks.Add(t);
        await db.SaveChangesAsync();
        return Ok(t);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, TaskDto dto)
    {
        var t = await db.Tasks.FirstOrDefaultAsync(x => x.Id == id && x.UserId == UserId);
        if (t is null) return NotFound();
        (t.Title, t.Description, t.DueDate) = (dto.Title, dto.Description, dto.DueDate);
        await db.SaveChangesAsync();
        return Ok(t);
    }

    [HttpPatch("{id:int}/move")]
    public async Task<IActionResult> Move(int id, MoveDto dto)
    {
        var uid = UserId;
        var t = await db.Tasks.FirstOrDefaultAsync(x => x.Id == id && x.UserId == uid);
        if (t is null) return NotFound();

        // Dồn các thẻ khác trong cột đích để chèn vào đúng vị trí
        var target = await db.Tasks.Where(x => x.UserId == uid && x.Status == dto.Status && x.Id != id && x.Position >= dto.Position).ToListAsync();
        target.ForEach(x => x.Position++);
        (t.Status, t.Position) = (dto.Status, dto.Position);
        await db.SaveChangesAsync();
        return Ok(t);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var t = await db.Tasks.FirstOrDefaultAsync(x => x.Id == id && x.UserId == UserId);
        if (t is null) return NotFound();
        db.Tasks.Remove(t);
        await db.SaveChangesAsync();
        return NoContent();
    }
}
