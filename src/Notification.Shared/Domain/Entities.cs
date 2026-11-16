namespace Notification.Shared.Domain;

/// <summary>Danh bạ người nhận, dùng cho chức năng "chọn" người nhận.</summary>
public class Contact
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Chiến dịch thông báo, chứa luôn nội dung (mẫu) cần gửi.</summary>
public class Campaign
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public NotificationChannel Channel { get; set; }
    public required string Subject { get; set; }

    /// <summary>Nội dung mẫu, hỗ trợ biến {{name}}.</summary>
    public required string Content { get; set; }

    public CampaignStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    public List<CampaignRecipient> Recipients { get; set; } = [];
    public List<Delivery> Deliveries { get; set; } = [];
}

/// <summary>Bản chụp danh sách người nhận tại thời điểm tạo chiến dịch.</summary>
public class CampaignRecipient
{
    public long Id { get; set; }
    public Guid CampaignId { get; set; }
    public Guid? ContactId { get; set; }
    public required string Name { get; set; }
    public required string Address { get; set; }
}

/// <summary>Job gửi một thông báo tới một người nhận.</summary>
public class Delivery
{
    public Guid Id { get; set; }
    public Guid CampaignId { get; set; }
    public required string RecipientName { get; set; }
    public required string RecipientAddress { get; set; }
    public DeliveryStatus Status { get; set; }
    public int AttemptCount { get; set; }
    public string? LastError { get; set; }
    public FailureKind? FailureKind { get; set; }

    /// <summary>Worker đang giữ hoặc đã xử lý job.</summary>
    public string? WorkerId { get; set; }

    public DateTimeOffset? LockedAt { get; set; }

    /// <summary>Thời điểm đã publish lên RabbitMQ; null nghĩa là chưa publish (outbox).</summary>
    public DateTimeOffset? PublishedAt { get; set; }

    public DateTimeOffset? SentAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public List<DeliveryAttempt> Attempts { get; set; } = [];
}

/// <summary>Lịch sử từng lần worker thử gửi một job.</summary>
public class DeliveryAttempt
{
    public long Id { get; set; }
    public Guid DeliveryId { get; set; }
    public int AttemptNo { get; set; }
    public required string WorkerId { get; set; }
    public bool Succeeded { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset FinishedAt { get; set; }
}
