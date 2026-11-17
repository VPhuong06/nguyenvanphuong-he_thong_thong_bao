using System.Net.Mail;
using System.Text.RegularExpressions;
using Notification.Shared.Domain;

namespace Campaign.Api.Validation;

public static partial class RecipientAddress
{
    /// <summary>
    /// Kiểm tra và chuẩn hoá địa chỉ theo kênh gửi, để việc loại trùng không phụ thuộc
    /// chữ hoa/thường hay dấu phân cách số điện thoại.
    /// </summary>
    public static bool TryNormalize(NotificationChannel channel, string? raw, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        var value = raw.Trim();
        switch (channel)
        {
            case NotificationChannel.Email:
                if (value.Length > 320 || !MailAddress.TryCreate(value, out var mail) || mail.Address != value)
                    return false;
                normalized = value.ToLowerInvariant();
                return true;

            case NotificationChannel.Sms:
                var phone = PhoneSeparators().Replace(value, "");
                if (!PhonePattern().IsMatch(phone))
                    return false;
                normalized = phone;
                return true;

            case NotificationChannel.Telegram:
                // Chat ID dạng số (âm với nhóm) hoặc @username của kênh công khai
                if (!TelegramChatPattern().IsMatch(value))
                    return false;
                normalized = value.StartsWith('@') ? value.ToLowerInvariant() : value;
                return true;

            default:
                return false;
        }
    }

    [GeneratedRegex(@"[\s.\-()]")]
    private static partial Regex PhoneSeparators();

    [GeneratedRegex(@"^\+?[0-9]{8,15}$")]
    private static partial Regex PhonePattern();

    [GeneratedRegex(@"^(-?[0-9]{5,20}|@[A-Za-z0-9_]{5,32})$")]
    private static partial Regex TelegramChatPattern();
}
