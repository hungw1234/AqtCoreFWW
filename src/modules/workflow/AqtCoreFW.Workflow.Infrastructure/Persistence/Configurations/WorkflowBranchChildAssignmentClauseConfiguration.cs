using AqtCoreFW.Infrastructure.Configuration;
using AqtCoreFW.Workflow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Workflow.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF config for WorkflowBranchChildAssignmentClause — one resolution clause of a
/// WorkflowBranchChildAssignment (FK + cascade configured on the parent side).
/// RefsJson is an unbounded JSON array of ids/codes; null for ref-less modes.
/// Mirrors WorkflowTransitionAssignmentClause.
/// </summary>
public sealed class WorkflowBranchChildAssignmentClauseConfiguration
    : EntityTypeConfigurationBase<WorkflowBranchChildAssignmentClause>
{
    protected override void ConfigureEntity(EntityTypeBuilder<WorkflowBranchChildAssignmentClause> builder)
    {
        builder.ToTable("WorkflowBranchChildAssignmentClauses");

        builder.Property(c => c.AssignmentId).IsRequired();
        builder.Property(c => c.Mode).IsRequired();
        builder.Property(c => c.RefsJson).HasColumnType("nvarchar(max)");

        builder.HasIndex(c => c.AssignmentId)
            .HasDatabaseName("IX_WorkflowBranchChildAssignmentClauses_AssignmentId");
    }
}
