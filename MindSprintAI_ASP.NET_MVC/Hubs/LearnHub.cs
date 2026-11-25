using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using MindSprintAI_ASP.NET_MVC.Services;

namespace MindSprintAI_ASP.NET_MVC.Hubs;

[Authorize]
public sealed class LearnHub(LearnConnectionService connections, AiAssistantService? ai = null) : Hub
{
    public Task JoinAi(int assignmentId) => Run(async () =>
    {
        if (ai == null) throw new ForbiddenException();
        await ai.StateAsync(assignmentId, Context.UserIdentifier ?? "");
        await Groups.AddToGroupAsync(Context.ConnectionId, SignalRLearnNotifier.AiGroup(assignmentId));
    });
    [AllowGuest]
    public Task JoinAttempt(long attemptId) => Run(() => connections.JoinAttemptAsync(
        Context.ConnectionId, attemptId, Context.UserIdentifier ?? "", LearnDevice.Read(Context.GetHttpContext())));

    [AllowGuest]
    public Task JoinWaiting(int assignmentId) => Run(() => connections.JoinWaitingAsync(
        Context.ConnectionId, assignmentId, Context.UserIdentifier ?? ""));

    public Task JoinLecturer(int assignmentId) => Run(() => connections.JoinLecturerAsync(
        Context.ConnectionId, assignmentId, Context.UserIdentifier ?? ""));

    public override Task OnDisconnectedAsync(Exception? exception) => connections.DisconnectAsync(Context.ConnectionId);

    private static async Task Run(Func<Task> action)
    {
        try { await action(); }
        catch (ForbiddenException) { throw new HubException("Bạn không có quyền vào phòng này."); }
        catch (BusinessException e) { throw new HubException(e.Message); }
    }
}
