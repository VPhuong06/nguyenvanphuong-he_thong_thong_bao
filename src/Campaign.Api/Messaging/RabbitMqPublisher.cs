using System.Text.Json;
using Microsoft.Extensions.Options;
using Notification.Shared.Messaging;
using RabbitMQ.Client;

namespace Campaign.Api.Messaging;

/// <summary>
/// Publish job lên RabbitMQ với publisher confirms: chỉ khi broker xác nhận đã nhận message
/// thì job mới được đánh dấu đã publish trong DB.
/// Kết nối được tạo khi cần, nên API vẫn chạy được lúc RabbitMQ đang dừng.
/// </summary>
public sealed class RabbitMqPublisher(IOptions<RabbitMqOptions> options, ILogger<RabbitMqPublisher> logger)
    : IAsyncDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IConnection? _connection;
    private IChannel? _channel;

    public async Task PublishAsync(SendNotificationMessage message, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var channel = await GetChannelAsync(ct);
            var props = new BasicProperties
            {
                Persistent = true,
                ContentType = "application/json",
                Type = nameof(SendNotificationMessage),
                MessageId = message.DeliveryId.ToString(),
                CorrelationId = message.CampaignId.ToString()
            };
            var body = JsonSerializer.SerializeToUtf8Bytes(message);

            // mandatory: message không vào được queue nào sẽ báo lỗi thay vì mất im lặng
            await channel.BasicPublishAsync(RabbitMqTopology.Exchange, RabbitMqTopology.SendRoutingKey,
                mandatory: true, basicProperties: props, body: body, cancellationToken: ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<IChannel> GetChannelAsync(CancellationToken ct)
    {
        if (_channel is { IsOpen: true })
            return _channel;

        await CloseAsync();

        var factory = RabbitMqConnection.CreateFactory(options.Value, "campaign-api-publisher", automaticRecovery: false);
        _connection = await factory.CreateConnectionAsync(ct);
        _channel = await _connection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
            ct);
        await RabbitMqTopology.DeclareAsync(_channel, ct);

        logger.LogInformation("Publisher đã kết nối RabbitMQ {Host}:{Port}", options.Value.Host, options.Value.Port);
        return _channel;
    }

    private async Task CloseAsync()
    {
        if (_channel is not null)
        {
            try { await _channel.DisposeAsync(); } catch { /* kênh đã hỏng, bỏ qua */ }
            _channel = null;
        }
        if (_connection is not null)
        {
            try { await _connection.DisposeAsync(); } catch { /* kết nối đã hỏng, bỏ qua */ }
            _connection = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await CloseAsync();
        _lock.Dispose();
    }
}
