using AqtCoreFW.Infrastructure.Configuration;
using AqtCoreFW.Workflow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Workflow.Infrastructure.Persistence.Configurations;

/// <summary>EF config for WorkflowParallelBranch — definition-level fan-out descriptor.</summary>
public sealed class WorkflowParallelBranchConfiguration : EntityTypeConfigurationBase<WorkflowParallelBranch>
{
    protected override void ConfigureEntity(EntityTypeBuilder<WorkflowParallelBranch> builder)
    {
        builder.ToTable("WorkflowParallelBranches", "workflow");

        builder.Property(b => b.DefinitionId).IsRequired();
        builder.Property(b => b.BranchingStateId).IsRequired();
        builder.Property(b => b.ContinuationStateId).IsRequired();
        builder.Property(b => b.BranchType).IsRequired();
        builder.Property(b => b.RequiredCount);
        builder.Property(b => b.AllowCancelOnAnyReject).IsRequired();
        builder.Property(b => b.TimeoutMinutes).IsRequired();

        // Single draggable fork gateway position (nullable — null ⇒ FE auto-derives initial spot).
        builder.Property(b => b.PositionX);
        builder.Property(b => b.PositionY);

        // One branching state per definition (only one parallel fork per state)
        builder.HasIndex(b => new { b.DefinitionId, b.BranchingStateId })
            .IsUnique()
            .HasDatabaseName("UX_WorkflowParallelBranches_DefinitionId_BranchingStateId");

        builder.HasIndex(b => b.DefinitionId)
            .HasDatabaseName("IX_WorkflowParallelBranches_DefinitionId");

        builder.HasMany(b => b.Children)
            .WithOne()
            .HasForeignKey(c => c.ParallelBranchId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
