using Admin.Web.Components;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSerilog(lc => lc.ReadFrom.Configuration(builder.Configuration));

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Admin Client chỉ giao tiếp với hệ thống qua Campaign API (REST)
var apiBaseUrl = builder.Configuration["CampaignApi:BaseUrl"]
    ?? throw new InvalidOperationException("Thiếu cấu hình CampaignApi:BaseUrl");
builder.Services.AddHttpClient("CampaignApi", c => c.BaseAddress = new Uri(apiBaseUrl));

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
