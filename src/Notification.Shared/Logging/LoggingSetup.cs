using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace Notification.Shared.Logging;

public static class LoggingSetup
{
    /// <summary>Tên property chứa correlation ID trong log.</summary>
    public const string CorrelationIdProperty = "CorrelationId";

    /// <summary>
    /// Cấu hình Serilog dùng chung cho mọi service: mức log đọc từ appsettings (Serilog:MinimumLevel),
    /// mỗi dòng log gắn tên service và correlation ID. Logging:Format=json để ghi log JSON có cấu trúc
    /// (mỗi property là một trường riêng, dễ lọc theo CampaignId, DeliveryId, CorrelationId).
    /// </summary>
    public static LoggerConfiguration ConfigureNotificationLogging(
        this LoggerConfiguration lc, IConfiguration configuration, string serviceName)
    {
        lc.ReadFrom.Configuration(configuration)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Service", serviceName);

        if (string.Equals(configuration["Logging:Format"], "json", StringComparison.OrdinalIgnoreCase))
            return lc.WriteTo.Console(new RenderedCompactJsonFormatter());

        return lc.Enrich.With<CorrelationSuffixEnricher>()
            .WriteTo.Console(outputTemplate:
                "[{Timestamp:HH:mm:ss} {Level:u3}] [{Service}] {SourceContext}: {Message:lj}{CorrelationSuffix}{NewLine}{Exception}");
    }

    /// <summary>Thêm " [cid:...]" vào cuối dòng log dạng text khi có correlation ID.</summary>
    private sealed class CorrelationSuffixEnricher : ILogEventEnricher
    {
        public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
        {
            var suffix = logEvent.Properties.TryGetValue(CorrelationIdProperty, out var value) &&
                         value is ScalarValue { Value: string id } && id.Length > 0
                ? $" [cid:{id}]"
                : "";
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("CorrelationSuffix", suffix));
        }
    }
}
