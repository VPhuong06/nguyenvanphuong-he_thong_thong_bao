namespace Campaign.Api.Contracts;

/// <summary>
/// Cấu trúc lỗi thống nhất cho mọi phản hồi lỗi do API tạo ra: Code để máy khách xử lý theo nhánh,
/// Message để hiển thị, Errors liệt kê lỗi từng trường khi dữ liệu không hợp lệ.
/// </summary>
public sealed record ApiError(string Code, string Message, IReadOnlyDictionary<string, string[]>? Errors = null);

public static class ApiErrors
{
    public static IResult Invalid(string code, IReadOnlyDictionary<string, string[]> errors) =>
        Results.BadRequest(new ApiError(code, "Dữ liệu không hợp lệ.", errors));

    public static IResult NotFound(string code, string message) =>
        Results.NotFound(new ApiError(code, message));

    public static IResult Conflict(string code, string message) =>
        Results.Conflict(new ApiError(code, message));
}
