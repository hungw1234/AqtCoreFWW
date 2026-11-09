using AqtCoreFW.Infrastructure.Configuration;
using AqtCoreFW.MasterData.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.MasterData.Infrastructure.Persistence.Configurations;

public sealed class WorkflowActionTaxonomyConfiguration : EntityTypeConfigurationBase<WorkflowActionTaxonomy>
{
    protected override void ConfigureEntity(EntityTypeBuilder<WorkflowActionTaxonomy> e)
    {
        e.ToTable("WorkflowActionTaxonomies");
        e.Property(x => x.Code).HasMaxLength(64).IsRequired();
        e.Property(x => x.NameVi).HasMaxLength(200).IsRequired();
        e.Property(x => x.NameEn).HasMaxLength(200).IsRequired();
        e.Property(x => x.I18nKey).HasMaxLength(100);
        e.Property(x => x.Icon).HasMaxLength(50);
        e.Property(x => x.DeprecatedReason).HasMaxLength(500);
        e.Property(x => x.RowVersion).IsRowVersion();
        e.HasIndex(x => x.Code).IsUnique();
        e.HasIndex(x => x.IsActive);
    }
}
