using System.Text.Json;
using System.Text.Json.Serialization;
using Notification.Shared.Domain;

namespace Notification.Shared.Messaging;

public enum DeliveryAttemptOutcome
{
    Sent,

    /// <summary>Lỗi tạm thời, job đã được đưa vào retry queue.</summary>
    RetryScheduled,

    /// <summary>Thất bại cuối cùng.</summary>
    Failed
}

/// <summary>
/// Event worker phát ra sau mỗi lần gửi, theo mẫu Phát hành–đăng ký (Publish–subscribe): publish lên
/// fanout exchange notify.events, mỗi service quan tâm (thống kê, audit…) có queue riêng và nhận một bản sao.
/// Khác với job gửi (Queue-based, mỗi job chỉ một worker xử lý), event chỉ thông báo việc đã xảy ra.
/// </summary>
public sealed record DeliveryAttemptedEvent(
    Guid DeliveryId,
    Guid CampaignId,
    string WorkerId,
    int AttemptNo,
    DeliveryAttemptOutcome Outcome,
    FailureKind? FailureKind,
    int DurationMs,
    DateTimeOffset OccurredAt)
{
    /// <summary>Định dạng JSON dùng chung cho bên phát và bên nhận event.</summary>
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };
}
