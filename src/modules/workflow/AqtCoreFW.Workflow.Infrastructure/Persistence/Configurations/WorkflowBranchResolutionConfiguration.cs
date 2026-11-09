using AqtCoreFW.Infrastructure.Configuration;
using AqtCoreFW.Workflow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Workflow.Infrastructure.Persistence.Configurations;

/// <summary>EF config for WorkflowBranchResolution — outcome routing per branch child.</summary>
public sealed class WorkflowBranchResolutionConfiguration : EntityTypeConfigurationBase<WorkflowBranchResolution>
{
    protected override void ConfigureEntity(EntityTypeBuilder<WorkflowBranchResolution> builder)
    {
        builder.ToTable("WorkflowBranchResolutions", "workflow");

        builder.Property(r => r.BranchChildId).IsRequired();
        builder.Property(r => r.ResolutionType).IsRequired();
        builder.Property(r => r.ResultingStateId);

        // One resolution type per child (e.g. only one Approved outcome per child)
        builder.HasIndex(r => new { r.BranchChildId, r.ResolutionType })
            .IsUnique()
            .HasDatabaseName("UX_WorkflowBranchResolutions_ChildId_ResolutionType");

        builder.HasIndex(r => r.BranchChildId)
            .HasDatabaseName("IX_WorkflowBranchResolutions_BranchChildId");
    }
}
