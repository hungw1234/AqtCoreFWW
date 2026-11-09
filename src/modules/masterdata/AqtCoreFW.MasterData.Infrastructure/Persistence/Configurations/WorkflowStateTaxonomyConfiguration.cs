using AqtCoreFW.Infrastructure.Configuration;
using AqtCoreFW.MasterData.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.MasterData.Infrastructure.Persistence.Configurations;

// Phase-00: workflow state taxonomy — global (no TenantId), soft-FK to WorkflowDefinition.
// Case-insensitive uniqueness on Code via SQL Server default CI_AS collation.
public sealed class WorkflowStateTaxonomyConfiguration : EntityTypeConfigurationBase<WorkflowStateTaxonomy>
{
    protected override void ConfigureEntity(EntityTypeBuilder<WorkflowStateTaxonomy> e)
    {
        e.ToTable("WorkflowStateTaxonomies");
        e.Property(x => x.Code).HasMaxLength(64).IsRequired();
        e.Property(x => x.NameVi).HasMaxLength(200).IsRequired();
        e.Property(x => x.NameEn).HasMaxLength(200).IsRequired();
        e.Property(x => x.I18nKey).HasMaxLength(100);
        e.Property(x => x.Icon).HasMaxLength(50);
        e.Property(x => x.Color).HasMaxLength(20);
        e.Property(x => x.DeprecatedReason).HasMaxLength(500);
        e.Property(x => x.Category)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();
        e.Property(x => x.RowVersion).IsRowVersion();
        e.HasIndex(x => x.Code).IsUnique();
        e.HasIndex(x => x.IsActive);
    }
}
