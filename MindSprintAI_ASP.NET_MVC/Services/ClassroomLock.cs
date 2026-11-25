using Microsoft.EntityFrameworkCore;
using MindSprintAI_ASP.NET_MVC.Data;
using MindSprintAI_ASP.NET_MVC.Data.Entities;

namespace MindSprintAI_ASP.NET_MVC.Services;

internal static class ClassroomLock
{
    internal static async Task<Classroom> RoomAsync(AppDbContext db, int id)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT [Id] FROM [Classrooms] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {id}");
        var room = await db.Classrooms.FindAsync(id) ?? throw new BusinessException("Không tìm thấy lớp.");
        await db.Entry(room).ReloadAsync();
        if (room.DeletedAt != null) throw new BusinessException("Lớp đã bị xóa.");
        return room;
    }

    // (48A, B5c) khóa lớp → bài theo Id → thành viên; không thay đổi lượt/đồng hồ.
    internal static async Task<List<Assignment>> AssignmentsAsync(AppDbContext db, int id)
    {
        var list = new List<Assignment>();
        foreach (var aid in await db.Assignments.Where(a => a.ClassroomId == id).OrderBy(a => a.Id).Select(a => a.Id).ToListAsync())
            list.Add(await AttemptLock.AssignmentAsync(db, aid));
        return list;
    }

    internal static async Task<List<ClassMember>> MembersAsync(AppDbContext db, int id)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT [Id] FROM [ClassMembers] WITH (UPDLOCK, HOLDLOCK) WHERE [ClassroomId] = {id}");
        var members = await db.ClassMembers.Where(m => m.ClassroomId == id).OrderBy(m => m.Id).ToListAsync();
        foreach (var member in members) await db.Entry(member).ReloadAsync();
        return members;
    }
}
