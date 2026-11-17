using System.Text.RegularExpressions;
using Notification.Shared.Logging;
using Serilog.Context;

namespace Campaign.Api.Infrastructure;

/// <summary>
/// Gắn correlation ID cho mỗi request: lấy từ header X-Correlation-Id của client (Admin Web)
/// hoặc tự sinh, đưa vào mọi dòng log trong request và trả lại trong response header.
/// Nhờ vậy một thao tác của admin được lần theo qua log của Admin Web và Campaign API.
/// </summary>
public sealed partial class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-Id";

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers[HeaderName].FirstOrDefault();
        // Chỉ nhận giá trị ngắn, an toàn để ghi vào log
        if (correlationId is null || !SafeId().IsMatch(correlationId))
            correlationId = Guid.NewGuid().ToString("N")[..12];

        context.Response.Headers[HeaderName] = correlationId;
        using (LogContext.PushProperty(LoggingSetup.CorrelationIdProperty, correlationId))
        {
            await next(context);
        }
    }

    [GeneratedRegex("^[A-Za-z0-9-]{1,64}$")]
    private static partial Regex SafeId();
}
