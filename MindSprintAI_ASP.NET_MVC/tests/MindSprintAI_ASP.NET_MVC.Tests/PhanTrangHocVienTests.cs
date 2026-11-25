using System.Net;
using System.Text.RegularExpressions;
using MindSprintAI_ASP.NET_MVC.Data.Entities;
using MindSprintAI_ASP.NET_MVC.Models;
using MindSprintAI_ASP.NET_MVC.Services;

namespace MindSprintAI_ASP.NET_MVC.Tests;

public class PhanTrangHocVienTests
{
    [Theory]
    [InlineData(1, 1, new int[] { 1 })]
    [InlineData(1, 8, new int[] { 1, 2, 0, 8 })]
    [InlineData(5, 8, new int[] { 1, 0, 4, 5, 6, 0, 8 })]
    [InlineData(8, 8, new int[] { 1, 0, 7, 8 })]
    [InlineData(2, 3, new int[] { 1, 2, 3 })]
    public void SoTrangVaDauRutGon(int page, int last, int[] expected) =>
        Assert.Equal(expected.Select(n => n == 0 ? (int?)null : n), PagerModel.Numbers(page, last));

    [Theory]
    [InlineData(1), InlineData(2), InlineData(3)]
    public async Task ThanhTrangDanhDauTrangHienTaiVaTatNutOBien(int page)
    {
        var html = WebUtility.HtmlDecode(await Tang4ViewTests.Render("Shared", "_Pager",
            new PagerModel(page, 3, n => $"/Classrooms/Details/7?page={n}#class-members-title", "Trang danh sách học viên")));
        Assert.Matches($"<a[^>]*aria-current=\"page\"[^>]*>{page}</a>", html);
        Assert.Single(Regex.Matches(html, "aria-current=\"page\""));
        Assert.Matches(page == 1
            ? "<span[^>]*aria-disabled=\"true\"[^>]*aria-label=\"Trang trước\""
            : $"<a[^>]*href=\"/Classrooms/Details/7\\?page={page - 1}#class-members-title\"[^>]*aria-label=\"Trang trước\"", html);
        Assert.Matches(page == 3
            ? "<span[^>]*aria-disabled=\"true\"[^>]*aria-label=\"Trang sau\""
            : $"<a[^>]*href=\"/Classrooms/Details/7\\?page={page + 1}#class-members-title\"[^>]*aria-label=\"Trang sau\"", html);
    }

    [Fact]
    public async Task MotTrangKhongHienThanhTrang() => Assert.DoesNotContain("<nav",
        await Tang4ViewTests.Render("Shared", "_Pager", new PagerModel(1, 1, n => $"?page={n}", "Trang danh sách học viên")));

    [Fact]
    public async Task TrangLopChiHienHocVienTrangDangChonVaGiuFormCu()
    {
        var room = new Classroom { Id = 7, Name = "Lớp thử" };
        for (var n = 1; n <= 45; n++)
            room.Members.Add(new ClassMember
            {
                Id = n, UserId = $"hv{n}", Role = ClassRole.Student, Status = ClassMemberStatus.Active,
                User = new ApplicationUser { DisplayName = $"Học viên {n:00}", Email = $"hv{n}@test.vn" }
            });
        var html = WebUtility.HtmlDecode(await Tang4ViewTests.Render("Classrooms", "Details", room, new()
        {
            ["Assignments"] = new List<Assignment>(), ["MemberPage"] = 2, ["MemberPageSize"] = 30
        }));
        Assert.Contains("Học viên 31–45 trên 45", html);
        Assert.DoesNotContain("hv30@test.vn", html);
        Assert.Contains("hv31@test.vn", html);
        Assert.Contains("hv45@test.vn", html);
        Assert.Equal(15, Regex.Matches(html, "data-confirm=\"Xóa Học viên").Count);
        Assert.Contains("Báo tên không phù hợp", html);
        Assert.Contains("name=\"__RequestVerificationToken\"", html);
        Assert.Matches("<option value=\"30\" selected=\"selected\">30</option>", html);
        Assert.Contains("<form method=\"post\" class=\"d-flex flex-wrap gap-2 align-items-end\"", html);
        Assert.Contains("#class-members-title", html);
        Assert.DoesNotContain("Xem thêm", html);
    }
}

[Collection("db")]
public class PhanTrangHocVienDbTests
{
    [Fact]
    public async Task LuuDocDuBonCoTrangVaTachRiengTaiKhoan()
    {
        using var h = new Harness();
        var user = await h.UserAsync("Giảng viên");
        var other = await h.UserAsync("Giảng viên khác");
        var presets = new UserSettingPresetService(h.Db, h.Clock);
        Assert.Equal(20, await presets.MemberPageSizeAsync(user));
        foreach (var size in new[] { 20, 30, 40, 50 })
        {
            await presets.SaveMemberPageSizeAsync(size, user);
            h.Db.ChangeTracker.Clear();
            Assert.Equal(size, await new UserSettingPresetService(h.Db, h.Clock).MemberPageSizeAsync(user));
            Assert.Equal(20, await presets.MemberPageSizeAsync(other));
        }
        await presets.SaveMemberPageSizeAsync(30, other);
        Assert.Equal(50, await presets.MemberPageSizeAsync(user));
        Assert.Equal(30, await presets.MemberPageSizeAsync(other));
        await Assert.ThrowsAsync<BusinessException>(() => presets.ResetAsync("Classroom.MemberPageSize", user));
        Assert.Equal(50, await presets.MemberPageSizeAsync(user));
    }

    [Fact]
    public async Task KhongNhanCoTrang25()
    {
        using var h = new Harness();
        var user = await h.UserAsync("Giảng viên");
        var presets = new UserSettingPresetService(h.Db, h.Clock);
        var error = await Assert.ThrowsAsync<BusinessException>(() => presets.SaveMemberPageSizeAsync(25, user));
        Assert.Equal("Số học viên mỗi trang chỉ chọn 20, 30, 40 hoặc 50.", error.Message);
        Assert.Equal(20, await presets.MemberPageSizeAsync(user));
    }

    [Theory]
    [InlineData("25"), InlineData("null"), InlineData("\"30\""), InlineData("{}"), InlineData("hỏng"), InlineData("30.5")]
    public async Task GiaTriCuKhongHopLeVe20(string json)
    {
        using var h = new Harness();
        var user = await h.UserAsync("Giảng viên");
        h.Db.UserSettingPresets.Add(new UserSettingPreset { UserId = user, Scope = "Classroom.MemberPageSize", ValuesJson = json });
        await h.Db.SaveChangesAsync();
        Assert.Equal(20, await new UserSettingPresetService(h.Db, h.Clock).MemberPageSizeAsync(user));
    }
}
