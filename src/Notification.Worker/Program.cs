using Notification.Shared.Data;
using Notification.Shared.Messaging;
using Notification.Worker;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSerilog(lc => lc.ReadFrom.Configuration(builder.Configuration));

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("Thiếu cấu hình ConnectionStrings:Default");
builder.Services.AddNotificationDb(connectionString);

builder.Services.Configure<RabbitMqOptions>(builder.Configuration.GetSection(RabbitMqOptions.SectionName));
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
