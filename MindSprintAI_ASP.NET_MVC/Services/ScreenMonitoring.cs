using Microsoft.EntityFrameworkCore;
using MindSprintAI_ASP.NET_MVC.Data;
using MindSprintAI_ASP.NET_MVC.Data.Entities;
using MindSprintAI_ASP.NET_MVC.Models;

namespace MindSprintAI_ASP.NET_MVC.Services;

public record ScreenSummary(int Count, int TotalSeconds, bool Away, bool Highlight, string? LatestTime, int? LatestSeconds, bool SignalLost = false,
    int LossCount = 0, int LossSeconds = 0, int OfflineCount = 0);
public record ScreenReply(int Count, string? Banner, bool Ended, string ReviewUrl);
public record OfflineLeave(ScreenLeaveKind Kind, int AgoSeconds, int? Seconds);
public static class ScreenMonitoringRules
{
    public const int OfflineLimit = 20; // (H10) tối đa số lần rời lúc mất mạng được báo lại mỗi lượt
    public static int Seconds(ScreenLeave leave, DateTime now) => Math.Max(0, (int)((leave.EndedAt ?? now) - leave.StartedAt).TotalSeconds);
    public static bool Countable(ScreenLeaveKind kind) => kind is ScreenLeaveKind.Hidden or ScreenLeaveKind.OtherWindow or ScreenLeaveKind.PageExit;
    public static void Update(ScreenLeave leave, DateTime now, bool close = false)
    {
        if (leave.EndedAt != null) return;
        leave.DurationSeconds = Seconds(leave, now);
        leave.Counted = Countable(leave.Kind) && leave.DurationSeconds >= 2; // (H5, H8)
        if (close) leave.EndedAt = now < leave.StartedAt ? leave.StartedAt : now;
    }
    public static void CloseAll(Attempt a, DateTime now)
    {
        foreach (var leave in a.ScreenLeaves) Update(leave, now, true);
    }
    public static ScreenSummary Summary(Attempt a, DateTime now)
    {
        var latest = a.ScreenLeaves.Where(l => l.Counted).OrderByDescending(l => l.StartedAt).FirstOrDefault();
        return new(a.ScreenLeaves.Count(l => l.Counted), a.ScreenLeaves.Where(l => l.Counted).Sum(l => Seconds(l, now)),
            a.ScreenLeaves.Any(l => l.EndedAt == null && Countable(l.Kind)),
            a.ScreenLeaves.Any(l => l.Counted || l.Kind == ScreenLeaveKind.SignalLoss),
            latest == null ? null : VnTime.Format(latest.StartedAt), latest == null ? null : Seconds(latest, now),
            a.ScreenLeaves.Any(l => l.Kind == ScreenLeaveKind.SignalLoss && l.EndedAt == null),
            a.ScreenLeaves.Count(l => l.Kind == ScreenLeaveKind.SignalLoss),
            a.ScreenLeaves.Where(l => l.Kind == ScreenLeaveKind.SignalLoss).Sum(l => Seconds(l, now)),
            a.ScreenLeaves.Count(l => l.Counted && l.ReportedOffline));
    }
    public static string? Banner(int? seconds, int count, int? limit) => seconds == null ? null
        : $"Bạn vừa rời màn hình làm bài {seconds} giây. Giảng viên đã được báo."
            + (limit is int n && n > count ? $" Còn {n - count} lần nữa bài sẽ tự nộp." : ""); // đã đủ N thì bài đã nộp, không nhắc "còn 0 lần".
    public static string EndText(Attempt a) => a.EndReason == AttemptEndReason.ScreenLeaveLimit
        ? $"Tự nộp vì rời màn hình đến lần thứ {a.AutoSubmitLeaves}" : DisplayText.Of(a.EndReason);
    public static string KindText(ScreenLeaveKind kind) => kind switch
    {
        ScreenLeaveKind.Hidden => "Chuyển trang khác hoặc thu nhỏ trình duyệt",
        ScreenLeaveKind.OtherWindow => "Chuyển cửa sổ hoặc ứng dụng khác",
        ScreenLeaveKind.PageExit => "Rời trang làm bài",
        _ => "Mất tín hiệu giám sát"
    };
}

