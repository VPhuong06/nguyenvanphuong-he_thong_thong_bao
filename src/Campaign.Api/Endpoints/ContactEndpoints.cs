using Campaign.Api.Contracts;
using Campaign.Api.Telegram;
using Microsoft.EntityFrameworkCore;
using Notification.Shared.Data;

namespace Campaign.Api.Endpoints;

public static class ContactEndpoints
{
    /// <summary>Danh bạ để Admin chọn người nhận khi tạo chiến dịch.</summary>
    public static IEndpointRouteBuilder MapContactEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/contacts", async (NotificationDbContext db, CancellationToken ct) =>
            await db.Contacts.AsNoTracking()
                .OrderBy(c => c.Name)
                .Select(c => new ContactResponse(c.Id, c.Name, c.Email, c.Phone, c.TelegramChatId))
                .ToListAsync(ct));

        // Thông tin bot để Admin hướng dẫn người nhận nhắn /start
        app.MapGet("/telegram", (TelegramBotInfo bot) => new TelegramInfoResponse(
            bot.Username is not null, bot.Username, bot.Username is null ? null : $"https://t.me/{bot.Username}"));

        return app;
    }
}
