using AqtCoreFW.Infrastructure.Configuration;
using AqtCoreFW.MasterData.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.MasterData.Infrastructure.Persistence.Configurations;

public sealed class ProvinceConfiguration : EntityTypeConfigurationBase<Province>
{
    protected override void ConfigureEntity(EntityTypeBuilder<Province> e)
    {
        e.ToTable("Provinces");
        e.Property(x => x.Code).HasMaxLength(10).IsRequired();
        e.Property(x => x.NameVi).HasMaxLength(200).IsRequired();
        e.Property(x => x.NameEn).HasMaxLength(200).IsRequired();
        e.HasIndex(x => x.Code).IsUnique();
    }
}
