using Microsoft.AspNetCore.Http;
using MindSprintAI_ASP.NET_MVC.Models;
using Xunit;

namespace MindSprintAI_ASP.NET_MVC.Tests;

// GV/học viên không phải dân IT: trang con phải có nút "Quay lại …" cùng một kiểu, không chỉ dựa vào nút Back của trình duyệt.
public class NutQuayLaiTests
{
    // Trang chính trên thanh bên, trang đăng nhập, trang đang làm bài và hộp xác nhận (đã có nút Hủy) không cần.
    private static readonly string[] Exempt =
    [
        "Home/Index", "Learn/Index", "Learn/Play", "Learn/Paper", "Learn/Consent", "Classrooms/Index", "Classrooms/Join",
        "StudySets/Index", "StudySets/Trash", "Notifications/Index", "Kanban/Index", "Schedule/Index", "WeakAreas/Index", "Overview/Index", "Notebooks/Index", "Settings/Index", "Vocab/Practice", "Vocab/Game", "Reports/Index", "Admin/Index", "Account/Index",
        "Account/Login", "Account/Register", "Account/ForgotPassword", "Account/ResetPassword", "Account/CheckEmail",
        "Account/Message", "Account/LoginTwoFactor", "Account/ConfirmLock", "Account/RecoveryCodes", "Account/AcceptPolicy", "Account/DeleteAccountConfirm",
        "Assignments/ChangeParts", "Assignments/CloseChoice", "Assignments/SelfReview", "Classrooms/ConfirmMembership",
        "Shared/Error", "Shared/Forbidden", "Shared/JoinQr",
    ];

    public static TheoryData<string> Pages()
    {
        var root = Path.Combine(Tang8PureTests.Root, "Views");
        var data = new TheoryData<string>();
        foreach (var file in Directory.GetFiles(root, "*.cshtml", SearchOption.AllDirectories))
        {
            var name = Path.GetRelativePath(root, file).Replace('\\', '/')[..^".cshtml".Length];
            if (Path.GetFileName(name).StartsWith('_') || Exempt.Contains(name)) continue;
            data.Add(name);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(Pages))]
    public void TrangConCoNutQuayLaiCungKieu(string page)
    {
        var html = File.ReadAllText(Path.Combine(Tang8PureTests.Root, "Views", page + ".cshtml"));
        Assert.Contains("b-back-icon", html);
        Assert.Contains("Quay lại", html);
        // Đường dẫn dạng chữ (breadcrumb) không thay được nút; trang soạn quiz giữ nó để thấy đang ở bộ học nào.
        if (page != "StudySets/Quiz") Assert.DoesNotContain("class=\"breadcrumb\"", html);
    }

    [Theory]
    [InlineData("https://rekal.vn/Notifications", "/Notifications")]
    [InlineData("https://rekal.vn/Classrooms/Details/1?tab=2", "/Classrooms/Details/1?tab=2")]
    [InlineData("https://khac.vn/Notifications", "/Account")]          // máy chủ khác
    [InlineData("http://rekal.vn/Notifications", "/Account")]           // khác giao thức
    [InlineData("https://rekal.vn/Invitations", "/Account")]            // chính trang này (gửi form lỗi)
    [InlineData("rác", "/Account")]
    [InlineData("", "/Account")]
    public void BackLinkChiNhanTrangTruocCuaRekal(string referer, string expected)
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = "https"; context.Request.Host = new("rekal.vn"); context.Request.Path = "/Invitations";
        if (referer.Length > 0) context.Request.Headers.Referer = referer;
        Assert.Equal(expected, BackLink.Url(context.Request, "/Account"));
    }
}
