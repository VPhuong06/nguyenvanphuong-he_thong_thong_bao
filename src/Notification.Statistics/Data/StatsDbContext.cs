using Microsoft.EntityFrameworkCore;

namespace Notification.Statistics.Data;

/// <summary>Một event kết quả gửi mà service thống kê đã nhận.</summary>
public class DeliveryEventRecord
{
    public long Id { get; set; }
    public Guid DeliveryId { get; set; }
    public Guid CampaignId { get; set; }
    public required string WorkerId { get; set; }
    public int AttemptNo { get; set; }
    public required string Outcome { get; set; }
    public string? FailureKind { get; set; }
    public int DurationMs { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
}

/// <summary>
/// Dữ liệu riêng của service thống kê, nằm trong database statistics_db tách khỏi database của
/// Campaign API: service chỉ biết về hệ thống qua event nhận được, không đọc bảng của service khác.
/// </summary>
public class StatsDbContext(DbContextOptions<StatsDbContext> options) : DbContext(options)
{
    public DbSet<DeliveryEventRecord> DeliveryEvents => Set<DeliveryEventRecord>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<DeliveryEventRecord>(e =>
        {
            e.ToTable("delivery_events");
            e.Property(x => x.WorkerId).HasMaxLength(100);
            e.Property(x => x.Outcome).HasMaxLength(20);
            e.Property(x => x.FailureKind).HasMaxLength(20);
            // Event có thể đến hai lần (at-least-once): mỗi lần gửi chỉ được ghi một lần
            e.HasIndex(x => new { x.DeliveryId, x.AttemptNo }).IsUnique();
            e.HasIndex(x => x.OccurredAt);
            e.HasIndex(x => x.CampaignId);
        });
    }
}
