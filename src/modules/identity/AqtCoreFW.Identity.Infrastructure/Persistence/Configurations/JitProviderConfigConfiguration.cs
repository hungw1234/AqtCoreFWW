using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Infrastructure.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Identity.Infrastructure.Persistence.Configurations;

public sealed class JitProviderConfigConfiguration : EntityTypeConfigurationBase<JitProviderConfig>
{
    protected override void ConfigureEntity(EntityTypeBuilder<JitProviderConfig> builder)
    {
        builder.ToTable("JitProviderConfigs", "identity");
        builder.Property(e => e.Scheme).HasMaxLength(100).IsRequired();
        builder.Property(e => e.DisplayName).HasMaxLength(200).IsRequired();
        builder.Property(e => e.DefaultRoleName).HasMaxLength(100).IsRequired();
        builder.Property(e => e.ClaimMappingJson).HasColumnType("nvarchar(max)");
        builder.Property(e => e.AllowedDomainsJson).HasColumnType("nvarchar(max)");
        builder.HasIndex(e => e.Scheme).IsUnique().HasDatabaseName("IX_JitProviderConfig_Scheme");
    }
}
