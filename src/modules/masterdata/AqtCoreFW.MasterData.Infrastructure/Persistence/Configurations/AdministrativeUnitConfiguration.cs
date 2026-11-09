using AqtCoreFW.Infrastructure.Configuration;
using AqtCoreFW.MasterData.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.MasterData.Infrastructure.Persistence.Configurations;

public sealed class AdministrativeUnitConfiguration : EntityTypeConfigurationBase<AdministrativeUnit>
{
    protected override void ConfigureEntity(EntityTypeBuilder<AdministrativeUnit> e)
    {
        e.ToTable("AdministrativeUnits");
        e.Property(x => x.Code).HasMaxLength(20).IsRequired();
        e.Property(x => x.NameVi).HasMaxLength(300).IsRequired();
        e.Property(x => x.NameEn).HasMaxLength(300).IsRequired();
        e.Property(x => x.ParentCode).HasMaxLength(20);
        e.Property(x => x.SuccessorCode).HasMaxLength(20);
        e.HasIndex(x => x.Code).IsUnique();
        e.HasIndex(x => new { x.Level, x.ParentCode });
    }
}
