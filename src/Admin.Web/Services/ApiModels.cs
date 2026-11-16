using Notification.Shared.Domain;

namespace Admin.Web.Services;

// Bản sao các contract của Campaign API: Admin Web chỉ giao tiếp với hệ thống qua REST,
// không tham chiếu trực tiếp tới project API.

public sealed record CampaignStats(int Total, int Pending, int Sent, int Failed)
{
    public int Done => Sent + Failed;
    public double Percent(int value) => Total == 0 ? 0 : value * 100.0 / Total;
}

public sealed record CampaignSummary(
    Guid Id,
    string Name,
    NotificationChannel Channel,
    CampaignStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    CampaignStats Stats);

public sealed record CampaignDetail(
    Guid Id,
    string Name,
    NotificationChannel Channel,
    string Subject,
    string Content,
    CampaignStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    CampaignStats Stats);

public sealed record DeliveryItem(
    Guid Id,
    string RecipientName,
    string RecipientAddress,
    DeliveryStatus Status,
    int AttemptCount,
    string? LastError,
    FailureKind? FailureKind,
    string? WorkerId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PublishedAt,
    DateTimeOffset? SentAt,
    DateTimeOffset UpdatedAt);

public sealed record DeliveryAttemptItem(
    int AttemptNo,
    string WorkerId,
    bool Succeeded,
    string? Error,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt);

public sealed record DeliveryDetail(DeliveryItem Delivery, IReadOnlyList<DeliveryAttemptItem> Attempts);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalItems)
{
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalItems / (double)PageSize));
}

public sealed record ContactItem(Guid Id, string Name, string? Email, string? Phone);

public sealed record RecipientInput(string? Name, string Address);

public sealed record CreateCampaignRequest(
    string Name,
    NotificationChannel Channel,
    string Subject,
    string Content,
    List<Guid> ContactIds,
    List<RecipientInput> Recipients);

public sealed record StartCampaignResult(Guid CampaignId, CampaignStatus Status, int JobCount);

public sealed record RetryFailedResult(Guid CampaignId, int Retried, int SkippedPermanent, int SkippedMaxAttempts);
