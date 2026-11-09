using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Domain.Enums;
using AqtCoreFW.Workflow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Workflow.Infrastructure.Jobs;

/// <summary>
/// Processes a single batch of workflow instances during version migration.
/// Called by WorkflowMigrationExecutor for each keyset page.
/// Each batch runs in its own EF transaction for SQL timeout safety.
/// Returns true if execution should continue, false if cancelled.
/// Phase-4b B4c: stateMapping = Dictionary&lt;long, long&gt; (T1 WorkflowState.Id bigint PKs).
///               WorkflowInstance.Id, WorkflowVersionMigration.Id, MigrationId are all long.
///               RecordMigration params are long (SourceDefinitionId, migrationId, newStateId, TargetDefinitionId).
/// </summary>
internal sealed class MigrationBatchProcessor(
    WorkflowDbContext context,
    IGuidGenerator gen,
    ILogger logger)
{
    // System actor ID for migration-initiated transitions
    private static readonly Guid MigrationActorId = Guid.Empty;

    /// <summary>
    /// Processes one page of instances within a single transaction.
    /// Cancel detection is handled by the executor before calling this method.
    /// stateMapping: T1 state Id → T1 state Id (both long bigint PKs).
    /// </summary>
    public async Task ProcessBatchAsync(
        WorkflowVersionMigration migration,
        IReadOnlyList<WorkflowInstance> batch,
        Dictionary<long, long> stateMapping,
        CancellationToken ct)
    {
        // Use a transaction if supported (not available in InMemory test provider)
        var useTransaction = context.Database.ProviderName != "Microsoft.EntityFrameworkCore.InMemory";
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? tx = null;

        if (useTransaction)
            tx = await context.Database.BeginTransactionAsync(ct);

        try
        {
            foreach (var instance in batch)
                await ProcessInstanceAsync(migration, instance, stateMapping, ct);

            // EF quirk: WorkflowInstanceHistory entities added to a tracked navigation collection
            // with a pre-set non-default Guid PK are detected as Modified (not Added) by DetectChanges.
            // WorkflowInstanceHistory is append-only — any Modified entry is a newly created record.
            // Fix state so InMemory (and SQL Server) will INSERT rather than UPDATE.
            foreach (var entry in context.ChangeTracker.Entries<WorkflowInstanceHistory>())
            {
                if (entry.State == EntityState.Modified)
                    entry.State = EntityState.Added;
            }

            await SaveWithConcurrencyHandlingAsync(ct);
            if (tx is not null) await tx.CommitAsync(ct);
        }
        catch
        {
            if (tx is not null) await tx.RollbackAsync(ct);
            throw;
        }
        finally
        {
            if (tx is not null) await tx.DisposeAsync();
        }

        // Recompute counts from items table (source of truth per spec)
        await RecomputeCountsAsync(migration, ct);
    }

    private async Task ProcessInstanceAsync(
        WorkflowVersionMigration migration,
        WorkflowInstance instance,
        Dictionary<long, long> stateMapping,
        CancellationToken ct)
    {
        // instance.CurrentStateId is long (T1 WorkflowState.Id)
        var oldStateId = instance.CurrentStateId;

        // Guard: load existing item for (MigrationId long, InstanceId long) — idempotency
        // IgnoreQueryFilters: Hangfire has no tenant context
        var existingItem = await context.VersionMigrationItems
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(i =>
                i.MigrationId == migration.Id && i.InstanceId == instance.Id, ct);

        if (existingItem?.Status == MigrationItemStatus.Succeeded)
            return; // Hangfire retry safety — already done

        // Upsert: create Pending item if not present
        WorkflowVersionMigrationItem item;
        if (existingItem is null)
        {
            // stateMapping.GetValueOrDefault returns 0L (long default) when key absent
            var mappedStateId = stateMapping.GetValueOrDefault(oldStateId);
            item = WorkflowVersionMigrationItem.Create(
                gen, migration.Id, instance.Id, oldStateId,
                mappedStateId == 0L ? oldStateId : mappedStateId);
            await context.VersionMigrationItems.AddAsync(item, ct);
        }
        else
        {
            item = existingItem;
        }

        // Check unmapped state
        if (!stateMapping.TryGetValue(oldStateId, out var newStateId))
        {
            item.MarkFailed("unmapped state");
            logger.LogWarning(
                "Migration {MigrationId}: instance {InstanceId} has unmapped state {StateId}.",
                migration.Id, instance.Id, oldStateId);
            return;
        }

        // Check concurrent transition (state changed since we loaded the batch)
        if (existingItem is not null && instance.CurrentStateId != oldStateId)
        {
            item.MarkSkipped("concurrent transition");
            return;
        }

        // Migrate: update instance via domain method (records history internally).
        // RecordMigration(gen, sourceDefinitionId long, migrationId long, newStateId long, targetDefinitionId long, actorId Guid).
        instance.RecordMigration(
            gen,
            migration.SourceDefinitionId,
            migration.Id,
            newStateId,
            migration.TargetDefinitionId,
            MigrationActorId);

        item.MarkSucceeded();
    }

    /// <summary>
    /// SaveChangesAsync with concurrency-conflict diagnostics.
    /// After D2 (EF concurrency token on <c>WorkflowInstance.CurrentStateId</c>), a concurrent
    /// user transition during migration batch save will trigger <see cref="Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException"/>.
    /// Behavior: log with diagnostic context, detach tracked entities, rethrow original exception
    /// so outer try-catch rolls the batch transaction back — Hangfire retry can re-process.
    /// </summary>
    private async Task SaveWithConcurrencyHandlingAsync(CancellationToken ct)
    {
        try
        {
            await context.SaveChangesAsync(ct);
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException ex)
        {
            var info = string.Join("; ", ex.Entries.Select(e =>
                $"{e.Entity.GetType().Name}(State={e.State})"));
            var tracked = string.Join(", ", context.ChangeTracker.Entries()
                .Where(e => e.State != Microsoft.EntityFrameworkCore.EntityState.Unchanged)
                .Select(e => $"{e.Entity.GetType().Name}({e.State})"));

            logger.LogWarning(ex,
                "Migration batch concurrency conflict — concurrent user transition during migration. "
                + "Conflict on: [{Info}]. Non-unchanged tracked: [{Tracked}]. Rolling back batch "
                + "for Hangfire retry. Consider draining user transitions before migration.",
                info, tracked);

            // Detach tracked entries so Hangfire retry path gets fresh state from the store
            foreach (var entry in context.ChangeTracker.Entries().ToList())
                entry.State = Microsoft.EntityFrameworkCore.EntityState.Detached;

            throw;
        }
    }

    /// <summary>
    /// Queries item counts from the DB (source of truth) and pushes to migration entity.
    /// migration.Id is long (T1 WorkflowVersionMigration.Id bigint PK).
    /// </summary>
    private async Task RecomputeCountsAsync(WorkflowVersionMigration migration, CancellationToken ct)
    {
        var migrated = await context.VersionMigrationItems
            .IgnoreQueryFilters()
            .CountAsync(i => i.MigrationId == migration.Id
                          && i.Status == MigrationItemStatus.Succeeded, ct);
        var failed = await context.VersionMigrationItems
            .IgnoreQueryFilters()
            .CountAsync(i => i.MigrationId == migration.Id
                          && i.Status == MigrationItemStatus.Failed, ct);
        var skipped = await context.VersionMigrationItems
            .IgnoreQueryFilters()
            .CountAsync(i => i.MigrationId == migration.Id
                          && i.Status == MigrationItemStatus.Skipped, ct);

        migration.SetCounts(migrated, failed, skipped);
        await SaveWithConcurrencyHandlingAsync(ct);
    }
}
