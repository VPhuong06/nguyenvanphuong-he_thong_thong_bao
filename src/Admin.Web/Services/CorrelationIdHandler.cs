using Notification.Shared.Logging;
using Serilog.Context;

namespace Admin.Web.Services;

/// <summary>
/// Gắn X-Correlation-Id cho mỗi request gửi tới Campaign API. Cùng mã này xuất hiện trong log
/// của Admin Web và Campaign API, nên lần theo được một thao tác qua cả hai service.
/// </summary>
public sealed class CorrelationIdHandler(ILogger<CorrelationIdHandler> logger) : DelegatingHandler
{
    public const string HeaderName = "X-Correlation-Id";

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var correlationId = Guid.NewGuid().ToString("N")[..12];
        request.Headers.TryAddWithoutValidation(HeaderName, correlationId);

        using (LogContext.PushProperty(LoggingSetup.CorrelationIdProperty, correlationId))
        {
            var response = await base.SendAsync(request, ct);
            // Chỉ ghi lời gọi thay đổi dữ liệu; các lời gọi đọc định kỳ (tự cập nhật) bỏ qua để log gọn
            if (request.Method != HttpMethod.Get)
                logger.LogInformation("Gọi Campaign API {Method} {Path} → {StatusCode}",
                    request.Method, request.RequestUri?.PathAndQuery, (int)response.StatusCode);
            return response;
        }
    }
}
