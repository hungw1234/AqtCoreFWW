using AqtCoreFW.Infrastructure.Configuration;
using AqtCoreFW.MasterData.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.MasterData.Infrastructure.Persistence.Configurations;

public sealed class DictionaryConfiguration : EntityTypeConfigurationBase<Dictionary>
{
    protected override void ConfigureEntity(EntityTypeBuilder<Dictionary> e)
    {
        e.ToTable("Dictionaries");
        e.Property(x => x.Code).HasMaxLength(100).IsRequired();
        e.Property(x => x.Name).HasMaxLength(200).IsRequired();
        e.Property(x => x.NameVi).HasMaxLength(200).IsRequired();
        e.Property(x => x.Description).HasMaxLength(1000);
        // Manual TenantId index dropped — base provides (TenantId, Id) + (TenantId, CreatedAt) for ITenantScoped.
        e.HasIndex(x => new { x.Code, x.TenantId }).IsUnique();
    }
}
