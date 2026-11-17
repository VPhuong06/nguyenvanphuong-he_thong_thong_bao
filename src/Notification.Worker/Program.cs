using Notification.Shared.Data;
using Notification.Shared.Logging;
using Notification.Shared.Messaging;
using Notification.Worker;
using Notification.Worker.Processing;
using Notification.Worker.Sending;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSerilog(lc => lc.ConfigureNotificationLogging(builder.Configuration, "worker"));

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("Thiếu cấu hình ConnectionStrings:Default");
builder.Services.AddNotificationDb(connectionString);

builder.Services.Configure<RabbitMqOptions>(builder.Configuration.GetSection(RabbitMqOptions.SectionName));
builder.Services.Configure<MockSenderOptions>(builder.Configuration.GetSection(MockSenderOptions.SectionName));
builder.Services.AddSingleton<MockNotificationSender>();

// Sender:Mode = Mock (mặc định, giả lập) hoặc Smtp (gửi email thật)
var senderMode = builder.Configuration["Sender:Mode"] ?? "Mock";
if (string.Equals(senderMode, "Smtp", StringComparison.OrdinalIgnoreCase))
{
    var smtp = builder.Configuration.GetSection(SmtpOptions.SectionName);
    if (string.IsNullOrWhiteSpace(smtp["Host"]) || string.IsNullOrWhiteSpace(smtp["FromAddress"]))
        throw new InvalidOperationException("Sender:Mode=Smtp cần cấu hình Smtp:Host và Smtp:FromAddress");

    builder.Services.Configure<SmtpOptions>(smtp);
    builder.Services.AddSingleton<SmtpNotificationSender>();
    builder.Services.AddSingleton<INotificationSender, ChannelRouterSender>();
}
else
{
    builder.Services.AddSingleton<INotificationSender>(sp => sp.GetRequiredService<MockNotificationSender>());
}
builder.Services.AddScoped<JobProcessor>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();

host.Services.GetRequiredService<ILogger<Program>>().LogInformation(
    "Chế độ gửi: {Mode}", string.Equals(senderMode, "Smtp", StringComparison.OrdinalIgnoreCase)
        ? $"email thật qua SMTP {builder.Configuration["Smtp:Host"]}:{builder.Configuration["Smtp:Port"] ?? "587"}, SMS giả lập"
        : "giả lập (không gửi thật)");

host.Run();
