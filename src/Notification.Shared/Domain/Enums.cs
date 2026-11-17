namespace Notification.Shared.Domain;

public enum NotificationChannel
{
    Email,
    Sms,
    Telegram
}

public enum CampaignStatus
{
    Draft,
    Running,
    Completed,
    CompletedWithFailures
}

public enum DeliveryStatus
{
    Pending,
    Processing,
    Sent,
    Failed
}

/// <summary>Loại lỗi của lần gửi gần nhất, quyết định job có nên chạy lại hay không.</summary>
public enum FailureKind
{
    /// <summary>Lỗi tạm thời (timeout, nhà cung cấp quá tải): chạy lại có thể thành công.</summary>
    Transient,

    /// <summary>Lỗi vĩnh viễn (địa chỉ sai, bị từ chối): chạy lại vẫn thất bại.</summary>
    Permanent
}
