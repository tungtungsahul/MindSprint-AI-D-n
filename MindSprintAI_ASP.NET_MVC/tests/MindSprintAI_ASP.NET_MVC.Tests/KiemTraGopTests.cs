using Microsoft.EntityFrameworkCore;
using MindSprintAI_ASP.NET_MVC.Data.Entities;
using MindSprintAI_ASP.NET_MVC.Services;

namespace MindSprintAI_ASP.NET_MVC.Tests;

// Thử tải 14/11/2026: 3 lượt hỏi đầu mỗi yêu cầu (khách hợp lệ, tài khoản còn, đang bị khóa) gộp thành 1 câu.
// Mỗi trường hợp phải cho đúng kết quả như 3 câu cũ.
[Collection("db")]
public class KiemTraGopTests
{
    [Fact]
    public async Task TaiKhoanThuongLayDauBaoMatVaTrangThaiKhoa()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(1);
        var now = h.Clock.UtcNow;
        var stamp = (await h.Db.Users.SingleAsync(u => u.Id == s.Student)).SecurityStamp;
        var ok = await SessionCheck.LoadAsync(h.Db, s.Student, null, now);
        Assert.Equal(new SessionCheck(stamp, false, null), ok);

        h.Db.AccountLocks.Add(new AccountLock { UserId = s.Student, Kind = AccountLockKind.Violation, StartsAt = now.AddMinutes(-1), EndsAt = now.AddMinutes(-0.5) });
        h.Db.AccountLocks.Add(new AccountLock { UserId = s.Student, Kind = AccountLockKind.Violation, StartsAt = now.AddMinutes(1) });
        await h.Db.SaveChangesAsync();
        Assert.False((await SessionCheck.LoadAsync(h.Db, s.Student, null, now))!.Locked); // khóa đã hết hạn / chưa tới giờ không tính
        h.Db.AccountLocks.Add(new AccountLock { UserId = s.Student, Kind = AccountLockKind.Protective, StartsAt = now.AddMinutes(-1) });
        await h.Db.SaveChangesAsync();
        Assert.True((await SessionCheck.LoadAsync(h.Db, s.Student, null, now))!.Locked);
        Assert.Null(await SessionCheck.LoadAsync(h.Db, "khong-ton-tai", null, now)); // tài khoản đã xóa → đăng xuất
    }

    [Fact]
    public async Task KhachChiHopLeKhiDungMaTrinhDuyetVaChuaGop()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(1);
        await h.Assignments.ChangeStrangerPolicyAsync(s.Assignment.Id, StrangerPolicy.Allow, s.Lecturer);
        var token = GuestService.NewToken();
        var guest = await new GuestService(h.Db, h.Clock, new NullLearnNotifier()).JoinAsync(s.Assignment.Id, "Hoa", token);
        var now = h.Clock.UtcNow;
        Assert.Equal(guest.Id, (await SessionCheck.LoadAsync(h.Db, guest.UserId, GuestService.HashToken(token), now))!.GuestId);
        Assert.Null((await SessionCheck.LoadAsync(h.Db, guest.UserId, GuestService.HashToken(GuestService.NewToken()), now))!.GuestId);

        guest.MergedIntoUserId = s.Student; await h.Db.SaveChangesAsync();
        Assert.Null((await SessionCheck.LoadAsync(h.Db, guest.UserId, GuestService.HashToken(token), now))!.GuestId);
        guest.MergedIntoUserId = null;
        var user = await h.Db.Users.SingleAsync(u => u.Id == guest.UserId); user.LockoutEnd = DateTimeOffset.MaxValue;
        await h.Db.SaveChangesAsync();
        Assert.Null((await SessionCheck.LoadAsync(h.Db, guest.UserId, GuestService.HashToken(token), now))!.GuestId); // khách bị khóa
    }
}
