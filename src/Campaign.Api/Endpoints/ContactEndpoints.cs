using Campaign.Api.Contracts;
using Microsoft.EntityFrameworkCore;
using Notification.Shared.Data;

namespace Campaign.Api.Endpoints;

public static class ContactEndpoints
{
    /// <summary>Danh bạ để Admin chọn người nhận khi tạo chiến dịch.</summary>
    public static IEndpointRouteBuilder MapContactEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/contacts", async (NotificationDbContext db, CancellationToken ct) =>
            await db.Contacts.AsNoTracking()
                .OrderBy(c => c.Name)
                .Select(c => new ContactResponse(c.Id, c.Name, c.Email, c.Phone))
                .ToListAsync(ct));

        return app;
    }
}
