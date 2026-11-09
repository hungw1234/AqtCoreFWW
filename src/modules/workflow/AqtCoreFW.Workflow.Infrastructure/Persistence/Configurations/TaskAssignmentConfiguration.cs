using AqtCoreFW.Infrastructure.Configuration;
using AqtCoreFW.Workflow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Workflow.Infrastructure.Persistence.Configurations;

/// <summary>EF config for TaskAssignment audit records.</summary>
public sealed class TaskAssignmentConfiguration : EntityTypeConfigurationBase<TaskAssignment>
{
    protected override void ConfigureEntity(EntityTypeBuilder<TaskAssignment> builder)
    {
        builder.ToTable("TaskAssignments");

        builder.Property(a => a.WorkflowTaskId).IsRequired();
        builder.Property(a => a.AssignedById).IsRequired();
        builder.Property(a => a.AssignedToId).IsRequired();
        builder.Property(a => a.AssignedAt).IsRequired();
        builder.Property(a => a.Reason).HasMaxLength(500);
        builder.Property(a => a.Type).IsRequired();

        // History lookup: all assignments for a task ordered by time
        builder.HasIndex(a => new { a.WorkflowTaskId, a.AssignedAt })
            .HasDatabaseName("IX_TaskAssignments_TaskId_AssignedAt");
    }
}
