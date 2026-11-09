using AqtCoreFW.Infrastructure.Configuration;
using AqtCoreFW.Notifications.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Notifications.Infrastructure.Persistence;

public sealed class NotificationTemplateEntityConfiguration : EntityTypeConfigurationBase<NotificationTemplate>
{
    protected override void ConfigureEntity(EntityTypeBuilder<NotificationTemplate> builder)
    {
        // T2 external addressing key — exposed in API URLs and DTOs (never leaks long Id)
        builder.Property(t => t.PublicId).IsRequired();
        builder.HasIndex(t => t.PublicId)
            .IsUnique()
            .HasDatabaseName("AK_NotificationTemplate_PublicId");

        builder.Property(t => t.TemplateKey).HasMaxLength(100).IsRequired();
        builder.Property(t => t.SubjectTemplate).HasMaxLength(500).IsRequired();
        builder.Property(t => t.BodyTemplate).HasMaxLength(20_000).IsRequired();
        builder.Property(t => t.Channel)
            .HasConversion(c => c.Value, v => Domain.ValueObjects.NotificationChannel.From(v))
            .HasMaxLength(10).IsRequired();

        // Unique per tenant + channel + key (prevents duplicate template keys)
        builder.HasIndex(t => new { t.TenantId, t.TemplateKey, t.Channel })
            .HasDatabaseName("IX_NotificationTemplate_TenantId_Key_Channel")
            .IsUnique();
    }
}
