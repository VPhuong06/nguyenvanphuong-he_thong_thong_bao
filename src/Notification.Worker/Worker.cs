using System.Text.Json;
using Microsoft.Extensions.Options;
using Notification.Shared.Logging;
using Notification.Shared.Messaging;
using Notification.Worker.Processing;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Serilog.Context;

namespace Notification.Worker;

/// <summary>
/// Consumer cạnh tranh trên queue notify.jobs. Nhiều instance worker cùng đọc một queue,
/// mỗi job chỉ được giao cho một worker.
/// </summary>
public class Worker(
    IOptions<RabbitMqOptions> options,
    IServiceScopeFactory scopeFactory,
    ILogger<Worker> logger) : BackgroundService
{
    // Khi DB tạm dừng, chờ một lúc rồi mới trả job về queue để tránh vòng lặp nhận–trả liên tục
    private static readonly TimeSpan RequeueDelay = TimeSpan.FromSeconds(5);

    // Hostname khác nhau giữa các container khi scale, dùng để biết job do worker nào xử lý
    private readonly string _workerId = Environment.MachineName;

    // Kênh riêng (có publisher confirms) để đẩy job vào retry queue
    private readonly SemaphoreSlim _publishLock = new(1, 1);
    private IChannel? _publishChannel;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = options.Value;
        await using var connection = await RabbitMqConnection.ConnectWithRetryAsync(
            opts, $"notification-worker-{_workerId}", logger, stoppingToken);
        await using var channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: false, publisherConfirmationTrackingEnabled: false,
                consumerDispatchConcurrency: opts.ConsumerConcurrency),
            stoppingToken);
        await using var publishChannel = await connection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
            stoppingToken);
        _publishChannel = publishChannel;

        await RabbitMqTopology.DeclareAsync(channel, stoppingToken);
        await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: opts.PrefetchCount, global: false,
            cancellationToken: stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, ea) => HandleMessageAsync(channel, ea, stoppingToken);

        await channel.BasicConsumeAsync(RabbitMqTopology.JobsQueue, autoAck: false, consumer, stoppingToken);
        logger.LogInformation("Worker {WorkerId} đang lắng nghe queue {Queue} (prefetch {Prefetch}, song song {Concurrency})",
            _workerId, RabbitMqTopology.JobsQueue, opts.PrefetchCount, opts.ConsumerConcurrency);

        await Task.Delay(Timeout.Infinite, stoppingToken).ContinueWith(_ => { }, CancellationToken.None);
    }

    private async Task HandleMessageAsync(IChannel channel, BasicDeliverEventArgs ea, CancellationToken ct)
    {
        SendNotificationMessage? message;
        try
        {
            message = JsonSerializer.Deserialize<SendNotificationMessage>(ea.Body.Span);
        }
        catch (JsonException)
        {
            message = null;
        }

        if (message is null)
        {
            // Message hỏng không bao giờ xử lý được: reject để RabbitMQ chuyển vào DLQ thay vì trả về queue mãi
            logger.LogError("Message {MessageId} không đọc được, chuyển vào {Dlq}",
                ea.BasicProperties.MessageId, RabbitMqTopology.DeadLetterQueue);
            await channel.BasicRejectAsync(ea.DeliveryTag, requeue: false, ct);
            return;
        }

        // Correlation ID của luồng bất đồng bộ là CampaignId (API gửi kèm trong thuộc tính CorrelationId
        // của message), nên log của mọi worker về cùng một chiến dịch có chung một mã để lần theo
        var correlationId = ea.BasicProperties.CorrelationId ?? message.CampaignId.ToString();
        using var _ = LogContext.PushProperty(LoggingSetup.CorrelationIdProperty, correlationId);
        using var __ = LogContext.PushProperty("CampaignId", message.CampaignId);
        using var ___ = LogContext.PushProperty("DeliveryId", message.DeliveryId);

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var processor = scope.ServiceProvider.GetRequiredService<JobProcessor>();
            var result = await processor.ProcessAsync(message, _workerId, ct);

            switch (result.Outcome)
            {
                case JobOutcome.HeldByOtherWorker:
                    await Task.Delay(RequeueDelay, ct);
                    await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: true, ct);
                    return;

                case JobOutcome.RetryScheduled:
                    // Đẩy vào retry queue trước rồi mới ack message gốc: nếu publish lỗi, message gốc
                    // được trả lại queue và job (đang Pending) vẫn được xử lý tiếp
                    await PublishRetryAsync(message, result.Retry!, ct);
                    await channel.BasicAckAsync(ea.DeliveryTag, multiple: false, ct);
                    return;

                case JobOutcome.Failed:
                    // Thất bại cuối cùng: kết quả đã ghi trong DB, reject để message vào DLQ phục vụ kiểm tra
                    await channel.BasicRejectAsync(ea.DeliveryTag, requeue: false, ct);
                    logger.LogInformation("Job {DeliveryId} thất bại cuối cùng, message chuyển vào {Dlq}",
                        message.DeliveryId, RabbitMqTopology.DeadLetterQueue);
                    return;

                default:
                    // Chỉ ack sau khi kết quả đã ghi vào DB: worker chết trước bước này thì RabbitMQ giao lại job
                    await channel.BasicAckAsync(ea.DeliveryTag, multiple: false, ct);
                    return;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Worker đang dừng: không ack, RabbitMQ sẽ giao lại job khi kênh đóng
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Lỗi xử lý job {DeliveryId}, trả lại queue sau {Delay}s",
                message.DeliveryId, RequeueDelay.TotalSeconds);
            try
            {
                await Task.Delay(RequeueDelay, ct);
                await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: true, ct);
            }
            catch (OperationCanceledException)
            {
                // Worker đang dừng: job chưa ack sẽ được giao lại
            }
        }
    }

    private async Task PublishRetryAsync(SendNotificationMessage message, RetryQueue retry, CancellationToken ct)
    {
        var props = new BasicProperties
        {
            Persistent = true,
            ContentType = "application/json",
            Type = nameof(SendNotificationMessage),
            MessageId = message.DeliveryId.ToString(),
            CorrelationId = message.CampaignId.ToString()
        };
        var body = JsonSerializer.SerializeToUtf8Bytes(message with { Attempt = message.Attempt + 1 });

        await _publishLock.WaitAsync(ct);
        try
        {
            await _publishChannel!.BasicPublishAsync(RabbitMqTopology.RetryExchange, retry.RoutingKey,
                mandatory: true, basicProperties: props, body: body, cancellationToken: ct);
        }
        finally
        {
            _publishLock.Release();
        }
    }
}
