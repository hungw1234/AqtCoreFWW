using AqtCoreFW.Infrastructure.Configuration;
using AqtCoreFW.Workflow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Workflow.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF config for WorkflowBranchChildAssignment — per-branch-child handler-assignment rule.
/// One assignment per child (unique BranchChildId); clauses cascade from the assignment.
/// Mirrors WorkflowTransitionAssignment. NO pol_*_Tenant RLS: config of the definition graph,
/// inherits its tenancy. Cascade-delete chain: ParallelBranch → Child → Assignment → Clause.
/// </summary>
public sealed class WorkflowBranchChildAssignmentConfiguration
    : EntityTypeConfigurationBase<WorkflowBranchChildAssignment>
{
    protected override void ConfigureEntity(EntityTypeBuilder<WorkflowBranchChildAssignment> builder)
    {
        builder.ToTable("WorkflowBranchChildAssignments");

        builder.Property(a => a.BranchChildId).IsRequired();
        builder.Property(a => a.CombineMode).IsRequired();
        builder.Property(a => a.AllowAssignorOverride).IsRequired();

        // One assignment per branch child — resolver loads by BranchChildId.
        builder.HasIndex(a => a.BranchChildId)
            .IsUnique()
            .HasDatabaseName("UX_WorkflowBranchChildAssignments_BranchChildId");

        // Dependent side of the one-to-one with WorkflowBranchChild (FK = BranchChildId).
        builder.HasOne<WorkflowBranchChild>()
            .WithOne(c => c.Assignment)
            .HasForeignKey<WorkflowBranchChildAssignment>(a => a.BranchChildId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(a => a.Clauses)
            .WithOne()
            .HasForeignKey(c => c.AssignmentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata
            .FindNavigation(nameof(WorkflowBranchChildAssignment.Clauses))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}
