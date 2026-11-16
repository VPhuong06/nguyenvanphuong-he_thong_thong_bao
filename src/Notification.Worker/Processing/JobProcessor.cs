using Microsoft.EntityFrameworkCore;
using Notification.Shared.Data;
using Notification.Shared.Domain;
using Notification.Shared.Messaging;
using Notification.Worker.Sending;

namespace Notification.Worker.Processing;

public enum JobOutcome
{
    Sent,
    Failed,

    /// <summary>Job đã xong (Sent/Failed) hoặc không còn tồn tại: bỏ qua, chỉ ack.</summary>
    Skipped,

    /// <summary>
    /// Job đang Processing dưới lease của worker khác. Message phải được trả lại queue: nếu worker kia
    /// đã chết, job sẽ được nhận lại khi lease hết hạn thay vì kẹt mãi ở Processing.
    /// </summary>
    HeldByOtherWorker
}

/// <summary>Xử lý một job gửi thông báo. Được tạo trong scope riêng cho mỗi message.</summary>
public sealed class JobProcessor(
    NotificationDbContext db,
    INotificationSender sender,
    ILogger<JobProcessor> logger)
{
    /// <summary>
    /// Job ở trạng thái Processing quá thời gian này được coi là worker giữ nó đã chết,
    /// worker khác được phép nhận lại.
    /// </summary>
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(1);

    public async Task<JobOutcome> ProcessAsync(SendNotificationMessage message, string workerId, CancellationToken ct)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var leaseExpired = startedAt - Lease;

        // Claim có điều kiện (idempotent consumer): message bị giao lại cho worker khác sẽ không gửi trùng
        var claimed = await db.Deliveries
            .Where(d => d.Id == message.DeliveryId &&
                        (d.Status == DeliveryStatus.Pending ||
                         (d.Status == DeliveryStatus.Processing && d.LockedAt < leaseExpired)))
            .ExecuteUpdateAsync(s => s
                .SetProperty(d => d.Status, DeliveryStatus.Processing)
                .SetProperty(d => d.WorkerId, workerId)
                .SetProperty(d => d.LockedAt, startedAt)
                .SetProperty(d => d.AttemptCount, d => d.AttemptCount + 1)
                .SetProperty(d => d.UpdatedAt, startedAt), ct);

        if (claimed == 0)
        {
            var current = await db.Deliveries.AsNoTracking()
                .Where(d => d.Id == message.DeliveryId)
                .Select(d => new { d.Status, d.WorkerId })
                .FirstOrDefaultAsync(ct);

            if (current?.Status == DeliveryStatus.Processing)
            {
                logger.LogInformation("Job {DeliveryId} đang do worker {OtherWorker} giữ, trả lại queue",
                    message.DeliveryId, current.WorkerId);
                return JobOutcome.HeldByOtherWorker;
            }

            logger.LogInformation("Bỏ qua job {DeliveryId}: đã xử lý xong ({Status})",
                message.DeliveryId, current?.Status.ToString() ?? "không tồn tại");
            return JobOutcome.Skipped;
        }

        var job = await db.Deliveries.AsNoTracking()
            .Where(d => d.Id == message.DeliveryId)
            .Join(db.Campaigns, d => d.CampaignId, c => c.Id, (d, c) => new
            {
                d.RecipientName, d.RecipientAddress, d.AttemptCount, c.Channel, c.Subject, c.Content
            })
            .FirstAsync(ct);

        var result = await sender.SendAsync(new SendRequest(
            job.Channel,
            job.RecipientAddress,
            Render(job.Subject, job.RecipientName, job.RecipientAddress),
            Render(job.Content, job.RecipientName, job.RecipientAddress)), ct);

        var finishedAt = DateTimeOffset.UtcNow;
        var newStatus = result.Succeeded ? DeliveryStatus.Sent : DeliveryStatus.Failed;

        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            await db.Deliveries
                .Where(d => d.Id == message.DeliveryId && d.WorkerId == workerId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(d => d.Status, newStatus)
                    .SetProperty(d => d.LastError, result.Error)
                    .SetProperty(d => d.SentAt, result.Succeeded ? finishedAt : null)
                    .SetProperty(d => d.LockedAt, (DateTimeOffset?)null)
                    .SetProperty(d => d.UpdatedAt, finishedAt), ct);

            db.DeliveryAttempts.Add(new DeliveryAttempt
            {
                DeliveryId = message.DeliveryId,
                AttemptNo = job.AttemptCount,
                WorkerId = workerId,
                Succeeded = result.Succeeded,
                Error = result.Error,
                StartedAt = startedAt,
                FinishedAt = finishedAt
            });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        if (result.Succeeded)
            logger.LogInformation("Worker {WorkerId} đã gửi job {DeliveryId} tới {Address} ({Duration} ms)",
                workerId, message.DeliveryId, job.RecipientAddress, (int)(finishedAt - startedAt).TotalMilliseconds);
        else
            logger.LogWarning("Worker {WorkerId} gửi job {DeliveryId} tới {Address} thất bại: {Error}",
                workerId, message.DeliveryId, job.RecipientAddress, result.Error);

        await TryCompleteCampaignAsync(message.CampaignId, ct);
        return result.Succeeded ? JobOutcome.Sent : JobOutcome.Failed;
    }

    /// <summary>
    /// Đánh dấu chiến dịch hoàn tất khi không còn job Pending/Processing. Điều kiện nằm trong câu UPDATE
    /// nên nhiều worker cùng gọi cũng chỉ một lần cập nhật thành công.
    /// </summary>
    private async Task TryCompleteCampaignAsync(Guid campaignId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var completed = await db.Database.ExecuteSqlAsync($"""
            UPDATE campaigns
            SET "Status" = CASE WHEN EXISTS (
                    SELECT 1 FROM deliveries WHERE "CampaignId" = {campaignId} AND "Status" = 'Failed')
                THEN 'CompletedWithFailures' ELSE 'Completed' END,
                "CompletedAt" = {now}
            WHERE "Id" = {campaignId}
              AND "Status" = 'Running'
              AND NOT EXISTS (
                SELECT 1 FROM deliveries
                WHERE "CampaignId" = {campaignId} AND "Status" IN ('Pending', 'Processing'))
            """, ct);

        if (completed > 0)
            logger.LogInformation("Chiến dịch {CampaignId} đã xử lý xong toàn bộ job", campaignId);
    }

    private static string Render(string template, string name, string address) =>
        template.Replace("{{name}}", name).Replace("{{address}}", address);
}
