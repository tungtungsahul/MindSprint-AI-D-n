using Microsoft.EntityFrameworkCore;
using MindSprintAI_ASP.NET_MVC.Data;
using MindSprintAI_ASP.NET_MVC.Data.Entities;

namespace MindSprintAI_ASP.NET_MVC.Services;

public record NotebookRow(int Id, string Title, int Sources, int Notes, DateTime CreatedAt);

// Sổ tay AI (MindSprint-AI-D-n NotebooksController) — giữ giới hạn nhóm: 20 nguồn/sổ, 200.000 ký tự/nguồn; chỉ chủ sổ xem/sửa.
// Hỏi đáp dùng cơ chế trả lời có trích dẫn của QuizEdu (GeminiClient.AskAsync + SourceGrounding): chỉ dựa trên nguồn,
// trích dẫn được kiểm nguyên văn; dùng API key Gemini của chính người dùng. "Tạo bộ học" đi qua trang xem trước nhập Excel.
public sealed class NotebookService(AppDbContext db, UserApiKeyService keys, GeminiClient gemini,
    AiGenerateService generator, TimeProvider clock)
{
    public const int MaxSources = 20, MaxCharsPerSource = 200_000, MaxNotebooks = 100, MaxNotes = 200;
    public const string NotFound = "Không tìm thấy sổ tay.";
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public Task<List<NotebookRow>> ListAsync(string userId) => db.Notebooks.AsNoTracking().Where(n => n.UserId == userId)
        .OrderByDescending(n => n.Id).Select(n => new NotebookRow(n.Id, n.Title, n.Sources.Count, n.Notes.Count, n.CreatedAt)).ToListAsync();

    public async Task<Notebook> GetAsync(int id, string userId) => await db.Notebooks.AsNoTracking()
        .Include(n => n.Sources.OrderBy(s => s.Id)).Include(n => n.Notes.OrderByDescending(x => x.Id))
        .SingleOrDefaultAsync(n => n.Id == id && n.UserId == userId) ?? throw new BusinessException(NotFound);

    public async Task<Notebook> CreateAsync(string? title, string userId)
    {
        if (await db.Notebooks.CountAsync(n => n.UserId == userId) >= MaxNotebooks) throw new BusinessException($"Mỗi người có tối đa {MaxNotebooks} sổ tay.");
        var notebook = new Notebook { UserId = userId, Title = Title(title, "Sổ tay mới"), CreatedAt = Now };
        db.Notebooks.Add(notebook);
        await db.SaveChangesAsync();
        return notebook;
    }

    public async Task DeleteAsync(int id, string userId)
    {
        await OwnAsync(id, userId);
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.NotebookSources.Where(s => s.NotebookId == id).ExecuteDeleteAsync();
        await db.NotebookNotes.Where(n => n.NotebookId == id).ExecuteDeleteAsync();
        await db.Notebooks.Where(n => n.Id == id).ExecuteDeleteAsync();
        await tx.CommitAsync();
    }

    public async Task AddSourceAsync(int id, string? title, string? text, NotebookSourceKind kind, string? origin, string userId)
    {
        await OwnAsync(id, userId);
        var content = AiGenerateService.Clean(text);
        if (content.Length == 0) throw new BusinessException("Nội dung nguồn trống.");
        if (content.Length > MaxCharsPerSource) content = content[..MaxCharsPerSource];
        await using var tx = await db.Database.BeginTransactionAsync();
        await AttemptLock.NotebookAsync(db, id);
        if (await db.NotebookSources.CountAsync(s => s.NotebookId == id) >= MaxSources) throw new BusinessException($"Mỗi sổ tay có tối đa {MaxSources} nguồn.");
        db.NotebookSources.Add(new() { NotebookId = id, Title = Title(title, "Văn bản dán"), Kind = kind, Origin = origin, Content = content, CreatedAt = Now });
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    public static readonly string[] ImageTypes = [".png", ".jpg", ".jpeg", ".webp"];

    // Gia sư OCR: ảnh đề/ghi chép → AI chép lại chữ → thành một nguồn để hỏi đáp, ghi chú, tạo thẻ. Ảnh không được lưu lại.
    public async Task AddImageSourceAsync(int id, string? title, IFormFile file, string userId)
    {
        await OwnAsync(id, userId);
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!ImageTypes.Contains(ext)) throw new BusinessException("Ảnh phải là PNG, JPG hoặc WEBP.");
        if (file.Length == 0 || file.Length > AiGenerateService.MaxFileBytes) throw new BusinessException("Ảnh tối đa 10 MB.");
        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer);
        var mime = ext switch { ".png" => "image/png", ".webp" => "image/webp", _ => "image/jpeg" };
        var text = await gemini.ReadImageAsync(await keys.UseAsync(userId), buffer.ToArray(), mime);
        await AddSourceAsync(id, string.IsNullOrWhiteSpace(title) ? Path.GetFileNameWithoutExtension(file.FileName) : title, text,
            NotebookSourceKind.Image, Path.GetFileName(file.FileName), userId);
    }

    public async Task DeleteSourceAsync(int id, int sourceId, string userId)
    {
        await OwnAsync(id, userId);
        if (await db.NotebookSources.Where(s => s.Id == sourceId && s.NotebookId == id).ExecuteDeleteAsync() == 0) throw new BusinessException("Không tìm thấy nguồn.");
    }

    public async Task AddNoteAsync(int id, string? title, string? content, string userId)
    {
        await OwnAsync(id, userId);
        if (string.IsNullOrWhiteSpace(content)) throw new BusinessException("Ghi chú trống.");
        if (content.Length > 20_000) throw new BusinessException("Ghi chú tối đa 20.000 ký tự.");
        if (await db.NotebookNotes.CountAsync(n => n.NotebookId == id) >= MaxNotes) throw new BusinessException($"Mỗi sổ tay có tối đa {MaxNotes} ghi chú.");
        // Giữ thụt dòng của dòng đầu (ghi chú là code, FR-13 nhóm): chỉ bỏ dòng trống ở đầu và khoảng trắng ở cuối.
        db.NotebookNotes.Add(new() { NotebookId = id, Title = Title(title, "Ghi chú"), Content = content.TrimStart('\r', '\n').TrimEnd(), CreatedAt = Now });
        await db.SaveChangesAsync();
    }

    public async Task DeleteNoteAsync(int id, int noteId, string userId)
    {
        await OwnAsync(id, userId);
        if (await db.NotebookNotes.Where(n => n.Id == noteId && n.NotebookId == id).ExecuteDeleteAsync() == 0) throw new BusinessException("Không tìm thấy ghi chú.");
    }

    public async Task<GroundedAnswer> AskAsync(int id, string? question, string userId, CancellationToken cancellationToken = default)
    {
        var notebook = await GetAsync(id, userId);
        if (string.IsNullOrWhiteSpace(question)) throw new BusinessException("Câu hỏi trống.");
        if (question.Length > 1000) throw new BusinessException("Câu hỏi tối đa 1.000 ký tự.");
        var source = Combined(notebook);
        var chunks = SourceGrounding.Select(source, question, null);
        var key = await keys.UseAsync(userId);
        var answer = await gemini.AskAsync(key, chunks, question.Trim(), null, cancellationToken);
        if (answer.Answer.Contains(key, StringComparison.Ordinal) || answer.Citations.Any(c => c.Quote?.Contains(key, StringComparison.Ordinal) == true))
            throw new BusinessException("Gemini trả về dữ liệu không hợp lệ. Vui lòng thử lại.");
        return SourceGrounding.Validate(answer, chunks, question);
    }

    // "Tạo bộ học từ sổ tay": AI soạn nháp từ mọi nguồn → trang xem trước; bộ học mang tên sổ tay chỉ được tạo khi bấm Lưu.
    public async Task<ImportPreview> CreateStudySetAsync(int id, int count, bool cards, string userId)
    {
        var notebook = await GetAsync(id, userId);
        // Người dùng chọn 15/11: chưa tạo gì cho tới khi bấm Lưu ở trang xem trước (ExcelImportService.SaveAsync tạo bộ + quiz
        // cùng giao dịch) → AI lỗi hay bấm Hủy thì không còn bộ học rỗng.
        return await generator.GenerateAsync(0, Combined(notebook), count, cards, userId, newSetTitle: notebook.Title);
    }

    public static string Combined(Notebook notebook)
    {
        if (notebook.Sources.Count == 0) throw new BusinessException("Hãy thêm ít nhất một nguồn trước.");
        return string.Join("\n\n", notebook.Sources.Select(s => $"{s.Title}\n\n{s.Content}"));
    }

    private async Task OwnAsync(int id, string userId)
    {
        if (!await db.Notebooks.AnyAsync(n => n.Id == id && n.UserId == userId)) throw new BusinessException(NotFound);
    }

    private static string Title(string? value, string fallback)
    {
        var title = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        return title.Length > 200 ? title[..200] : title;
    }
}