public partial class AttemptService
{
    // (H2) clientAt chỉ dùng để nhận định dạng giao thức; tuyệt đối không dùng tính giờ.
    public async Task<ScreenReply> ScreenEventAsync(long id, string userId, string action, ScreenLeaveKind? kind = null, DateTimeOffset? clientAt = null,
        IReadOnlyList<OfflineLeave>? offline = null)
    {
        if (action is not ("start" or "end" or "heartbeat" or "offline") || kind == ScreenLeaveKind.SignalLoss
            || kind != null && !Enum.IsDefined(kind.Value)
            || action == "offline" && (offline is not { Count: > 0 and <= ScreenMonitoringRules.OfflineLimit }
                || offline.Any(o => o.Kind is not (ScreenLeaveKind.Hidden or ScreenLeaveKind.OtherWindow))))
            throw new BusinessException("Thông tin theo dõi không hợp lệ.");
        if (action is "end" or "heartbeat" && kind == null && await HeartbeatFastAsync(id, userId))
            return new(await db.ScreenLeaves.CountAsync(l => l.AttemptId == id && l.Counted), null, false, $"/Learn/Review/{id}");
        await using var tx = await db.Database.BeginTransactionAsync();
        var a = await LoadForLearnerAsync(id, userId, writing: true);
        if (!a.MonitorScreen || a.DeviceConsentAt == null) throw new ForbiddenException();
        var lastSeen = a.MonitorHeartbeatAt ?? a.StartedAt; // (H10) mốc trước khi mất mạng, đọc trước khi tín hiệu này ghi đè.
        if (!AttemptRules.IsOpen(a))
        {
            // (H10) bài đã kết thúc: chỉ ghi vào nhật ký, không tự nộp hay chuông.
            if (action == "offline") { AddOfflineLeaves(a, lastSeen, offline!, a.EndedAt ?? Now); await db.SaveChangesAsync(); }
            await tx.CommitAsync(); await NotifyLiveChangesAsync();
            return new(a.ScreenLeaves.Count(l => l.Counted), null, true, $"/Learn/Review/{id}");
        }
        // (H2) bền qua restart, dùng cùng khóa hàng; cả heartbeat cũng chịu hạn mức.
        if (a.MonitorWindowAt == null || a.MonitorWindowAt <= Now.AddMinutes(-1))
        { a.MonitorWindowAt = Now; a.MonitorEventCount = 0; }
        if (a.MonitorEventCount >= 60) throw new BusinessException("Bạn thao tác quá nhanh. Hãy chờ một chút.");
        a.MonitorEventCount++;
        var screenBefore = ScreenState(a);
        await ObserveScreenAsync(a); // bắt lại cả khoảng mất tín hiệu trước heartbeat này.
        foreach (var loss in a.ScreenLeaves.Where(l => l.Kind == ScreenLeaveKind.SignalLoss && l.EndedAt == null))
            ScreenMonitoringRules.Update(loss, Now, true);
        a.MonitorHeartbeatAt = Now;
        int? returnedSeconds = null;
        var open = a.ScreenLeaves.FirstOrDefault(l => l.EndedAt == null && ScreenMonitoringRules.Countable(l.Kind));
        if (AttemptRules.IsOpen(a) && action == "start")
        {
            if (kind == null) throw new BusinessException("Chọn việc vừa xảy ra.");
            if (open == null) a.ScreenLeaves.Add(new() { Kind = kind.Value, StartedAt = Now });
            // (H2) blur/hidden/pagehide cùng một lần rời, giữ mốc sớm nhất và loại cụ thể nhất.
            else if (kind == ScreenLeaveKind.PageExit || kind == ScreenLeaveKind.Hidden && open.Kind == ScreenLeaveKind.OtherWindow)
                open.Kind = kind.Value;
        }
        else if (action == "end" && open != null)
        {
            ScreenMonitoringRules.Update(open, Now, true);
            returnedSeconds = open.DurationSeconds;
        }
        else if (action == "offline") returnedSeconds = AddOfflineLeaves(a, lastSeen, offline!, Now);
        await ObserveScreenAsync(a);
        if (ScreenState(a) != screenBefore) liveChanges.Add(a.Participant.AssignmentId); // gồm cả start/end giữa hai lần quan sát.
        await db.SaveChangesAsync(); await tx.CommitAsync(); await NotifyLiveChangesAsync();
        if (!AttemptRules.IsOpen(a)) await notifier.AttemptAsync(id, "ended", new(AttemptId: id, Url: $"/Learn/Review/{id}"));
        var count = a.ScreenLeaves.Count(l => l.Counted);
        return new(count, ScreenMonitoringRules.Banner(returnedSeconds, count, a.AutoSubmitLeaves), !AttemptRules.IsOpen(a), $"/Learn/Review/{id}");
    }

