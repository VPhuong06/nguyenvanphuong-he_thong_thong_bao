using Campaign.Api.Endpoints;
using Campaign.Api.Infrastructure;
using Notification.Shared.Data;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSerilog(lc => lc.ReadFrom.Configuration(builder.Configuration));

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("Thiếu cấu hình ConnectionStrings:Default");
builder.Services.AddNotificationDb(connectionString);

var app = builder.Build();

app.UseSerilogRequestLogging();

await DatabaseInitializer.EnsureCreatedAsync(app.Services, app.Lifetime.ApplicationStopping);

app.MapGet("/", () => Results.Ok(new { service = "Campaign.Api", status = "running" }));
app.MapCampaignEndpoints();

app.Run();
