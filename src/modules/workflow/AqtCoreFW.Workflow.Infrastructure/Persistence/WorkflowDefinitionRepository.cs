using System.Transactions;
using AqtCoreFW.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.Workflow.Application.Repositories;
using AqtCoreFW.Workflow.Application.Services;
using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Domain.Errors;
using FluentResults;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Workflow.Infrastructure.Persistence;

/// <summary>
/// EF Core implementation of IWorkflowDefinitionRepository.
/// T2 entity: IRepository key is Guid (PublicId AK). GetWithStatesAndTransitionsAsync uses PublicId.
/// Phase-4b B4c: GetWithStatesAndTransitionsByInternalIdAsync added — lookup by long internal Id
///   for handlers that only have the FK (e.g. WorkflowInstance.DefinitionId = long).
/// </summary>
public sealed class WorkflowDefinitionRepository(
    WorkflowDbContext context,
    ICrossModuleUnitOfWork? uow = null)
    : GenericRepository<WorkflowDefinition, Guid>(context, uow), IWorkflowDefinitionRepository
{
    private WorkflowDbContext Ctx => (WorkflowDbContext)Context;

    // This entity's EF primary key is the long Id; PublicId is an alternate key. The generic
    // repository resolves by EF.Find([id]), which always targets the PRIMARY KEY — so feeding it the
    // Guid TKey (PublicId) throws "key value ... was of type 'Guid' ... does not match 'long'".
    // Override the two Find-based methods to resolve by PublicId, matching every other lookup here.

    /// <inheritdoc/>
    public override async Task<Result<WorkflowDefinition>> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        await EnsureEnlistedAsync(cancellationToken);
        var definition = await Ctx.TrackingQuery<WorkflowDefinition>()
            .FirstOrDefaultAsync(d => d.PublicId == id, cancellationToken);

        return definition is null
            ? Result.Fail(WorkflowErrors.DefinitionNotFound(id))
            : Result.Ok(definition);
    }

    /// <inheritdoc/>
    public override async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await EnsureEnlistedAsync(cancellationToken);
        var definition = await Ctx.TrackingQuery<WorkflowDefinition>()
            .FirstOrDefaultAsync(d => d.PublicId == id, cancellationToken);
        if (definition is null) return;

        // SoftDeleteInterceptor converts EntityState.Deleted → IsDeleted = true (mirrors base DeleteAsync).
        Ctx.Set<WorkflowDefinition>().Remove(definition);
        await Ctx.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Result<WorkflowDefinition>> GetWithStatesAndTransitionsAsync(
        Guid publicId,
        CancellationToken cancellationToken = default)
    {
        await EnsureEnlistedAsync(cancellationToken);
        var definition = await Ctx.TrackingQuery<WorkflowDefinition>()
            .Include(d => d.States)
            .Include(d => d.Transitions)
            .AsSplitQuery()
            .FirstOrDefaultAsync(d => d.PublicId == publicId, cancellationToken);

        return definition is null
            ? Result.Fail(WorkflowErrors.DefinitionNotFound(publicId))
            : Result.Ok(definition);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Lookup by internal long Id (bigint PK). Used by handlers that have only the FK long value
    /// (e.g. WorkflowInstance.DefinitionId) and need the full definition graph.
    /// </remarks>
    public async Task<Result<WorkflowDefinition>> GetWithStatesAndTransitionsByInternalIdAsync(
        long internalId,
        CancellationToken cancellationToken = default)
    {
        await EnsureEnlistedAsync(cancellationToken);
        var definition = await Ctx.TrackingQuery<WorkflowDefinition>()
            .Include(d => d.States)
            .Include(d => d.Transitions)
            .AsSplitQuery()
            .FirstOrDefaultAsync(d => d.Id == internalId, cancellationToken);

        return definition is null
            ? Result.Fail(new Error($"WorkflowDefinition with internal Id '{internalId}' not found."))
            : Result.Ok(definition);
    }

    /// <inheritdoc/>
    public async Task<bool> HasActiveInstancesAsync(
        Guid definitionPublicId,
        CancellationToken cancellationToken = default)
    {
        // Resolve PublicId → internal Id (T2 entity pattern) then check instances by long FK
        var internalId = await Ctx.Query<WorkflowDefinition>()
            .Where(d => d.PublicId == definitionPublicId)
            .Select(d => (long?)d.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (internalId is null) return false;

        return await Ctx.Query<WorkflowInstance>()
            .AnyAsync(i => i.DefinitionId == internalId.Value && i.CompletedAt == null, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Result<WorkflowDefinition>> GetLatestByKeyAsync(
        string definitionKey,
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        await EnsureEnlistedAsync(cancellationToken);
        // Prefer tenant-specific definition; fall back to global template (TenantId=Guid.Empty)
        // per InProcessWorkflowModuleClient.CreateInstanceByDefinitionKeyAsync documented behavior.
        // Tenants may override globally-seeded definitions (e.g., CaseProcess) by inserting their own row.
        var definition = await Ctx.TrackingQuery<WorkflowDefinition>()
            .Include(d => d.States)
            .Include(d => d.Transitions)
            .AsSplitQuery()
            .FirstOrDefaultAsync(
                d => d.DefinitionKey == definitionKey
                  && d.TenantId == tenantId
                  && d.IsLatest,
                cancellationToken);

        if (definition is null && tenantId != Guid.Empty)
        {
            definition = await Ctx.TrackingQuery<WorkflowDefinition>()
                .Include(d => d.States)
                .Include(d => d.Transitions)
                .AsSplitQuery()
                .FirstOrDefaultAsync(
                    d => d.DefinitionKey == definitionKey
                      && d.TenantId == Guid.Empty
                      && d.IsLatest,
                    cancellationToken);
        }

        return definition is null
            ? Result.Fail(new Error($"No latest WorkflowDefinition found for key '{definitionKey}' in tenant '{tenantId}' (and no global template)."))
            : Result.Ok(definition);
    }

    /// <inheritdoc/>
    public async Task<int> GetMaxVersionByKeyAsync(
        string definitionKey,
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        await EnsureEnlistedAsync(cancellationToken);
        // IgnoreQueryFilters bypasses the soft-delete filter on purpose: a soft-deleted version still
        // occupies its number in the unfiltered (TenantId, DefinitionKey, Version) unique index, so the
        // next version must clear deleted rows too. Still scoped to the explicit tenant — no cross-tenant
        // leak. MaxAsync over an int column with no rows throws, so project to int? and coalesce to 0.
        var max = await Ctx.Set<WorkflowDefinition>()
            .IgnoreQueryFilters()
            .Where(d => d.DefinitionKey == definitionKey && d.TenantId == tenantId)
            .Select(d => (int?)d.Version)
            .MaxAsync(cancellationToken);

        return max ?? 0;
    }

    /// <inheritdoc/>
    public async Task<WorkflowDefinition?> GetHighestSurvivingByKeyAsync(
        string definitionKey,
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        await EnsureEnlistedAsync(cancellationToken);
        // Default filter applies (excludes soft-deleted) — only a surviving row may reclaim IsLatest.
        // Tracked so the caller can flip the flag and persist.
        return await Ctx.TrackingQuery<WorkflowDefinition>()
            .Where(d => d.DefinitionKey == definitionKey && d.TenantId == tenantId)
            .OrderByDescending(d => d.Version)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// 2-query + in-memory left-join pattern:
    ///   1) fetch outgoing <see cref="WorkflowTransition"/> rows for <paramref name="stateId"/>,
    ///   2) fetch <see cref="WorkflowTransitionAssignment"/> rows (with Clauses) for those transition IDs,
    ///   3) left-join in memory — assignment is null if no rule was configured for the transition.
    /// Clauses are eagerly loaded so the resolver does not hit the DB on field access
    /// (P1 field-access config has no lazy proxy, an un-Included Clauses is silently empty).
    /// </remarks>
    public async Task<IReadOnlyList<OutgoingAssignment>> GetOutgoingWithAssignmentsAsync(
        long stateId,
        CancellationToken ct = default)
    {
        await EnsureEnlistedAsync(ct);
        var trans = await Ctx.Query<WorkflowTransition>()
            .Where(t => t.FromStateId == stateId)
            .ToListAsync(ct);

        if (trans.Count == 0) return Array.Empty<OutgoingAssignment>();

        var tids = trans.Select(t => t.Id).ToList();

        var asgs = await Ctx.Query<WorkflowTransitionAssignment>()
            .Include(a => a.Clauses)
            .Where(a => tids.Contains(a.WorkflowTransitionId))
            .ToListAsync(ct);

        // O(1) lookup — a transition has at most one assignment rule in Model B
        var byTid = asgs.ToDictionary(a => a.WorkflowTransitionId);

        return trans
            .Select(t => new OutgoingAssignment(
                t,
                byTid.TryGetValue(t.Id, out var a) ? a : null))
            .ToList();
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Tracking load for R3 canonicalization writeback (Phase 5c F1).
    /// Uses <c>TrackingQuery&lt;T&gt;</c> (= <c>Set&lt;T&gt;()</c>) for both queries so EF change-tracker
    /// watches the Clause entities; a subsequent SaveChangesAsync persists any RefsJson mutations.
    /// WorkflowTransition has no Assignments nav-prop (T1 entity, no reverse-collection) — mirrors
    /// the 2-query + in-memory join of GetOutgoingWithAssignmentsAsync but in tracking mode.
    /// </remarks>
    public async Task<(WorkflowDefinition? Definition, IReadOnlyDictionary<long, OutgoingAssignment?> AssignmentByTransitionId)>
        GetForValidationAsync(long definitionId, CancellationToken ct = default)
    {
        await EnsureEnlistedAsync(ct);
        // Query 1: definition + States + Transitions (tracked).
        var definition = await Ctx.TrackingQuery<WorkflowDefinition>()
            .Include(d => d.States)
            .Include(d => d.Transitions)
            .AsSplitQuery()
            .FirstOrDefaultAsync(d => d.Id == definitionId, ct);

        if (definition is null)
            return (null, new Dictionary<long, OutgoingAssignment?>());

        // Query 2: assignments + clauses for all transitions of this definition (tracked).
        // Filter by DefinitionId join via transition IDs to stay within one definition's scope.
        var transitionIds = definition.Transitions.Select(t => t.Id).ToList();

        Dictionary<long, OutgoingAssignment?> lookup = new();
        if (transitionIds.Count > 0)
        {
            var asgs = await Ctx.TrackingQuery<WorkflowTransitionAssignment>()
                .Include(a => a.Clauses)
                .Where(a => transitionIds.Contains(a.WorkflowTransitionId))
                .ToListAsync(ct);

            var byTid = asgs.ToDictionary(a => a.WorkflowTransitionId);

            foreach (var t in definition.Transitions)
            {
                lookup[t.Id] = new OutgoingAssignment(
                    t,
                    byTid.TryGetValue(t.Id, out var a) ? a : null);
            }
        }

        return (definition, lookup);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<WorkflowParallelBranch>> GetParallelBranchesForValidationAsync(
        long definitionId, CancellationToken ct = default)
    {
        await EnsureEnlistedAsync(ct);
        // NoTracking — Activate validation only READS branch pools (no canonicalization write-back).
        // AsSplitQuery avoids a cartesian blow-up across the two nested collections (Children + Clauses).
        return await Ctx.Query<WorkflowParallelBranch>()
            .Include(b => b.Children).ThenInclude(c => c.Assignment!).ThenInclude(a => a.Clauses)
            .Where(b => b.DefinitionId == definitionId)
            .AsSplitQuery()
            .ToListAsync(ct);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<WorkflowParallelBranch>> GetParallelBranchesForCloneAsync(
        long definitionId, CancellationToken ct = default)
    {
        await EnsureEnlistedAsync(ct);
        // Clone needs the FULL branch sub-graph, including resolutions — which Activate validation does not
        // load (it reads pools only). Omitting resolutions here would silently drop them from the clone.
        // NoTracking: the clone projects these into name-keyed specs and re-inserts via ReplaceGraphAsync.
        return await Ctx.Query<WorkflowParallelBranch>()
            .Include(b => b.Children).ThenInclude(c => c.Assignment!).ThenInclude(a => a.Clauses)
            .Include(b => b.Children).ThenInclude(c => c.Resolutions)
            .Where(b => b.DefinitionId == definitionId)
            .AsSplitQuery()
            .ToListAsync(ct);
    }

    /// <inheritdoc/>
    public async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        await EnsureEnlistedAsync(ct);
        return await Ctx.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Three-phase atomic graph replace (plan 260523-1241 Phase 04):
    ///   Phase 1) raw-SQL DELETE old assignments (orphan-safe — no FK to WorkflowTransitions),
    ///            raw-SQL DELETE old transitions then states (FK order),
    ///            detach now-orphaned tracker entries + clear aggregate's collections,
    ///            AddRange newStates → SaveChanges → SQL Server populates IDENTITY state.Id.
    ///   Phase 2) build transitions from <paramref name="transitionSpecs"/> resolving FKs via
    ///            the post-save name→Id map, AddRange transitions → SaveChanges →
    ///            SQL Server populates IDENTITY transition.Id (5b-A invariant).
    ///   Phase 3) foreach spec WHERE Assignment != null: lookup transition.Id by
    ///            (FromStateName, ActionName) → WorkflowTransitionAssignment.Create(transitionId, …)
    ///            → AddClause per spec clause → Ctx.Set&lt;&gt;().Add → SaveChanges.
    ///            spec.Assignment == null = Q2 replace-all delete (pre-flight DELETE above).
    ///
    /// Wrapped in a TransactionScope (TransactionScopeAsyncFlowOption.Enabled — required for
    /// async/await re-enlistment) so a failure in any phase rolls back all three phases.
    /// Mirrors the proven two-phase pattern in CreateWorkflowDefinitionCommandHandler.
    /// </summary>
    public async Task ReplaceGraphAsync(
        WorkflowDefinition definition,
        List<WorkflowState> newStates,
        IReadOnlyList<NewTransitionSpec> transitionSpecs,
        IReadOnlyList<NewBranchSpec> branchSpecs,
        Guid modifiedBy,
        CancellationToken cancellationToken = default)
    {
        var defId = definition.Id;
        var db = Ctx.Database;

        using var scope = new TransactionScope(
            TransactionScopeOption.Required,
            new TransactionOptions { IsolationLevel = IsolationLevel.ReadCommitted },
            TransactionScopeAsyncFlowOption.Enabled);

        // Phase 04 (plan 260523-1241) — pre-flight: clear orphan assignments BEFORE Phase 1's
        // transition DELETE. WorkflowTransitionAssignments has NO FK to WorkflowTransitions
        // (migration 20260519015732 confirmed: only unique index UX_…_TransitionId), so cascade
        // does NOT fire. Explicit DELETE required to implement Q2 replace-all semantics.
        // Clauses cascade-delete automatically via FK_WorkflowTransitionAssignmentClauses_…_AssignmentId.
        await db.ExecuteSqlRawAsync(
            @"DELETE FROM workflow.WorkflowTransitionAssignments
              WHERE WorkflowTransitionId IN (
                  SELECT Id FROM workflow.WorkflowTransitions WHERE DefinitionId = {0}
              )",
            new object[] { defId },
            cancellationToken);

        await db.ExecuteSqlRawAsync(
            "DELETE FROM workflow.WorkflowTransitions WHERE DefinitionId = {0}",
            new object[] { defId },
            cancellationToken);

        // Replace-all for parallel branches. Branch state-FKs are bare bigint (no FK to WorkflowStates),
        // so a state DELETE does NOT cascade branches — an explicit DELETE is required (same reason the
        // transitions/assignments are cleared manually). Children + resolutions DO cascade off the branch
        // via FK_WorkflowBranchChildren_…_ParallelBranchId / FK_WorkflowBranchResolutions_…_BranchChildId.
        // Deleting before the state DELETE keeps branch rows from briefly dangling on now-deleted state ids.
        await db.ExecuteSqlRawAsync(
            "DELETE FROM workflow.WorkflowParallelBranches WHERE DefinitionId = {0}",
            new object[] { defId },
            cancellationToken);

        await db.ExecuteSqlRawAsync(
            "DELETE FROM workflow.WorkflowStates WHERE DefinitionId = {0}",
            new object[] { defId },
            cancellationToken);

        // The original definition was loaded via Include() → tracker holds the now-deleted rows.
        // Detach them so SaveChanges does not try to re-issue UPDATE/DELETE against ghosts
        // (this is the DbUpdateConcurrencyException scenario the previous all-raw-SQL path
        // tried to avoid).
        foreach (var entry in Ctx.ChangeTracker.Entries<WorkflowState>().ToList())
            entry.State = EntityState.Detached;
        foreach (var entry in Ctx.ChangeTracker.Entries<WorkflowTransition>().ToList())
            entry.State = EntityState.Detached;

        definition.ClearGraph(modifiedBy);

        foreach (var s in newStates)
            definition.AddState(s);

        await Ctx.SaveChangesAsync(cancellationToken);

        var stateIdByName = definition.States
            .ToDictionary(s => s.Name, s => s.Id, StringComparer.Ordinal);

        foreach (var spec in transitionSpecs)
        {
            // Handler validates name presence before passing specs; defensive TryGetValue
            // mirrors CreateWorkflowDefinitionCommandHandler so a contract drift surfaces
            // as a typed error instead of KeyNotFoundException → unhandled 500.
            if (!stateIdByName.TryGetValue(spec.FromStateName, out var fromId))
                throw new InvalidOperationException(
                    $"State '{spec.FromStateName}' missing from saved graph (handler/validator drift).");
            if (!stateIdByName.TryGetValue(spec.ToStateName, out var toId))
                throw new InvalidOperationException(
                    $"State '{spec.ToStateName}' missing from saved graph (handler/validator drift).");

            var transition = WorkflowTransition.Create(
                defId, fromId, toId,
                spec.ActionName, spec.ActionLabelVi, spec.ActionLabelEn,
                spec.ConditionsJson, spec.SortOrder, spec.IsRecall,
                spec.SourceHandle, spec.TargetHandle);

            definition.AddTransition(transition);
        }

        await Ctx.SaveChangesAsync(cancellationToken);

        // Phase 04 (plan 260523-1241) — Phase 3: persist Assignment + Clause payloads.
        // Transition IDENTITY values are now populated by Phase 2 SaveChanges above (5b-A invariant).
        // Build (FromStateName, ActionName) → transition map from definition.Transitions post-save.
        // Pre-flight DELETE above cleared orphan rows; this loop adds the new replacements (Q2).
        var transitionByKey = definition.Transitions
            .ToDictionary(t => (t.FromStateId, t.ActionName), t => t.Id);

        foreach (var spec in transitionSpecs)
        {
            if (spec.Assignment is null) continue;

            // Resolve saved transition Id via state name → Id (already built in Phase 2) + ActionName.
            if (!stateIdByName.TryGetValue(spec.FromStateName, out var fromStateId))
                throw new InvalidOperationException(
                    $"State '{spec.FromStateName}' missing from saved graph (Phase 3 drift).");
            if (!transitionByKey.TryGetValue((fromStateId, spec.ActionName), out var transitionId))
                throw new InvalidOperationException(
                    $"Transition '{spec.ActionName}' from '{spec.FromStateName}' missing post-Phase-2 (Phase 3 drift).");

            var assignment = WorkflowTransitionAssignment.Create(
                transitionId,
                spec.Assignment.CombineMode,
                spec.Assignment.AllowAssignorOverride);

            foreach (var clause in spec.Assignment.Clauses)
                assignment.AddClause(clause.Mode, clause.RefsJson);

            Ctx.Set<WorkflowTransitionAssignment>().Add(assignment);
        }

        await Ctx.SaveChangesAsync(cancellationToken);

        // Parallel branches — name-keyed insert AFTER state IDENTITY is populated. Inserted inline
        // (not via ParallelBranchRepository) for symmetry with the transition/assignment persistence
        // above: same TransactionScope, same name→Id resolution via stateIdByName (built post
        // state-SaveChanges). Branches need state ids but NOT transition ids, so they slot in after
        // the transition insert. Children/resolutions are built
        // through the aggregate navigation (AddChild / AddResolution) so EF assigns their FKs after the
        // branch IDENTITY is populated on insert — pass placeholder 0 to the child/resolution factories.
        foreach (var branchSpec in branchSpecs)
        {
            var branchingId = ResolveStateId(stateIdByName, branchSpec.BranchingStateName, "branch branching");
            var continuationId = ResolveStateId(stateIdByName, branchSpec.ContinuationStateName, "branch continuation");

            var branch = WorkflowParallelBranch.Create(
                defId, branchingId, continuationId,
                branchSpec.BranchType, branchSpec.RequiredCount,
                branchSpec.AllowCancelOnAnyReject, branchSpec.TimeoutMinutes,
                branchSpec.PositionX, branchSpec.PositionY);

            foreach (var childSpec in branchSpec.Children)
            {
                var targetId = ResolveStateId(stateIdByName, childSpec.TargetStateName, "branch child target");
                var child = WorkflowBranchChild.Create(
                    parallelBranchId: 0, targetId, childSpec.Name, childSpec.DisplayNameVi,
                    childSpec.DepartmentId, childSpec.SortOrder, childSpec.DefaultAssigneeId);

                foreach (var resSpec in childSpec.Resolutions)
                {
                    long? resultingId = resSpec.ResultingStateName is null
                        ? null
                        : ResolveStateId(stateIdByName, resSpec.ResultingStateName, "branch resolution resulting");
                    child.AddResolution(WorkflowBranchResolution.Create(
                        branchChildId: 0, resSpec.ResolutionType, resultingId));
                }

                // Per-child handler-assignment rule (pool). Built through the aggregate nav so EF assigns
                // BranchChildId after the child IDENTITY is populated on insert (placeholder 0 — same
                // pattern as resolutions above). Null spec = no rule (replace-all already cleared old rows
                // via the branch DELETE cascade).
                if (childSpec.Assignment is { } asgSpec)
                {
                    var assignment = WorkflowBranchChildAssignment.Create(
                        branchChildId: 0, asgSpec.CombineMode, asgSpec.AllowAssignorOverride);
                    foreach (var clause in asgSpec.Clauses)
                        assignment.AddClause(clause.Mode, clause.RefsJson);
                    child.SetAssignment(assignment);
                }

                branch.AddChild(child);
            }

            Ctx.Set<WorkflowParallelBranch>().Add(branch);
        }

        await Ctx.SaveChangesAsync(cancellationToken);

        scope.Complete();
    }

    // Resolves a state name → its freshly-assigned long Id. A miss means handler/validator drift
    // (names are guarded pre-persist) — surface a typed error instead of a KeyNotFoundException 500.
    private static long ResolveStateId(
        IReadOnlyDictionary<string, long> stateIdByName, string stateName, string context)
    {
        if (!stateIdByName.TryGetValue(stateName, out var id))
            throw new InvalidOperationException(
                $"State '{stateName}' missing from saved graph ({context} resolution drift).");
        return id;
    }
}
