using Notification.Shared.Data;
using Notification.Shared.Messaging;
using Notification.Worker;
using Notification.Worker.Processing;
using Notification.Worker.Sending;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSerilog(lc => lc.ReadFrom.Configuration(builder.Configuration));

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("Thiếu cấu hình ConnectionStrings:Default");
builder.Services.AddNotificationDb(connectionString);

builder.Services.Configure<RabbitMqOptions>(builder.Configuration.GetSection(RabbitMqOptions.SectionName));
builder.Services.Configure<MockSenderOptions>(builder.Configuration.GetSection(MockSenderOptions.SectionName));
builder.Services.AddSingleton<INotificationSender, MockNotificationSender>();
builder.Services.AddScoped<JobProcessor>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
