using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using MindSprintAI_ASP.NET_MVC.Data.Entities;
using MindSprintAI_ASP.NET_MVC.Services;
using MindSprintAI_ASP.NET_MVC.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authentication;

namespace MindSprintAI_ASP.NET_MVC.Controllers;

public class ClassroomsController(ClassroomService classrooms, AssignmentService assignments, JoinCodeService codes,
    JoinCodeGuardService guard, TurnstileService turnstile, GuestService guests, SignInManager<ApplicationUser> signIn,
    UserSettingPresetService presets = null!) : AppController
{
    public async Task<IActionResult> Index()
    {
        // Chế độ Học viên: trang Lớp liệt kê lớp đã tham gia, không hiện phần tạo/dạy lớp của giảng viên.
        var me = signIn == null ? null : await signIn.UserManager.GetUserAsync(User);
        var learner = me != null && me.CurrentMode != UserMode.Lecturer;
        ViewData["LearnerMode"] = learner;
        if (!learner) ViewData["LecturerSummaries"] = await classrooms.GetLecturerSummariesAsync(UserId);
        return View(await classrooms.ListMyClassesAsync(UserId));
    }

    [HttpPost]
    public async Task<IActionResult> Create(string name, ClassJoinPolicy joinPolicy)
    {
        var room = await classrooms.CreateAsync(name, joinPolicy, UserId);
        return RedirectToAction(nameof(Details), new { id = room.Id });
    }

    public async Task<IActionResult> Details(int id, int page = 1, [FromServices] RankingService? ranking = null)
    {
        var room = await classrooms.GetForLecturerAsync(id, UserId);
        if (ranking != null) ViewData["Ranking"] = await ranking.ForClassAsync(id, UserId);
        var size = await presets.MemberPageSizeAsync(UserId);
        var count = room.Members.Count(m => m.Role == ClassRole.Student && m.Status == ClassMemberStatus.Active);
        ViewData["MemberPage"] = Math.Clamp(page, 1, Math.Max(1, (int)Math.Ceiling(count / (double)size)));
        ViewData["MemberPageSize"] = size;
        ViewData["Code"] = (await codes.GetActiveAsync(JoinCodeTarget.Classroom, id))?.Code;
        ViewData["UndoCodeUntil"] = await codes.UndoUntilAsync(JoinCodeTarget.Classroom, id);
        ViewData["Assignments"] = await assignments.ListForClassAsync(id);
        ViewData["AssignmentWork"] = await classrooms.GetAssignmentWorkAsync(id);
        ViewData["MainLecturer"] = await classrooms.IsMainLecturerAsync(id, UserId);
        return View(room);
    }

    [HttpPost]
    public async Task<IActionResult> MemberPageSize(int id, int size)
    {
        await classrooms.GetForLecturerAsync(id, UserId);
        await presets.SaveMemberPageSizeAsync(size, UserId);
        Success("Đã đổi số học viên mỗi trang.");
        return RedirectToAction(nameof(Details), null, new { id, page = 1 }, "class-members-title");
    }

    [HttpPost]
    public async Task<IActionResult> JoinPolicy(int id, ClassJoinPolicy policy)
    {
        await classrooms.ChangeJoinPolicyAsync(id, policy, UserId);
        Success("Đã lưu cách vào lớp.");
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> ChangeCode(int id, bool undo = false)
    {
        var code = await classrooms.ChangeCodeAsync(id, UserId, undo);
        Success(undo ? $"Đã dùng lại mã cũ {code.Code}. Người đã vào lớp vẫn giữ nguyên." : $"Đã đổi mã vào lớp thành {code.Code}. Mã cũ hết hiệu lực.");
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpGet, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Qr(int id)
    {
        await classrooms.GetForLecturerAsync(id, UserId);
        var code = (await codes.GetActiveAsync(JoinCodeTarget.Classroom, id))?.Code
            ?? throw new BusinessException("Lớp chưa có mã.");
        return View("~/Views/Shared/JoinQr.cshtml", new JoinQrModel(code,
            Url.Action(nameof(Join), "Classrooms", new { code }, Request.Scheme)!,
            Url.Action(nameof(Join), "Classrooms", null, Request.Scheme)!));
    }

    [HttpPost]
    public async Task<IActionResult> Decide(int id, int memberId, bool approve)
    {
        await classrooms.DecideAsync(memberId, approve, UserId);
        Success(approve ? "Đã duyệt vào lớp." : "Đã từ chối.");
        return RedirectToAction(nameof(Details), null, new { id }, "class-pending");
    }

    [HttpPost]
    public async Task<IActionResult> RemoveMember(int id, int memberId, bool confirmed = false)
    {
        var confirmation = await classrooms.RemoveMemberAsync(memberId, UserId, confirmed);
        if (confirmation != null) return View("ConfirmMembership", confirmation);
        Success("Đã xóa học viên khỏi lớp.");
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> Leave(int id, bool confirmed = false)
    {
        var confirmation = await classrooms.LeaveAsync(id, UserId, confirmed);
        if (confirmation != null) return View("ConfirmMembership", confirmation);
        Success("Đã rời lớp. Bài và điểm cũ vẫn xem được.");
        return RedirectToAction("Index", "Learn");
    }

    [AllowAnonymous, AllowGuest]
    public async Task<IActionResult> Join()
    {
        ViewData["Code"] = Request.Query["code"].ToString();
        await ChallengeAsync();
        return View();
    }

    private async Task ChallengeAsync()
    {
        ViewData["ChallengeRequired"] = await guard.RequiresAsync(User.Identity?.IsAuthenticated == true && !GuestAccess.IsGuest(User) ? UserId : null,
            HttpContext.Connection.RemoteIpAddress?.ToString(), LearnDevice.Read(HttpContext));
        ViewData["TurnstileSiteKey"] = turnstile.Configured ? turnstile.SiteKey : "";
    }

    [AllowAnonymous, AllowGuest, HttpPost, EnableRateLimiting(AccountRateLimits.JoinCodePolicy)]
    public async Task<IActionResult> Join(string code, string? displayName = null, bool acceptPolicy = false)
    {
        ViewData["GuestName"] = displayName;
        code = new string((code ?? "").Where(c => !char.IsWhiteSpace(c)).ToArray());
        ViewData["Code"] = code;
        JoinCode resolved;
        try
        {
            resolved = await guard.ResolveAsync(code, User.Identity?.IsAuthenticated == true && !GuestAccess.IsGuest(User) ? UserId : null,
                Request.Form["cf-turnstile-response"], HttpContext.Connection.RemoteIpAddress?.ToString(), LearnDevice.Read(HttpContext));
        }
        catch (BusinessException ex)
        { await RecordRejectionAsync(ex);
            ModelState.AddModelError("", ex.Message);
            await ChallengeAsync();
            return View();
        }
        if (resolved.TargetType == JoinCodeTarget.Assignment && (User.Identity?.IsAuthenticated != true || GuestAccess.IsGuest(User)))
        {
            var entryOk = false; // bài đã đóng/không vào được thì không hỏi tên khách nữa
            try
            {
                var token = Request.Cookies[GuestService.CookieName];
                await guests.CheckEntryAsync(resolved.TargetId, token);
                entryOk = true;
                var resumed = await guests.ResumeAsync(token);
                if (resumed == null && string.IsNullOrWhiteSpace(displayName))
                {
                    ViewData["GuestNameRequired"] = true;
                    await ChallengeAsync();
                    return View();
                }
                // Pháp lý C3: khách mới, hoặc khách cũ vào bài chưa từng vào, phải tick ô đồng ý; GuestService ghi lại lúc đồng ý.
                if (!acceptPolicy && await guests.NeedsConsentAsync(resolved.TargetId, token))
                {
                    // Lần đầu khách cũ tới bài mới chưa thấy ô nên chưa báo lỗi; khách mới thì đã thấy ô cạnh ô tên.
                    if (resumed == null || Request.Form.ContainsKey("acceptPolicyShown"))
                        ModelState.AddModelError("", "Hãy tick vào ô đồng ý để tiếp tục.");
                    ViewData["GuestNameRequired"] = resumed == null;
                    ViewData["GuestConsentRequired"] = true;
                    await ChallengeAsync();
                    return View();
                }
                if (token == null || GuestService.HashToken(token) == null) token = GuestService.NewToken();
                var guest = await guests.JoinAsync(resolved.TargetId, displayName, token);
                var expires = await assignments.GetGuestLifetimeAsync(resolved.TargetId);
                Response.Cookies.Append(GuestService.CookieName, token, new CookieOptions { HttpOnly = true, Secure = Request.IsHttps,
                    SameSite = SameSiteMode.Lax, Path = "/", IsEssential = true, MaxAge = TimeSpan.FromDays(365) });
                await signIn.SignInAsync(guest.User, new AuthenticationProperties { IsPersistent = true, ExpiresUtc = expires });
                return RedirectToAction("Assignment", "Learn", new { id = resolved.TargetId });
            }
            catch (BusinessException ex)
            { await RecordRejectionAsync(ex);
                ModelState.AddModelError("", ex.Message);
                ViewData["GuestNameRequired"] = entryOk;
                await ChallengeAsync();
                return View();
            }
        }
        if (GuestAccess.IsGuest(User)) throw new ForbiddenException();
        if (User.Identity?.IsAuthenticated != true) return RedirectToAction("Login", "Account",
            new { returnUrl = Url.Action(nameof(Join), "Classrooms", new { code }) });
        var result = await classrooms.JoinByCodeAsync(code, UserId);
        switch (result.Outcome)
        {
            case JoinOutcome.OpenAssignment:
                return RedirectToAction("Assignment", "Learn", new { id = result.AssignmentId });
            case JoinOutcome.PendingApproval:
                Success("Đang chờ giảng viên duyệt. Khi được duyệt bạn sẽ thấy bài của lớp.");
                break;
            case JoinOutcome.AlreadyMember:
                Success("Bạn đã ở trong lớp này.");
                if (result.ClassroomId is { } existing) return RedirectToAction(nameof(Room), new { id = existing });
                break;
            default:
                Success("Đã vào lớp.");
                // Góp ý 17/11: vào lớp xong thì mở luôn trang lớp (bài được giao, xếp hạng), không về trang Luyện tập chung.
                if (result.ClassroomId is { } joined) return RedirectToAction(nameof(Room), new { id = joined });
                break;
        }
        return RedirectToAction("Index", "Learn");
    }

    // Trang lớp của học viên (góp ý 17/11): bài của lớp với nút vào bài ngay (không cần mã 8 số), xếp hạng, thành viên.
    public async Task<IActionResult> Room(int id, [FromServices] AttemptService attempts, [FromServices] RankingService ranking)
    {
        if (await classrooms.IsLecturerAsync(id, UserId)) return RedirectToAction(nameof(Details), new { id });
        var room = await classrooms.MemberNamesAsync(id, UserId);   // chỉ thành viên đang học
        ViewData["Assignments"] = (await assignments.ListForLearnerAsync(UserId)).Where(a => a.ClassroomId == id).ToList();
        ViewData["Participations"] = await attempts.ListMyParticipationsAsync(UserId);
        ViewData["Ranking"] = await ranking.ForClassAsync(id, UserId);
        return View(room);
    }

    [HttpPost]
    public async Task<IActionResult> Leaderboard(int id, bool enabled, [FromServices] RankingService ranking)
    {
        await ranking.SetEnabledAsync(id, enabled, UserId);
        Success(enabled ? "Học viên đã xem được bảng xếp hạng." : "Đã ẩn bảng xếp hạng với học viên.");
        return Redirect(Url.Action(nameof(Details), new { id }) + "#class-rank-title");
    }

    public async Task<IActionResult> Members(int id) => View(await classrooms.MemberNamesAsync(id, UserId));

    [HttpPost]
    public async Task<IActionResult> AddAssistant(int id, string email)
    {
        await classrooms.AddAssistantAsync(id, email, UserId);
        Success(InvitationService.SentMessage);
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> RemoveAssistant(int id, int memberId)
    {
        await classrooms.RemoveAssistantAsync(id, memberId, UserId);
        Success("Đã gỡ giảng viên phụ.");
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> Delete(int id)
    {
        await classrooms.DeleteAsync(id, UserId);
        Success("Đã xóa lớp.");
        return RedirectToAction(nameof(Index));
    }
}
