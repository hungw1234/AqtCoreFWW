using AqtCoreFW.Infrastructure.Configuration;
using AqtCoreFW.MasterData.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.MasterData.Infrastructure.Persistence.Configurations;

// Province 63→34 legacy mapping table (QD 19/2025/QD-TTg, effective 01/07/2025).
public sealed class ProvinceLegacyMappingConfiguration : EntityTypeConfigurationBase<ProvinceLegacyMapping>
{
    protected override void ConfigureEntity(EntityTypeBuilder<ProvinceLegacyMapping> e)
    {
        e.ToTable("ProvinceLegacyMappings");
        e.Property(x => x.OldCode).HasMaxLength(10).IsRequired();
        e.Property(x => x.NewCode).HasMaxLength(10).IsRequired();
        e.Property(x => x.Notes).HasMaxLength(500);
        e.HasIndex(x => x.OldCode).IsUnique(); // Each old code maps to exactly one new code
        e.HasIndex(x => x.NewCode); // Many old codes may map to same new code (mergers)
    }
}
