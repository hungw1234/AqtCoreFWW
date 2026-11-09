using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Infrastructure.Webhooks;

/// <summary>
/// Dedicated DbContext for webhook engine (schema: webhooks).
/// Separate from ModuleDbContext — no tenant global filter needed here;
/// tenant isolation is enforced via explicit TenantId predicate in queries.
/// Registered as Scoped by WebhookRegistration.AddWebhookEngine().
/// </summary>
public sealed class WebhookDbContext(DbContextOptions<WebhookDbContext> options)
    : DbContext(options)
{
    public DbSet<WebhookSubscription> Subscriptions { get; set; } = default!;
    public DbSet<WebhookDeliveryAttempt> DeliveryAttempts { get; set; } = default!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema("webhooks");

        modelBuilder.Entity<WebhookSubscription>(e =>
        {
            e.ToTable("subscriptions");
            e.HasKey(x => x.Id);
            e.Property(x => x.TenantId).IsRequired();
            e.Property(x => x.EndpointUrl).HasMaxLength(2048).IsRequired();
            e.Property(x => x.SecretHash).HasMaxLength(128).IsRequired();
            e.Property(x => x.EventTypesJson).HasColumnType("nvarchar(max)").IsRequired();
            e.Property(x => x.IsActive).IsRequired();
            e.Property(x => x.CreatedAt).IsRequired();

            // Tenant-scoped subscription lookup (hot path: DispatchAsync)
            e.HasIndex(x => new { x.TenantId, x.IsActive })
                .HasDatabaseName("IX_Subscriptions_TenantId_IsActive");

            // EventTypesJson is nvarchar(max) — cannot be indexed in SQL Server.
            // LIKE queries acceptable for ≤10 subscriptions/type (NF2).
        });

        modelBuilder.Entity<WebhookDeliveryAttempt>(e =>
        {
            e.ToTable("delivery_attempts");
            e.HasKey(x => x.Id);
            e.Property(x => x.SubscriptionId).IsRequired();
            e.Property(x => x.EventType).HasMaxLength(200).IsRequired();
            e.Property(x => x.Payload).HasColumnType("nvarchar(max)").IsRequired();
            e.Property(x => x.AttemptNumber).IsRequired();
            e.Property(x => x.ErrorMessage).HasMaxLength(2000);
            e.Property(x => x.IsSuccess).IsRequired();
            e.Property(x => x.AttemptedAt).IsRequired();

            // Delivery history lookup by subscription (paginated admin view)
            e.HasIndex(x => new { x.SubscriptionId, x.AttemptedAt })
                .HasDatabaseName("IX_DeliveryAttempts_SubscriptionId_AttemptedAt")
                .IsDescending(false, true);
        });
    }
}
