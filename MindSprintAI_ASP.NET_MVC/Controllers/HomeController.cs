using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using MindSprintAI_ASP.NET_MVC.Data.Entities;
using MindSprintAI_ASP.NET_MVC.Models;
using MindSprintAI_ASP.NET_MVC.Services;

namespace MindSprintAI_ASP.NET_MVC.Controllers;

[AllowAnonymous]
public class HomeController(UserManager<ApplicationUser> users) : Controller
{
    // Trang chủ kiểu nhóm cho cả hai chế độ (người dùng chốt 15/11/2026, thay cho chuyển hướng theo vai 37B/38A);
    // chưa đăng nhập thì cùng trang, bản cho khách (model null). Khách vào bằng mã về trang nhập mã như cũ.
    public async Task<IActionResult> Index([FromServices] HomeService? home = null)
    {
        if (User.Identity?.IsAuthenticated != true) return View();
        var user = await users.GetUserAsync(User);
        if (user == null) return View();
        if (user.IsGuest) return RedirectToAction("Join", "Classrooms");
        home ??= HttpContext.RequestServices.GetRequiredService<HomeService>();
        return View(await home.GetAsync(user));
    }

    // "Trợ giúp" cuối thanh bên như nhóm (bản nhóm chỉ là dòng chữ; ở đây là trang hướng dẫn thật).
    [HttpGet("tro-giup"), AllowGuest, AllowWithoutPolicy]
    public IActionResult Help() => View();

    [HttpGet("chinh-sach"), AllowGuest, AllowWithoutPolicy]
    public IActionResult Privacy([FromServices] IConfiguration config) => View(LegalInfo.From(config));

    [HttpGet("dieu-khoan"), AllowGuest, AllowWithoutPolicy]
    public IActionResult Terms([FromServices] IConfiguration config) => View(LegalInfo.From(config));

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true), AllowWithoutPolicy]
    public IActionResult Error() =>
        View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
}
