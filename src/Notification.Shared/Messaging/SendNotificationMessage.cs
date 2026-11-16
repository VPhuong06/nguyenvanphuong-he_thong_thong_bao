namespace Notification.Shared.Messaging;

/// <summary>
/// Message job gửi trên queue. Chỉ mang khoá tham chiếu; worker đọc nội dung từ DB,
/// vì DB là nguồn sự thật về trạng thái.
/// </summary>
public sealed record SendNotificationMessage(Guid DeliveryId, Guid CampaignId, int Attempt);
