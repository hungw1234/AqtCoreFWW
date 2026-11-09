using AqtCoreFW.Infrastructure.Configuration;
using AqtCoreFW.Workflow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Workflow.Infrastructure.Persistence.Configurations;

/// <summary>EF config for WorkflowTransition — child of WorkflowDefinition.</summary>
public sealed class WorkflowTransitionConfiguration : EntityTypeConfigurationBase<WorkflowTransition>
{
    protected override void ConfigureEntity(EntityTypeBuilder<WorkflowTransition> builder)
    {
        builder.ToTable("WorkflowTransitions");

        builder.Property(t => t.ActionName).HasMaxLength(100).IsRequired();
        builder.Property(t => t.ActionLabelVi).HasMaxLength(200).IsRequired();
        builder.Property(t => t.ActionLabelEn).HasMaxLength(200).IsRequired();
        builder.Property(t => t.ConditionsJson).HasMaxLength(2000);
        builder.Property(t => t.SortOrder).IsRequired();
        builder.Property(t => t.IsRecall).IsRequired();        // recall/withdraw — only creator may execute

        // Visual designer handle anchors — fixed short codes ("t-out"|"r-in"|…, ≤8 chars). 16 = headroom, avoids nvarchar(max) row bloat.
        builder.Property(t => t.SourceHandle).HasMaxLength(16);
        builder.Property(t => t.TargetHandle).HasMaxLength(16);

        // Unique: one action name per FromState per definition
        builder.HasIndex(t => new { t.DefinitionId, t.FromStateId, t.ActionName })
            .IsUnique()
            .HasDatabaseName("UX_WorkflowTransitions_DefinitionId_FromState_Action");

        builder.HasIndex(t => new { t.DefinitionId, t.FromStateId })
            .HasDatabaseName("IX_WorkflowTransitions_DefinitionId_FromStateId");
    }
}
