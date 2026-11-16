using System.Text.Json;
using Microsoft.Extensions.Options;
using Notification.Shared.Messaging;
using Notification.Worker.Processing;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

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

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = options.Value;
        await using var connection = await RabbitMqConnection.ConnectWithRetryAsync(
            opts, $"notification-worker-{_workerId}", logger, stoppingToken);
        await using var channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: false, publisherConfirmationTrackingEnabled: false,
                consumerDispatchConcurrency: opts.ConsumerConcurrency),
            stoppingToken);

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
            // Message hỏng không bao giờ xử lý được: bỏ đi thay vì trả về queue mãi
            logger.LogError("Message {MessageId} không đọc được, loại bỏ", ea.BasicProperties.MessageId);
            await channel.BasicRejectAsync(ea.DeliveryTag, requeue: false, ct);
            return;
        }

        using var _ = logger.BeginScope(new Dictionary<string, object>
        {
            ["CampaignId"] = message.CampaignId,
            ["DeliveryId"] = message.DeliveryId
        });

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var processor = scope.ServiceProvider.GetRequiredService<JobProcessor>();
            var outcome = await processor.ProcessAsync(message, _workerId, ct);

            if (outcome == JobOutcome.HeldByOtherWorker)
            {
                await Task.Delay(RequeueDelay, ct);
                await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: true, ct);
                return;
            }

            // Chỉ ack sau khi kết quả đã ghi vào DB: worker chết trước bước này thì RabbitMQ giao lại job
            await channel.BasicAckAsync(ea.DeliveryTag, multiple: false, ct);
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
}
