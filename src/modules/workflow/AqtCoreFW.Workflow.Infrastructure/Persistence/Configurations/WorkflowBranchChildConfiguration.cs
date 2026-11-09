using AqtCoreFW.Infrastructure.Configuration;
using AqtCoreFW.Workflow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Workflow.Infrastructure.Persistence.Configurations;

/// <summary>EF config for WorkflowBranchChild — individual sub-branch within a parallel fork.</summary>
public sealed class WorkflowBranchChildConfiguration : EntityTypeConfigurationBase<WorkflowBranchChild>
{
    protected override void ConfigureEntity(EntityTypeBuilder<WorkflowBranchChild> builder)
    {
        builder.ToTable("WorkflowBranchChildren", "workflow");

        builder.Property(c => c.ParallelBranchId).IsRequired();
        builder.Property(c => c.TargetStateId).IsRequired();
        builder.Property(c => c.Name).HasMaxLength(100).IsRequired();
        builder.Property(c => c.DisplayNameVi).HasMaxLength(200).IsRequired();
        builder.Property(c => c.DepartmentId).HasMaxLength(100);
        builder.Property(c => c.DefaultAssigneeId).IsRequired(false);
        builder.Property(c => c.SortOrder).IsRequired();

        // Unique machine name per branch
        builder.HasIndex(c => new { c.ParallelBranchId, c.Name })
            .IsUnique()
            .HasDatabaseName("UX_WorkflowBranchChildren_ParallelBranchId_Name");

        builder.HasIndex(c => c.ParallelBranchId)
            .HasDatabaseName("IX_WorkflowBranchChildren_ParallelBranchId");

        builder.HasMany(c => c.Resolutions)
            .WithOne()
            .HasForeignKey(r => r.BranchChildId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
