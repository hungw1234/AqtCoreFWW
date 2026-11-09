using System.Transactions;
using AqtCoreFW.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.Workflow.Application.Repositories;
using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Domain.Errors;
using FluentResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage; // IDbContextTransaction (Infrastructure-only; not exposed to Application)

namespace AqtCoreFW.Workflow.Infrastructure.Persistence;

/// <summary>
/// EF Core implementation of IWorkflowInstanceRepository.
/// T2 entity: IRepository key is Guid (PublicId AK). FindAsync uses PublicId via EF key selector.
/// Phase-4b B4c: added GetWithHistoryByInternalIdAsync (lookup by long FK)
///   and updated GetActiveInstanceCountByStateAsync signature (Guid→long for T1 state IDs).
/// </summary>
public sealed class WorkflowInstanceRepository(
    WorkflowDbContext context,
    ICrossModuleUnitOfWork? uow = null)
    : GenericRepository<WorkflowInstance, Guid>(context, uow), IWorkflowInstanceRepository
{
    private WorkflowDbContext Ctx => (WorkflowDbContext)Context;

    /// <inheritdoc/>
    /// <remarks>
    /// WorkflowInstance is a T2 entity: its EF primary key is the internal long Id, while the
    /// cross-module contract addresses it by Guid PublicId (alternate key). The base FindAsync
    /// keys on the long PK, so a Guid lookup must query PublicId explicitly — delegating to base
    /// would throw an ArgumentException (Guid vs long key mismatch).
    ///
    /// Also enlists WorkflowDbContext on the ambient unit of work before the first EF query on the
    /// BP path so cross-module writes share one SQL connection (no distributed-transaction
    /// promotion on Linux, where MSDTC is unavailable).
    /// </remarks>
    public override async Task<Result<WorkflowInstance>> GetByIdAsync(
        Guid id, CancellationToken cancellationToken = default)
    {
        await EnsureEnlistedAsync(cancellationToken);
        var instance = await Ctx.Query<WorkflowInstance>()
            .FirstOrDefaultAsync(i => i.PublicId == id, cancellationToken);
        return instance is null
            ? Result.Fail(WorkflowErrors.InstanceNotFound(id))
            : Result.Ok(instance);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Same T2 reason as <see cref="GetByIdAsync"/>: the base FindAsync keys on the long PK, so a
    /// Guid (PublicId) delete would throw the Guid-vs-long key mismatch. Resolve by PublicId first.
    /// No call site hard-deletes a WorkflowInstance today (instances are completed/cancelled), but
    /// keeping the override consistent with GetByIdAsync removes the latent throw the moment a delete
    /// path is introduced.
    /// </remarks>
    public override async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await EnsureEnlistedAsync(cancellationToken);
        var instance = await Ctx.TrackingQuery<WorkflowInstance>()
            .FirstOrDefaultAsync(i => i.PublicId == id, cancellationToken);
        if (instance is null) return;

        // SoftDeleteInterceptor converts EntityState.Deleted → IsDeleted = true (mirrors base DeleteAsync).
        Ctx.Set<WorkflowInstance>().Remove(instance);
        await Ctx.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Result<WorkflowInstance>> GetWithHistoryAsync(
        Guid publicId,
        CancellationToken cancellationToken = default)
    {
        await EnsureEnlistedAsync(cancellationToken);
        var instance = await Ctx.TrackingQuery<WorkflowInstance>()
            .Include(i => i.History)
            .FirstOrDefaultAsync(i => i.PublicId == publicId, cancellationToken);

        return instance is null
            ? Result.Fail(WorkflowErrors.InstanceNotFound(publicId))
            : Result.Ok(instance);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Lookup by internal long Id (bigint PK). Used by branch-join handlers that only have
    /// WorkflowInstanceBranchStatus.InstanceId (long FK) and need the aggregate for ApplyParallelJoinTransition.
    /// </remarks>
    public async Task<Result<WorkflowInstance>> GetWithHistoryByInternalIdAsync(
        long internalId,
        CancellationToken cancellationToken = default)
    {
        await EnsureEnlistedAsync(cancellationToken);
        var instance = await Ctx.TrackingQuery<WorkflowInstance>()
            .Include(i => i.History)
            .FirstOrDefaultAsync(i => i.Id == internalId, cancellationToken);

        return instance is null
            ? Result.Fail(new Error($"WorkflowInstance with internal Id '{internalId}' not found."))
            : Result.Ok(instance);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Lightweight single-column projection — no History load, non-tracking read.
    /// Returns null if the row does not exist (orphan FK invariant — caller must fail loudly).
    /// </remarks>
    public async Task<Guid?> GetPublicIdByInternalIdAsync(
        long internalId,
        CancellationToken cancellationToken = default)
    {
        await EnsureEnlistedAsync(cancellationToken);
        return await Ctx.Query<WorkflowInstance>()
            .Where(i => i.Id == internalId)
            .Select(i => (Guid?)i.PublicId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<WorkflowInstance>> GetActiveInstancesByEntityAsync(
        string entityType,
        Guid entityId,
        CancellationToken cancellationToken = default)
    {
        await EnsureEnlistedAsync(cancellationToken);
        return await Ctx.Query<WorkflowInstance>()
            .Where(i => i.EntityType == entityType
                     && i.EntityId == entityId
                     && i.CompletedAt == null)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Returns active instance counts grouped by CurrentStateId (long — T1 WorkflowState.Id)
    /// for a definition filtered by internal long Id (T2 WorkflowDefinition.Id FK).
    /// </remarks>
    public async Task<Dictionary<long, int>> GetActiveInstanceCountByStateAsync(
        long definitionInternalId,
        CancellationToken cancellationToken = default)
    {
        await EnsureEnlistedAsync(cancellationToken);
        return await Ctx.Query<WorkflowInstance>()
            .Where(i => i.DefinitionId == definitionInternalId && i.CompletedAt == null)
            .GroupBy(i => i.CurrentStateId)
            .Select(g => new { StateId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.StateId, x => x.Count, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<bool> HasInstancesWithActiveBranchAsync(
        Guid definitionPublicId,
        CancellationToken cancellationToken = default)
    {
        await EnsureEnlistedAsync(cancellationToken);
        // Resolve PublicId → internal Id first (T2 entity pattern)
        var definitionInternalId = await Ctx.Query<WorkflowDefinition>()
            .Where(d => d.PublicId == definitionPublicId)
            .Select(d => (long?)d.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (definitionInternalId is null) return false;

        var activeInstanceIds = Ctx.Query<WorkflowInstance>()
            .Where(i => i.DefinitionId == definitionInternalId.Value && i.CompletedAt == null)
            .Select(i => i.Id);

        return await Ctx.InstanceBranchStatuses
            .AnyAsync(
                bs => activeInstanceIds.Contains(bs.InstanceId)
                    && bs.Status == WorkflowInstanceBranchStatus.Statuses.InProgress,
                cancellationToken);
    }

    /// <inheritdoc/>
    public void Stage(WorkflowInstance instance)
    {
        // Enlist NEW instance in ChangeTracker (EntityState.Added) without saving.
        // The shared WorkflowDbContext (scoped per request) means
        // IWorkflowInstanceRepository.SaveChangesAsync flushes this alongside any first-task
        // staged via IWorkflowTaskFactory in one round-trip (S7 single-SaveChanges invariant).
        // Replaces GenericRepository.AddAsync at instance-creation sites whose auto-save would
        // break atomicity (instance row committed before first-task staging).
        Ctx.Set<WorkflowInstance>().Add(instance);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Slice 7: DRY-composed over <see cref="UpdateWithStateGuardNoSaveAsync"/> +
    /// inline save + catch. Preserves <see cref="WorkflowErrors.StateConflict(Guid)"/> (with Guid)
    /// for the 3 existing callers (CompleteTask, CancelTask, ResolveBranchChild) so their error
    /// messages are byte-identical to pre-Slice-7. The outer ETR boundary uses
    /// <see cref="SaveChangesAsync"/> directly (returns Guid-less StateConflictAtBoundary).
    /// </remarks>
    public async Task<Result> UpdateWithStateGuardAsync(
        WorkflowInstance instance,
        CancellationToken cancellationToken = default)
    {
        await EnsureEnlistedAsync(cancellationToken);
        var staged = await UpdateWithStateGuardNoSaveAsync(instance, cancellationToken);
        if (staged.IsFailed) return staged;
        try
        {
            await Ctx.SaveChangesAsync(cancellationToken);
            return Result.Ok();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Fail(WorkflowErrors.StateConflict(instance.PublicId));
        }
    }

    /// <inheritdoc/>
    public async Task<Result> UpdateWithStateGuardNoSaveAsync(
        WorkflowInstance instance,
        CancellationToken cancellationToken = default)
    {
        if (Ctx.Entry(instance).State == EntityState.Detached)
            Ctx.Set<WorkflowInstance>().Update(instance);

        // Fix: new history entries (added via _history.Add in ApplyTransition) may be
        // detected as Modified instead of Added because they have pre-assigned long IDs
        // from the DB IDENTITY column — mark as Added if not yet in DB.
        foreach (var entry in Ctx.ChangeTracker.Entries<WorkflowInstanceHistory>()
            .Where(e => e.State == EntityState.Modified))
        {
            var dbValues = await entry.GetDatabaseValuesAsync(cancellationToken);
            if (dbValues is null)
                entry.State = EntityState.Added;
        }

        // No SaveChangesAsync here — caller (ETR) owns the single flush via SaveChangesAsync().
        return Result.Ok();
    }

    /// <inheritdoc/>
    public async Task<Result<int>> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await EnsureEnlistedAsync(cancellationToken);
        try
        {
            return Result.Ok(await Ctx.SaveChangesAsync(cancellationToken));
        }
        catch (DbUpdateConcurrencyException)
        {
            // Translate to Result.Fail — no EF Core type crosses the Application→Infrastructure boundary.
            return Result.Fail(WorkflowErrors.StateConflictAtBoundary());
        }
        catch (DbUpdateException ex) when (IsDuplicateActiveEntity(ex))
        {
            // A second active instance for the same (EntityType, EntityId, TenantId) collided with the
            // unique active-entity index. Translate the raw 2601/2627 into a clean conflict (409) instead
            // of a 500. The narrow `when` filter keeps FK / other DbUpdateExceptions bubbling.
            return Result.Fail(WorkflowErrors.InstanceAlreadyActive());
        }
    }

    // True ONLY when the DbUpdateException wraps a duplicate-key violation (2601 unique index /
    // 2627 unique constraint) on the active-entity index specifically. SaveChangesAsync is the shared
    // flush path (transitions stage history/branch-status rows with their own unique indexes); scoping
    // to the index name keeps an unrelated collision from being mislabeled "active instance already exists".
    private static bool IsDuplicateActiveEntity(DbUpdateException ex) =>
        ex.InnerException is Microsoft.Data.SqlClient.SqlException sql
        && (sql.Number == 2601 || sql.Number == 2627)
        && sql.Message.Contains("UX_WorkflowInstances_Active_Entity", System.StringComparison.Ordinal);

    /// <inheritdoc/>
    public void ClearChanges() => Ctx.ChangeTracker.Clear();

    // ── Ambient-enlist transaction management ───────────────────────────────────
    // Kept in repository to preserve Application→Infrastructure boundary (Application layer
    // must not reference WorkflowDbContext or System.Transactions directly).

    /// <inheritdoc/>
    public bool HasAmbientTransaction()
    {
        // Two distinct ambient-transaction sources must both be tested:
        //   1. Transaction.Current is non-null when a System.Transactions.TransactionScope is active
        //      on the calling thread — legacy callers that still open their own scope.
        //   2. Ctx.Database.CurrentTransaction is non-null when this DbContext has been enlisted via
        //      ICrossModuleUnitOfWork.EnlistAsync (UseTransactionAsync wires the shared local tx).
        //      This path avoids TransactionScope/DTC entirely; the outer handler owns commit/rollback.
        // ExecuteTransitionCommandHandler checks this before opening its own EF transaction:
        //   if TRUE → cooperative (caller owns commit); if FALSE → handler opens its own tx.
        return Transaction.Current is not null || Ctx.Database.CurrentTransaction is not null;
    }

    /// <inheritdoc/>
    public async Task<IWorkflowTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        var tx = await Ctx.Database.BeginTransactionAsync(cancellationToken);
        return new EfWorkflowTransaction(tx);
    }

    /// <summary>
    /// Thin wrapper adapting <see cref="IDbContextTransaction"/> to <see cref="IWorkflowTransaction"/>.
    /// Keeps EF Core out of the Application layer — only created/used in Infrastructure.
    /// </summary>
    private sealed class EfWorkflowTransaction(IDbContextTransaction inner) : IWorkflowTransaction
    {
        public Task CommitAsync(CancellationToken ct = default) => inner.CommitAsync(ct);
        public Task RollbackAsync(CancellationToken ct = default) => inner.RollbackAsync(ct);
        public ValueTask DisposeAsync() => inner.DisposeAsync();
        public void Dispose() => inner.Dispose();
    }
}
