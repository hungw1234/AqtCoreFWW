using AqtCoreFW.ModuleName.Domain.Entities;
using AqtCoreFW.Infrastructure.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.ModuleName.Infrastructure.Persistence.Configurations;

public sealed class ModuleNameEntityConfiguration : EntityTypeConfigurationBase<ModuleNameEntity>
{
    protected override void ConfigureEntity(EntityTypeBuilder<ModuleNameEntity> builder)
    {
        builder.ToTable("ModuleNames");
        builder.Property(e => e.TenantId).IsRequired();
        builder.Property(e => e.Title).HasMaxLength(200).IsRequired();
        builder.Property(e => e.Description).HasMaxLength(2000);
        builder.HasIndex(e => new { e.TenantId, e.Title });
    }
}
