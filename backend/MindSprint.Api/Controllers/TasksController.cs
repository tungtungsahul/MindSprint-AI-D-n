using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using MindSprint.Api.Data;
using MindSprint.Api.Models;
using TaskStatus = MindSprint.Api.Models.TaskStatus;

namespace MindSprint.Api.Controllers;

public record TaskDto(string Title, string? Description, DateTime? DueDate, string? Priority = null, TaskStatus? Status = null,
    Guid? ClientRequestId = null, int? DurationMinutes = null);
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
        if (Validate(dto) is { } error) return BadRequest(new { message = error });
        var uid = UserId;
        if (dto.ClientRequestId is { } requestId)
        {
            var existing = await db.Tasks.FirstOrDefaultAsync(task => task.UserId == uid && task.ClientRequestId == requestId);
            if (existing is not null) return Ok(existing);
        }
        var status = dto.Status ?? TaskStatus.Todo;
        var pos = await db.Tasks.Where(t => t.UserId == uid && t.Status == status).CountAsync();
        var normalizedDueDate = NormalizeUtc(dto.DueDate);
        var t = new KanbanTask { UserId = uid, Title = dto.Title.Trim(), Description = dto.Description?.Trim(),
            DueDate = normalizedDueDate, ClientRequestId = dto.ClientRequestId, DurationMinutes = dto.DurationMinutes,
            Priority = dto.Priority ?? "Medium", Status = status, Position = pos };
        db.Tasks.Add(t);
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException ex) when (dto.ClientRequestId is not null
            && ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Two retries can race. The unique index chooses one row; return that row to the other request.
            db.Entry(t).State = EntityState.Detached;
            var existing = await db.Tasks.FirstOrDefaultAsync(task => task.UserId == uid && task.ClientRequestId == dto.ClientRequestId);
            if (existing is not null) return Ok(existing);
            throw;
        }
        return Ok(t);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, TaskDto dto)
    {
        if (Validate(dto) is { } error) return BadRequest(new { message = error });
        var t = await db.Tasks.FirstOrDefaultAsync(x => x.Id == id && x.UserId == UserId);
        if (t is null) return NotFound();
        var normalizedDueDate = NormalizeUtc(dto.DueDate);
        (t.Title, t.Description, t.DueDate) = (dto.Title.Trim(), dto.Description?.Trim(), normalizedDueDate);
        if (dto.DurationMinutes is not null) t.DurationMinutes = dto.DurationMinutes;
        t.Priority = dto.Priority ?? t.Priority;
        if (dto.Status is { } status && status != t.Status) await Arrange(t, status, int.MaxValue);
        await db.SaveChangesAsync();
        return Ok(t);
    }

    private static DateTime? NormalizeUtc(DateTime? value)
    {
        if (value is null) return null;
        return value.Value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.Value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)
        };
    }

    [HttpPatch("{id:int}/move")]
    public async Task<IActionResult> Move(int id, MoveDto dto)
    {
        if (!Enum.IsDefined(dto.Status) || dto.Position < 0) return BadRequest(new { message = "Trạng thái hoặc vị trí công việc không hợp lệ." });
        var uid = UserId;
        var t = await db.Tasks.FirstOrDefaultAsync(x => x.Id == id && x.UserId == uid);
        if (t is null) return NotFound();

        await Arrange(t, dto.Status, dto.Position);
        await db.SaveChangesAsync();
        return Ok(t);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var t = await db.Tasks.FirstOrDefaultAsync(x => x.Id == id && x.UserId == UserId);
        if (t is null) return NotFound();
        db.Tasks.Remove(t);
        var remaining = await db.Tasks.Where(x => x.UserId == UserId && x.Status == t.Status && x.Id != id)
            .OrderBy(x => x.Position).ThenBy(x => x.Id).ToListAsync();
        for (var position = 0; position < remaining.Count; position++) remaining[position].Position = position;
        await db.SaveChangesAsync();
        return NoContent();
    }

    private static string? Validate(TaskDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Title) || dto.Title.Trim().Length > 200) return "Tiêu đề cần có từ 1 đến 200 ký tự.";
        if (dto.Description?.Length > 5000) return "Mô tả tối đa 5000 ký tự.";
        if (dto.Priority is not (null or "Low" or "Medium" or "High")) return "Mức ưu tiên không hợp lệ.";
        if (dto.Status is { } status && !Enum.IsDefined(status)) return "Trạng thái không hợp lệ.";
        if (dto.ClientRequestId == Guid.Empty) return "Mã yêu cầu không hợp lệ.";
        if (dto.DurationMinutes is < 1 or > 480) return "Thời lượng phải nằm trong khoảng 1 đến 480 phút.";
        return null;
    }

    private async Task Arrange(KanbanTask task, TaskStatus status, int position)
    {
        var oldStatus = task.Status;
        var others = await db.Tasks.Where(x => x.UserId == UserId && x.Id != task.Id && (x.Status == oldStatus || x.Status == status))
            .OrderBy(x => x.Position).ThenBy(x => x.Id).ToListAsync();
        if (oldStatus != status)
        {
            var previous = others.Where(x => x.Status == oldStatus).ToList();
            for (var i = 0; i < previous.Count; i++) previous[i].Position = i;
        }
        var target = others.Where(x => x.Status == status).ToList();
        task.Status = status;
        target.Insert(Math.Min(position, target.Count), task);
        for (var i = 0; i < target.Count; i++) target[i].Position = i;
    }
}
