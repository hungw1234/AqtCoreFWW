using AqtCoreFW.Infrastructure.Configuration;
using AqtCoreFW.Workflow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Workflow.Infrastructure.Persistence.Configurations;

/// <summary>EF config for WorkflowBranchChildStatus — runtime resolution tracking per child branch.</summary>
public sealed class WorkflowBranchChildStatusConfiguration : EntityTypeConfigurationBase<WorkflowBranchChildStatus>
{
    protected override void ConfigureEntity(EntityTypeBuilder<WorkflowBranchChildStatus> builder)
    {
        builder.ToTable("WorkflowBranchChildStatuses", "workflow");

        builder.Property(cs => cs.InstanceBranchStatusId).IsRequired();
        builder.Property(cs => cs.BranchChildId).IsRequired();
        builder.Property(cs => cs.ResolutionType); // nullable — null = pending
        builder.Property(cs => cs.ResolvedBy);
        builder.Property(cs => cs.ResolvedAt);
        builder.Property(cs => cs.Comment).HasMaxLength(1000);

        // Optimistic concurrency — prevents double-resolution race on same child
        builder.Property(cs => cs.RowVersion).IsRowVersion();

        // One status entry per child per branch execution
        builder.HasIndex(cs => new { cs.InstanceBranchStatusId, cs.BranchChildId })
            .IsUnique()
            .HasDatabaseName("UX_WorkflowBranchChildStatuses_BranchStatusId_ChildId");

        builder.HasIndex(cs => cs.InstanceBranchStatusId)
            .HasDatabaseName("IX_WorkflowBranchChildStatuses_InstanceBranchStatusId");
    }
}
