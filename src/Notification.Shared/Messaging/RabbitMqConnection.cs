using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace Notification.Shared.Messaging;

public static class RabbitMqConnection
{
    /// <summary>
    /// Kết nối tới RabbitMQ, thử lại cho tới khi broker sẵn sàng. Sau khi đã kết nối,
    /// automatic recovery của client tự nối lại khi broker tạm dừng.
    /// </summary>
    public static ConnectionFactory CreateFactory(RabbitMqOptions options, string clientName, bool automaticRecovery) =>
        new()
        {
            HostName = options.Host,
            Port = options.Port,
            UserName = options.UserName,
            Password = options.Password,
            VirtualHost = options.VirtualHost,
            ClientProvidedName = clientName,
            AutomaticRecoveryEnabled = automaticRecovery,
            TopologyRecoveryEnabled = automaticRecovery,
            NetworkRecoveryInterval = TimeSpan.FromSeconds(5),
            RequestedConnectionTimeout = TimeSpan.FromSeconds(5)
        };

    public static async Task<IConnection> ConnectWithRetryAsync(
        RabbitMqOptions options, string clientName, ILogger logger, CancellationToken ct)
    {
        var factory = CreateFactory(options, clientName, automaticRecovery: true);

        var delay = TimeSpan.FromSeconds(2);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var connection = await factory.CreateConnectionAsync(ct);
                logger.LogInformation("Đã kết nối RabbitMQ {Host}:{Port} (lần thử {Attempt})",
                    options.Host, options.Port, attempt);
                return connection;
            }
            catch (BrokerUnreachableException ex)
            {
                logger.LogWarning("Chưa kết nối được RabbitMQ {Host}:{Port} (lần thử {Attempt}): {Error}. Thử lại sau {Delay}s",
                    options.Host, options.Port, attempt, ex.Message, delay.TotalSeconds);
                await Task.Delay(delay, ct);
                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 30));
            }
        }
    }
}
