using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Notification.Shared.Messaging;
using Notification.Statistics.Data;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Notification.Statistics.Messaging;

/// <summary>
/// Subscriber của fanout exchange notify.events. Service thống kê có queue riêng, nên nhận bản sao
/// của mọi event mà không lấy mất event của subscriber khác (khác với notify.jobs, nơi các worker
/// tranh nhau lấy job).
/// </summary>
public sealed class DeliveryEventsConsumer(
    IOptions<RabbitMqOptions> options,
    IServiceScopeFactory scopeFactory,
    ILogger<DeliveryEventsConsumer> logger) : BackgroundService
{
    public const string Queue = "stats.delivery-events";

    private static readonly TimeSpan RequeueDelay = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var connection = await RabbitMqConnection.ConnectWithRetryAsync(
            options.Value, "statistics-subscriber", logger, stoppingToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);

        await channel.ExchangeDeclareAsync(RabbitMqTopology.EventsExchange, ExchangeType.Fanout, durable: true,
            autoDelete: false, cancellationToken: stoppingToken);
        await channel.QueueDeclareAsync(Queue, durable: true, exclusive: false, autoDelete: false,
            cancellationToken: stoppingToken);
        await channel.QueueBindAsync(Queue, RabbitMqTopology.EventsExchange, routingKey: "",
            cancellationToken: stoppingToken);
        await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 50, global: false, cancellationToken: stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, ea) => HandleAsync(channel, ea, stoppingToken);
        await channel.BasicConsumeAsync(Queue, autoAck: false, consumer, stoppingToken);

        logger.LogInformation("Service thống kê đăng ký nhận event từ {Exchange} qua queue {Queue}",
            RabbitMqTopology.EventsExchange, Queue);

        await Task.Delay(Timeout.Infinite, stoppingToken).ContinueWith(_ => { }, CancellationToken.None);
    }

    private async Task HandleAsync(IChannel channel, BasicDeliverEventArgs ea, CancellationToken ct)
    {
        DeliveryAttemptedEvent? e;
        try
        {
            e = JsonSerializer.Deserialize<DeliveryAttemptedEvent>(ea.Body.Span, DeliveryAttemptedEvent.Json);
        }
        catch (JsonException)
        {
            e = null;
        }

        if (e is null)
        {
            logger.LogError("Event {MessageId} không đọc được, bỏ qua", ea.BasicProperties.MessageId);
            await channel.BasicRejectAsync(ea.DeliveryTag, requeue: false, ct);
            return;
        }

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<StatsDbContext>();

            var outcome = e.Outcome.ToString();
            var failureKind = e.FailureKind?.ToString();
            var receivedAt = DateTimeOffset.UtcNow;
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO delivery_events ("DeliveryId", "CampaignId", "WorkerId", "AttemptNo", "Outcome",
                                             "FailureKind", "DurationMs", "OccurredAt", "ReceivedAt")
                VALUES ({e.DeliveryId}, {e.CampaignId}, {e.WorkerId}, {e.AttemptNo}, {outcome},
                        {failureKind}, {e.DurationMs}, {e.OccurredAt}, {receivedAt})
                ON CONFLICT ("DeliveryId", "AttemptNo") DO NOTHING
                """, ct);

            await channel.BasicAckAsync(ea.DeliveryTag, multiple: false, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Lỗi ghi event của job {DeliveryId}, trả lại queue sau {Delay}s",
                e.DeliveryId, RequeueDelay.TotalSeconds);
            try
            {
                await Task.Delay(RequeueDelay, ct);
                await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: true, ct);
            }
            catch (OperationCanceledException)
            {
            }
        }
    }
}
