using Notification.Shared.Data;
using Notification.Shared.Logging;
using Notification.Shared.Messaging;
using Notification.Shared.Telegram;
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

// Sender:Mode = Mock (mặc định, mọi kênh giả lập) hoặc Real (gửi thật các kênh đã cấu hình).
// Giá trị "Smtp" của phiên bản trước vẫn được hiểu là Real.
var senderMode = builder.Configuration["Sender:Mode"] ?? "Mock";
var realMode = senderMode.Equals("Real", StringComparison.OrdinalIgnoreCase)
               || senderMode.Equals("Smtp", StringComparison.OrdinalIgnoreCase);

var smtp = builder.Configuration.GetSection(SmtpOptions.SectionName);
var smtpConfigured = !string.IsNullOrWhiteSpace(smtp["Host"]) && !string.IsNullOrWhiteSpace(smtp["FromAddress"]);
builder.Services.Configure<SmtpOptions>(smtp);
builder.Services.AddSingleton<SmtpNotificationSender>();

var telegram = builder.Configuration.GetSection(TelegramOptions.SectionName);
var telegramConfigured = !string.IsNullOrWhiteSpace(telegram["BotToken"]);
builder.Services.Configure<TelegramOptions>(telegram);
// URL của Bot API chứa token, nên tắt log của HttpClient này
builder.Services.AddHttpClient(TelegramApi.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(15))
    .RemoveAllLoggers();
builder.Services.AddSingleton<TelegramNotificationSender>();

builder.Services.AddSingleton<INotificationSender>(sp => new ChannelRouterSender(
    sp.GetRequiredService<MockNotificationSender>(),
    realMode,
    realMode && smtpConfigured ? sp.GetRequiredService<SmtpNotificationSender>() : null,
    realMode && telegramConfigured ? sp.GetRequiredService<TelegramNotificationSender>() : null));

builder.Services.AddScoped<JobProcessor>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();

host.Services.GetRequiredService<ILogger<Program>>().LogInformation("Chế độ gửi: {Mode}", realMode
    ? $"gửi thật — Email: {(smtpConfigured ? $"SMTP {smtp["Host"]}:{smtp["Port"] ?? "587"}" : "chưa cấu hình")}, " +
      $"Telegram: {(telegramConfigured ? "bot đã cấu hình" : "chưa cấu hình")}, SMS: chưa tích hợp"
    : "giả lập (không gửi thật)");

host.Run();
