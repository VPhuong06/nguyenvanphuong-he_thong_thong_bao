using System.Text.Json;
using Microsoft.Extensions.Options;
using Notification.Shared.Messaging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Notification.Worker;

/// <summary>
/// Consumer cạnh tranh trên queue notify.jobs. Nhiều instance worker cùng đọc một queue,
/// mỗi job chỉ được giao cho một worker.
/// </summary>
public class Worker(IOptions<RabbitMqOptions> options, ILogger<Worker> logger) : BackgroundService
{
    // Hostname khác nhau giữa các container khi scale, dùng để biết job do worker nào xử lý
    private readonly string _workerId = Environment.MachineName;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = options.Value;
        await using var connection = await RabbitMqConnection.ConnectWithRetryAsync(
            opts, $"notification-worker-{_workerId}", logger, stoppingToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);

        await RabbitMqTopology.DeclareAsync(channel, stoppingToken);
        await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: opts.PrefetchCount, global: false,
            cancellationToken: stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, ea) =>
        {
            var message = JsonSerializer.Deserialize<SendNotificationMessage>(ea.Body.Span);
            logger.LogInformation("Worker {WorkerId} nhận job {DeliveryId} của chiến dịch {CampaignId}",
                _workerId, message?.DeliveryId, message?.CampaignId);

            // TODO: claim job trong DB, gửi thông báo, ghi kết quả rồi mới ack
            await channel.BasicAckAsync(ea.DeliveryTag, multiple: false, cancellationToken: stoppingToken);
        };

        await channel.BasicConsumeAsync(RabbitMqTopology.JobsQueue, autoAck: false, consumer, stoppingToken);
        logger.LogInformation("Worker {WorkerId} đang lắng nghe queue {Queue} (prefetch {Prefetch})",
            _workerId, RabbitMqTopology.JobsQueue, opts.PrefetchCount);

        await Task.Delay(Timeout.Infinite, stoppingToken).ContinueWith(_ => { }, CancellationToken.None);
    }
}
