using AqtCoreFW.Infrastructure.Configuration;
using AqtCoreFW.MasterData.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.MasterData.Infrastructure.Persistence.Configurations;

public sealed class ExternalMappingConfiguration : EntityTypeConfigurationBase<ExternalMapping>
{
    protected override void ConfigureEntity(EntityTypeBuilder<ExternalMapping> e)
    {
        e.ToTable("ExternalMappings");
        e.Property(x => x.InternalCode).HasMaxLength(100).IsRequired();
        e.Property(x => x.ExternalSystem).HasMaxLength(100).IsRequired();
        e.Property(x => x.ExternalCode).HasMaxLength(100).IsRequired();
        e.HasIndex(x => new { x.ExternalSystem, x.ExternalCode, x.TenantId });
        e.HasIndex(x => new { x.InternalCode, x.ExternalSystem, x.TenantId });
        e.HasOne(x => x.Dictionary).WithMany()
            .HasForeignKey(x => x.DictionaryId).OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);
    }
}
