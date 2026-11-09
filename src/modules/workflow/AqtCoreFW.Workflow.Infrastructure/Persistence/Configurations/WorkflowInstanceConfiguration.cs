using AqtCoreFW.Infrastructure.Configuration;
using AqtCoreFW.Workflow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Workflow.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF config for WorkflowInstance aggregate root.
/// Layered concurrency strategy (D2, 2026-04-17):
///   (1) App-layer catches sequential races early via FromStateId equality check in
///       ExecuteTransitionCommandHandler — fails fast with InvalidTransition (GOV_WFL_003).
///   (2) DB-layer catches simultaneous races via IsConcurrencyToken on CurrentStateId —
///       EF generates UPDATE ... WHERE Id=@id AND CurrentStateId=@original; 0 affected rows
///       throws DbUpdateConcurrencyException → StateConflict (GOV_WFL_013).
/// Scalar-column concurrency token avoids bug 33d4973f (RowVersion propagated Modified
/// state to history children) because EF only tracks original/current of THIS property.
/// SlaBreachNotifiedAt indexed for efficient SlaBreachCheckerJob polling.
/// </summary>
public sealed class WorkflowInstanceConfiguration
    : EntityTypeConfigurationBase<WorkflowInstance>
{
    protected override void ConfigureEntity(EntityTypeBuilder<WorkflowInstance> builder)
    {
        builder.ToTable("WorkflowInstances");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.EntityType).HasMaxLength(100).IsRequired();
        builder.Property(i => i.TenantId).IsRequired();
        builder.Property(i => i.DefinitionId).IsRequired();
        builder.Property(i => i.CurrentStateId).IsRequired().IsConcurrencyToken();
        builder.Property(i => i.EntityId).IsRequired();
        builder.Property(i => i.DossierId);   // nullable per-run identity (no IsRequired)
        builder.Property(i => i.StartedAt).IsRequired();
        builder.Property(i => i.CurrentStateEnteredAt).IsRequired();

        builder.HasMany(i => i.History)
            .WithOne()
            .HasForeignKey(h => h.InstanceId)
            .OnDelete(DeleteBehavior.Cascade);

        // SLA breach job: find active instances overdue and not yet notified
        builder.HasIndex(i => new { i.TenantId, i.CompletedAt, i.SlaBreachNotifiedAt })
            .HasDatabaseName("IX_WorkflowInstances_TenantId_CompletedAt_SlaBreach");

        // Entity lookup: find instances for a given business object
        builder.HasIndex(i => new { i.EntityType, i.EntityId })
            .HasDatabaseName("IX_WorkflowInstances_EntityType_EntityId");

        // OUTBOX-1: one active instance per (EntityType, EntityId, TenantId) while DossierId IS NULL.
        // Rows carrying a per-run DossierId (BusinessProcess runs) are exempt, permitting N concurrent
        // runs per definition. Collapses the create-race into an atomic DB constraint; the synchronous
        // SaveChanges path translates a 2601/2627 collision to a clean conflict (GOV_WFL_020 → 409).
        builder.HasIndex(i => new { i.EntityType, i.EntityId, i.TenantId })
            .IsUnique()
            .HasFilter("[CompletedAt] IS NULL AND [DossierId] IS NULL")
            .HasDatabaseName("UX_WorkflowInstances_Active_Entity");

        builder.Ignore(i => i.DomainEvents);

        // T5-10: Version migration tracking fields
        builder.Property(i => i.OriginalDefinitionId);
        builder.Property(i => i.LastMigrationId);

        // Filtered indexes: only rows that have been migrated (sparse — most rows NULL)
        builder.HasIndex(i => i.OriginalDefinitionId)
            .HasDatabaseName("IX_WorkflowInstances_OriginalDefinitionId")
            .HasFilter("[OriginalDefinitionId] IS NOT NULL");

        builder.HasIndex(i => i.LastMigrationId)
            .HasDatabaseName("IX_WorkflowInstances_LastMigrationId")
            .HasFilter("[LastMigrationId] IS NOT NULL");
    }
}
