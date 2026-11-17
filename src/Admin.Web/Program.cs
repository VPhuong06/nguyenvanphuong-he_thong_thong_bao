using Admin.Web.Components;
using Admin.Web.Services;
using Notification.Shared.Logging;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSerilog(lc => lc.ConfigureNotificationLogging(builder.Configuration, "admin-web"));

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddHealthChecks();

// Admin Client chỉ giao tiếp với hệ thống qua Campaign API (REST)
var apiBaseUrl = builder.Configuration["CampaignApi:BaseUrl"]
    ?? throw new InvalidOperationException("Thiếu cấu hình CampaignApi:BaseUrl");
builder.Services.AddTransient<CorrelationIdHandler>();
builder.Services.AddHttpClient<CampaignApiClient>(CampaignApiClient.HttpClientName, c =>
    {
        c.BaseAddress = new Uri(apiBaseUrl.TrimEnd('/') + "/");
        c.Timeout = TimeSpan.FromSeconds(10);
    })
    .AddHttpMessageHandler<CorrelationIdHandler>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

app.UseAntiforgery();

app.MapHealthChecks("/health");
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
