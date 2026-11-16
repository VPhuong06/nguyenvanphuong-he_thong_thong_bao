using Microsoft.EntityFrameworkCore;
using Notification.Shared.Data;
using Notification.Shared.Domain;
using Notification.Shared.Messaging;

namespace Campaign.Api.Messaging;

/// <summary>
/// Transactional outbox: bảng deliveries chính là outbox. Job được ghi vào DB trước (PublishedAt = null),
/// dispatcher publish lên RabbitMQ rồi mới đánh dấu PublishedAt. Nếu RabbitMQ đang dừng, job vẫn nằm
/// an toàn trong DB và được publish bù khi broker chạy lại.
/// </summary>
public sealed class OutboxDispatcher(
    IServiceScopeFactory scopeFactory,
    RabbitMqPublisher publisher,
    OutboxSignal signal,
    ILogger<OutboxDispatcher> logger) : BackgroundService
{
    private const int BatchSize = 200;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var brokerDown = false;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                int published;
                do
                {
                    published = await DispatchBatchAsync(stoppingToken);
                } while (published == BatchSize);

                if (brokerDown)
                {
                    logger.LogInformation("Đã publish lại được lên RabbitMQ");
                    brokerDown = false;
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                // Chỉ log đầy đủ lần đầu để tránh ngập log khi broker dừng lâu
                if (!brokerDown)
                    logger.LogWarning("Chưa publish được job, sẽ thử lại sau {Interval}s: {Error}",
                        PollInterval.TotalSeconds, ex.Message);
                brokerDown = true;
            }

            await signal.WaitAsync(PollInterval, stoppingToken);
        }
    }

    private async Task<int> DispatchBatchAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // SKIP LOCKED: nếu chạy nhiều instance API, mỗi job chỉ được một dispatcher lấy.
        // Không ghép thêm toán tử LINQ để EF chạy nguyên câu SQL, giữ được FOR UPDATE.
        var batch = await db.Deliveries
            .FromSql($"""
                SELECT * FROM deliveries
                WHERE "PublishedAt" IS NULL AND "Status" = 'Pending'
                ORDER BY "CreatedAt"
                LIMIT {BatchSize}
                FOR UPDATE SKIP LOCKED
                """)
            .AsNoTracking()
            .ToListAsync(ct);

        if (batch.Count == 0)
            return 0;

        var publishedIds = new List<Guid>(batch.Count);
        try
        {
            foreach (var job in batch)
            {
                await publisher.PublishAsync(new SendNotificationMessage(job.Id, job.CampaignId, job.AttemptCount + 1), ct);
                publishedIds.Add(job.Id);
            }
        }
        finally
        {
            // Đánh dấu cả khi lỗi giữa chừng, để những job đã publish không bị publish lại không cần thiết
            if (publishedIds.Count > 0)
            {
                var now = DateTimeOffset.UtcNow;
                await db.Deliveries
                    .Where(d => publishedIds.Contains(d.Id))
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(d => d.PublishedAt, now)
                        .SetProperty(d => d.UpdatedAt, now), CancellationToken.None);
                await tx.CommitAsync(CancellationToken.None);

                foreach (var group in batch.Where(j => publishedIds.Contains(j.Id)).GroupBy(j => j.CampaignId))
                    logger.LogInformation("Đã publish {Count} job của chiến dịch {CampaignId} lên {Queue}",
                        group.Count(), group.Key, RabbitMqTopology.JobsQueue);
            }
        }

        return publishedIds.Count;
    }
}
