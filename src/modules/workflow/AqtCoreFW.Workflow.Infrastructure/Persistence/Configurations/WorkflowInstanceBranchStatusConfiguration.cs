using AqtCoreFW.Infrastructure.Configuration;
using AqtCoreFW.Workflow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Workflow.Infrastructure.Persistence.Configurations;

/// <summary>EF config for WorkflowInstanceBranchStatus — runtime parallel branch tracking per instance.</summary>
public sealed class WorkflowInstanceBranchStatusConfiguration : EntityTypeConfigurationBase<WorkflowInstanceBranchStatus>
{
    protected override void ConfigureEntity(EntityTypeBuilder<WorkflowInstanceBranchStatus> builder)
    {
        builder.ToTable("WorkflowInstanceBranchStatuses", "workflow");

        builder.Property(s => s.InstanceId).IsRequired();
        builder.Property(s => s.ParallelBranchId).IsRequired();
        builder.Property(s => s.Status).HasMaxLength(20).IsRequired();
        builder.Property(s => s.ResolvedAt);

        // Optimistic concurrency — prevents race conditions in parallel resolution
        builder.Property(s => s.RowVersion).IsRowVersion();

        // One branch execution per instance per branch definition
        builder.HasIndex(s => new { s.InstanceId, s.ParallelBranchId })
            .IsUnique()
            .HasDatabaseName("UX_WorkflowInstanceBranchStatuses_InstanceId_BranchId");

        builder.HasIndex(s => s.InstanceId)
            .HasDatabaseName("IX_WorkflowInstanceBranchStatuses_InstanceId");

        builder.HasMany(s => s.ChildStatuses)
            .WithOne()
            .HasForeignKey(cs => cs.InstanceBranchStatusId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
