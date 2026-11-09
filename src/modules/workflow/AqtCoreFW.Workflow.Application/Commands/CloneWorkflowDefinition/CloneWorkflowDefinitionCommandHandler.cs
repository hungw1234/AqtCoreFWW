using System.Transactions;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Errors;
using AqtCoreFW.Workflow.Application.Commands.CreateWorkflowDefinition;
using AqtCoreFW.Workflow.Application.DTOs;
using AqtCoreFW.Workflow.Application.Repositories;
using AqtCoreFW.Workflow.Domain.Errors;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Workflow.Application.Commands.CloneWorkflowDefinition;

/// <summary>
/// Clones a definition as a new version. Performs a FULL deep-copy of the definition graph:
/// states + transitions + transition-assignments (pools) + parallel branches (fork + children +
/// per-child pools + resolutions). The state/transition/assignment/branch persistence is delegated
/// to the canonical <see cref="IWorkflowDefinitionRepository.ReplaceGraphAsync"/> (the same path a
/// graph save uses) so the clone cannot drift from the save contract; the only clone-specific work
/// kept here is the <c>AutoTransitionOnTimeoutId</c> remap, which references transition ids that
/// exist only after the clone's transitions persist (ReplaceGraphAsync does not remap it).
///
/// Earlier this handler copied only states + transitions, silently dropping the two satellite
/// families (assignments live in a separate entity keyed by transition id; branches are a separate
/// aggregate keyed by definition id) — producing an un-saveable, un-activatable clone for any pooled
/// or parallel-branch definition.
/// </summary>
public sealed class CloneWorkflowDefinitionCommandHandler(
    IWorkflowDefinitionRepository repository,
    IGuidGenerator gen)
    : IRequestHandler<CloneWorkflowDefinitionCommand, Result<WorkflowDefinitionDto>>
{
    public async Task<Result<WorkflowDefinitionDto>> Handle(
        CloneWorkflowDefinitionCommand request,
        CancellationToken cancellationToken)
    {
        // Load source with states + transitions (tracked). PublicId (Guid) → internal long Id.
        var sourceResult = await repository.GetWithStatesAndTransitionsAsync(request.SourceId, cancellationToken);
        if (sourceResult.IsFailed)
            return Result.Fail(WorkflowErrors.DefinitionNotFound(request.SourceId));
        var source = sourceResult.Value;

        // Second load (long-keyed) supplies the per-transition assignment map; GetForValidationAsync is
        // the only method exposing it. Returns the same tracked def/state/transition instances (identity
        // map) — we keep `source` and take only the assignment dictionary. Admin-rare op; redundancy ok.
        var (_, assignmentByTransitionId) = await repository.GetForValidationAsync(source.Id, cancellationToken);
        // Clone-specific loader: includes branch-child resolutions, which the Activate-validation loader
        // omits — reusing that one here would silently drop resolutions from the clone.
        var sourceBranches = await repository.GetParallelBranchesForCloneAsync(source.Id, cancellationToken);

        // Source state id → stable Name. Every cross-reference (transition FK, branch state refs) is
        // projected onto Names so ReplaceGraphAsync can re-resolve them to the clone's fresh ids.
        var oldStateNameById = source.States.ToDictionary(s => s.Id, s => s.Name);

        // Defense-in-depth: any cross-reference to an unknown state means corrupt source — abort with a
        // typed error rather than let the name projection throw KeyNotFoundException (→ 500). Branch
        // state-refs (branching/continuation/child-target/resolution-result) are bare bigint with no DB
        // FK, the same risk class as transition FKs, so they get the same guard.
        foreach (var t in source.Transitions)
        {
            if (!oldStateNameById.ContainsKey(t.FromStateId) || !oldStateNameById.ContainsKey(t.ToStateId))
                return Result.Fail(new ValidationError(
                    $"Source definition '{request.SourceId}' has a transition with an unresolvable state FK " +
                    $"(FromStateId={t.FromStateId}, ToStateId={t.ToStateId}); clone aborted to avoid propagating corrupt data."));
        }

        bool StateKnown(long id) => oldStateNameById.ContainsKey(id);
        foreach (var b in sourceBranches)
        {
            if (!StateKnown(b.BranchingStateId) || !StateKnown(b.ContinuationStateId)
                || b.Children.Any(c => !StateKnown(c.TargetStateId)
                    || c.Resolutions.Any(r => r.ResultingStateId is { } rid && !StateKnown(rid))))
                return Result.Fail(new ValidationError(
                    $"Source definition '{request.SourceId}' has a parallel branch with an unresolvable state FK; " +
                    "clone aborted to avoid propagating corrupt data."));
        }

        // Project the graph into name-keyed specs BEFORE any persistence — ReplaceGraphAsync detaches
        // all tracked state/transition entries mid-call, so the source graph must be fully read first.
        var transitionSpecs = CloneGraphProjection.BuildTransitionSpecs(
            source.Transitions, oldStateNameById, assignmentByTransitionId);
        var branchSpecs = CloneGraphProjection.BuildBranchSpecs(sourceBranches, oldStateNameById);

        // Capture the auto-timeout wiring by stable keys now (pre-detach): cloneStateName → the source
        // auto-transition's (FromStateName, ActionName). Re-resolved to the clone's transition id post-save.
        var autoTimeoutByStateName = new Dictionary<string, (string FromName, string ActionName)>(StringComparer.Ordinal);
        foreach (var s in source.States)
        {
            if (s.AutoTransitionOnTimeoutId is not { } autoTranId) continue;
            var srcTran = source.Transitions.FirstOrDefault(t => t.Id == autoTranId);
            if (srcTran is null) continue; // dangling ref in a valid source shouldn't occur; skip defensively.
            autoTimeoutByStateName[s.Name] = (oldStateNameById[srcTran.FromStateId], srcTran.ActionName);
        }

        // Atomicity: wrap source-supersede + clone shell + full graph replace + auto-timeout remap in one
        // ambient TransactionScope. ReplaceGraphAsync opens a nested Required scope that joins this ambient,
        // so a failure anywhere rolls back the IsLatest=false flip on the source too.
        using var scope = new TransactionScope(
            TransactionScopeOption.Required,
            new TransactionOptions { IsolationLevel = IsolationLevel.ReadCommitted },
            TransactionScopeAsyncFlowOption.Enabled);

        source.MarkNotLatest(request.CreatedBy);
        await repository.UpdateAsync(source, cancellationToken);

        // Next version must clear every number ever used for this key, including soft-deleted versions —
        // the unique index is not IsDeleted-filtered, so a bare source.Version+1 collides with a deleted
        // sibling of that version and fails the insert (the clone-after-deleted-version 500).
        // Two concurrent clones both read the same MAX and both try MAX+1; the unique index rejects the
        // loser with a 500. Acceptable for an admin-rare op — the index is the correct serializer and no
        // data corrupts; not worth a lock here.
        var nextVersion = await repository.GetMaxVersionByKeyAsync(
            source.DefinitionKey, source.TenantId, cancellationToken) + 1;

        // Clone the metadata shell only (key, nextVersion, IsLatest). Clone() copies states, but the full
        // graph is rebuilt by ReplaceGraphAsync — drop the transient states so the shell persists empty.
        var clone = source.Clone(gen, request.CreatedBy, nextVersion);
        clone.ClearGraph(request.CreatedBy);
        await repository.AddAsync(clone, cancellationToken); // SaveChanges here assigns clone.Id (+ flips source).

        var cloneStates = CloneGraphProjection.BuildCloneStates(clone.Id, source.States);
        await repository.ReplaceGraphAsync(
            clone, cloneStates, transitionSpecs, branchSpecs, request.CreatedBy, cancellationToken);

        await RemapAutoTransitionOnTimeoutAsync(clone, autoTimeoutByStateName, cancellationToken);

        scope.Complete();

        return Result.Ok(CreateWorkflowDefinitionCommandHandler.MapToDto(clone));
    }

    /// <summary>
    /// Repoints each clone state's <c>AutoTransitionOnTimeoutId</c> at the clone's freshly-persisted
    /// transition id, resolved via the stable (FromStateName, ActionName) key captured pre-save.
    /// No-op when the source had no auto-timeout states. ReplaceGraphAsync does not handle this.
    /// </summary>
    private async Task RemapAutoTransitionOnTimeoutAsync(
        Domain.Entities.WorkflowDefinition clone,
        IReadOnlyDictionary<string, (string FromName, string ActionName)> autoTimeoutByStateName,
        CancellationToken cancellationToken)
    {
        if (autoTimeoutByStateName.Count == 0) return;

        var cloneStateNameById = clone.States.ToDictionary(s => s.Id, s => s.Name);
        var cloneStateByName = clone.States.ToDictionary(s => s.Name, s => s, StringComparer.Ordinal);
        var cloneTranIdByKey = clone.Transitions.ToDictionary(
            t => (cloneStateNameById[t.FromStateId], t.ActionName), t => t.Id);

        var dirty = false;
        foreach (var (stateName, key) in autoTimeoutByStateName)
        {
            if (!cloneTranIdByKey.TryGetValue(key, out var cloneTranId)) continue;
            if (!cloneStateByName.TryGetValue(stateName, out var cs)) continue;
            cs.Update(
                cs.Name, cs.DisplayNameVi, cs.DisplayNameEn, cs.IsInitial, cs.IsFinal,
                cs.Color, cs.SortOrder, cs.SlaHours, cloneTranId, cs.PositionX, cs.PositionY);
            dirty = true;
        }

        if (dirty)
            await repository.SaveChangesAsync(cancellationToken);
    }
}
