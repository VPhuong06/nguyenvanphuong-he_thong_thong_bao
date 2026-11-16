using System.Text.Json.Serialization;
using Campaign.Api.Endpoints;
using Campaign.Api.Infrastructure;
using Campaign.Api.Messaging;
using Notification.Shared.Data;
using Notification.Shared.Messaging;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSerilog(lc => lc.ReadFrom.Configuration(builder.Configuration));

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("Thiếu cấu hình ConnectionStrings:Default");
builder.Services.AddNotificationDb(connectionString);

builder.Services.Configure<RabbitMqOptions>(builder.Configuration.GetSection(RabbitMqOptions.SectionName));
builder.Services.AddSingleton<RabbitMqPublisher>();
builder.Services.AddSingleton<OutboxSignal>();
builder.Services.AddHostedService<OutboxDispatcher>();

// Enum trao đổi dạng chuỗi ("Email", "Draft") cho dễ đọc
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();

app.UseSerilogRequestLogging();

await DatabaseInitializer.EnsureCreatedAsync(app.Services, app.Lifetime.ApplicationStopping);

app.MapGet("/", () => Results.Ok(new { service = "Campaign.Api", status = "running" }));
app.MapCampaignEndpoints();
app.MapContactEndpoints();

app.Run();
