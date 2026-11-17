using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Notification.Shared.Logging;
using Notification.Shared.Messaging;
using Notification.Statistics.Data;
using Notification.Statistics.Endpoints;
using Notification.Statistics.Messaging;
using Serilog;
using Serilog.Events;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSerilog(lc => lc.ConfigureNotificationLogging(builder.Configuration, "statistics"));

var connectionString = builder.Configuration.GetConnectionString("Statistics")
    ?? throw new InvalidOperationException("Thiếu cấu hình ConnectionStrings:Statistics");
builder.Services.AddDbContext<StatsDbContext>(o => o.UseNpgsql(connectionString));

builder.Services.Configure<RabbitMqOptions>(builder.Configuration.GetSection(RabbitMqOptions.SectionName));
builder.Services.AddHostedService<DeliveryEventsConsumer>();

builder.Services.AddHealthChecks()
    .AddCheck<StatsDbHealthCheck>("postgres", tags: ["ready"]);

var app = builder.Build();

app.UseSerilogRequestLogging(o =>
{
    o.GetLevel = (ctx, _, ex) =>
        ex is not null || ctx.Response.StatusCode >= 500 ? LogEventLevel.Error
        : ctx.Request.Path.StartsWithSegments("/health") || ctx.Request.Path.StartsWithSegments("/stats")
            ? LogEventLevel.Verbose
            : LogEventLevel.Information;
});

await StatsDbInitializer.EnsureCreatedAsync(app.Services, app.Lifetime.ApplicationStopping);

app.MapGet("/", () => Results.Ok(new { service = "Notification.Statistics", status = "running" }));
app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") });
app.MapStatsEndpoints();

app.Run();

/// <summary>Kiểm tra kết nối tới database thống kê.</summary>
internal sealed class StatsDbHealthCheck(StatsDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            return await db.Database.CanConnectAsync(ct)
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("Không kết nối được PostgreSQL");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Không kết nối được PostgreSQL", ex);
        }
    }
}
