using System.Text.Json.Serialization;
using Campaign.Api.Endpoints;
using Campaign.Api.Infrastructure;
using Campaign.Api.Messaging;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Notification.Shared.Data;
using Notification.Shared.Logging;
using Notification.Shared.Messaging;
using Serilog;
using Serilog.Events;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSerilog(lc => lc.ConfigureNotificationLogging(builder.Configuration, "campaign-api"));

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("Thiếu cấu hình ConnectionStrings:Default");
builder.Services.AddNotificationDb(connectionString);

builder.Services.Configure<RabbitMqOptions>(builder.Configuration.GetSection(RabbitMqOptions.SectionName));
builder.Services.AddSingleton<RabbitMqPublisher>();
builder.Services.AddSingleton<OutboxSignal>();
builder.Services.AddHostedService<OutboxDispatcher>();

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<DatabaseUnavailableHandler>();

builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("postgres", tags: ["ready"])
    .AddCheck<RabbitMqHealthCheck>("rabbitmq", tags: ["ready"]);

// Enum trao đổi dạng chuỗi ("Email", "Draft") cho dễ đọc
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseExceptionHandler();
app.UseSerilogRequestLogging(o =>
{
    // Healthcheck được gọi định kỳ, không ghi log mỗi lần để tránh ngập log
    o.GetLevel = (ctx, _, ex) =>
        ex is not null || ctx.Response.StatusCode >= 500 ? LogEventLevel.Error
        : ctx.Request.Path.StartsWithSegments("/health") ? LogEventLevel.Verbose
        : LogEventLevel.Information;
});

await DatabaseInitializer.EnsureCreatedAsync(app.Services, app.Lifetime.ApplicationStopping);

app.MapGet("/", () => Results.Ok(new { service = "Campaign.Api", status = "running" }));

// /health: process còn sống; /health/ready: kết nối được PostgreSQL và RabbitMQ
app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = HealthResponseWriter.WriteAsync
});

app.MapCampaignEndpoints();
app.MapDeliveryEndpoints();
app.MapContactEndpoints();

app.Run();
