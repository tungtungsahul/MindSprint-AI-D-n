using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MindSprintAI_ASP.NET_MVC.Data.Entities;
using MindSprintAI_ASP.NET_MVC.Services;

namespace MindSprintAI_ASP.NET_MVC.Tests;

public class SecurityRound3PureTests
{
    private static WebApplication RateApp()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddAccountRateLimits();
        return builder.Build();
    }

    private static RequestDelegate RatePipeline(WebApplication app)
    {
        var pipeline = new ApplicationBuilder(app.Services);
        pipeline.UseRateLimiter();
        pipeline.Run(_ => Task.CompletedTask);
        return pipeline.Build();
    }

    private static async Task<int> SendAsync(WebApplication app, RequestDelegate request, string address,
        string policy = "account-post", string? userId = null)
    {
        var http = new DefaultHttpContext { RequestServices = app.Services };
        http.Connection.RemoteIpAddress = IPAddress.Parse(address);
        http.Request.Method = "POST";
        http.Response.Body = new MemoryStream();
        if (userId != null)
            http.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId)], "test"));
        http.SetEndpoint(new Endpoint(_ => Task.CompletedTask,
            new EndpointMetadataCollection(new EnableRateLimitingAttribute(policy)), policy));
        await request(http);
        return http.Response.StatusCode;
    }

    [Fact]
    public async Task T4_Ipv6DoiDiaChiTrongCung64VanChung100LanMoiPhut()
    {
        await using var app = RateApp(); var request = RatePipeline(app);
        for (var i = 1; i <= 100; i++)
            Assert.Equal(200, await SendAsync(app, request, $"2001:db8:1234:5678::{i:x}"));
        Assert.Equal(429, await SendAsync(app, request, "2001:db8:1234:5678::101"));
        Assert.Equal(429, await SendAsync(app, request, "2001:db8:1234:5678:ffff:ffff:ffff:ffff"));
        Assert.Equal(200, await SendAsync(app, request, "2001:db8:1234:5679::1"));
    }

    [Fact]
    public async Task T4_Ipv4CungMangVanTinhRiengTheoTungDiaChi()
    {
        await using var app = RateApp(); var request = RatePipeline(app);
        for (var i = 0; i < 100; i++)
        {
            Assert.Equal(200, await SendAsync(app, request, "192.0.2.1"));
            Assert.Equal(200, await SendAsync(app, request, "192.0.2.2"));
        }
        Assert.Equal(429, await SendAsync(app, request, "192.0.2.1"));
        Assert.Equal(429, await SendAsync(app, request, "192.0.2.2"));
    }

    [Fact]
    public async Task T4_AiVan10LanMoiPhutTheoNguoiDungDocLapAccountPost()
    {
        await using var app = RateApp(); var request = RatePipeline(app);
        for (var i = 0; i < 10; i++)
            Assert.Equal(200, await SendAsync(app, request, $"2001:db8::{i + 1:x}", "ai-question", "teacher-1"));
        Assert.Equal(429, await SendAsync(app, request, "2001:db8:1::1", "ai-question", "teacher-1"));
        Assert.Equal(200, await SendAsync(app, request, "2001:db8::1", "ai-question", "teacher-2"));
        Assert.Equal(200, await SendAsync(app, request, "2001:db8::1", "account-post", "teacher-1"));
        var options = AccountRateLimits.AiOptions();
        Assert.Equal(10, options.PermitLimit); Assert.Equal(TimeSpan.FromMinutes(1), options.Window);
    }
}

[Collection("db")]
public class SecurityRound3DbTests
{
    [Fact]
    public async Task T2_GvCuRoiLopKhongChamLaiDuocQuizDaXoaSauChuyenLop()
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(1);
        await h.Assignments.CloseNowAsync(s.Assignment.Id, s.Lecturer);
        await h.Library.DeleteQuizAsync(s.Quiz.Id, s.Lecturer);
        var receiverId = await h.UserAsync("Người nhận lớp");
        var receiver = (await h.Db.Users.FindAsync(receiverId))!;
        receiver.EmailConfirmed = true; await h.Db.SaveChangesAsync();
        var transfers = new ClassTransferService(h.Db, h.Classrooms, new(h.Db, h.Clock),
            new(h.Db, h.Clock, h.Notifier), h.Clock, h.Invitations);
        var transfer = await transfers.OfferAsync(s.Room.Id, receiver.Email!, false, s.Lecturer);
        await transfers.DecideAsync(transfer.Id, true, receiver.Id);

