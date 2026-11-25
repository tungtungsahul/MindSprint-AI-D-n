namespace MindSprintAI_ASP.NET_MVC.Tests;

public class RekalBatchViewTests
{
    private static string Read(string path) => File.ReadAllText(Path.Combine(Tang8PureTests.Root, path));

    [Fact]
    public void TrinhSoanGiuFormCuVaChiThemThaoTacDaCo()
    {
        var view = Read("Views/StudySets/Quiz.cshtml");
        foreach (var value in new[] { "id=\"question-form\"", "id=\"options\"", "id=\"Type\"", "id=\"form-error\"", "id=\"add-option\"", "data-question-presets", "data-new-question", "form=\"question-form\"", "title=\"Sửa câu hỏi\"", "title=\"Đưa lên\"", "title=\"Đưa xuống\"", "title=\"Xóa câu hỏi\"", "_QuestionAnswers" }) Assert.Contains(value, view);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(view, ">Thêm câu hỏi</a>").Count);
        Assert.Contains("~/js/quiz-editor.js", view);
    }

    [Fact]
    public void ThuVienDangHangVaChiTietCoAnHienDapAn()
    {
        var library = Read("Views/StudySets/Index.cshtml");
        foreach (var value in new[] { "c-library-row", "Giao bài", ">Mở</a>", "ellipsis", "asp-action=\"Rename\"", "asp-action=\"Delete\"", "library-search", "library-sort" }) Assert.Contains(value, library);
        var details = Read("Views/StudySets/Details.cshtml");
        Assert.Contains("asp-action=\"CreateQuiz\"", details);   // 18/11: thêm quiz trong trang bộ học, không còn trong thẻ Thư viện
        Assert.DoesNotContain("Quiz và tài liệu", library);
        Assert.Contains("data-answer-toggle", details);
        Assert.Contains("type=\"checkbox\"", details);
        Assert.Contains("for=\"show-answers\"", details);
        Assert.Contains("mindsprint-show-answers", Read("wwwroot/js/library-tools.js"));
        Assert.Contains("grid-template-columns:repeat(2,minmax(0,1fr))", Read("wwwroot/css/mindsprint-pages-c.css"));
    }

    [Fact]
    public void NamTheCauHoiCanhBaoVaFragment()
    {
        var view = Read("Views/Assignments/Details.cshtml");
        foreach (var key in new[] { "theo-doi", "cau-hoi", "chong-gian-lan", "cai-dat", "cham-lai" })
        {
            Assert.Contains($"id=\"tab-{key}\"", view);
            Assert.Contains($"id=\"pane-{key}\"", view);
        }
        Assert.Contains("#cau-hoi", Read("wwwroot/js/assignment-tabs.js"));
        Assert.Contains("_DeviceChange", Read("Views/Assignments/_Warnings.cshtml"));
        Assert.Contains("Không chấm điểm", Read("Views/Assignments/_QuestionMatrix.cshtml"));
    }

    [Fact]
    public void NutQuayLaiDungKieuMoiVaDongBaiGiaoCoHoiLai()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "Views"), "*.cshtml", SearchOption.AllDirectories))
            Assert.DoesNotMatch(@"<a [^>]*>← ", File.ReadAllText(file));
        Assert.Contains("asp-action=\"Close\" asp-route-id=\"@a.Id\" class=\"mt-2\" data-close-popup data-confirm=", Read("Views/Assignments/Details.cshtml"));
        Assert.Contains("close-choice.js", Read("Views/Assignments/Details.cshtml"));
        Assert.Contains("role=\"alertdialog\"", Read("Views/Assignments/CloseChoice.cshtml"));
    }

    [Fact]
    public async Task TrangThongBaoDungChoNhieuControllerVanTroDungDangNhap()
    {
        // Dựng dưới ReportsController: asp-action="Login" không ghi controller sẽ thành /Reports/Login (không tồn tại).
        Assert.Contains("asp-controller=\"Account\" asp-action=\"Login\"", Read("Views/Account/Message.cshtml"));
        var html = await Tang4ViewTests.Render("Account", "Message", "Đã gửi báo cáo cho quản trị viên.");
        // Bộ dựng trang của test không có bảng đường dẫn (href rỗng); chỉ kiểm người chưa đăng nhập thấy nút Đăng nhập.
        Assert.Contains("Đăng nhập</a>", System.Net.WebUtility.HtmlDecode(html));
    }

    [Theory]
    [InlineData(true, "Không có lời mời nào đang chờ gửi tới")]
    [InlineData(false, "chưa được xác minh")]
    public async Task TrangLoiMoiTrongNoiRoDangTimTheoEmailNao(bool confirmed, string expected)
    {
        // Mở email lời mời nhưng đăng nhập sai tài khoản: trang nói đang tìm theo email nào và cách xử lý.
        var html = System.Net.WebUtility.HtmlDecode(await Tang4ViewTests.Render("Invitations", "Index",
            new MindSprintAI_ASP.NET_MVC.Services.InvitationPage([], [], "gv.lan@truong.edu.vn", confirmed)));
        Assert.Contains(expected, html);
        Assert.Contains("gv.lan@truong.edu.vn", html);
        Assert.Contains("đăng nhập (hoặc đăng ký) bằng đúng email đó", html);
        Assert.Contains("Bạn chưa gửi lời mời nào", html);
    }

    [Theory]
    [InlineData("Lớp 10A", "Lớp 10A")]
    [InlineData("lớp tối thứ 3", "lớp tối thứ 3")]
    [InlineData("CTDL 01", "lớp CTDL 01")]
    public void NutQuayLaiKhongLapChuLop(string name, string expected) =>
        Assert.Equal(expected, MindSprintAI_ASP.NET_MVC.Models.DisplayText.ClassName(name));

    [Fact]
    public void NutTuChoiHoiLaiRiengTrongFormDuyet()
    {
        // Form Duyệt/Từ chối dùng chung: chỉ nút Từ chối hỏi lại (data-confirm trên nút, site.js đọc e.submitter).
        Assert.Contains("e.submitter?.dataset?.confirm", Read("wwwroot/js/site.js"));
        foreach (var path in new[] { "Views/Assignments/_DeviceChange.cshtml", "Views/Assignments/_ApproveStranger.cshtml" })
            Assert.Matches("value=\"false\"[^>]*data-confirm=", Read(path));
    }

    [Fact]
    public void ThaoTacKhongHoanTacNoiRoHauQua()
    {
        // Xóa lớp và chấm lại không đảo ngược được: câu hỏi lại phải nói điều đó, không chỉ "Xóa lớp?".
        var classroom = Read("Views/Classrooms/Details.cshtml");
        Assert.Contains("bạn không tự khôi phục lớp được", classroom);
        Assert.Contains("<h3 class=\"h6 mt-4 pt-3 border-top\">Xóa lớp</h3>", classroom);
        var regrade = Read("Views/Assignments/_RegradeForm.cshtml");
        Assert.Contains("không hoàn tác được", regrade);
        Assert.DoesNotContain("cách 1", regrade);
    }

    [Fact]
    public void ThanhBenCoTenNganVaThungRacChung()
    {
        var layout = Read("Views/Shared/_Layout.cshtml");
        // MindSprint 15/11: tên mục theo menu nhóm (Trang chủ/Thư viện/Luyện tập); vào lớp bằng mã chuyển xuống nút cuối thanh bên.
        foreach (var label in new[] { "Hôm nay", "Thư viện", "Luyện tập", "Lớp", "Việc", "Quản trị", "Thùng rác", "Báo cáo" })
            Assert.Contains($"class=\"nav-short\" aria-hidden=\"true\">{label}</span>", layout);
        // MindSprint: chuông thông báo nằm ở header ứng dụng như giao diện nhóm, không còn trong thanh bên.
        var bell = layout.Split('\n').Single(l => l.Contains("id=\"notification-bell\""));
        Assert.Contains("class=\"app-icon-btn", bell);
        Assert.True(layout.IndexOf("class=\"app-topbar\"", StringComparison.Ordinal) < layout.IndexOf("id=\"notification-bell\"", StringComparison.Ordinal));
        Assert.Contains("id=\"notification-unread\"", layout);
        Assert.Contains("asp-action=\"Trash\"", layout);
        // Học viên cũng có báo cáo và hội thoại trợ lý trong Thùng rác, nên không giấu theo chế độ Giảng viên.
        var trashLine = layout.Split('\n').First(l => l.Contains("sidebar-trash"));
        Assert.DoesNotContain("lecturer", trashLine);
        var before = layout[..layout.IndexOf("sidebar-trash", StringComparison.Ordinal)];
        Assert.DoesNotContain("@if (lecturer", before[before.LastIndexOf("sidebar-secondary", StringComparison.Ordinal)..]);
        Assert.Contains("0.6875rem", Read("wwwroot/css/mindsprint.css"));
        foreach (var path in new[] { "Views/StudySets/Index.cshtml", "Views/Assignments/Details.cshtml" })
            Assert.DoesNotContain("asp-action=\"Trash\"", Read(path));
    }

    [Fact]
    public void BangDiemGiuTimKiemNgoaiVungLiveVaCoDuPhong()
    {
        var script = Read("wwwroot/js/gradebook-pager.js");
        foreach (var text in new[] { "MutationObserver", "mindsprint-gradebook-size", "try", "catch", "normalize('NFD')", "aria-live", "observer.disconnect()" })
            Assert.Contains(text, script);
        var view = Read("Views/Assignments/Details.cshtml");
        Assert.True(view.IndexOf("id=\"gradebook-search\"", StringComparison.Ordinal) < view.IndexOf("id=\"assignment-live\"", StringComparison.Ordinal));
        Assert.Contains("hidden id=\"assignment-qr-fallback\"", view);
        Assert.Contains("fallback.hidden = false", Read("wwwroot/js/assignment-tabs.js"));
    }
}
