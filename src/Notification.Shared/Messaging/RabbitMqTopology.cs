using RabbitMQ.Client;

namespace Notification.Shared.Messaging;

/// <summary>
/// Topology Queue-based: một direct exchange và một queue chung cho mọi worker
/// (competing consumers). Không dùng fanout để tránh gửi trùng.
/// </summary>
public static class RabbitMqTopology
{
    public const string Exchange = "notify.exchange";
    public const string JobsQueue = "notify.jobs";
    public const string SendRoutingKey = "send";

    /// <summary>Khai báo idempotent, API và Worker đều gọi khi khởi động.</summary>
    public static async Task DeclareAsync(IChannel channel, CancellationToken ct = default)
    {
        await channel.ExchangeDeclareAsync(Exchange, ExchangeType.Direct, durable: true, autoDelete: false,
            cancellationToken: ct);
        await channel.QueueDeclareAsync(JobsQueue, durable: true, exclusive: false, autoDelete: false,
            cancellationToken: ct);
        await channel.QueueBindAsync(JobsQueue, Exchange, SendRoutingKey, cancellationToken: ct);
    }
}
