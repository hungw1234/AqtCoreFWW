using AqtCoreFW.Infrastructure.Configuration;
using AqtCoreFW.Workflow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Workflow.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF config for WorkflowTransitionAssignmentClause (plan 260519-0421 v2, P1).
/// Child of WorkflowTransitionAssignment (FK + cascade configured on the parent side).
/// RefsJson is an unbounded JSON array of ids/codes; null for ref-less modes.
/// </summary>
public sealed class WorkflowTransitionAssignmentClauseConfiguration
    : EntityTypeConfigurationBase<WorkflowTransitionAssignmentClause>
{
    protected override void ConfigureEntity(EntityTypeBuilder<WorkflowTransitionAssignmentClause> builder)
    {
        builder.ToTable("WorkflowTransitionAssignmentClauses");

        builder.Property(c => c.AssignmentId).IsRequired();
        builder.Property(c => c.Mode).IsRequired();
        builder.Property(c => c.RefsJson).HasColumnType("nvarchar(max)");

        builder.HasIndex(c => c.AssignmentId)
            .HasDatabaseName("IX_WorkflowTransitionAssignmentClauses_AssignmentId");
    }
}
