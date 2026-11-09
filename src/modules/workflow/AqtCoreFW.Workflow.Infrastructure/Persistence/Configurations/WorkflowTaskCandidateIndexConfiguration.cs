using AqtCoreFW.Infrastructure.Configuration;
using AqtCoreFW.Workflow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Workflow.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF config for WorkflowTaskCandidateIndex (plan 260519-0421 v2, P1).
/// Tenant-scoped runtime read-model (RT2-8: gets pol_*_Tenant RLS via the migration).
/// Inbox seek path = (TenantId, PrincipalType, PrincipalRefId); provenance lookup =
/// (WorkflowTaskId, ClauseId). EntityTypeConfigurationBase auto-adds (TenantId, Id)/
/// (TenantId, CreatedAt) composites because the entity is ITenantScoped.
/// </summary>
public sealed class WorkflowTaskCandidateIndexConfiguration
    : EntityTypeConfigurationBase<WorkflowTaskCandidateIndex>
{
    protected override void ConfigureEntity(EntityTypeBuilder<WorkflowTaskCandidateIndex> builder)
    {
        builder.ToTable("WorkflowTaskCandidateIndex");

        builder.Property(r => r.WorkflowTaskId).IsRequired();
        builder.Property(r => r.ClauseId).IsRequired();
        builder.Property(r => r.CombineMode).IsRequired();
        builder.Property(r => r.PrincipalType).IsRequired();
        builder.Property(r => r.PrincipalRefId).HasMaxLength(64).IsRequired();
        builder.Property(r => r.TenantId).IsRequired();

        // Inbox eligibility seek (tenant in the predicate — defense-in-depth, not RLS-only).
        builder.HasIndex(r => new { r.TenantId, r.PrincipalType, r.PrincipalRefId })
            .HasDatabaseName("IX_WorkflowTaskCandidateIndex_Tenant_Principal");

        // Provenance / cascade-cleanup on task lifecycle.
        builder.HasIndex(r => new { r.WorkflowTaskId, r.ClauseId })
            .HasDatabaseName("IX_WorkflowTaskCandidateIndex_TaskId_ClauseId");

        // DCAND-a (plan 260521-1008): explicit FK constraint child→parent.
        // WithMany() = no reverse ICollection on WorkflowTask — preserves rowversion invariant
        // (WorkflowTask.cs:43-48 remarks, bug-33d4973f: tracked child nav propagates Modified state
        // into parent rowversion check → false concurrency failures on Claim).
        // OnDelete = Restrict: both entities are soft-deleted; hard-delete not a normal operation;
        // Restrict ensures any future accidental hard-delete fails loudly rather than silently
        // dropping candidate-index rows (fail-loud > silent data loss).
        // No new index needed: IX_WorkflowTaskCandidateIndex_TaskId_ClauseId already covers FK lookup.
        builder.HasOne(r => r.WorkflowTask)
            .WithMany()
            .HasForeignKey(r => r.WorkflowTaskId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_WorkflowTaskCandidateIndex_WorkflowTasks_WorkflowTaskId");
    }
}
