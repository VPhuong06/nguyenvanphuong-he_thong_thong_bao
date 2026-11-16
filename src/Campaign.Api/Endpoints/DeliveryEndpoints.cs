using System.Linq.Expressions;
using Campaign.Api.Contracts;
using Campaign.Api.Messaging;
using Microsoft.EntityFrameworkCore;
using Notification.Shared.Data;
using Notification.Shared.Domain;

namespace Campaign.Api.Endpoints;

public static class DeliveryEndpoints
{
    public static IEndpointRouteBuilder MapDeliveryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/deliveries");

        group.MapGet("/{id:guid}", GetDelivery);
        group.MapPost("/{id:guid}/retry", RetryDelivery);

        return app;
    }

    /// <summary>Chi tiết một job kèm lịch sử từng lần gửi.</summary>
    private static async Task<IResult> GetDelivery(Guid id, NotificationDbContext db, CancellationToken ct)
    {
        var delivery = await db.Deliveries.AsNoTracking()
            .Where(d => d.Id == id)
            .Select(ToResponse)
            .FirstOrDefaultAsync(ct);
        if (delivery is null)
            return Results.NotFound();

        var attempts = await db.DeliveryAttempts.AsNoTracking()
            .Where(a => a.DeliveryId == id)
            .OrderBy(a => a.AttemptNo)
            .Select(a => new DeliveryAttemptResponse(a.AttemptNo, a.WorkerId, a.Succeeded, a.Error, a.StartedAt,
                a.FinishedAt))
            .ToListAsync(ct);

        return Results.Ok(new DeliveryDetailResponse(delivery, attempts));
    }

    /// <summary>
    /// Chạy lại một job thất bại. Admin chọn từng job nên không giới hạn loại lỗi hay số lần thử.
    /// </summary>
    private static async Task<IResult> RetryDelivery(
        Guid id, NotificationDbContext db, OutboxSignal outboxSignal, ILoggerFactory loggerFactory, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var retried = await db.Deliveries
            .Where(d => d.Id == id && d.Status == DeliveryStatus.Failed)
            .ExecuteUpdateAsync(RetryPolicy.ResetToPending(now), ct);

        if (retried == 0)
        {
            var status = await db.Deliveries.Where(d => d.Id == id).Select(d => (DeliveryStatus?)d.Status)
                .FirstOrDefaultAsync(ct);
            return status is null
                ? Results.NotFound()
                : Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Không thể chạy lại",
                    detail: $"Job đang ở trạng thái {status}, chỉ chạy lại được job {DeliveryStatus.Failed}.");
        }

        var campaignId = await db.Deliveries.Where(d => d.Id == id).Select(d => d.CampaignId).FirstAsync(ct);
        await db.Campaigns
            .Where(c => c.Id == campaignId && c.Status != CampaignStatus.Draft)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.Status, CampaignStatus.Running)
                .SetProperty(c => c.CompletedAt, (DateTimeOffset?)null), ct);

        await tx.CommitAsync(ct);
        outboxSignal.Notify();

        loggerFactory.CreateLogger(typeof(DeliveryEndpoints)).LogInformation(
            "Chạy lại job {DeliveryId} của chiến dịch {CampaignId}", id, campaignId);
        return Results.Accepted($"/deliveries/{id}");
    }

    internal static readonly Expression<Func<Delivery, DeliveryResponse>> ToResponse = d =>
        new DeliveryResponse(
            d.Id, d.RecipientName, d.RecipientAddress, d.Status, d.AttemptCount, d.LastError, d.FailureKind,
            d.WorkerId, d.CreatedAt, d.PublishedAt, d.SentAt, d.UpdatedAt);
}
