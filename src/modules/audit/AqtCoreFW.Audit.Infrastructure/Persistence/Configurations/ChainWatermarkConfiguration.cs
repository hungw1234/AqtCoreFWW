using AqtCoreFW.Audit.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Audit.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF mapping for <see cref="ChainWatermark"/>. Stored in the "audit" schema (inherited from
/// AuditDbContext.HasDefaultSchema). Manual <see cref="IEntityTypeConfiguration{TEntity}"/> — the
/// entity does not derive from the shared <c>Entity</c> base (no soft-delete, no tenant-scoping
/// composite indexes; UpdatedAtUtc is the only timestamp and it is datetimeoffset, not the base
/// class's datetime2 CreatedAt/UpdatedAt pair), so <c>EntityTypeConfigurationBase&lt;T&gt;</c> does
/// not apply — mirrors <see cref="LoginAttemptConfiguration"/>'s manual-configuration pattern.
/// </summary>
public sealed class ChainWatermarkConfiguration : IEntityTypeConfiguration<ChainWatermark>
{
    public void Configure(EntityTypeBuilder<ChainWatermark> b)
    {
        b.ToTable("ChainWatermarks");
        b.HasKey(e => e.Id);
        b.Property(e => e.Id).ValueGeneratedOnAdd().UseIdentityColumn();

        b.Property(e => e.LastSequenceId).IsRequired();
        b.Property(e => e.ChainHead).HasMaxLength(128).IsRequired();
        b.Property(e => e.WatermarkHmac).HasMaxLength(128).IsRequired();
        b.Property(e => e.UpdatedAtUtc).IsRequired();

        // EF Core translates a unique index on a nullable column into a FILTERED index
        // (WHERE TenantId IS NOT NULL) — it enforces uniqueness per named tenant but does NOT bound
        // the NULL (system chain) row to one, mirroring AuditRetentionPolicyConfiguration's identical
        // filtered-index note. The "exactly one NULL watermark row" invariant is enforced at the
        // application layer instead: HmacChainService.UpsertWatermarkAsync always does an
        // update-else-insert, and the per-tenant sp_getapplock (tenantKey "system" for the NULL
        // chain) serializes every writer, so two concurrent passes can never both take the insert
        // branch for the same tenant.
        b.HasIndex(e => e.TenantId)
            .IsUnique()
            .HasDatabaseName("IX_ChainWatermarks_TenantId");
    }
}
