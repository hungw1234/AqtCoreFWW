using AqtCoreFW.Infrastructure.Configuration;
using AqtCoreFW.MasterData.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.MasterData.Infrastructure.Persistence.Configurations;

public sealed class JobTitleConfiguration : EntityTypeConfigurationBase<JobTitle>
{
    protected override void ConfigureEntity(EntityTypeBuilder<JobTitle> e)
    {
        e.ToTable("JobTitles");
        e.Property(x => x.Code).HasMaxLength(50).IsRequired();
        e.Property(x => x.NameVi).HasMaxLength(200).IsRequired();
        e.Property(x => x.NameEn).HasMaxLength(200).IsRequired();
        e.Property(x => x.TenantId).HasMaxLength(100);
        e.HasIndex(x => new { x.Code, x.TenantId }).IsUnique();
    }
}