    // Lối nhanh (người dùng duyệt 13/11/2026 sau thử tải: tín hiệu "vẫn đang làm" 10 giây/máy chiếm ~70% yêu cầu, mỗi tín hiệu lại
    // nạp cả bài làm + kiểm mọi quy tắc). Khi CHẮC CHẮN không có gì phải xử lý thì chỉ ghi "lần cuối thấy" bằng một câu UPDATE
    // (khóa đúng dòng lượt làm). Chỉ cần một điều kiện sai → trả false → đi lối đầy đủ như cũ (ghi rời/quay lại, mất tín hiệu ≥45 giây,
    // tự nộp N lần, chuông GV, hết giờ, máy khác, vượt 60 tín hiệu/phút). Lối nhanh không chốt "câu hết giờ riêng": việc đó vẫn xảy ra
    // khi mở câu/lưu câu trả lời/nộp bài/công việc nền, kết quả chấm không đổi.
    // (H10) Không tin giờ máy học viên: mọi mốc bị kẹp trong [lần cuối máy chủ nhận tín hiệu, until]. Trả thời lượng lần rời cuối đã đóng (dòng nhắc H4).
    private int? AddOfflineLeaves(Attempt a, DateTime lastSeen, IReadOnlyList<OfflineLeave> items, DateTime until)
    {
        static DateTime Clamp(DateTime v, DateTime lo, DateTime hi) => v > hi ? hi : v < lo ? lo : v;
        var ended = !AttemptRules.IsOpen(a);
        int? last = null;
        foreach (var item in items)
        {
            if (a.ScreenLeaves.Count(l => l.ReportedOffline) >= ScreenMonitoringRules.OfflineLimit) break;
            var start = Clamp(Now.AddSeconds(-Math.Max(0, item.AgoSeconds)), lastSeen, until);
            var leave = new ScreenLeave { Kind = item.Kind, StartedAt = start, ReportedOffline = true };
            if (item.Seconds == null && !ended)
            {
                // vẫn đang ở ngoài: mở lần rời, lần "end" kế tiếp đóng như thường; đã có lần rời đang mở thì là cùng một lần.
                if (!a.ScreenLeaves.Any(l => l.EndedAt == null && ScreenMonitoringRules.Countable(l.Kind))) a.ScreenLeaves.Add(leave);
                continue;
            }
            ScreenMonitoringRules.Update(leave, item.Seconds is int s ? Clamp(start.AddSeconds(Math.Max(0, s)), start, until) : until, true);
            a.ScreenLeaves.Add(leave);
            last = leave.DurationSeconds;
        }
        return last;
    }

