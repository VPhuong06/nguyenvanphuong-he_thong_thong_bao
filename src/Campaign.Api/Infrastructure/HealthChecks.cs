using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Notification.Shared.Data;
using Notification.Shared.Messaging;

namespace Campaign.Api.Infrastructure;

public sealed class DatabaseHealthCheck(NotificationDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            return await db.Database.CanConnectAsync(ct)
                ? HealthCheckResult.Healthy("Kết nối PostgreSQL bình thường")
                : HealthCheckResult.Unhealthy("Không kết nối được PostgreSQL");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Không kết nối được PostgreSQL", ex);
        }
    }
}

public sealed class RabbitMqHealthCheck(IOptions<RabbitMqOptions> options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            var factory = RabbitMqConnection.CreateFactory(options.Value, "campaign-api-healthcheck", automaticRecovery: false);
            factory.RequestedConnectionTimeout = TimeSpan.FromSeconds(3);
            await using var connection = await factory.CreateConnectionAsync(ct);
            return HealthCheckResult.Healthy("Kết nối RabbitMQ bình thường");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Không kết nối được RabbitMQ", ex);
        }
    }
}

/// <summary>Trả kết quả healthcheck dạng JSON, liệt kê từng thành phần.</summary>
public static class HealthResponseWriter
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    };

    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json; charset=utf-8";
        return context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                description = e.Value.Description,
                durationMs = (int)e.Value.Duration.TotalMilliseconds
            })
        }, Json));
    }
}
