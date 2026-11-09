using AqtCoreFW.Infrastructure.Configuration;
using AqtCoreFW.Workflow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Workflow.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF config for WorkflowTask — HOT TABLE (Phase-04d B5): non-clustered PK + clustered (CreatedAt, Id)
/// trades single-row PK lookup for time-range polling speed (escalation/SLA jobs scan latest first).
/// Status index supports escalation job polling (open/in-progress tasks by due date).
/// AssigneeId index supports GetTasksByAssignee query.
/// </summary>
public sealed class WorkflowTaskConfiguration
    : HotTableConfigurationBase<WorkflowTask>
{
    protected override void ConfigureEntity(EntityTypeBuilder<WorkflowTask> builder)
    {
        builder.ToTable("WorkflowTasks");

        builder.Property(t => t.Title).HasMaxLength(500).IsRequired();
        builder.Property(t => t.Description).HasMaxLength(2000);
        builder.Property(t => t.Status).IsRequired();
        builder.Property(t => t.Priority).IsRequired();
        builder.Property(t => t.WorkflowInstanceId).IsRequired();

        // PHR-V2 Phase 6 S4 / GATE-1 Path A: explicit nav-prop relationship.
        // Enables EF IDENTITY-FK propagation in a single SaveChanges for the instance-creation
        // first-task path (CreateWorkflowInstanceCommandHandler + InProcessWorkflowModuleClient).
        // WithMany() — shadow inverse — preserves the WorkflowInstance no-child-collection invariant
        // (bug 33d4973f safety; see WorkflowInstanceConfiguration remarks).
        // OnDelete restricted — instances are never hard-deleted (soft-delete via global filter).
        builder.HasOne(t => t.Instance)
            .WithMany()
            .HasForeignKey(t => t.WorkflowInstanceId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        // Parallel branch child link (nullable — non-branch tasks have null). Same IDENTITY-FK
        // propagation pattern as the Instance relationship above: the fork-staging path sets the
        // navigation (not the scalar FK) so EF writes the child status's DB-generated Id into the FK
        // when both rows insert in one SaveChanges. NoAction — the column lives on WorkflowTasks and the
        // child-status lifecycle is owned by the branch aggregate; avoids a multiple-cascade-path conflict.
        builder.HasOne(t => t.BranchChildStatus)
            .WithMany()
            .HasForeignKey(t => t.BranchChildStatusId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(t => t.BranchChildStatusId)
            .HasFilter("[BranchChildStatusId] IS NOT NULL")
            .HasDatabaseName("IX_WorkflowTasks_BranchChildStatusId");

        // Escalation job: find open tasks past due date per instance
        builder.HasIndex(t => new { t.WorkflowInstanceId, t.Status })
            .HasDatabaseName("IX_WorkflowTasks_InstanceId_Status");

        // Assignee workload queries
        builder.HasIndex(t => new { t.TenantId, t.AssigneeId, t.Status })
            .HasDatabaseName("IX_WorkflowTasks_TenantId_AssigneeId_Status");

        // Delegation: nullable — non-null means task is currently delegated
        // Index supports DelegationExpiryJob: find delegated tasks per tenant
        builder.Property(t => t.DelegatedFromUserId);
        builder.HasIndex(t => t.DelegatedFromUserId)
            .HasFilter("[DelegatedFromUserId] IS NOT NULL")
            .HasDatabaseName("IX_WorkflowTasks_DelegatedFromUserId");

        // Pool-task delta (plan 260519-0421 v2, P1).
        // RowVersion: rowversion optimistic-concurrency token = the Claim race-tiebreak.
        // Safe here (no tracked child nav on WorkflowTask) — see entity remarks / bug 33d4973f.
        builder.Property(t => t.RowVersion).IsRowVersion();
        builder.Property(t => t.CandidateRefsJson).HasColumnType("nvarchar(max)");
        builder.Property(t => t.ClaimedAt);

        builder.Ignore(t => t.DomainEvents);
    }
}
