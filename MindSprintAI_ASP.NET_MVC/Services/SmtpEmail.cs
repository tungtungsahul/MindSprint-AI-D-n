using System.Net;
using System.Net.Mail;
using System.Text;
using System.Threading.Channels;

namespace MindSprintAI_ASP.NET_MVC.Services;

// Tên thương hiệu người dùng nhìn thấy (người dùng chốt 2026-11-07). Đổi tên chỉ sửa dòng này.
// KHÔNG dùng cho tên ứng dụng/purpose Data Protection hay tên DB: đổi những chỗ đó làm mất khả năng giải mã dữ liệu cũ.
public static class Brand
{
    public const string Name = "MindSprint AI";
}

public sealed record OutgoingEmail(string To, string Subject, string HtmlBody);

// Cấu hình "Smtp:*" lấy từ user-secrets (dev) hoặc biến môi trường Smtp__* (server), không để trong appsettings.
public sealed class SmtpSettings
{
    public string? Host { get; set; }
    public int Port { get; set; } = 587;
    public string? UserName { get; set; }
    public string? Password { get; set; }
    public string? FromAddress { get; set; }
    public bool Configured => !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(UserName) && !string.IsNullOrWhiteSpace(Password);
}

// Đưa email vào hàng đợi: trang không phải chờ máy chủ SMTP (1–3 giây), lỗi mạng tạm thời được gửi lại.
// shortcut: hàng đợi nằm trong bộ nhớ, tắt app lúc còn email chưa gửi thì mất; đổi sang bảng Outbox trong DB nếu cần chắc chắn.
public sealed class QueuedEmailSender(Channel<OutgoingEmail> queue, FileEmailSender? devCopy) : IEmailSender
{
    public async Task SendAsync(string to, string subject, string htmlBody)
    {
        // Máy dev vẫn ghi bản sao ra App_Data/emails để thử (đọc mã 2FA, link xác minh) mà không cần mở hộp thư.
        if (devCopy != null) await devCopy.SendAsync(to, subject, htmlBody);
        if (IsReservedDomain(to)) return;
        if (!queue.Writer.TryWrite(new(to, subject, htmlBody)))
            throw new BusinessException("Hệ thống đang bận gửi email. Vui lòng thử lại sau ít phút.");
    }

    // Tên miền dành riêng cho thử nghiệm (RFC 2606/6761, .local của mạng nội bộ) không có hộp thư thật:
    // gửi qua SMTP chỉ sinh thư bị trả lại, làm giảm uy tín địa chỉ gửi của MindSprint AI.
    public static bool IsReservedDomain(string address)
    {
        var at = address.LastIndexOf('@');
        if (at < 0) return false;
        var domain = address[(at + 1)..].TrimEnd('.').ToLowerInvariant();
        return domain is "localhost" or "example.com" or "example.net" or "example.org"
            || new[] { ".test", ".example", ".invalid", ".localhost", ".local" }.Any(suffix => domain.EndsWith(suffix, StringComparison.Ordinal))
            || domain is "test" or "example" or "invalid" or "local";
    }
}

public sealed class SmtpEmailWorker(Channel<OutgoingEmail> queue, SmtpSettings settings, ILogger<SmtpEmailWorker> log) : BackgroundService
{
    public const int Attempts = 3;

    // multipart/alternative: phần chữ thường trước, HTML sau (thư chỉ có HTML bị bộ lọc thư rác trừ điểm).
    public static MailMessage Build(SmtpSettings settings, OutgoingEmail email)
    {
        var message = new MailMessage(new MailAddress(settings.FromAddress ?? settings.UserName!, Brand.Name), new MailAddress(email.To))
        {
            Subject = email.Subject, Body = EmailLayout.PlainText(email.HtmlBody), IsBodyHtml = false,
            SubjectEncoding = Encoding.UTF8, BodyEncoding = Encoding.UTF8, HeadersEncoding = Encoding.UTF8
        };
        message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(
            EmailLayout.Wrap(email.Subject, email.HtmlBody), Encoding.UTF8, "text/html"));
        // System.Net.Mail không tự thêm Message-ID; thiếu thì Gmail tự chèn và đánh dấu SMTPIN_ADDED_MISSING (bị trừ điểm thư rác).
        message.Headers.Add("Message-ID", $"<{Guid.NewGuid():N}@{message.From!.Host}>");
        return message;
    }

    // shortcut: System.Net.Mail.SmtpClient (Microsoft khuyên MailKit cho dự án mới) chỉ hỗ trợ STARTTLS (cổng 587, không 465);
    // đủ cho Gmail + mật khẩu ứng dụng. Đổi sang MailKit nếu cần OAuth2 hoặc cổng 465.
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var email in queue.Reader.ReadAllAsync(stoppingToken))
        {
            var domain = LogRedaction.EmailDomain(email.To); // (NK1) Chỉ tên miền + kết quả.
            log.LogInformation("SMTP {Domain}: {Outcome}", domain, "Started");
            var sent = false;
            for (var attempt = 1; attempt <= Attempts; attempt++)
            {
                try
                {
                    using var client = new SmtpClient(settings.Host, settings.Port)
                    {
                        EnableSsl = true, Credentials = new NetworkCredential(settings.UserName, settings.Password), Timeout = 30000
                    };
                    using var message = Build(settings, email);
                    await client.SendMailAsync(message, stoppingToken);
                    sent = true;
                    break;
                }
                catch (Exception ex) when (ex is SmtpException or IOException or InvalidOperationException or FormatException
                    && !stoppingToken.IsCancellationRequested)
                {
                    // Không ghi nội dung hay địa chỉ đầy đủ: email có thể chứa mã 2FA, link đặt lại mật khẩu.
                    log.LogWarning("SMTP {Domain}: {Outcome}", domain, attempt == Attempts ? "Failed" : "Retry");
                    if (attempt < Attempts) await Task.Delay(TimeSpan.FromSeconds(10 * attempt), stoppingToken);
                }
            }
            log.LogInformation("SMTP {Domain}: {Outcome}, {Count} items", domain, sent ? "Sent" : "Failed", sent ? 1 : 0);
        }
    }
}

public static class EmailServices
{
    public static IServiceCollection AddEmail(this IServiceCollection services, IConfiguration config, IWebHostEnvironment env)
    {
        var smtp = config.GetSection("Smtp").Get<SmtpSettings>() ?? new();
        if (!smtp.Configured) return services.AddSingleton<IEmailSender, FileEmailSender>();
        services.AddSingleton(smtp);
        services.AddSingleton(Channel.CreateBounded<OutgoingEmail>(new BoundedChannelOptions(1000) { SingleReader = true }));
        services.AddSingleton<FileEmailSender>();
        services.AddSingleton<IEmailSender>(sp => new QueuedEmailSender(sp.GetRequiredService<Channel<OutgoingEmail>>(),
            env.IsDevelopment() ? sp.GetRequiredService<FileEmailSender>() : null));
        return services.AddHostedService<SmtpEmailWorker>();
    }
}
