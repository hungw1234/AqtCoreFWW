using AqtCoreFW.Infrastructure.Configuration;
using AqtCoreFW.Notifications.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Notifications.Infrastructure.Persistence;

public sealed class NotificationPreferenceEntityConfiguration : EntityTypeConfigurationBase<NotificationPreference>
{
    protected override void ConfigureEntity(EntityTypeBuilder<NotificationPreference> builder)
    {
        builder.Property(p => p.Channel)
            .HasConversion(c => c.Value, v => Domain.ValueObjects.NotificationChannel.From(v))
            .HasMaxLength(10).IsRequired();

        // One preference record per user+tenant+channel
        builder.HasIndex(p => new { p.UserId, p.TenantId, p.Channel })
            .HasDatabaseName("IX_NotificationPreference_UserId_TenantId_Channel")
            .IsUnique();
    }
}
