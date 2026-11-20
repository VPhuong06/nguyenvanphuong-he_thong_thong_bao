using System.Net.Sockets;
using Campaign.Api.Contracts;
using Microsoft.AspNetCore.Diagnostics;
using Npgsql;

namespace Campaign.Api.Infrastructure;

/// <summary>
/// Khi PostgreSQL tạm dừng, trả 503 kèm ApiError thay vì lỗi 500, để client biết có thể thử lại sau.
/// </summary>
public sealed class DatabaseUnavailableHandler(ILogger<DatabaseUnavailableHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        if (!IsDatabaseUnavailable(exception))
            return false;

        logger.LogWarning("Database tạm thời không khả dụng khi xử lý {Method} {Path}: {Error}",
            context.Request.Method, context.Request.Path, exception.Message);

        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        await context.Response.WriteAsJsonAsync(new ApiError("DATABASE_UNAVAILABLE",
            "Cơ sở dữ liệu tạm thời không khả dụng, vui lòng thử lại sau ít phút."), ct);
        return true;
    }

    private static bool IsDatabaseUnavailable(Exception exception)
    {
        for (var ex = exception; ex is not null; ex = ex.InnerException)
        {
            if (ex is NpgsqlException { IsTransient: true } or SocketException or TimeoutException)
                return true;
        }
        return false;
    }
}