    private async Task<bool> HeartbeatFastAsync(long id, string userId)
    {
        var deviceId = device.Id;
        var header = await db.Attempts.Where(a => a.Id == id).Select(a => new { a.Participant.AssignmentId, a.Participant.UserId }).SingleOrDefaultAsync();
        if (header == null || header.UserId != userId) return false; // để lối đầy đủ báo lỗi như cũ
        // Tài khoản 2 rà 13/11/2026: phải xếp hàng cùng các yêu cầu khác của chính học viên (khóa chung bài + khóa "ngăn kéo"),
        // và lấy giờ SAU khi có khóa; nếu không, câu UPDATE dưới READ_COMMITTED_SNAPSHOT có thể đọc danh sách lần rời cũ
        // và "nuốt" lần quay lại vừa gửi. Không nạp bài làm: chỉ hai câu khóa nhẹ + một câu UPDATE.
        await using var tx = await db.Database.BeginTransactionAsync();
        await AttemptLock.LearnerLockOnlyAsync(db, header.AssignmentId, userId);
        var now = Now;
        var windowStart = now.AddMinutes(-1);
        var lossFrom = now.AddSeconds(-45);
        var rows = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE a SET
                MonitorHeartbeatAt = {now},
                MonitorWindowAt = CASE WHEN a.MonitorWindowAt IS NULL OR a.MonitorWindowAt <= {windowStart} THEN {now} ELSE a.MonitorWindowAt END,
                MonitorEventCount = CASE WHEN a.MonitorWindowAt IS NULL OR a.MonitorWindowAt <= {windowStart} THEN 1 ELSE a.MonitorEventCount + 1 END,
                Version = a.Version + 1
            FROM Attempts a
            JOIN Participants p ON p.Id = a.ParticipantId
            JOIN Assignments s ON s.Id = p.AssignmentId
            WHERE a.Id = {id} AND p.UserId = {userId}
              AND a.Status IN ({(int)AttemptStatus.InProgress}, {(int)AttemptStatus.Paused})
              AND a.MonitorScreen = 1 AND a.DeviceConsentAt IS NOT NULL
              AND a.ActiveDeviceId COLLATE Latin1_General_BIN2 = {deviceId}
              AND (a.DeadlineAt IS NULL OR a.DeadlineAt > {now})
              AND s.Status <> {(int)AssignmentStatus.Closed}
              AND (s.DueAt IS NULL OR s.DueAt > {now})
              AND COALESCE(a.MonitorHeartbeatAt, a.StartedAt) > {lossFrom}
              AND NOT EXISTS (SELECT 1 FROM ScreenLeaves l WHERE l.AttemptId = a.Id AND l.EndedAt IS NULL)
              AND (a.LeaveBellSent = 1 OR NOT EXISTS (SELECT 1 FROM ScreenLeaves l WHERE l.AttemptId = a.Id AND l.Counted = 1))
              AND (a.AutoSubmitLeaves IS NULL OR (SELECT COUNT(*) FROM ScreenLeaves l WHERE l.AttemptId = a.Id AND l.Counted = 1) < a.AutoSubmitLeaves)
              AND (a.MonitorWindowAt IS NULL OR a.MonitorWindowAt <= {windowStart} OR a.MonitorEventCount < 60)
            """);
        if (rows != 1) return false; // tx bị hủy khi thoát; lối đầy đủ chạy trong giao dịch riêng
        await tx.CommitAsync();
        return true;
    }

    // (H3) chỉ những gì bảng GV hiển thị; heartbeat không đổi gì thì không bắt GV tải lại bảng.
    private static (int, bool, bool, bool) ScreenState(Attempt a) => (a.ScreenLeaves.Count(l => l.Counted),
        a.ScreenLeaves.Any(l => l.EndedAt == null && ScreenMonitoringRules.Countable(l.Kind)),
        a.ScreenLeaves.Any(l => l.EndedAt == null && l.Kind == ScreenLeaveKind.SignalLoss), AttemptRules.IsOpen(a));

    private async Task ObserveScreenAsync(Attempt a)
    {
        if (!a.MonitorScreen || a.DeviceConsentAt == null) return;
        var before = ScreenState(a);
        foreach (var leave in a.ScreenLeaves) ScreenMonitoringRules.Update(leave, Now);
        // (H2) heartbeat dừng dù kết nối vẫn còn cũng phải được phát hiện; dùng mốc disconnect của tracker khi có.
        var last = a.MonitorHeartbeatAt ?? a.StartedAt;
        var lossAt = a.DisconnectedAt is DateTime disconnected && disconnected > last ? disconnected : last;
        if (AttemptRules.IsOpen(a) && Now - last >= TimeSpan.FromSeconds(45) && !a.ScreenLeaves.Any(l => l.Kind == ScreenLeaveKind.SignalLoss && l.EndedAt == null))
            a.ScreenLeaves.Add(new() { Kind = ScreenLeaveKind.SignalLoss, StartedAt = lossAt <= Now ? lossAt : Now }); // (H11) tính từ tín hiệu cuối, gần thời gian mất mạng thật
        var count = a.ScreenLeaves.Count(l => l.Counted);
        if (count > 0 && !a.LeaveBellSent)
        {
            a.LeaveBellSent = true; // (H3, F11) đánh dấu cả khi mọi GV tắt chuông, không báo muộn khi bật lại.
            await BellLecturersAsync(a, NotificationType.ScreenLeave, "Học viên vừa rời màn hình làm bài. Hãy xem tab Giám sát làm bài của bài giao.");
        }
        if (AttemptRules.IsOpen(a) && a.AutoSubmitLeaves is int limit && count >= limit)
        {
            // (H8) cùng End với hết giờ: chấm nháp, làm chủ, trạng thái, điểm liệt và đóng giờ câu.
            AttemptRules.End(a, a.Participant, AttemptEndReason.ScreenLeaveLimit, Now);
            await CardCompletion.RefreshAsync(db, a.Participant, Now);
        }
        if (ScreenState(a) != before) liveChanges.Add(a.Participant.AssignmentId);
    }

    public Task CheckScreenAsync(long id) => CheckScreenAsync(id, retry: true);

    private async Task CheckScreenAsync(long id, bool retry)
    {
        var header = await db.Attempts.Where(a => a.Id == id).Select(a => new { a.ParticipantId, a.Participant.AssignmentId, a.Participant.UserId }).SingleOrDefaultAsync();
        if (header == null) return;
        await using var tx = await db.Database.BeginTransactionAsync();
        await AttemptLock.LearnerAsync(db, header.AssignmentId, header.UserId ?? "participant:" + header.ParticipantId);
        await AttemptLock.ParticipantAsync(db, header.ParticipantId);
        var a = await AttemptLock.LoadAsync(db, id);
        // Đọc người làm trước khi khóa: khách vừa gộp vào tài khoản thì lượt đã sang người khác → thả khóa, đọc lại (13/11/2026).
        if (a.ParticipantId != header.ParticipantId) { await tx.RollbackAsync(); await tx.DisposeAsync(); if (retry) await CheckScreenAsync(id, false); return; }
        var wasOpen = AttemptRules.IsOpen(a);
        AttemptRules.ExpireIfDue(a, a.Participant, Now);
        await ObserveScreenAsync(a);
        await db.SaveChangesAsync(); await tx.CommitAsync(); await NotifyLiveChangesAsync();
        if (wasOpen && !AttemptRules.IsOpen(a)) await notifier.AttemptAsync(id, "ended", new(AttemptId: id, Url: $"/Learn/Review/{id}"));
    }
}

// (H2, H8) máy chủ tự phát hiện mất tín hiệu và đủ 2 giây, không chờ học viên quay lại.
public sealed class ScreenMonitorWorker(IServiceScopeFactory scopes, ILogger<ScreenMonitorWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                using var run = new WorkerRun(logger, nameof(ScreenMonitorWorker)); // (NK1)
                using var scope = scopes.CreateScope();
                try
                {
                    scope.ServiceProvider.GetRequiredService<AuditLog>().Attach();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    var now = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime;
                    var ids = await db.Attempts.Where(a => a.MonitorScreen && a.DeviceConsentAt != null
                        && (a.Status == AttemptStatus.InProgress || a.Status == AttemptStatus.Paused)
                        && ((a.MonitorHeartbeatAt ?? a.StartedAt) <= now.AddSeconds(-45)
                                && !a.ScreenLeaves.Any(l => l.Kind == ScreenLeaveKind.SignalLoss && l.EndedAt == null)
                            || a.ScreenLeaves.Any(l => l.EndedAt == null && !l.Counted && l.Kind != ScreenLeaveKind.SignalLoss && l.StartedAt <= now.AddSeconds(-2))
                            || a.DeadlineAt <= now)).Select(a => a.Id).ToListAsync(stoppingToken);
                    foreach (var id in ids)
                    {
                        await scope.ServiceProvider.GetRequiredService<AttemptService>().CheckScreenAsync(id);
                        run.Count++;
                    }
                }
                catch (Exception) when (!stoppingToken.IsCancellationRequested)
                { run.Failed = true; logger.LogWarning("Chưa cập nhật được theo dõi làm bài. Sẽ thử lại."); }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
