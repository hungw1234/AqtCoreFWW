using AqtCoreFW.Notifications.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Notifications.Infrastructure.Persistence;

public sealed class NotificationLogEntityConfiguration : IEntityTypeConfiguration<NotificationLog>
{
    public void Configure(EntityTypeBuilder<NotificationLog> builder)
    {
        // T3 plain-class: InternalId is bigint IDENTITY PK, MessageId is Guid AK
        builder.HasKey(l => l.InternalId);
        builder.Property(l => l.InternalId).ValueGeneratedOnAdd().UseIdentityColumn();
        builder.Property(l => l.MessageId)
            .HasColumnType("uniqueidentifier")
            .IsRequired();
        builder.HasIndex(l => l.MessageId)
            .IsUnique()
            .HasDatabaseName("AK_NotificationLog_MessageId");

        builder.Property(l => l.CreatedAt).HasColumnType("datetime2(3)");

        builder.Property(l => l.CorrelationId).HasMaxLength(200).IsRequired();

        // Unique index enforces dedup guarantee (S52 spec)
        builder.HasIndex(l => new { l.TemplateId, l.RecipientId, l.CorrelationId })
            .HasDatabaseName("IX_NotificationLog_TemplateId_RecipientId_CorrelationId")
            .IsUnique();
    }
}
