using System.Net.Sockets;
using Microsoft.AspNetCore.Diagnostics;
using Npgsql;

namespace Campaign.Api.Infrastructure;

/// <summary>
/// Khi PostgreSQL tạm dừng, trả 503 kèm thông báo rõ ràng thay vì lỗi 500, để client biết
/// có thể thử lại sau.
/// </summary>
public sealed class DatabaseUnavailableHandler(
    IProblemDetailsService problemDetails,
    ILogger<DatabaseUnavailableHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        if (!IsDatabaseUnavailable(exception))
            return false;

        logger.LogWarning("Database tạm thời không khả dụng khi xử lý {Method} {Path}: {Error}",
            context.Request.Method, context.Request.Path, exception.Message);

        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            Exception = exception,
            ProblemDetails =
            {
                Status = StatusCodes.Status503ServiceUnavailable,
                Title = "Cơ sở dữ liệu tạm thời không khả dụng",
                Detail = "Vui lòng thử lại sau ít phút."
            }
        });
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
