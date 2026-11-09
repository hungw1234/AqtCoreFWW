using AqtCoreFW.Audit.Domain.Entities;
using AqtCoreFW.Infrastructure.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Audit.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF mapping for <see cref="AuditRetentionPolicy"/>. Stored in the "audit" schema.
/// (TenantId, Category) is the natural unique key under SQL Server's filtered-unique-index
/// semantics — both columns are nullable, so the index filters out null rows; uniqueness
/// for the global-default row (TenantId IS NULL, Category IS NULL) is enforced by the
/// retention-policy command handler instead.
/// </summary>
public sealed class AuditRetentionPolicyConfiguration : EntityTypeConfigurationBase<AuditRetentionPolicy>
{
    protected override void ConfigureEntity(EntityTypeBuilder<AuditRetentionPolicy> b)
    {
        b.ToTable("AuditRetentionPolicies");

        b.Property(e => e.RetentionDays).IsRequired();
        b.Property(e => e.ArchiveBeforePurge).IsRequired();
        b.Property(e => e.ArchivePath).HasMaxLength(512);
        b.Property(e => e.Category).HasConversion<int?>();

        b.Property(e => e.RowVersion).IsRowVersion();

        // AuditableEntity props
        b.Property(e => e.CreatedBy).IsRequired();
        b.Property(e => e.ClassificationLevel).HasConversion<int>();

        b.HasIndex(e => new { e.TenantId, e.Category })
            .HasDatabaseName("IX_AuditRetentionPolicies_TenantId_Category")
            .IsUnique(false);
    }
}
