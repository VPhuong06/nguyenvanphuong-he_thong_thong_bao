using Microsoft.EntityFrameworkCore;
using Notification.Statistics.Data;

namespace Notification.Statistics.Endpoints;

public sealed record OverviewResponse(
    int Attempts,
    int Sent,
    int RetryScheduled,
    int Failed,
    int SentAfterRetry,
    int AvgSendMs,
    int Workers,
    int Campaigns,
    int LastMinute,
    DateTimeOffset? LastEventAt);

public sealed record WorkerStatsResponse(
    string WorkerId,
    int Attempts,
    int Sent,
    int RetryScheduled,
    int Failed,
    int AvgMs,
    DateTimeOffset LastSeenAt);

public sealed record TimelinePoint(DateTimeOffset Minute, int Sent, int RetryScheduled, int Failed);

public static class StatsEndpoints
{
    public static IEndpointRouteBuilder MapStatsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/stats");

        group.MapGet("/overview", GetOverview);
        group.MapGet("/workers", GetWorkers);
        group.MapGet("/timeline", GetTimeline);

        return app;
    }

    /// <summary>Tổng quan các lần gửi; campaignId để xem riêng một chiến dịch.</summary>
    private static async Task<OverviewResponse> GetOverview(StatsDbContext db, CancellationToken ct, Guid? campaignId)
    {
        var since = DateTimeOffset.UtcNow.AddMinutes(-1);
        var overview = await Filter(db, campaignId)
            .GroupBy(_ => 1)
            .Select(g => new OverviewResponse(
                g.Count(),
                g.Count(e => e.Outcome == "Sent"),
                g.Count(e => e.Outcome == "RetryScheduled"),
                g.Count(e => e.Outcome == "Failed"),
                g.Count(e => e.Outcome == "Sent" && e.AttemptNo > 1),
                (int)(g.Where(e => e.Outcome == "Sent").Average(e => (double?)e.DurationMs) ?? 0),
                g.Select(e => e.WorkerId).Distinct().Count(),
                g.Select(e => e.CampaignId).Distinct().Count(),
                g.Count(e => e.OccurredAt > since),
                g.Max(e => (DateTimeOffset?)e.OccurredAt)))
            .FirstOrDefaultAsync(ct);

        return overview ?? new OverviewResponse(0, 0, 0, 0, 0, 0, 0, 0, 0, null);
    }

    /// <summary>Số lần gửi theo từng worker: cho thấy job được chia cho các worker thế nào.</summary>
    private static async Task<List<WorkerStatsResponse>> GetWorkers(StatsDbContext db, CancellationToken ct,
        Guid? campaignId) =>
        await Filter(db, campaignId)
            .GroupBy(e => e.WorkerId)
            .OrderByDescending(g => g.Count())
            .Select(g => new WorkerStatsResponse(
                g.Key,
                g.Count(),
                g.Count(e => e.Outcome == "Sent"),
                g.Count(e => e.Outcome == "RetryScheduled"),
                g.Count(e => e.Outcome == "Failed"),
                (int)g.Average(e => e.DurationMs),
                g.Max(e => e.OccurredAt)))
            .ToListAsync(ct);

    /// <summary>Số lần gửi theo từng phút trong khoảng thời gian gần đây.</summary>
    private static async Task<IResult> GetTimeline(StatsDbContext db, CancellationToken ct, Guid? campaignId,
        int minutes = 15)
    {
        if (minutes is < 1 or > 24 * 60)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["minutes"] = ["minutes phải trong khoảng 1–1440."]
            });

        var now = DateTimeOffset.UtcNow;
        var from = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, TimeSpan.Zero)
            .AddMinutes(-(minutes - 1));

        var rows = await Filter(db, campaignId)
            .Where(e => e.OccurredAt >= from)
            .Select(e => new { e.OccurredAt, e.Outcome })
            .ToListAsync(ct);

        // Gom theo phút ở phía service; điền 0 cho phút không có lần gửi nào để biểu đồ liền mạch
        var byMinute = rows.GroupBy(r => (int)(r.OccurredAt - from).TotalMinutes).ToDictionary(g => g.Key);
        var points = Enumerable.Range(0, minutes).Select(i =>
        {
            byMinute.TryGetValue(i, out var g);
            return new TimelinePoint(from.AddMinutes(i),
                g?.Count(r => r.Outcome == "Sent") ?? 0,
                g?.Count(r => r.Outcome == "RetryScheduled") ?? 0,
                g?.Count(r => r.Outcome == "Failed") ?? 0);
        }).ToList();

        return Results.Ok(points);
    }

    private static IQueryable<DeliveryEventRecord> Filter(StatsDbContext db, Guid? campaignId)
    {
        var query = db.DeliveryEvents.AsNoTracking();
        return campaignId is { } id ? query.Where(e => e.CampaignId == id) : query;
    }
}
