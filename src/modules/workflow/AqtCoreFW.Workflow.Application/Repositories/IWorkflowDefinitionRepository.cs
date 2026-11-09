using AqtCoreFW.SharedKernel.Domain.Repositories;
using AqtCoreFW.Workflow.Application.Services;
using AqtCoreFW.Workflow.Domain.Entities;
using FluentResults;

namespace AqtCoreFW.Workflow.Application.Repositories;

/// <summary>
/// Write-side repository for WorkflowDefinition aggregate.
/// Extends generic CRUD with definition-specific lookup.
/// Phase-4b B4c: T2 entity — IRepository&lt;T, Guid&gt; key is PublicId (external Guid AK).
/// </summary>
public interface IWorkflowDefinitionRepository : IRepository<WorkflowDefinition, Guid>
{
    /// <summary>Returns definition with its States and Transitions collections loaded. publicId = WorkflowDefinition.PublicId.</summary>
    Task<Result<WorkflowDefinition>> GetWithStatesAndTransitionsAsync(
        Guid publicId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns definition with its States and Transitions collections loaded, looked up by internal long Id.
    /// Used by handlers that only have the FK long Id (e.g. WorkflowInstance.DefinitionId).
    /// Phase-4b B4c: needed because WorkflowInstance.DefinitionId is long (FK to T2.Id).
    /// </summary>
    Task<Result<WorkflowDefinition>> GetWithStatesAndTransitionsByInternalIdAsync(
        long internalId,
        CancellationToken cancellationToken = default);

    /// <summary>Returns true if any WorkflowInstance referencing this definition is still active (not completed).</summary>
    Task<bool> HasActiveInstancesAsync(
        Guid definitionPublicId,
        CancellationToken cancellationToken = default);

    /// <summary>Returns the latest version of a definition by key and tenant, with States and Transitions loaded.</summary>
    Task<Result<WorkflowDefinition>> GetLatestByKeyAsync(
        string definitionKey,
        Guid tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Highest version number for a (tenant, key), counting soft-deleted rows. The
    /// (TenantId, DefinitionKey, Version) unique index is not filtered by IsDeleted, so a clone's
    /// next version must clear every number ever used — including deleted ones — or the insert collides.
    /// Returns 0 when the key has no rows at all.
    /// </summary>
    Task<int> GetMaxVersionByKeyAsync(
        string definitionKey,
        Guid tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Highest-version NON-deleted definition for a (tenant, key), tracked for mutation. Used to hand the
    /// IsLatest flag to the surviving newest version when the current latest is deleted. Returns null when
    /// no surviving row remains (the whole key was deleted).
    /// </summary>
    Task<WorkflowDefinition?> GetHighestSurvivingByKeyAsync(
        string definitionKey,
        Guid tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all outgoing transitions from <paramref name="stateId"/> paired with their
    /// assignment rule (null if no rule configured for that transition).
    /// Uses a 2-query + in-memory left-join pattern — transitions query then assignments query,
    /// joined in memory on WorkflowTransitionId. Clauses are eagerly loaded.
    /// Phase-3 Slice-5: feeds the Model B fail-CLOSED resolver gate.
    /// </summary>
    Task<IReadOnlyList<OutgoingAssignment>> GetOutgoingWithAssignmentsAsync(
        long stateId,
        CancellationToken ct = default);

    /// <summary>
    /// Atomically replaces the graph for a definition. Wrapped in a TransactionScope so
    /// the DELETE / state-INSERT / transition-INSERT sequence rolls back on any failure.
    ///
    /// State IDs are IDENTITY-assigned, so the handler cannot pre-build transitions with
    /// correct FromStateId/ToStateId. The repository:
    ///   1) ExecuteSqlRaw DELETE old transitions then states (FK order),
    ///   2) clears stale tracker entries + the aggregate's domain collections,
    ///   3) AddRange newStates → SaveChanges → DB populates state.Id,
    ///   4) builds transitions from <paramref name="transitionSpecs"/> resolving FKs
    ///      via the now-populated state name→Id map,
    ///   5) AddRange transitions → SaveChanges.
    /// </summary>
    Task ReplaceGraphAsync(
        WorkflowDefinition definition,
        List<WorkflowState> newStates,
        IReadOnlyList<NewTransitionSpec> transitionSpecs,
        IReadOnlyList<NewBranchSpec> branchSpecs,
        Guid modifiedBy,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads the full definition graph (States → Transitions → Assignments → Clauses) in
    /// TRACKING mode for the R3 canonicalization writeback path (Phase 5c).
    ///
    /// IMPORTANT: Do NOT use this for read-only callers — use GetOutgoingWithAssignmentsAsync
    /// instead (NoTracking, 3 existing callers in ETR / GetAvailable / ResolveBranchChild).
    ///
    /// Pattern: 2 tracked queries joined in memory (same as GetOutgoingWithAssignmentsAsync
    /// but tracking). WorkflowTransition has no Assignments nav-prop (T1 entity), so the
    /// assignment load is a separate Set&lt;T&gt; query filtered by transition IDs.
    /// Returns null if definition not found.
    /// </summary>
    Task<(WorkflowDefinition? Definition, IReadOnlyDictionary<long, OutgoingAssignment?> AssignmentByTransitionId)>
        GetForValidationAsync(long definitionId, CancellationToken ct = default);

    /// <summary>
    /// Persists tracked changes (R3 canonicalization rewrites) accumulated in the current
    /// DbContext scope. Exposed so callers (client / handler) can save without injecting
    /// the DbContext directly. Only call after GetForValidationAsync on the happy path.
    /// </summary>
    Task<int> SaveChangesAsync(CancellationToken ct = default);

    /// <summary>
    /// Loads the definition's parallel branches with children + per-child assignment + clauses, for
    /// branch-aware Activate validation (R1 exemptions + RB5 pool check). NoTracking — Activate does NOT
    /// canonicalize branch-child clauses (Save already persists them canonical), so no write-back is needed.
    /// Returns an empty list when the definition has no branches. Separate from
    /// <see cref="GetForValidationAsync"/> (which loads the tracked States+Transitions graph for R3
    /// canonicalization writeback) to keep that method's contract — and its callers' mocks — unchanged.
    /// </summary>
    Task<IReadOnlyList<WorkflowParallelBranch>> GetParallelBranchesForValidationAsync(
        long definitionId, CancellationToken ct = default);

    /// <summary>
    /// Loads the definition's parallel branches with the FULL sub-graph — children + per-child assignment +
    /// clauses AND resolutions — for a deep-copy clone. Distinct from
    /// <see cref="GetParallelBranchesForValidationAsync"/>, which omits resolutions (Activate validation
    /// reads pools only); reusing that for clone would silently drop branch-child resolutions.
    /// NoTracking — the clone projects these into name-keyed specs and re-inserts via ReplaceGraphAsync.
    /// </summary>
    Task<IReadOnlyList<WorkflowParallelBranch>> GetParallelBranchesForCloneAsync(
        long definitionId, CancellationToken ct = default);
}
