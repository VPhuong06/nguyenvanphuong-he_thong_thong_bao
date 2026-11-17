using System.Net;
using System.Net.Sockets;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Notification.Worker.Sending;

public sealed class SmtpOptions
{
    public const string SectionName = "Smtp";

    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;

    /// <summary>Để trống khi máy chủ SMTP không yêu cầu đăng nhập (ví dụ Mailpit).</summary>
    public string UserName { get; set; } = "";

    public string Password { get; set; } = "";
    public string FromAddress { get; set; } = "";
    public string FromName { get; set; } = "Hệ thống gửi thông báo";
}

/// <summary>
/// Gửi email thật qua SMTP (Gmail, Brevo, Mailpit…). Giữ một kết nối dùng lại giữa các lần gửi
/// để không phải bắt tay TLS và đăng nhập lại cho mỗi email.
/// </summary>
public sealed class SmtpNotificationSender(IOptions<SmtpOptions> options, ILogger<SmtpNotificationSender> logger)
    : INotificationSender, IAsyncDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private SmtpClient? _client;

    public async Task<SendResult> SendAsync(SendRequest request, CancellationToken ct)
    {
        var opts = options.Value;
        if (!MailboxAddress.TryParse(request.Address, out var recipient))
            return SendResult.Permanent($"Địa chỉ email không hợp lệ: {request.Address}");

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(opts.FromName, opts.FromAddress));
        // Giữ tên người nhận để hiện trong ô "To" của hộp thư, trừ khi tên chỉ là chính địa chỉ email
        if (request.Name != request.Address)
            recipient.Name = request.Name;
        message.To.Add(recipient);
        message.Subject = request.Subject;
        message.Body = new BodyBuilder
        {
            TextBody = request.Body,
            HtmlBody = $"<div style=\"font-family:Segoe UI,Arial,sans-serif;font-size:14px\">{WebUtility.HtmlEncode(request.Body).Replace("\n", "<br>")}</div>"
        }.ToMessageBody();

        await _lock.WaitAsync(ct);
        try
        {
            var client = await GetClientAsync(opts, ct);
            await client.SendAsync(message, ct);
            return SendResult.Ok();
        }
        catch (SmtpCommandException ex)
        {
            // Máy chủ đã trả lời: mã 5xx với người nhận hoặc nội dung là lỗi vĩnh viễn, còn lại thử lại được
            var code = (int)ex.StatusCode;
            var error = $"Máy chủ SMTP từ chối ({code}): {ex.Message}";
            if (code >= 500 && ex.ErrorCode is SmtpErrorCode.RecipientNotAccepted or SmtpErrorCode.MessageNotAccepted)
                return SendResult.Permanent(error);

            await ResetClientAsync();
            return SendResult.Transient(error);
        }
        catch (AuthenticationException ex)
        {
            logger.LogError("Đăng nhập SMTP {Host} thất bại, kiểm tra SMTP_USER/SMTP_PASSWORD: {Error}", opts.Host, ex.Message);
            await ResetClientAsync();
            return SendResult.Transient($"Đăng nhập SMTP thất bại: {ex.Message}");
        }
        catch (Exception ex) when (ex is IOException or SocketException or SmtpProtocolException
                                       or ServiceNotConnectedException or TimeoutException
                                       || (ex is OperationCanceledException && !ct.IsCancellationRequested))
        {
            await ResetClientAsync();
            return SendResult.Transient($"Không kết nối được máy chủ SMTP {opts.Host}:{opts.Port}: {ex.Message}");
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<SmtpClient> GetClientAsync(SmtpOptions opts, CancellationToken ct)
    {
        if (_client is { IsConnected: true })
            return _client;

        await ResetClientAsync();
        var client = new SmtpClient { Timeout = 30_000 };
        // Auto: cổng 465 dùng SSL ngay từ đầu, cổng 587 nâng cấp lên TLS bằng STARTTLS
        await client.ConnectAsync(opts.Host, opts.Port, SecureSocketOptions.Auto, ct);
        if (!string.IsNullOrEmpty(opts.UserName))
            await client.AuthenticateAsync(opts.UserName, opts.Password, ct);

        logger.LogInformation("Đã kết nối máy chủ SMTP {Host}:{Port}", opts.Host, opts.Port);
        return _client = client;
    }

    private async Task ResetClientAsync()
    {
        if (_client is null)
            return;
        try
        {
            if (_client.IsConnected)
                await _client.DisconnectAsync(quit: true);
        }
        catch
        {
            // Kết nối đã hỏng, bỏ qua
        }
        _client.Dispose();
        _client = null;
    }

    public async ValueTask DisposeAsync()
    {
        await ResetClientAsync();
        _lock.Dispose();
    }
}

/// <summary>Chọn bộ gửi theo kênh: email gửi thật qua SMTP, SMS vẫn giả lập (cần nhà cung cấp trả phí).</summary>
public sealed class ChannelRouterSender(SmtpNotificationSender email, MockNotificationSender mock) : INotificationSender
{
    public Task<SendResult> SendAsync(SendRequest request, CancellationToken ct) =>
        request.Channel == Shared.Domain.NotificationChannel.Email
            ? email.SendAsync(request, ct)
            : mock.SendAsync(request, ct);
}
