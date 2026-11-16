namespace Campaign.Api.Endpoints;

public static class CampaignEndpoints
{
    public static IEndpointRouteBuilder MapCampaignEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/campaigns");

        // TODO: các handler sẽ được cài đặt ở các bước tiếp theo
        group.MapPost("/", () => NotImplemented());
        group.MapGet("/", () => NotImplemented());
        group.MapGet("/{id:guid}", (Guid id) => NotImplemented());
        group.MapPost("/{id:guid}/start", (Guid id) => NotImplemented());
        group.MapGet("/{id:guid}/deliveries", (Guid id) => NotImplemented());

        return app;
    }

    private static IResult NotImplemented() =>
        Results.Problem(statusCode: StatusCodes.Status501NotImplemented, title: "Chưa cài đặt");
}