        Assert.Equal(s.Quiz.Id, s.Assignment.QuizId); // Quiz gốc chỉ còn dùng cho lịch sử.
        Assert.NotEqual(s.Quiz.StudySetId, s.Assignment.StudySetId);
        Assert.Equal(ClassMemberStatus.Left,
            (await h.Db.ClassMembers.SingleAsync(m => m.ClassroomId == s.Room.Id && m.UserId == s.Lecturer)).Status);
        await Assert.ThrowsAsync<ForbiddenException>(() => h.Regrades.GetAuthorizedAsync(s.Assignment.Id, s.Lecturer));
        await Assert.ThrowsAsync<ForbiddenException>(() => h.Regrades.QuestionsAsync(s.Assignment.Id, s.Lecturer));
        await Assert.ThrowsAsync<ForbiddenException>(() => h.Regrades.ApplyAsync(s.Assignment.Id,
            new(s.Quiz.Questions.Single().Id, RegradeMethod.FullCredit, "Sửa điểm", Guid.NewGuid(), []), s.Lecturer));
        Assert.Empty(await h.Db.Regrades.Where(r => r.AssignmentId == s.Assignment.Id).ToListAsync());
        Assert.Equal(s.Assignment.Id, (await h.Regrades.GetAuthorizedAsync(s.Assignment.Id, receiver.Id)).Id);
    }

    [Theory]
    [InlineData(false), InlineData(true)]
    public async Task T2_ChuBoHocDangDungVanChamLaiDuocKeCaBaiCuChuaCoStudySetId(bool legacy)
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(1);
        var owner = await h.UserAsync("Chủ bộ học ngoài lớp");
        (await h.Db.StudySets.FindAsync(s.Quiz.StudySetId))!.OwnerId = owner;
        if (legacy) s.Assignment.StudySetId = null;
        await h.Db.SaveChangesAsync();
        Assert.False(await h.Db.ClassMembers.AnyAsync(m => m.ClassroomId == s.Room.Id && m.UserId == owner));
        Assert.Equal(s.Assignment.Id, (await h.Regrades.GetAuthorizedAsync(s.Assignment.Id, owner)).Id);
        Assert.Single(await h.Regrades.QuestionsAsync(s.Assignment.Id, owner));
        // GV đang hoạt động vẫn có quyền dù không còn sở hữu bộ học.
        Assert.Equal(s.Assignment.Id, (await h.Regrades.GetAuthorizedAsync(s.Assignment.Id, s.Lecturer)).Id);
        Assert.NotNull(await h.Regrades.ApplyAsync(s.Assignment.Id,
            new(s.Quiz.Questions.Single().Id, RegradeMethod.FullCredit, "Sửa điểm", Guid.NewGuid(), []), owner));
    }

    [Theory]
    [InlineData(false), InlineData(true)]
    public async Task T3_MaDungKhongDatLaiBoDem(bool anonymous)
    {
        using var h = new Harness(); var s = await h.ScenarioAsync(1);
        var userId = anonymous ? null : s.Student;
        var network = Guid.NewGuid().ToString("N");
        var remoteIp = $"2001:db8:{network[..4]}:{network[4..8]}::1";
        var guard = new JoinCodeGuardService(h.Db, h.Codes, h.Clock, Tang5bPureTests.Turnstile());
        var browser = anonymous ? "may-t3" : null; // khách: 10 lần sai tính theo trình duyệt (chốt 13/11/2026)
        for (var i = 0; i < 9; i++)
            await Assert.ThrowsAsync<BusinessException>(() => guard.ResolveAsync("wrong", userId, null, remoteIp, browser));
        Assert.False(await guard.RequiresAsync(userId, remoteIp, browser));
        Assert.NotNull(await guard.ResolveAsync(s.ClassCode, userId, null, remoteIp, browser));
        await Assert.ThrowsAsync<BusinessException>(() => guard.ResolveAsync("wrong", userId, null, remoteIp, browser));
        Assert.True(await guard.RequiresAsync(userId, remoteIp, browser));
        var row = Assert.Single(h.Db.ChangeTracker.Entries<JoinCodeFailure>(), e => e.Entity.KeyType == (anonymous ? 3 : 1)).Entity;
        Assert.Equal(10, row.FailCount);
        Assert.Equal(anonymous ? 3 : 1, row.KeyType);

        // 5b mới (12/11): áp dụng cho cả khách lẫn người đã đăng nhập.
        {
            await Assert.ThrowsAsync<BusinessException>(() => guard.ResolveAsync(s.ClassCode, userId, null, remoteIp, browser));
            Assert.NotNull(await guard.ResolveAsync(s.ClassCode, userId, "fake-valid", remoteIp, browser));
            Assert.True(await guard.RequiresAsync(userId, remoteIp, browser));
            Assert.Equal(10, row.FailCount);
            h.Clock.UtcNow = h.Clock.UtcNow.AddHours(24);
            Assert.False(await guard.RequiresAsync(userId, remoteIp, browser));
        }
    }
}
