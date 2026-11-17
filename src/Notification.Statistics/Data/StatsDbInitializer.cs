namespace Notification.Statistics.Data;

public static class StatsDbInitializer
{
    /// <summary>
    /// Tạo database statistics_db và bảng nếu chưa có. Thử lại vì container Postgres có thể chưa
    /// sẵn sàng khi service khởi động.
    /// </summary>
    public static async Task EnsureCreatedAsync(IServiceProvider services, CancellationToken ct)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(StatsDbInitializer));
        var delay = TimeSpan.FromSeconds(2);

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var scope = services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<StatsDbContext>();
                await db.Database.EnsureCreatedAsync(ct);
                logger.LogInformation("Database thống kê sẵn sàng (lần thử {Attempt})", attempt);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException && attempt < 15)
            {
                logger.LogWarning("Chưa kết nối được database thống kê (lần thử {Attempt}): {Error}. Thử lại sau {Delay}s",
                    attempt, ex.Message, delay.TotalSeconds);
                await Task.Delay(delay, ct);
                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 15));
            }
        }
    }
}
