using AqtCoreFW.SharedKernel.Domain.Repositories;
using AqtCoreFW.Workflow.Domain.Entities;
using FluentResults;

namespace AqtCoreFW.Workflow.Application.Repositories;

/// <summary>
/// Write-side repository for WorkflowInstance aggregate.
/// GetActiveInstancesByEntityAsync used by SLA breach checker.
/// Phase-4b B4c: T2 entity — IRepository&lt;T, Guid&gt; key is PublicId (external Guid AK).
/// </summary>
public interface IWorkflowInstanceRepository : IRepository<WorkflowInstance, Guid>
{
    /// <summary>Loads instance with full history — needed for ExecuteTransition command. publicId = WorkflowInstance.PublicId.</summary>
    Task<Result<WorkflowInstance>> GetWithHistoryAsync(
        Guid publicId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads instance with full history by internal long Id (bigint PK).
    /// Used by branch-join handlers that only have WorkflowInstanceBranchStatus.InstanceId (long).
    /// Phase-4b B4c: internal-only overload — not exposed via IWorkflowModuleClient.
    /// </summary>
    Task<Result<WorkflowInstance>> GetWithHistoryByInternalIdAsync(
        long internalId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lightweight projection: returns only WorkflowInstance.PublicId (Guid AK) by internal long Id.
    /// Used by task command handlers to populate DTO without loading the full aggregate + History.
    /// Returns null when the instance does not exist (orphan FK → caller should fail loudly).
    /// Phase-07 PR-D2: M1 fix — replaces GetWithHistoryByInternalIdAsync for DTO-only call sites.
    /// </summary>
    Task<Guid?> GetPublicIdByInternalIdAsync(
        long internalId,
        CancellationToken cancellationToken = default);

    /// <summary>Returns all non-completed instances for a business entity (e.g. Case).</summary>
    Task<IReadOnlyList<WorkflowInstance>> GetActiveInstancesByEntityAsync(
        string entityType,
        Guid entityId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves <see cref="WorkflowInstance"/> with a layered concurrency guard.
    /// App-layer catches sequential races early via <c>FromStateId == CurrentStateId</c>
    /// check in the command handler; DB-layer (EF concurrency token on
    /// <c>CurrentStateId</c>) catches simultaneous races — <see cref="DbUpdateConcurrencyException"/>
    /// is translated to Result.Fail(<c>WorkflowErrorCodes.StateConflict</c>) so Application
    /// layer stays free of EF Core dependency.
    /// </summary>
    Task<Result> UpdateWithStateGuardAsync(
        WorkflowInstance instance,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stages a NEW <see cref="WorkflowInstance"/> in the EF ChangeTracker (EntityState.Added)
    /// WITHOUT calling <c>SaveChangesAsync</c>. Caller MUST invoke <see cref="SaveChangesAsync"/>
    /// afterward to flush atomically alongside any same-context staging (e.g. first
    /// <c>WorkflowTask</c> via <c>IWorkflowTaskFactory</c>) — S7 single-SaveChanges invariant.
    /// Phase 5b: replaces <see cref="IRepository{TEntity,TKey}.AddAsync"/> at instance-creation
    /// sites where <c>GenericRepository.AddAsync</c>'s auto-save would break atomicity.
    /// </summary>
    void Stage(WorkflowInstance instance);

    // ── Slice 7: Defer-save split (D1-α + D2-a + D5-α) ────────────────────────────────────────────
    // Decomposed variants used by ExecuteTransitionCommandHandler for the single-SaveChanges boundary.
    // Three existing callers (CompleteTask:91, CancelTask:69, ResolveBranchChild:159) retain the
    // original UpdateWithStateGuardAsync (which DRY-composes the two new primitives internally).

    /// <summary>
    /// Stages <see cref="WorkflowInstance"/> changes (history-state fix + entity tracking) WITHOUT
    /// calling <c>SaveChangesAsync</c>. Caller MUST invoke <see cref="SaveChangesAsync"/> afterward
    /// to flush all staged entities atomically in one round-trip.
    /// Used by <c>ExecuteTransitionCommandHandler</c> to defer the save until after all staging
    /// (branch entities, attachments) is complete.
    /// <see cref="DbUpdateConcurrencyException"/> is NOT caught here — it is deferred to the
    /// boundary <see cref="SaveChangesAsync"/> call.
    /// </summary>
    Task<Result> UpdateWithStateGuardNoSaveAsync(
        WorkflowInstance instance,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Flushes all pending changes to the database in a single round-trip.
    /// Translates <see cref="Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException"/> to
    /// <c>Result.Fail(<see cref="AqtCoreFW.Workflow.Domain.Errors.WorkflowErrors.StateConflictAtBoundary()"/>)</c>
    /// so Application layer stays free of EF Core dependency (Application→Infrastructure boundary).
    /// Returns <c>Result.Ok(rowsAffected)</c> on success; the row count is informational only
    /// (callers MUST NOT assume a specific count).
    /// </summary>
    Task<Result<int>> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Detaches all tracked entities from the underlying DbContext's ChangeTracker.
    /// Safe to call after a rollback attempt (success OR fail) to prevent stale Added/Modified
    /// entries from leaking into future operations on the same scoped DbContext (M1 close — Slice 7).
    /// </summary>
    void ClearChanges();

    /// <summary>
    /// Returns the count of active (non-completed) instances per CurrentStateId (long)
    /// for a given source definition (by long internal Id).
    /// Used by ValidateMigrationMapping to detect unmapped states with active instances.
    /// Phase-4b B4c: key changed Guid→long (T1 state Id; T2 definition internal Id).
    /// </summary>
    Task<Dictionary<long, int>> GetActiveInstanceCountByStateAsync(
        long definitionInternalId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns whether any active instance on the source definition has an active
    /// WorkflowInstanceBranchStatus (Status="InProgress").
    /// V1 scope cut: such instances are ineligible for migration.
    /// Phase-4b B4c: definitionPublicId = WorkflowDefinition.PublicId (Guid AK).
    /// </summary>
    Task<bool> HasInstancesWithActiveBranchAsync(
        Guid definitionPublicId,
        CancellationToken cancellationToken = default);

    // ── Slice 6: Ambient-enlist transaction management (Application→Infrastructure boundary) ────────
    // These members keep WorkflowDbContext out of the Application layer. The repository
    // implementation checks both Transaction.Current (MSDTC/ambient TransactionScope) AND
    // db.Database.CurrentTransaction (explicit EF transaction opened earlier in the same scope).
    // The handler calls HasAmbientTransaction() first; only if false does it call BeginTransactionAsync.

    /// <summary>
    /// Returns true if there is an active ambient transaction for the underlying DbContext.
    /// Checks <c>System.Transactions.Transaction.Current</c> (MSDTC/TransactionScope) AND
    /// <c>db.Database.CurrentTransaction</c> (explicit EF transaction). Scout §4 confirmed:
    /// <c>db.Database.CurrentTransaction</c> is null inside a TransactionScope — must check both.
    /// </summary>
    /// <remarks>
    /// Slice 7.1: when this returns true (Mode B), the caller (typically a BP cross-module handler)
    /// owns the ambient <c>TransactionScope</c> and is responsible for the cooperative-rollback
    /// contract — see <c>ExecuteTransitionCommandHandler.Handle</c> remarks for the full caller contract.
    /// </remarks>
    bool HasAmbientTransaction();

    /// <summary>
    /// Begins an explicit database transaction on the underlying DbContext.
    /// Caller MUST commit or rollback via the returned <see cref="IWorkflowTransaction"/>.
    /// </summary>
    Task<IWorkflowTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);
}
