using Microsoft.EntityFrameworkCore;
using Notification.Shared.Data;
using Notification.Shared.Domain;

namespace Campaign.Api.Infrastructure;

public static class DatabaseInitializer
{
    /// <summary>
    /// Tạo schema nếu chưa có. Thử lại vì container Postgres có thể chưa sẵn sàng
    /// khi API khởi động.
    /// </summary>
    public static async Task EnsureCreatedAsync(IServiceProvider services, CancellationToken ct)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DatabaseInitializer));
        var delay = TimeSpan.FromSeconds(2);

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var scope = services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
                await db.Database.EnsureCreatedAsync(ct);
                await UpgradeSchemaAsync(db, ct);
                await SeedContactsAsync(db, logger, ct);
                logger.LogInformation("Database sẵn sàng (lần thử {Attempt})", attempt);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException && attempt < 15)
            {
                logger.LogWarning("Chưa kết nối được database (lần thử {Attempt}): {Error}. Thử lại sau {Delay}s",
                    attempt, ex.Message, delay.TotalSeconds);
                await Task.Delay(delay, ct);
                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 15));
            }
        }
    }

    /// <summary>
    /// EnsureCreated không thêm cột vào database đã tồn tại. Các cột thêm sau được bổ sung ở đây bằng câu
    /// lệnh chạy lại được nhiều lần, để nâng cấp mà không phải xoá dữ liệu.
    /// </summary>
    private static async Task UpgradeSchemaAsync(NotificationDbContext db, CancellationToken ct)
    {
        await db.Database.ExecuteSqlRawAsync("""
            ALTER TABLE contacts ADD COLUMN IF NOT EXISTS "TelegramChatId" character varying(64);
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_contacts_TelegramChatId" ON contacts ("TelegramChatId");
            """, ct);
    }

    /// <summary>Tạo danh bạ mẫu khi bảng còn trống, để có sẵn người nhận khi demo.</summary>
    private static async Task SeedContactsAsync(NotificationDbContext db, ILogger logger, CancellationToken ct)
    {
        if (await db.Contacts.AnyAsync(ct))
            return;

        var now = DateTimeOffset.UtcNow;
        var contacts = Enumerable.Range(1, 20).Select(i => new Contact
        {
            Id = Guid.CreateVersion7(),
            Name = $"Người nhận {i:D2}",
            Email = $"nguoinhan{i:D2}@example.com",
            Phone = $"+849000000{i:D2}",
            CreatedAt = now
        });

        db.Contacts.AddRange(contacts);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Đã tạo {Count} liên hệ mẫu", 20);
    }
}
