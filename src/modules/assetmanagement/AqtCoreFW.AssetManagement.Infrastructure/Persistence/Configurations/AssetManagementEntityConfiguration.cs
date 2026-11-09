using AqtCoreFW.AssetManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.AssetManagement.Infrastructure.Persistence.Configurations;

public sealed class AssetManagementEntityConfiguration : IEntityTypeConfiguration<AssetManagementEntity>
{
    public void Configure(EntityTypeBuilder<AssetManagementEntity> builder)
    {
        builder.ToTable("AssetManagements");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.TenantId).IsRequired();
        builder.Property(e => e.Title).HasMaxLength(200).IsRequired();
        builder.Property(e => e.Description).HasMaxLength(2000);
        builder.HasIndex(e => new { e.TenantId, e.Title });
        builder.HasQueryFilter(e => !e.IsDeleted);
    }
}
