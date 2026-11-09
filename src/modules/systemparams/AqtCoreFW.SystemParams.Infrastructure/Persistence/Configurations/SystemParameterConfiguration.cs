using AqtCoreFW.Infrastructure.Configuration;
using AqtCoreFW.SystemParams.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.SystemParams.Infrastructure.Persistence.Configurations;

public sealed class SystemParameterConfiguration : EntityTypeConfigurationBase<SystemParameter>
{
    protected override void ConfigureEntity(EntityTypeBuilder<SystemParameter> e)
    {
        e.ToTable("SystemParameters");
        e.Property(x => x.Key).HasMaxLength(200).IsRequired();
        e.Property(x => x.Value).HasMaxLength(4000).IsRequired();
        e.Property(x => x.Description).HasMaxLength(500).IsRequired();
        e.Property(x => x.TenantId).HasMaxLength(100);
        e.HasIndex(x => new { x.Key, x.TenantId }).IsUnique();
    }
}
