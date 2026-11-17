using Notification.Shared.Domain;

namespace Campaign.Api.Contracts;

/// <summary>
/// Tạo chiến dịch. Người nhận có thể chọn từ danh bạ (ContactIds), nhập trực tiếp (Recipients) hoặc cả hai.
/// </summary>
public sealed record CreateCampaignRequest(
    string Name,
    NotificationChannel Channel,
    string Subject,
    string Content,
    List<Guid>? ContactIds,
    List<RecipientInput>? Recipients);

public sealed record RecipientInput(string? Name, string Address);

/// <summary>Số liệu theo dõi chiến dịch. Pending gồm cả job đang được worker xử lý.</summary>
public sealed record CampaignStats(int Total, int Pending, int Sent, int Failed);

public sealed record CampaignSummaryResponse(
    Guid Id,
    string Name,
    NotificationChannel Channel,
    CampaignStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    CampaignStats Stats);

public sealed record CampaignDetailResponse(
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

public sealed record StartCampaignResponse(Guid CampaignId, CampaignStatus Status, int JobCount);

public sealed record DeliveryResponse(
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
    DateTimeOffset? NextRetryAt,
    DateTimeOffset UpdatedAt);

public sealed record DeliveryAttemptResponse(
    int AttemptNo,
    string WorkerId,
    bool Succeeded,
    string? Error,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt);

public sealed record DeliveryDetailResponse(DeliveryResponse Delivery, IReadOnlyList<DeliveryAttemptResponse> Attempts);

/// <summary>Kết quả chạy lại job thất bại của một chiến dịch.</summary>
public sealed record RetryFailedResponse(
    Guid CampaignId,
    int Retried,
    int SkippedPermanent,
    int SkippedMaxAttempts);

public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalItems);

public sealed record ContactResponse(Guid Id, string Name, string? Email, string? Phone, string? TelegramChatId);

/// <param name="BotUsername">Tên bot để người nhận tìm và nhắn /start; null khi chưa cấu hình bot.</param>
public sealed record TelegramInfoResponse(bool Enabled, string? BotUsername, string? BotLink);
