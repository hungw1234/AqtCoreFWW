using AqtCoreFW.Audit.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Audit.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF mapping for <see cref="SecurityEventLog"/>. T3 plain-class:
/// InternalId = bigint identity PK, MessageId = Guid AK for external addressing.
/// </summary>
public sealed class SecurityEventLogConfiguration : IEntityTypeConfiguration<SecurityEventLog>
{
    public void Configure(EntityTypeBuilder<SecurityEventLog> b)
    {
        b.ToTable("SecurityEventLogs");
        b.HasKey(e => e.InternalId);
        b.Property(e => e.InternalId).UseIdentityColumn();
        b.HasAlternateKey(e => e.MessageId);

        b.Property(e => e.CreatedAt).HasColumnType("datetime2(3)");

        b.Property(e => e.EventType).HasMaxLength(128).IsRequired();
        b.Property(e => e.Description).HasColumnType("nvarchar(max)").IsRequired();
        b.Property(e => e.Source).HasMaxLength(256);
        b.Property(e => e.IpAddress).HasMaxLength(64);

        b.HasIndex(e => new { e.TenantId, e.OccurredAt });
        b.HasIndex(e => e.EventType);
        b.HasIndex(e => e.IsSecurityRelevant);
    }
}
