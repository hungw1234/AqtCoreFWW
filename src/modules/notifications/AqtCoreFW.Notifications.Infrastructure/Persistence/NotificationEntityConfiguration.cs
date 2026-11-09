using AqtCoreFW.Infrastructure.Configuration;
using AqtCoreFW.Notifications.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Notifications.Infrastructure.Persistence;

public sealed class NotificationEntityConfiguration : EntityTypeConfigurationBase<Notification>
{
    protected override void ConfigureEntity(EntityTypeBuilder<Notification> builder)
    {
        builder.Property(n => n.Subject).HasMaxLength(500).IsRequired();
        builder.Property(n => n.Body).HasMaxLength(20_000).IsRequired();
        builder.Property(n => n.CorrelationId).HasMaxLength(200);
        builder.Property(n => n.Channel)
            .HasConversion(c => c.Value, v => Domain.ValueObjects.NotificationChannel.From(v))
            .HasMaxLength(10).IsRequired();
        builder.Property(n => n.Status)
            .HasConversion<string>()
            .HasMaxLength(20).IsRequired();

        // Index for fast unread count queries (small, selective on IsRead=false).
        builder.HasIndex(n => new { n.RecipientUserId, n.TenantId, n.IsRead })
            .HasDatabaseName("IX_Notification_Recipient_Tenant_IsRead");

        // Covering index for bell + list feed.
        // Supports: WHERE RecipientUserId=? AND TenantId=? AND IsDeleted=0
        //           ORDER BY IsRead ASC, CreatedAt DESC  (unread first, newest first)
        // IncludeProperties cover the SELECT projection → no Key Lookup.
        // Filtered on IsDeleted=0 → excludes soft-deleted rows, keeps index lean.
        builder.HasIndex(n => new { n.RecipientUserId, n.TenantId, n.IsRead, n.CreatedAt })
            .IsDescending(false, false, false, true)
            .IncludeProperties(n => new { n.Subject, n.Body, n.Channel, n.ReadAt })
            .HasFilter("[IsDeleted] = 0")
            .HasDatabaseName("IX_Notification_Unread_Feed");
    }
}
