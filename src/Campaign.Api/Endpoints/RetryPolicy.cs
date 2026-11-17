using Microsoft.EntityFrameworkCore.Query;
using Notification.Shared.Domain;
using Notification.Shared.Messaging;

namespace Campaign.Api.Endpoints;

public static class RetryPolicy
{
    /// <summary>
    /// Chạy lại hàng loạt chỉ áp dụng cho job đã thử ít hơn số lần này. Mỗi lượt gửi gồm lần đầu
    /// cộng các lần worker tự thử lại, nên giới hạn tương đương khoảng 3 lượt.
    /// </summary>
    public static readonly int MaxAttempts = 3 * (1 + RabbitMqTopology.RetryQueues.Count);

    /// <summary>
    /// Đưa job về Pending, xoá PublishedAt để outbox publish lại và đặt lại số lần tự thử lại
    /// cho lượt mới. Lỗi cũ vẫn giữ trong delivery_attempts; LastError được ghi đè ở lần gửi tiếp theo.
    /// </summary>
    public static Action<UpdateSettersBuilder<Delivery>> ResetToPending(DateTimeOffset now) => s => s
        .SetProperty(d => d.Status, DeliveryStatus.Pending)
        .SetProperty(d => d.PublishedAt, (DateTimeOffset?)null)
        .SetProperty(d => d.WorkerId, (string?)null)
        .SetProperty(d => d.LockedAt, (DateTimeOffset?)null)
        .SetProperty(d => d.AutoRetryCount, 0)
        .SetProperty(d => d.NextRetryAt, (DateTimeOffset?)null)
        .SetProperty(d => d.UpdatedAt, now);
}
