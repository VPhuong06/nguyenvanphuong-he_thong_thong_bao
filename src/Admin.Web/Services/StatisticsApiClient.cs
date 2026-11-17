using System.Net.Http.Json;
using System.Text.Json;

namespace Admin.Web.Services;

public sealed record StatsOverview(
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

public sealed record WorkerStats(
    string WorkerId,
    int Attempts,
    int Sent,
    int RetryScheduled,
    int Failed,
    int AvgMs,
    DateTimeOffset LastSeenAt);

public sealed record StatsTimelinePoint(DateTimeOffset Minute, int Sent, int RetryScheduled, int Failed)
{
    public int Total => Sent + RetryScheduled + Failed;
}

/// <summary>Client gọi Statistics service, một service độc lập với Campaign API.</summary>
public sealed class StatisticsApiClient(HttpClient http, ILogger<StatisticsApiClient> logger)
{
    public const string HttpClientName = "StatisticsApi";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task<StatsOverview> GetOverviewAsync(Guid? campaignId, CancellationToken ct = default) =>
        GetAsync<StatsOverview>($"stats/overview{Query(campaignId)}", ct);

    public Task<IReadOnlyList<WorkerStats>> GetWorkersAsync(Guid? campaignId, CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<WorkerStats>>($"stats/workers{Query(campaignId)}", ct);

    public Task<IReadOnlyList<StatsTimelinePoint>> GetTimelineAsync(int minutes, Guid? campaignId,
        CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<StatsTimelinePoint>>(
            $"stats/timeline?minutes={minutes}{(campaignId is null ? "" : $"&campaignId={campaignId}")}", ct);

    private static string Query(Guid? campaignId) => campaignId is null ? "" : $"?campaignId={campaignId}";

    private async Task<T> GetAsync<T>(string url, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            response = await http.GetAsync(url, ct);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning("Không kết nối được Statistics service: {Error}", ex.Message);
            throw new ApiException("Không kết nối được Statistics service. Việc gửi thông báo vẫn hoạt động bình thường.");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
                throw new ApiException($"Statistics service trả lỗi {(int)response.StatusCode}.");
            return (await response.Content.ReadFromJsonAsync<T>(Json, ct))!;
        }
    }
}
