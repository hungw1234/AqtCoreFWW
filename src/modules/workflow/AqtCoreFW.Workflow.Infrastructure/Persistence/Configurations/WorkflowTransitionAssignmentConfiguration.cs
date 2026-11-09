using AqtCoreFW.Infrastructure.Configuration;
using AqtCoreFW.Workflow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Workflow.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF config for WorkflowTransitionAssignment (plan 260519-0421 v2, P1).
/// Separate entity (RT2-9) — unique per transition so the resolver query-by-transition is a seek.
/// NO pol_*_Tenant RLS (RT2-8): config of the definition graph, inherits its tenancy.
/// </summary>
public sealed class WorkflowTransitionAssignmentConfiguration
    : EntityTypeConfigurationBase<WorkflowTransitionAssignment>
{
    protected override void ConfigureEntity(EntityTypeBuilder<WorkflowTransitionAssignment> builder)
    {
        builder.ToTable("WorkflowTransitionAssignments");

        builder.Property(a => a.WorkflowTransitionId).IsRequired();
        builder.Property(a => a.CombineMode).IsRequired();
        builder.Property(a => a.AllowAssignorOverride).IsRequired();

        // One assignment per transition — resolver loads by WorkflowTransitionId.
        builder.HasIndex(a => a.WorkflowTransitionId)
            .IsUnique()
            .HasDatabaseName("UX_WorkflowTransitionAssignments_TransitionId");

        builder.HasMany(a => a.Clauses)
            .WithOne()
            .HasForeignKey(c => c.AssignmentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata
            .FindNavigation(nameof(WorkflowTransitionAssignment.Clauses))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}
