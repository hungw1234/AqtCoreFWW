using AqtCoreFW.Infrastructure.Configuration;
using AqtCoreFW.Reporting.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Reporting.Infrastructure.Persistence.Configurations;

public sealed class QueryCatalogEntryConfiguration : EntityTypeConfigurationBase<QueryCatalogEntry>
{
    protected override void ConfigureEntity(EntityTypeBuilder<QueryCatalogEntry> builder)
    {
        builder.ToTable("QueryCatalogEntries");

        builder.Property(q => q.Code).HasMaxLength(100).IsRequired();
        builder.Property(q => q.Description).HasMaxLength(500).IsRequired();
        builder.Property(q => q.DescriptionVi).HasMaxLength(500).IsRequired();
        builder.Property(q => q.DapperSql).HasMaxLength(8000).IsRequired();
        builder.Property(q => q.RequiredParams).HasMaxLength(1000).IsRequired();
        builder.Property(q => q.OptionalParams).HasMaxLength(1000).IsRequired();
        builder.Property(q => q.RequiredRole).HasMaxLength(50);

        // Unique on Code — machine-readable identifier
        builder.HasIndex(q => q.Code).IsUnique();
        builder.HasIndex(q => q.IsActive);
    }
}
