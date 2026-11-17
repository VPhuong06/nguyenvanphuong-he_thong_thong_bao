using RabbitMQ.Client;

namespace Notification.Shared.Messaging;

/// <summary>
/// Topology Queue-based: một direct exchange và một queue chung cho mọi worker
/// (competing consumers). Không dùng fanout để tránh gửi trùng.
///
/// Retry tự động: worker đẩy job lỗi tạm thời vào một retry queue có TTL. Hết TTL, RabbitMQ
/// dead-letter message về lại exchange chính, nên job quay lại notify.jobs sau khoảng chờ.
///
/// Dead-letter queue: message bị worker reject (lỗi cuối cùng hoặc message hỏng) được RabbitMQ
/// chuyển sang notify.dlq để kiểm tra sau.
/// </summary>
public static class RabbitMqTopology
{
    public const string Exchange = "notify.exchange";
    public const string JobsQueue = "notify.jobs";
    public const string SendRoutingKey = "send";

    public const string RetryExchange = "notify.retry";

    public const string DeadLetterExchange = "notify.dlx";
    public const string DeadLetterQueue = "notify.dlq";

    /// <summary>Các mức chờ tăng dần giữa những lần tự thử lại.</summary>
    public static readonly IReadOnlyList<RetryQueue> RetryQueues =
    [
        new("notify.retry.10s", "10s", TimeSpan.FromSeconds(10)),
        new("notify.retry.30s", "30s", TimeSpan.FromSeconds(30)),
        new("notify.retry.60s", "60s", TimeSpan.FromSeconds(60))
    ];

    /// <summary>Khai báo idempotent, API và Worker đều gọi khi khởi động.</summary>
    public static async Task DeclareAsync(IChannel channel, CancellationToken ct = default)
    {
        await channel.ExchangeDeclareAsync(Exchange, ExchangeType.Direct, durable: true, autoDelete: false,
            cancellationToken: ct);

        await channel.ExchangeDeclareAsync(DeadLetterExchange, ExchangeType.Fanout, durable: true, autoDelete: false,
            cancellationToken: ct);
        await channel.QueueDeclareAsync(DeadLetterQueue, durable: true, exclusive: false, autoDelete: false,
            cancellationToken: ct);
        await channel.QueueBindAsync(DeadLetterQueue, DeadLetterExchange, routingKey: "", cancellationToken: ct);

        await channel.QueueDeclareAsync(JobsQueue, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?> { ["x-dead-letter-exchange"] = DeadLetterExchange },
            cancellationToken: ct);
        await channel.QueueBindAsync(JobsQueue, Exchange, SendRoutingKey, cancellationToken: ct);

        await channel.ExchangeDeclareAsync(RetryExchange, ExchangeType.Direct, durable: true, autoDelete: false,
            cancellationToken: ct);
        foreach (var retry in RetryQueues)
        {
            await channel.QueueDeclareAsync(retry.Queue, durable: true, exclusive: false, autoDelete: false,
                arguments: new Dictionary<string, object?>
                {
                    ["x-message-ttl"] = (int)retry.Delay.TotalMilliseconds,
                    ["x-dead-letter-exchange"] = Exchange,
                    ["x-dead-letter-routing-key"] = SendRoutingKey
                },
                cancellationToken: ct);
            await channel.QueueBindAsync(retry.Queue, RetryExchange, retry.RoutingKey, cancellationToken: ct);
        }
    }
}

/// <summary>Một retry queue: message nằm chờ hết <see cref="Delay"/> rồi quay lại queue chính.</summary>
public sealed record RetryQueue(string Queue, string RoutingKey, TimeSpan Delay);
