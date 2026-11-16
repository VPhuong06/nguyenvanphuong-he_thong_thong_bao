using Notification.Shared.Domain;

namespace Admin.Web.Components.Shared;

/// <summary>Nhãn tiếng Việt và class CSS cho các trạng thái.</summary>
public static class Labels
{
    // Hiển thị giờ Việt Nam; không phụ thuộc múi giờ của container (thường là UTC)
    private static readonly TimeSpan DisplayOffset = TimeSpan.FromHours(7);

    public static string Of(CampaignStatus status) => status switch
    {
        CampaignStatus.Draft => "Nháp",
        CampaignStatus.Running => "Đang gửi",
        CampaignStatus.Completed => "Hoàn tất",
        CampaignStatus.CompletedWithFailures => "Hoàn tất, có lỗi",
        _ => status.ToString()
    };

    public static string Of(DeliveryStatus status) => status switch
    {
        DeliveryStatus.Pending => "Chờ gửi",
        DeliveryStatus.Processing => "Đang xử lý",
        DeliveryStatus.Sent => "Đã gửi",
        DeliveryStatus.Failed => "Thất bại",
        _ => status.ToString()
    };

    public static string Of(NotificationChannel channel) => channel switch
    {
        NotificationChannel.Email => "Email",
        NotificationChannel.Sms => "SMS",
        _ => channel.ToString()
    };

    public static string Of(FailureKind kind) => kind switch
    {
        FailureKind.Transient => "Tạm thời",
        FailureKind.Permanent => "Vĩnh viễn",
        _ => kind.ToString()
    };

    public static string Css(CampaignStatus status) => status switch
    {
        CampaignStatus.Draft => "badge badge-muted",
        CampaignStatus.Running => "badge badge-info",
        CampaignStatus.Completed => "badge badge-success",
        CampaignStatus.CompletedWithFailures => "badge badge-warning",
        _ => "badge"
    };

    public static string Css(DeliveryStatus status) => status switch
    {
        DeliveryStatus.Pending => "badge badge-muted",
        DeliveryStatus.Processing => "badge badge-info",
        DeliveryStatus.Sent => "badge badge-success",
        DeliveryStatus.Failed => "badge badge-danger",
        _ => "badge"
    };

    public static string Time(DateTimeOffset? time) =>
        time?.ToOffset(DisplayOffset).ToString("dd/MM/yyyy HH:mm:ss") ?? "—";

    public static string ShortTime(DateTimeOffset? time) =>
        time?.ToOffset(DisplayOffset).ToString("HH:mm:ss") ?? "—";
}
