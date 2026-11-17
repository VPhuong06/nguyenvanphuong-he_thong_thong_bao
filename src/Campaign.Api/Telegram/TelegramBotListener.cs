using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Notification.Shared.Data;
using Notification.Shared.Domain;
using Notification.Shared.Telegram;

namespace Campaign.Api.Telegram;

/// <summary>Tên bot lấy được khi khởi động; null khi chưa cấu hình hoặc chưa kết nối được Telegram.</summary>
public sealed class TelegramBotInfo
{
    public string? Username { get; set; }
}

/// <summary>
/// Nhận tin nhắn gửi tới bot bằng long polling (getUpdates). Người nhận nhắn /start để được thêm vào
/// danh bạ kèm chat ID, nhắn /stop để huỷ đăng ký. Chỉ một tiến trình được gọi getUpdates cho một bot,
/// nên việc này đặt ở Campaign API (một instance, cũng là nơi quản lý danh bạ), không đặt ở worker.
/// </summary>
public sealed class TelegramBotListener(
    IHttpClientFactory httpFactory,
    IOptions<TelegramOptions> options,
    IServiceScopeFactory scopeFactory,
    TelegramBotInfo botInfo,
    ILogger<TelegramBotListener> logger) : BackgroundService
{
    private static readonly TimeSpan ErrorDelay = TimeSpan.FromSeconds(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            logger.LogInformation("Chưa cấu hình TELEGRAM_BOT_TOKEN, không nhận đăng ký qua Telegram");
            return;
        }

        var telegram = new TelegramApi(httpFactory.CreateClient(TelegramApi.HttpClientName), options);
        long offset = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (botInfo.Username is null)
                {
                    var me = await telegram.GetMeAsync(stoppingToken);
                    if (!me.Ok)
                    {
                        logger.LogError("Token Telegram không hợp lệ ({Code}): {Error}", me.ErrorCode, me.Description);
                        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                        continue;
                    }
                    botInfo.Username = me.Result!.Username;
                    logger.LogInformation("Bot Telegram @{Bot} sẵn sàng nhận đăng ký (nhắn /start)", botInfo.Username);
                }

                var updates = await telegram.GetUpdatesAsync(offset, timeoutSeconds: 25, stoppingToken);
                if (!updates.Ok)
                {
                    // 409: một tiến trình khác đang nhận tin cho cùng bot (hoặc bot đang đặt webhook)
                    logger.LogWarning("Không nhận được tin nhắn từ Telegram ({Code}): {Error}",
                        updates.ErrorCode, updates.Description);
                    await Task.Delay(ErrorDelay, stoppingToken);
                    continue;
                }

                foreach (var update in updates.Result ?? [])
                {
                    offset = update.UpdateId + 1;
                    if (update.Message is { Text: { } text, Chat.Type: "private" } message)
                        await HandleCommandAsync(telegram, message, text.Trim(), stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning("Lỗi khi nhận tin nhắn Telegram: {Error}. Thử lại sau {Delay}s",
                    ex.Message, ErrorDelay.TotalSeconds);
                await Task.Delay(ErrorDelay, stoppingToken);
            }
        }
    }

    private async Task HandleCommandAsync(TelegramApi telegram, TelegramMessage message, string text,
        CancellationToken ct)
    {
        var chatId = message.Chat.Id.ToString();
        var name = string.Join(' ', new[] { message.Chat.FirstName, message.Chat.LastName }
            .Where(s => !string.IsNullOrWhiteSpace(s)));
        if (name.Length == 0)
            name = message.Chat.Username ?? $"Telegram {chatId}";

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();

        if (text.StartsWith("/start", StringComparison.OrdinalIgnoreCase))
        {
            var contact = await db.Contacts.FirstOrDefaultAsync(c => c.TelegramChatId == chatId, ct);
            if (contact is null)
            {
                db.Contacts.Add(new Contact
                {
                    Id = Guid.CreateVersion7(),
                    Name = name,
                    TelegramChatId = chatId,
                    CreatedAt = DateTimeOffset.UtcNow
                });
            }
            else
            {
                contact.Name = name;
            }
            await db.SaveChangesAsync(ct);

            logger.LogInformation("Người nhận {Name} (chat {ChatId}) đăng ký nhận thông báo Telegram", name, chatId);
            await telegram.SendMessageAsync(chatId,
                $"Xin chào <b>{WebUtility.HtmlEncode(name)}</b>! Bạn đã đăng ký nhận thông báo.\n" +
                "Nhắn /stop nếu muốn huỷ đăng ký.", ct);
        }
        else if (text.StartsWith("/stop", StringComparison.OrdinalIgnoreCase))
        {
            await db.Contacts.Where(c => c.TelegramChatId == chatId).ExecuteDeleteAsync(ct);
            logger.LogInformation("Người nhận chat {ChatId} huỷ đăng ký Telegram", chatId);
            await telegram.SendMessageAsync(chatId, "Bạn đã huỷ đăng ký nhận thông báo. Nhắn /start để đăng ký lại.", ct);
        }
        else
        {
            await telegram.SendMessageAsync(chatId, "Nhắn /start để đăng ký nhận thông báo, /stop để huỷ.", ct);
        }
    }
}
