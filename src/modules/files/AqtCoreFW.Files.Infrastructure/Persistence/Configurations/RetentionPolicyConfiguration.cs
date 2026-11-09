using AqtCoreFW.Files.Domain.Entities;
using AqtCoreFW.Infrastructure.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Files.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF config for RetentionPolicy (T1: long Id PK, no PublicId AK).
/// DocumentType indexed with TenantId for policy lookup during file classification.
/// Only active policies fetched by enforcement job — IsActive index included.
/// Phase-4b B3b: use EntityTypeConfigurationBase&lt;T&gt; (single-arg; base handles Id/soft-delete/timestamps).
/// </summary>
public sealed class RetentionPolicyConfiguration
    : EntityTypeConfigurationBase<RetentionPolicy>
{
    protected override void ConfigureEntity(EntityTypeBuilder<RetentionPolicy> builder)
    {
        builder.ToTable("RetentionPolicies");

        builder.Property(p => p.Name).HasMaxLength(200).IsRequired();
        builder.Property(p => p.DocumentType).HasMaxLength(100).IsRequired();
        builder.Property(p => p.RetainDays).IsRequired();
        builder.Property(p => p.IsActive).IsRequired();
        builder.Property(p => p.TenantId).IsRequired();

        // Policy lookup by document type within tenant
        builder.HasIndex(p => new { p.TenantId, p.DocumentType, p.IsActive })
            .HasDatabaseName("IX_RetentionPolicies_TenantId_DocumentType_IsActive");
    }
}
