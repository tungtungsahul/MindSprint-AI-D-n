using Microsoft.EntityFrameworkCore;
using MindSprintAI_ASP.NET_MVC.Data.Entities;
using MindSprintAI_ASP.NET_MVC.Services;

namespace MindSprintAI_ASP.NET_MVC.Tests;

// Kanban chuyển từ MindSprint-AI-D-n (TasksController): giữ đúng quy tắc của nhóm.
[Collection("db")]
public class KanbanTests
{
    private static KanbanService Kanban(Harness h) => new(h.Db);
    private static async Task<List<(string Title, KanbanStatus Status, int Position)>> Board(Harness h, string userId) =>
        (await Kanban(h).ListAsync(userId)).Select(t => (t.Title, t.Status, t.Position)).ToList();

    [Theory]
    [InlineData("", "Tiêu đề cần có từ 1 đến 200 ký tự.")]
    [InlineData("   ", "Tiêu đề cần có từ 1 đến 200 ký tự.")]
    public async Task TieuDeRongBiTuChoi(string title, string message)
    {
        using var h = new Harness();
        var u = await h.UserAsync("Lan");
        Assert.Equal(message, (await Assert.ThrowsAsync<BusinessException>(() => Kanban(h).CreateAsync(u, new(title, null, null)))).Message);
    }

    [Fact]
    public async Task GioiHanDoDaiVaGiaTriKhongHopLe()
    {
        using var h = new Harness();
        var u = await h.UserAsync("Lan");
        await Kanban(h).CreateAsync(u, new(new string('a', 200), new string('b', 5000), null)); // đúng mức tối đa vẫn nhận
        await Assert.ThrowsAsync<BusinessException>(() => Kanban(h).CreateAsync(u, new(new string('a', 201), null, null)));
        await Assert.ThrowsAsync<BusinessException>(() => Kanban(h).CreateAsync(u, new("Việc", new string('b', 5001), null)));
        await Assert.ThrowsAsync<BusinessException>(() => Kanban(h).CreateAsync(u, new("Việc", null, null, (KanbanPriority)9)));
        await Assert.ThrowsAsync<BusinessException>(() => Kanban(h).CreateAsync(u, new("Việc", null, null, null, (KanbanStatus)9)));
        Assert.Single(await Kanban(h).ListAsync(u));
    }

    [Fact]
    public async Task TaoMoiMacDinhVaThuTuTheoCotRiengTungNguoi()
    {
        using var h = new Harness();
        var lan = await h.UserAsync("Lan"); var minh = await h.UserAsync("Minh");
        var first = await Kanban(h).CreateAsync(lan, new("  Ôn chương 3  ", "   ", new DateTime(2026, 11, 20, 15, 30, 0)));
        Assert.Equal(("Ôn chương 3", (string?)null, KanbanPriority.Medium, KanbanStatus.Todo, 0), (first.Title, first.Description, first.Priority, first.Status, first.Position));
        Assert.Equal(new DateTime(2026, 11, 20, 0, 0, 0, DateTimeKind.Utc), first.DueDate); // hạn là một ngày
        await Kanban(h).CreateAsync(lan, new("B", null, null));
        await Kanban(h).CreateAsync(lan, new("C", null, null, KanbanPriority.High, KanbanStatus.Done));
        await Kanban(h).CreateAsync(minh, new("Của Minh", null, null));
        Assert.Equal([("Ôn chương 3", KanbanStatus.Todo, 0), ("B", KanbanStatus.Todo, 1), ("C", KanbanStatus.Done, 0)], await Board(h, lan));
        Assert.Equal([("Của Minh", KanbanStatus.Todo, 0)], await Board(h, minh));
    }

    [Fact]
    public async Task ChuyenCotDanhSoLaiCaHaiCot()
    {
        using var h = new Harness();
        var u = await h.UserAsync("Lan");
        var a = await Kanban(h).CreateAsync(u, new("A", null, null));
        await Kanban(h).CreateAsync(u, new("B", null, null));
        await Kanban(h).CreateAsync(u, new("C", null, null));
        await Kanban(h).CreateAsync(u, new("X", null, null, null, KanbanStatus.Doing));
        await Kanban(h).MoveAsync(u, a.Id, KanbanStatus.Doing, 0);
        Assert.Equal([("B", KanbanStatus.Todo, 0), ("C", KanbanStatus.Todo, 1), ("A", KanbanStatus.Doing, 0), ("X", KanbanStatus.Doing, 1)], await Board(h, u));
        // Vị trí vượt quá cuối cột thì về cuối; đổi thứ tự trong cùng cột.
        await Kanban(h).MoveAsync(u, a.Id, KanbanStatus.Doing, 99);
        Assert.Equal([("B", KanbanStatus.Todo, 0), ("C", KanbanStatus.Todo, 1), ("X", KanbanStatus.Doing, 0), ("A", KanbanStatus.Doing, 1)], await Board(h, u));
        await Assert.ThrowsAsync<BusinessException>(() => Kanban(h).MoveAsync(u, a.Id, KanbanStatus.Done, -1));
        await Assert.ThrowsAsync<BusinessException>(() => Kanban(h).MoveAsync(u, a.Id, (KanbanStatus)7, 0));
    }

