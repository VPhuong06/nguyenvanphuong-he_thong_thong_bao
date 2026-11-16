namespace Notification.Shared.Domain;

public enum NotificationChannel
{
    Email,
    Sms
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
