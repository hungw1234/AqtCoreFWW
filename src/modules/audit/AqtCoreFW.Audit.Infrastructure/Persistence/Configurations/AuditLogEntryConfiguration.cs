using AqtCoreFW.Audit.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Audit.Infrastructure.Persistence.Configurations;

public sealed class AuditLogEntryConfiguration : IEntityTypeConfiguration<AuditLogEntry>
{
    public void Configure(EntityTypeBuilder<AuditLogEntry> b)
    {
        b.ToTable("AuditLogEntries");
        b.HasKey(e => e.InternalId);
        b.Property(e => e.InternalId).UseIdentityColumn();
        b.HasAlternateKey(e => e.MessageId);

        b.Property(e => e.CreatedAt).HasColumnType("datetime2(3)");

        b.Property(e => e.UserName).HasMaxLength(256).IsRequired();
        b.Property(e => e.Action).HasMaxLength(64).IsRequired();
        b.Property(e => e.ModuleName).HasMaxLength(128).IsRequired();
        b.Property(e => e.ResourceType).HasMaxLength(128).IsRequired();
        b.Property(e => e.ResourceId).HasMaxLength(256);
        b.Property(e => e.IpAddress).HasMaxLength(64);
        b.Property(e => e.CorrelationId).HasMaxLength(128);
        b.Property(e => e.HmacSignature).HasMaxLength(128).IsRequired();
        b.Property(e => e.DataSnapshot).HasColumnType("nvarchar(max)");

        // SequenceId is externally assigned (by SetSequenceId) — regular bigint, not an identity column.
        // Non-nullable long: cannot use IsRequired(false). HasDefaultValue(0) sets DB default only.
        b.Property(e => e.SequenceId).HasDefaultValue(0);

        // QĐ 742 mục 3.1.b — Category + Severity stored as int (mirrors SecurityIncident).
        // Both default to AppAccess/Low at the C# level; M1 migration will add the columns
        // NULLABLE + backfill old rows, M2 migration will flip to NOT NULL.
        b.Property(e => e.Category).HasConversion<int>();
        b.Property(e => e.Severity).HasConversion<int>();
        b.Property(e => e.SchemaVersion).HasDefaultValue(2);

        // Indexes for common filter patterns
        b.HasIndex(e => new { e.TenantId, e.OccurredAt });
        b.HasIndex(e => new { e.UserId, e.OccurredAt });
        b.HasIndex(e => e.Action);
        b.HasIndex(e => e.ModuleName);
        b.HasIndex(e => new { e.TenantId, e.Category, e.OccurredAt });
        b.HasIndex(e => new { e.Severity, e.OccurredAt });
    }
}
