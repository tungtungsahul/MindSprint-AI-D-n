using System.Text;
using System.Threading.Channels;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using MindSprintAI_ASP.NET_MVC.Services;
using Xunit;

namespace MindSprintAI_ASP.NET_MVC.Tests;

// Đợt 7a: gửi email thật qua SMTP (người dùng yêu cầu 2026-11-07), không gọi máy chủ SMTP thật trong test.
public class Tang7aTests
{
    private sealed class Env(string root, string name) : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = root;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ApplicationName { get; set; } = "test";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = root;
        public string EnvironmentName { get; set; } = name;
    }

    private static ServiceProvider Build(Dictionary<string, string?> config, string environment, string root)
    {
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System); services.AddLogging();
        var env = new Env(root, environment); services.AddSingleton<IWebHostEnvironment>(env);
        services.AddEmail(new ConfigurationBuilder().AddInMemoryCollection(config).Build(), env);
        return services.BuildServiceProvider();
    }

    private static readonly Dictionary<string, string?> Smtp = new()
    {
        ["Smtp:Host"] = "smtp.example.test", ["Smtp:UserName"] = "he.thong@example.test", ["Smtp:Password"] = "mat-khau-ung-dung"
    };

    [Fact]
    public void ChuaCauHinhThiVanGhiFileKhongChayTienTrinhGui()
    {
        using var sp = Build(new() { ["Smtp:Host"] = "smtp.example.test" }, Environments.Production, Path.GetTempPath());
        Assert.IsType<FileEmailSender>(sp.GetRequiredService<IEmailSender>());
        Assert.Empty(sp.GetServices<IHostedService>());
    }

    [Fact]
    public async Task CoCauHinhThiDuaVaoHangDoiVaMayDevGhiThemBanSao()
    {
        var root = Directory.CreateTempSubdirectory("tang7a").FullName;
        try
        {
            using var sp = Build(Smtp, Environments.Development, root);
            var sender = Assert.IsType<QueuedEmailSender>(sp.GetRequiredService<IEmailSender>());
            Assert.Contains(sp.GetServices<IHostedService>(), s => s is SmtpEmailWorker);
            await sender.SendAsync("hv@truong.edu.vn", "Mã đăng nhập", "<p>123456</p>");
            Assert.True(sp.GetRequiredService<Channel<OutgoingEmail>>().Reader.TryRead(out var queued));
            Assert.Equal(("hv@truong.edu.vn", "Mã đăng nhập"), (queued!.To, queued.Subject));
            Assert.Single(Directory.GetFiles(Path.Combine(root, "App_Data", "emails")));
            // Tài khoản thử (tên miền dành riêng) vẫn có bản sao trên máy nhưng không gửi SMTP, tránh thư bị trả lại.
            await sender.SendAsync("gv.lan@test.local", "Đăng nhập mới", "<p>x</p>");
            Assert.False(sp.GetRequiredService<Channel<OutgoingEmail>>().Reader.TryRead(out _));
            Assert.Equal(2, Directory.GetFiles(Path.Combine(root, "App_Data", "emails")).Length);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void ServerKhongGhiBanSaoRaDia()
    {
        using var sp = Build(Smtp, Environments.Production, Path.GetTempPath());
        var sender = sp.GetRequiredService<IEmailSender>();
        var copy = typeof(QueuedEmailSender).GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            .Single(f => f.FieldType == typeof(FileEmailSender)).GetValue(sender);
        Assert.Null(copy);
    }

    [Fact]
    public void ThuGuiDiTuTenThuongHieuVaGiuTiengViet()
    {
        var settings = new SmtpSettings { Host = "h", UserName = "he.thong@example.test", Password = "p" };
        using var message = SmtpEmailWorker.Build(settings, new("hv@example.test", "Đặt lại mật khẩu " + Brand.Name, "<p>Chào bạn</p>"));
        Assert.Equal(Brand.Name, message.From!.DisplayName); Assert.Equal("he.thong@example.test", message.From.Address);
        Assert.Equal("text/html", Assert.Single(message.AlternateViews).ContentType.MediaType); Assert.Equal(Encoding.UTF8, message.SubjectEncoding);
        Assert.Matches(@"^<[0-9a-f]{32}@example\.test>$", message.Headers["Message-ID"]); // thiếu thì Gmail trừ điểm thư rác
        settings.FromAddress = "thong.bao@example.test";
        using var other = SmtpEmailWorker.Build(settings, new("hv@example.test", "s", "b"));
        Assert.Equal("thong.bao@example.test", other.From!.Address);
    }

    [Fact]
    public async Task HangDoiDayThiBaoBanKhongNuotEmail()
    {
        var full = Channel.CreateBounded<OutgoingEmail>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.Wait });
        var sender = new QueuedEmailSender(full, null);
        await sender.SendAsync("a@truong.edu.vn", "s", "b");
        await Assert.ThrowsAsync<BusinessException>(() => sender.SendAsync("b@truong.edu.vn", "s", "b"));
    }

    [Theory]
    [InlineData("gv.lan@test.local", true), InlineData("a@example.test", true), InlineData("a@EXAMPLE.COM", true)]
    [InlineData("a@x.invalid", true), InlineData("a@localhost", true), InlineData("a@foo.localhost.", true)]
    [InlineData("hv@gmail.com", false), InlineData("a@truong.edu.vn", false), InlineData("a@localhost.vn", false), InlineData("a@mytest.com", false)]
    public void TenMienDanhRiengKhongGuiSmtp(string address, bool reserved) =>
        Assert.Equal(reserved, QueuedEmailSender.IsReservedDomain(address));
}