    [Fact]
    public async Task SuaDoiCotThiVeCuoiCotMoi()
    {
        using var h = new Harness();
        var u = await h.UserAsync("Lan");
        var a = await Kanban(h).CreateAsync(u, new("A", null, null));
        await Kanban(h).CreateAsync(u, new("D", null, null, null, KanbanStatus.Done));
        var edited = await Kanban(h).UpdateAsync(u, a.Id, new("A mới", "Mô tả", null, KanbanPriority.Low, KanbanStatus.Done));
        Assert.Equal(("A mới", "Mô tả", KanbanPriority.Low, KanbanStatus.Done, 1), (edited.Title, edited.Description, edited.Priority, edited.Status, edited.Position));
        // Không gửi mức ưu tiên thì giữ mức cũ.
        Assert.Equal(KanbanPriority.Low, (await Kanban(h).UpdateAsync(u, a.Id, new("A mới", null, null))).Priority);
    }

    [Fact]
    public async Task XoaDanhSoLaiCot()
    {
        using var h = new Harness();
        var u = await h.UserAsync("Lan");
        await Kanban(h).CreateAsync(u, new("A", null, null));
        var b = await Kanban(h).CreateAsync(u, new("B", null, null));
        await Kanban(h).CreateAsync(u, new("C", null, null));
        await Kanban(h).DeleteAsync(u, b.Id);
        Assert.Equal([("A", KanbanStatus.Todo, 0), ("C", KanbanStatus.Todo, 1)], await Board(h, u));
    }

    [Fact]
    public async Task NguoiKhacKhongSuaChuyenXoaDuocVaKhongBietViecTonTai()
    {
        using var h = new Harness();
        var lan = await h.UserAsync("Lan"); var minh = await h.UserAsync("Minh");
        var task = await Kanban(h).CreateAsync(lan, new("Của Lan", null, null));
        foreach (var act in new Func<Task>[] {
            () => Kanban(h).UpdateAsync(minh, task.Id, new("Sửa trộm", null, null)),
            () => Kanban(h).MoveAsync(minh, task.Id, KanbanStatus.Done, 0),
            () => Kanban(h).DeleteAsync(minh, task.Id),
            () => Kanban(h).DeleteAsync(lan, 999_999) })
            Assert.Equal(KanbanService.NotFound, (await Assert.ThrowsAsync<BusinessException>(act)).Message);
        Assert.Equal([("Của Lan", KanbanStatus.Todo, 0)], await Board(h, lan));
    }

    [Fact]
    public async Task HaiTabCungTaoMotLucKhongTrungViTri()
    {
        using var h = new Harness();
        var u = await h.UserAsync("Lan");
        // Mỗi "tab" một DbContext riêng như hai yêu cầu web song song; khóa theo người xếp hàng nên vị trí không trùng.
        await Task.WhenAll(Enumerable.Range(0, 8).Select(async i =>
        {
            await using var db = DatabaseFixture.NewContext();
            await new KanbanService(db).CreateAsync(u, new($"Việc {i}", null, null));
        }));
        var positions = (await Board(h, u)).Select(t => t.Position).Order().ToList();
        Assert.Equal(Enumerable.Range(0, 8), positions);
    }

    [Fact]
    public async Task XoaTaiKhoanXoaLuonViecKanban()
    {
        // AccountDeletionService.cs: dòng KanbanTasks.ExecuteDeleteAsync nằm cùng nhóm dữ liệu cá nhân bị xóa.
        var source = File.ReadAllText(Path.Combine(Tang8PureTests.Root, "Services/AccountDeletionService.cs"));
        Assert.Contains("db.KanbanTasks.Where(x => x.UserId == userId).ExecuteDeleteAsync()", source);
        using var h = new Harness();
        var u = await h.UserAsync("Lan");
        await Kanban(h).CreateAsync(u, new("A", null, null));
        await h.Db.KanbanTasks.Where(x => x.UserId == u).ExecuteDeleteAsync();
        Assert.Empty(await Kanban(h).ListAsync(u));
    }
}
