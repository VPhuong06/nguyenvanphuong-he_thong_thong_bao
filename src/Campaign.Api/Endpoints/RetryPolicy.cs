using Microsoft.EntityFrameworkCore.Query;
using Notification.Shared.Domain;

namespace Campaign.Api.Endpoints;

public static class RetryPolicy
{
    /// <summary>Chạy lại hàng loạt chỉ áp dụng cho job đã thử ít hơn số lần này.</summary>
    public const int MaxAttempts = 5;

    /// <summary>
    /// Đưa job về Pending và xoá PublishedAt để outbox publish lại. Lỗi cũ vẫn giữ trong
    /// delivery_attempts; LastError được ghi đè ở lần gửi tiếp theo.
    /// </summary>
    public static Action<UpdateSettersBuilder<Delivery>> ResetToPending(DateTimeOffset now) => s => s
        .SetProperty(d => d.Status, DeliveryStatus.Pending)
        .SetProperty(d => d.PublishedAt, (DateTimeOffset?)null)
        .SetProperty(d => d.WorkerId, (string?)null)
        .SetProperty(d => d.LockedAt, (DateTimeOffset?)null)
        .SetProperty(d => d.UpdatedAt, now);
}
