using System.Net;
using Microsoft.Extensions.Options;
using Notification.Shared.Telegram;

namespace Notification.Worker.Sending;

/// <summary>Gửi thông báo thật tới người nhận Telegram qua Bot API (sendMessage).</summary>
public sealed class TelegramNotificationSender(IHttpClientFactory httpFactory, IOptions<TelegramOptions> options)
    : INotificationSender
{
    public async Task<SendResult> SendAsync(SendRequest request, CancellationToken ct)
    {
        var telegram = new TelegramApi(httpFactory.CreateClient(TelegramApi.HttpClientName), options);
        var html = $"<b>{WebUtility.HtmlEncode(request.Subject)}</b>\n\n{WebUtility.HtmlEncode(request.Body)}";
        try
        {
            var response = await telegram.SendMessageAsync(request.Address, html, ct);
            if (response.Ok)
                return SendResult.Ok();

            var error = $"Telegram từ chối ({response.ErrorCode}): {response.Description}";
            // 400 chat không tồn tại, 403 người nhận đã chặn bot: gửi lại vẫn hỏng.
            // 429 gửi quá nhanh, 5xx, 401 token sai (sửa cấu hình xong là gửi được): thử lại sau.
            return response.ErrorCode is 400 or 403 ? SendResult.Permanent(error) : SendResult.Transient(error);
        }
        catch (Exception ex) when (ex is HttpRequestException
                                   || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            return SendResult.Transient($"Không kết nối được Telegram: {ex.Message}");
        }
    }
}

/// <summary>
/// Chọn bộ gửi theo kênh. Chế độ Mock: mọi kênh giả lập. Chế độ gửi thật: Email qua SMTP, Telegram qua bot;
/// kênh chưa cấu hình (hoặc SMS, chưa tích hợp nhà cung cấp) báo lỗi rõ ràng thay vì giả vờ đã gửi.
/// </summary>
public sealed class ChannelRouterSender(
    MockNotificationSender mock,
    bool realMode,
    SmtpNotificationSender? email,
    TelegramNotificationSender? telegram) : INotificationSender
{
    public Task<SendResult> SendAsync(SendRequest request, CancellationToken ct)
    {
        if (!realMode)
            return mock.SendAsync(request, ct);

        return request.Channel switch
        {
            Shared.Domain.NotificationChannel.Email => email?.SendAsync(request, ct)
                ?? Task.FromResult(SendResult.Permanent("Chưa cấu hình SMTP (SMTP_HOST, SMTP_FROM) để gửi email thật")),
            Shared.Domain.NotificationChannel.Telegram => telegram?.SendAsync(request, ct)
                ?? Task.FromResult(SendResult.Permanent("Chưa cấu hình TELEGRAM_BOT_TOKEN để gửi Telegram")),
            _ => Task.FromResult(SendResult.Permanent("Kênh SMS chưa tích hợp nhà cung cấp gửi tin thật"))
        };
    }
}
