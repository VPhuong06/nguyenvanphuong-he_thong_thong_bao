using Microsoft.Extensions.Options;
using Notification.Shared.Domain;

namespace Notification.Worker.Sending;

public sealed record SendRequest(NotificationChannel Channel, string Address, string Subject, string Body);

public sealed record SendResult(bool Succeeded, string? Error)
{
    public static SendResult Ok() => new(true, null);
    public static SendResult Fail(string error) => new(false, error);
}

public interface INotificationSender
{
    Task<SendResult> SendAsync(SendRequest request, CancellationToken ct);
}

public sealed class MockSenderOptions
{
    public const string SectionName = "MockSender";

    public int MinDelayMs { get; set; } = 200;
    public int MaxDelayMs { get; set; } = 800;

    /// <summary>Tỉ lệ lỗi ngẫu nhiên (0–1), giả lập nhà cung cấp tạm thời lỗi.</summary>
    public double FailureRate { get; set; } = 0.1;
}

/// <summary>
/// Giả lập nhà cung cấp email/SMS: có độ trễ và tỉ lệ lỗi cấu hình được.
/// Địa chỉ chứa "fail" luôn lỗi, để chủ động tạo job thất bại khi demo.
/// </summary>
public sealed class MockNotificationSender(IOptions<MockSenderOptions> options, ILogger<MockNotificationSender> logger)
    : INotificationSender
{
    public async Task<SendResult> SendAsync(SendRequest request, CancellationToken ct)
    {
        var opts = options.Value;
        await Task.Delay(Random.Shared.Next(opts.MinDelayMs, Math.Max(opts.MinDelayMs, opts.MaxDelayMs) + 1), ct);

        if (request.Address.Contains("fail", StringComparison.OrdinalIgnoreCase))
            return SendResult.Fail("Địa chỉ người nhận bị từ chối (giả lập)");

        if (Random.Shared.NextDouble() < opts.FailureRate)
            return SendResult.Fail("Nhà cung cấp tạm thời không phản hồi (giả lập)");

        logger.LogDebug("[{Channel}] Gửi tới {Address}: {Subject}", request.Channel, request.Address, request.Subject);
        return SendResult.Ok();
    }
}
