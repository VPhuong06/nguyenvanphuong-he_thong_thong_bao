using System.Linq.Expressions;
using Campaign.Api.Contracts;
using Campaign.Api.Validation;
using Microsoft.EntityFrameworkCore;
using Notification.Shared.Data;
using Notification.Shared.Domain;
using CampaignEntity = Notification.Shared.Domain.Campaign;

namespace Campaign.Api.Endpoints;

public static class CampaignEndpoints
{
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;

    public static IEndpointRouteBuilder MapCampaignEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/campaigns");

        group.MapPost("/", CreateCampaign);
        group.MapGet("/", ListCampaigns);
        group.MapGet("/{id:guid}", GetCampaign);

        // TODO: các handler sẽ được cài đặt ở các bước tiếp theo
        group.MapPost("/{id:guid}/start", (Guid id) => NotImplemented());
        group.MapGet("/{id:guid}/deliveries", (Guid id) => NotImplemented());

        return app;
    }

    private static async Task<IResult> CreateCampaign(
        CreateCampaignRequest request, NotificationDbContext db, ILoggerFactory loggerFactory, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 200)
            errors["name"] = ["Tên chiến dịch bắt buộc, tối đa 200 ký tự."];
        if (string.IsNullOrWhiteSpace(request.Subject) || request.Subject.Trim().Length > 500)
            errors["subject"] = ["Tiêu đề bắt buộc, tối đa 500 ký tự."];
        if (string.IsNullOrWhiteSpace(request.Content))
            errors["content"] = ["Nội dung thông báo bắt buộc."];
        if (!Enum.IsDefined(request.Channel))
            errors["channel"] = ["Kênh gửi không hợp lệ."];

        var recipients = new List<CampaignRecipient>();
        var seen = new HashSet<string>();
        var invalid = new List<string>();

        void AddRecipient(Guid? contactId, string? name, string? address)
        {
            if (!RecipientAddress.TryNormalize(request.Channel, address, out var normalized))
            {
                invalid.Add(string.IsNullOrWhiteSpace(address) ? $"{name} (thiếu địa chỉ)" : address);
                return;
            }
            // Bỏ người nhận trùng thay vì báo lỗi, vì danh sách nhập tay hay bị lặp
            if (!seen.Add(normalized))
                return;

            recipients.Add(new CampaignRecipient
            {
                ContactId = contactId,
                Name = string.IsNullOrWhiteSpace(name) ? normalized : name.Trim(),
                Address = normalized
            });
        }

        if (request.ContactIds is { Count: > 0 })
        {
            var ids = request.ContactIds.Distinct().ToList();
            var contacts = await db.Contacts.AsNoTracking().Where(c => ids.Contains(c.Id)).ToListAsync(ct);

            var missing = ids.Except(contacts.Select(c => c.Id)).ToList();
            if (missing.Count > 0)
                errors["contactIds"] = [$"Không tìm thấy liên hệ: {string.Join(", ", missing)}"];

            foreach (var contact in contacts.OrderBy(c => c.Name))
                AddRecipient(contact.Id, contact.Name,
                    request.Channel == NotificationChannel.Email ? contact.Email : contact.Phone);
        }

        foreach (var input in request.Recipients ?? [])
            AddRecipient(null, input.Name, input.Address);

        if (invalid.Count > 0)
            errors["recipients"] = [$"Địa chỉ không hợp lệ với kênh {request.Channel}: {string.Join(", ", invalid)}"];
        else if (recipients.Count == 0 && !errors.ContainsKey("contactIds"))
            errors["recipients"] = ["Chiến dịch cần ít nhất một người nhận."];

        if (errors.Count > 0)
            return Results.ValidationProblem(errors);

        var campaign = new CampaignEntity
        {
            Id = Guid.CreateVersion7(),
            Name = request.Name.Trim(),
            Channel = request.Channel,
            Subject = request.Subject.Trim(),
            Content = request.Content,
            Status = CampaignStatus.Draft,
            CreatedAt = DateTimeOffset.UtcNow,
            Recipients = recipients
        };

        db.Campaigns.Add(campaign);
        await db.SaveChangesAsync(ct);

        loggerFactory.CreateLogger(typeof(CampaignEndpoints)).LogInformation(
            "Đã tạo chiến dịch {CampaignId} \"{CampaignName}\" kênh {Channel} với {RecipientCount} người nhận",
            campaign.Id, campaign.Name, campaign.Channel, recipients.Count);

        var detail = await db.Campaigns.AsNoTracking()
            .Where(c => c.Id == campaign.Id)
            .Select(ToDetail)
            .FirstAsync(ct);
        return Results.Created($"/campaigns/{campaign.Id}", detail);
    }

    private static async Task<IResult> ListCampaigns(
        NotificationDbContext db, CancellationToken ct, string? status, int page = 1, int pageSize = DefaultPageSize)
    {
        var errors = new Dictionary<string, string[]>();
        if (page < 1)
            errors["page"] = ["page phải từ 1 trở lên."];
        if (pageSize is < 1 or > MaxPageSize)
            errors["pageSize"] = [$"pageSize phải trong khoảng 1–{MaxPageSize}."];

        CampaignStatus? statusFilter = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (Enum.TryParse<CampaignStatus>(status, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed))
                statusFilter = parsed;
            else
                errors["status"] = [$"status phải là một trong: {string.Join(", ", Enum.GetNames<CampaignStatus>())}."];
        }

        if (errors.Count > 0)
            return Results.ValidationProblem(errors);

        var query = db.Campaigns.AsNoTracking();
        if (statusFilter is { } s)
            query = query.Where(c => c.Status == s);

        var totalItems = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(c => c.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(ToSummary)
            .ToListAsync(ct);

        return Results.Ok(new PagedResponse<CampaignSummaryResponse>(items, page, pageSize, totalItems));
    }

    private static async Task<IResult> GetCampaign(Guid id, NotificationDbContext db, CancellationToken ct)
    {
        var detail = await db.Campaigns.AsNoTracking()
            .Where(c => c.Id == id)
            .Select(ToDetail)
            .FirstOrDefaultAsync(ct);

        return detail is null ? Results.NotFound() : Results.Ok(detail);
    }

    // Số liệu được đếm trực tiếp từ bảng deliveries nên luôn đúng khi nhiều worker cập nhật song song
    private static readonly Expression<Func<CampaignEntity, CampaignSummaryResponse>> ToSummary = c =>
        new CampaignSummaryResponse(
            c.Id, c.Name, c.Channel, c.Status, c.CreatedAt, c.StartedAt, c.CompletedAt,
            new CampaignStats(
                c.Recipients.Count,
                c.Deliveries.Count(d => d.Status == DeliveryStatus.Pending || d.Status == DeliveryStatus.Processing),
                c.Deliveries.Count(d => d.Status == DeliveryStatus.Sent),
                c.Deliveries.Count(d => d.Status == DeliveryStatus.Failed)));

    private static readonly Expression<Func<CampaignEntity, CampaignDetailResponse>> ToDetail = c =>
        new CampaignDetailResponse(
            c.Id, c.Name, c.Channel, c.Subject, c.Content, c.Status, c.CreatedAt, c.StartedAt, c.CompletedAt,
            new CampaignStats(
                c.Recipients.Count,
                c.Deliveries.Count(d => d.Status == DeliveryStatus.Pending || d.Status == DeliveryStatus.Processing),
                c.Deliveries.Count(d => d.Status == DeliveryStatus.Sent),
                c.Deliveries.Count(d => d.Status == DeliveryStatus.Failed)));

    private static IResult NotImplemented() =>
        Results.Problem(statusCode: StatusCodes.Status501NotImplemented, title: "Chưa cài đặt");
}
