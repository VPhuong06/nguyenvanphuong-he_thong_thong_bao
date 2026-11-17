using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Notification.Shared.Domain;

namespace Notification.Shared.Data;

public class NotificationDbContext(DbContextOptions<NotificationDbContext> options) : DbContext(options)
{
    public DbSet<Contact> Contacts => Set<Contact>();
    public DbSet<Campaign> Campaigns => Set<Campaign>();
    public DbSet<CampaignRecipient> CampaignRecipients => Set<CampaignRecipient>();
    public DbSet<Delivery> Deliveries => Set<Delivery>();
    public DbSet<DeliveryAttempt> DeliveryAttempts => Set<DeliveryAttempt>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Contact>(e =>
        {
            e.ToTable("contacts");
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Email).HasMaxLength(320);
            e.Property(x => x.Phone).HasMaxLength(30);
            e.Property(x => x.TelegramChatId).HasMaxLength(64);
            e.HasIndex(x => x.TelegramChatId).IsUnique();
        });

        b.Entity<Campaign>(e =>
        {
            e.ToTable("campaigns");
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Subject).HasMaxLength(500);
            e.Property(x => x.Channel).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
            e.HasIndex(x => x.Status);
            e.HasMany(x => x.Recipients).WithOne().HasForeignKey(x => x.CampaignId);
            e.HasMany(x => x.Deliveries).WithOne().HasForeignKey(x => x.CampaignId);
        });

        b.Entity<CampaignRecipient>(e =>
        {
            e.ToTable("campaign_recipients");
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Address).HasMaxLength(320);
            e.HasIndex(x => new { x.CampaignId, x.Address }).IsUnique();
        });

        b.Entity<Delivery>(e =>
        {
            e.ToTable("deliveries");
            e.Property(x => x.RecipientName).HasMaxLength(200);
            e.Property(x => x.RecipientAddress).HasMaxLength(320);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.FailureKind).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.WorkerId).HasMaxLength(100);
            // Đếm Pending/Sent/Failed theo chiến dịch
            e.HasIndex(x => new { x.CampaignId, x.Status });
            // Chặn sinh job trùng khi start nhiều lần
            e.HasIndex(x => new { x.CampaignId, x.RecipientAddress }).IsUnique();
            e.HasMany(x => x.Attempts).WithOne().HasForeignKey(x => x.DeliveryId);
        });

        b.Entity<DeliveryAttempt>(e =>
        {
            e.ToTable("delivery_attempts");
            e.Property(x => x.WorkerId).HasMaxLength(100);
            e.HasIndex(x => x.DeliveryId);
        });
    }
}

public static class NotificationDbServiceCollectionExtensions
{
    public static IServiceCollection AddNotificationDb(this IServiceCollection services, string connectionString) =>
        services.AddDbContext<NotificationDbContext>(o => o.UseNpgsql(connectionString));
}
