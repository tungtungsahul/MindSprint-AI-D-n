using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MindSprintAI_ASP.NET_MVC.Data.Entities;
using MindSprintAI_ASP.NET_MVC.Models;
using MindSprintAI_ASP.NET_MVC.Services;

namespace MindSprintAI_ASP.NET_MVC.Tests;

// Tự cập nhật hồ sơ (người dùng chốt 19/11/2026): đổi tên như lúc đăng ký + cách nhau ≥ 7 ngày; đổi mật khẩu theo quy tắc (55).
[Collection("db")]
public class HoSoCaNhanTests
{
    private static UserManager<ApplicationUser> Users(Data.AppDbContext db)
    {
        var options = new IdentityOptions(); options.Password.RequiredLength = 8;
        return new(new UserStore<ApplicationUser>(db), Options.Create(options), new PasswordHasher<ApplicationUser>(),
            [], [new PasswordValidator<ApplicationUser>(new VietnameseIdentityErrors())], new UpperInvariantLookupNormalizer(),
            new VietnameseIdentityErrors(), null!, NullLogger<UserManager<ApplicationUser>>.Instance);
    }

    private static async Task<ApplicationUser> FreshAsync(Harness h, string id) => await h.Db.Users.AsNoTracking().SingleAsync(u => u.Id == id);

    [Fact]
    public async Task DoiTen_CatKhoangTrang_LuuThoiDiem_VaPhaiCach7Ngay()
    {
        using var h = new Harness(); using var users = Users(h.Db);
        var id = await h.UserAsync("Tên cũ");
        var profile = new ProfileService(h.Db, users, new SafetyEmail(), h.Clock);

        await profile.RenameAsync(id, "  Nguyễn Văn Mới  ");
        var user = await FreshAsync(h, id);
        Assert.Equal("Nguyễn Văn Mới", user.DisplayName);
        Assert.Equal(h.Clock.UtcNow, user.DisplayNameChangedAt);
        Assert.Equal(h.Clock.UtcNow.AddDays(7), profile.NextRenameAt(user));

        h.Clock.UtcNow = h.Clock.UtcNow.AddDays(6);
        var error = await Assert.ThrowsAsync<BusinessException>(() => profile.RenameAsync(id, "Tên khác"));
        Assert.Contains("ít nhất 7 ngày", error.Message);
        Assert.Equal("Nguyễn Văn Mới", (await FreshAsync(h, id)).DisplayName);

        h.Clock.UtcNow = h.Clock.UtcNow.AddDays(1);
        await profile.RenameAsync(id, "Tên khác");
        Assert.Equal("Tên khác", (await FreshAsync(h, id)).DisplayName);
    }

    [Theory]
    [InlineData("", "Nhập tên hiển thị.")]
    [InlineData("   ", "Nhập tên hiển thị.")]
    [InlineData("Tên cũ", "Tên mới giống tên đang dùng.")]
    public async Task DoiTen_TuChoiTenRongHoacTrung(string name, string message)
    {
        using var h = new Harness(); using var users = Users(h.Db);
        var id = await h.UserAsync("Tên cũ");
        var profile = new ProfileService(h.Db, users, new SafetyEmail(), h.Clock);
        Assert.Equal(message, (await Assert.ThrowsAsync<BusinessException>(() => profile.RenameAsync(id, name))).Message);
        Assert.Null((await FreshAsync(h, id)).DisplayNameChangedAt);
    }

    [Fact]
    public async Task DoiTen_QuaDai_BiTuChoi_GioiHanNhuLucDangKy()
    {
        using var h = new Harness(); using var users = Users(h.Db);
        var id = await h.UserAsync("Tên cũ");
        var profile = new ProfileService(h.Db, users, new SafetyEmail(), h.Clock);
        await Assert.ThrowsAsync<BusinessException>(() => profile.RenameAsync(id, new string('a', ProfileService.MaxName + 1)));
        await profile.RenameAsync(id, new string('a', ProfileService.MaxName));
        Assert.Equal(ProfileService.MaxName, (await FreshAsync(h, id)).DisplayName.Length);
    }

    [Fact]
    public async Task DoiMatKhau_KiemTraMatKhauCu_DoiStamp_VaGuiEmail()
    {
        using var h = new Harness(); using var users = Users(h.Db);
        var user = new ApplicationUser { UserName = $"pw-{Guid.NewGuid():N}", Email = $"{Guid.NewGuid():N}@test.vn", DisplayName = "Người đổi", EmailConfirmed = true };
        Assert.True((await users.CreateAsync(user, "MatKhau@Cu1")).Succeeded);
        var email = new SafetyEmail();
        var profile = new ProfileService(h.Db, users, email, h.Clock);

        Assert.Equal("Mật khẩu hiện tại không đúng.",
            (await Assert.ThrowsAsync<BusinessException>(() => profile.ChangePasswordAsync(user.Id, "sai-roi", "MatKhau@Moi2", "MatKhau@Moi2", "http://x/Account"))).Message);
        Assert.Equal("Hai mật khẩu mới không khớp.",
            (await Assert.ThrowsAsync<BusinessException>(() => profile.ChangePasswordAsync(user.Id, "MatKhau@Cu1", "MatKhau@Moi2", "MatKhau@Moi3", "http://x/Account"))).Message);
        Assert.Equal("Mật khẩu mới phải khác mật khẩu hiện tại.",
            (await Assert.ThrowsAsync<BusinessException>(() => profile.ChangePasswordAsync(user.Id, "MatKhau@Cu1", "MatKhau@Cu1", "MatKhau@Cu1", "http://x/Account"))).Message);
        Assert.Contains("ít nhất 8 ký tự",
            (await Assert.ThrowsAsync<BusinessException>(() => profile.ChangePasswordAsync(user.Id, "MatKhau@Cu1", "ngan1", "ngan1", "http://x/Account"))).Message);
        Assert.Empty(email.Subjects);

        var stamp = (await FreshAsync(h, user.Id)).SecurityStamp;
        await profile.ChangePasswordAsync(user.Id, "MatKhau@Cu1", "MatKhau@Moi2", "MatKhau@Moi2", "http://x/Account");
        var after = await users.FindByIdAsync(user.Id);
        Assert.True(await users.CheckPasswordAsync(after!, "MatKhau@Moi2"));
        Assert.False(await users.CheckPasswordAsync(after!, "MatKhau@Cu1"));
        Assert.NotEqual(stamp, (await FreshAsync(h, user.Id)).SecurityStamp); // thiết bị khác bị đăng xuất (quy tắc 55)
        Assert.Single(email.Subjects);
        Assert.StartsWith("Đã đổi mật khẩu", email.Subjects[0]);
        Assert.Contains("Khóa bảo vệ", email.Messages[0]);
    }

    [Fact]
    public async Task DoiMatKhau_TaiKhoanChuaCoMatKhau_ChiDanSangDatMatKhau()
    {
        using var h = new Harness(); using var users = Users(h.Db);
        var id = await h.UserAsync("Chỉ Google");
        var profile = new ProfileService(h.Db, users, new SafetyEmail(), h.Clock);
        Assert.Contains("Đặt mật khẩu",
            (await Assert.ThrowsAsync<BusinessException>(() => profile.ChangePasswordAsync(id, "x", "MatKhau@Moi2", "MatKhau@Moi2", "http://x/Account"))).Message);
    }

    [Fact]
    public void NhatKy_CoTenChoHanhDongMoi()
    {
        Assert.Equal("Đổi tên hiển thị", AuditAction.DisplayNameChanged.Text());
        Assert.Equal("Chấm bài theo đề", AuditAction.PaperGraded.Text());
    }
}
