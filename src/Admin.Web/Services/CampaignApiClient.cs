using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Admin.Web.Services;

/// <summary>Lỗi khi gọi Campaign API, đã chuyển thành thông báo đọc được cho người dùng.</summary>
public sealed class ApiException(string message, IReadOnlyDictionary<string, string[]>? errors = null)
    : Exception(message)
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors ?? new Dictionary<string, string[]>();
}

public sealed class CampaignApiClient(HttpClient http, ILogger<CampaignApiClient> logger)
{
    public const string HttpClientName = "CampaignApi";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public Task<PagedResult<CampaignSummary>> ListCampaignsAsync(string? status, int page, int pageSize,
        CancellationToken ct = default) =>
        GetAsync<PagedResult<CampaignSummary>>(
            $"campaigns?page={page}&pageSize={pageSize}{(string.IsNullOrEmpty(status) ? "" : $"&status={status}")}", ct);

    public Task<CampaignDetail> GetCampaignAsync(Guid id, CancellationToken ct = default) =>
        GetAsync<CampaignDetail>($"campaigns/{id}", ct);

    public Task<PagedResult<DeliveryItem>> ListDeliveriesAsync(Guid campaignId, string? status, int page, int pageSize,
        CancellationToken ct = default) =>
        GetAsync<PagedResult<DeliveryItem>>(
            $"campaigns/{campaignId}/deliveries?page={page}&pageSize={pageSize}{(string.IsNullOrEmpty(status) ? "" : $"&status={status}")}",
            ct);

    public Task<DeliveryDetail> GetDeliveryAsync(Guid id, CancellationToken ct = default) =>
        GetAsync<DeliveryDetail>($"deliveries/{id}", ct);

    public Task<IReadOnlyList<ContactItem>> ListContactsAsync(CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<ContactItem>>("contacts", ct);

    public Task<CampaignDetail> CreateCampaignAsync(CreateCampaignRequest request, CancellationToken ct = default) =>
        PostAsync<CampaignDetail>("campaigns", request, ct);

    public Task<StartCampaignResult> StartCampaignAsync(Guid id, CancellationToken ct = default) =>
        PostAsync<StartCampaignResult>($"campaigns/{id}/start", null, ct);

    public Task<RetryFailedResult> RetryFailedAsync(Guid id, bool includePermanent, CancellationToken ct = default) =>
        PostAsync<RetryFailedResult>($"campaigns/{id}/retry-failed?includePermanent={includePermanent}", null, ct);

    /// <summary>Trạng thái PostgreSQL và RabbitMQ do Campaign API báo; null khi không kết nối được API.</summary>
    public async Task<SystemHealth?> GetHealthAsync(CancellationToken ct = default)
    {
        try
        {
            // /health/ready trả 503 khi có thành phần lỗi nhưng vẫn kèm nội dung chi tiết
            using var response = await http.GetAsync("health/ready", ct);
            return await response.Content.ReadFromJsonAsync<SystemHealth>(Json, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException ||
                                   (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            return null;
        }
    }

    public async Task RetryDeliveryAsync(Guid id, CancellationToken ct = default) =>
        await SendAsync(() => http.PostAsync($"deliveries/{id}/retry", null, ct), ct);

    private async Task<T> GetAsync<T>(string url, CancellationToken ct)
    {
        var response = await SendAsync(() => http.GetAsync(url, ct), ct);
        return (await response.Content.ReadFromJsonAsync<T>(Json, ct))!;
    }

    private async Task<T> PostAsync<T>(string url, object? body, CancellationToken ct)
    {
        var response = await SendAsync(() => body is null
            ? http.PostAsync(url, null, ct)
            : http.PostAsJsonAsync(url, body, Json, ct), ct);
        return (await response.Content.ReadFromJsonAsync<T>(Json, ct))!;
    }

    private async Task<HttpResponseMessage> SendAsync(Func<Task<HttpResponseMessage>> send, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            response = await send();
        }
        catch (HttpRequestException ex)
        {
            // Campaign API tạm dừng: báo cho người dùng thay vì làm hỏng trang
            logger.LogWarning("Không kết nối được Campaign API: {Error}", ex.Message);
            throw new ApiException("Không kết nối được Campaign API. Vui lòng thử lại sau.");
        }

        if (response.IsSuccessStatusCode)
            return response;

        var problem = await TryReadProblemAsync(response, ct);
        var message = response.StatusCode switch
        {
            HttpStatusCode.NotFound => "Không tìm thấy dữ liệu.",
            _ => problem?.Detail ?? problem?.Title ?? $"Campaign API trả lỗi {(int)response.StatusCode}."
        };
        throw new ApiException(message, problem?.Errors);
    }

    private static async Task<ProblemResponse?> TryReadProblemAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<ProblemResponse>(Json, ct);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private sealed record ProblemResponse(string? Title, string? Detail, Dictionary<string, string[]>? Errors);
}
