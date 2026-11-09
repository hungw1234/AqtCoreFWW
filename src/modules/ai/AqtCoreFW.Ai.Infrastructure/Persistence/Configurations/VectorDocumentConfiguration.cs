using AqtCoreFW.Ai.Domain.Entities;
using AqtCoreFW.Infrastructure.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Ai.Infrastructure.Persistence.Configurations;

/// <summary>EF Core configuration for VectorDocument — schema "ai", table "vector_documents".</summary>
public sealed class VectorDocumentConfiguration : EntityTypeConfigurationBase<VectorDocument>
{
    protected override void ConfigureEntity(EntityTypeBuilder<VectorDocument> builder)
    {
        builder.ToTable("vector_documents", "ai");

        builder.Property(v => v.TenantId).IsRequired();
        builder.Property(v => v.Collection).IsRequired().HasMaxLength(200);
        builder.Property(v => v.OriginalFileName).IsRequired().HasMaxLength(500);
        builder.Property(v => v.ChunkCount).IsRequired();
        builder.Property(v => v.IndexedAtUtc).IsRequired();
        builder.Property(v => v.IsDeleted).IsRequired().HasDefaultValue(false);

        // SourceDocumentId is nullable — adhoc text ingestion has no file reference
        builder.Property(v => v.SourceDocumentId).IsRequired(false);

        // Multi-tenant partition indexes (VectorDocument is not ITenantScoped — keep manual).
        builder.HasIndex(v => v.TenantId);
        builder.HasIndex(v => new { v.TenantId, v.SourceDocumentId });

        // Soft-delete query filter applied by base.ConfigureSoftDelete.
    }
}
