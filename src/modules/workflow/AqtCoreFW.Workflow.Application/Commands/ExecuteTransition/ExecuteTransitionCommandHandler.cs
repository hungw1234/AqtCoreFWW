using System.Diagnostics;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.SharedKernel.Errors;
using AqtCoreFW.Workflow.Application.DTOs;
using AqtCoreFW.Workflow.Application.Repositories;
using AqtCoreFW.Workflow.Application.Services;
using AqtCoreFW.Workflow.Application.Telemetry;
using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Domain.Enums;
using AqtCoreFW.Workflow.Domain.Errors;
using FluentResults;
using MediatR;
using Microsoft.Extensions.Logging;
// WorkflowErrorCodes lives in SharedKernel.Contracts.Clients (cross-module error code registry)

namespace AqtCoreFW.Workflow.Application.Commands.ExecuteTransition;

public sealed class ExecuteTransitionCommandHandler(
    IWorkflowInstanceRepository instanceRepository,
    IWorkflowDefinitionRepository definitionRepository,
    IConditionEvaluator conditionEvaluator,
    IParallelBranchRepository branchRepository,
    IParallelBranchService branchService,
    ITransitionAttachmentRepository attachmentRepository,
    IIdentityModuleClient identityClient,
    IWorkflowHandlerResolver resolver,
    ILogger<ExecuteTransitionCommandHandler> logger,
    IGuidGenerator gen,
    // Phase 5a: task-creation delegated to IWorkflowTaskFactory (DSEAM-a, DRY — extracted from :316-338).
    IWorkflowTaskFactory taskFactory,
    // Used to complete the source-state task(s) when a transition advances the instance, so they
    // stop surfacing in the inbox for an instance that has already moved on (orphaned-task fix).
    IWorkflowTaskRepository taskRepository,
    // Loads the actor's org-unit codes for transition-authz parity with claim eligibility (so
    // ByOrgUnit/ByRole/ByGroup transitions are executable by their holders, not just ByPermission).
    IOrgUnitModuleClient orgUnitClient)
    : IRequestHandler<ExecuteTransitionCommand, Result<WorkflowInstanceDto>>
{
    /// <summary>
    /// Executes a workflow transition for the specified instance.
    /// </summary>
    /// <remarks>
    /// <para><b>Cooperative-rollback contract (M2 — Slice 7):</b></para>
    /// <para>
    /// When invoked inside an ambient <c>TransactionScope</c> (e.g. the BP runtime path at
    /// <c>ExecuteBusinessProcessTransitionCommandHandler.cs:118-125</c> and
    /// <c>StartBusinessProcessRunCommandHandler.cs:112</c>), this handler does NOT call
    /// <c>scope.Complete()</c> — it enlists into the caller's ambient transaction and returns a
    /// <see cref="FluentResults.Result"/> indicating success or failure.
    /// </para>
    /// <para>
    /// On <c>Result.IsFailed</c>, the <b>caller MUST</b> inspect the result and skip
    /// <c>scope.Complete()</c> so the ambient <c>TransactionScope</c> rolls back automatically
    /// on disposal. Failure to short-circuit will commit a partially-staged workflow state to the DB.
    /// </para>
    /// </remarks>
    /// <seealso cref="IWorkflowTransaction"/>
    /// <seealso cref="IWorkflowInstanceRepository.HasAmbientTransaction"/>
    public async Task<Result<WorkflowInstanceDto>> Handle(
        ExecuteTransitionCommand request,
        CancellationToken cancellationToken)
    {
        // BP MVP P0: emit perf histogram on every exit path (success and fail).
        // Tags are best-effort — tenant/action_name resolve only after the instance + transition load.
        var sw = Stopwatch.StartNew();
        var tenantId = Guid.Empty;
        var actionName = "unknown";
        var result = "fail";
        try
        {
            var handleResult = await HandleCore(
                request,
                cancellationToken,
                (tenant, action) => { tenantId = tenant; actionName = action; });
            result = handleResult.IsSuccess ? "ok" : "fail";
            return handleResult;
        }
        finally
        {
            sw.Stop();
            WorkflowMetrics.ExecuteTransitionDurationMs.Record(
                sw.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("tenant", tenantId),
                new KeyValuePair<string, object?>("action_name", actionName),
                new KeyValuePair<string, object?>("result", result));
        }
    }

    private async Task<Result<WorkflowInstanceDto>> HandleCore(
        ExecuteTransitionCommand request,
        CancellationToken cancellationToken,
        Action<Guid, string> setTags)
    {
        // Load instance with history for transition validation (app-layer state guard below)
        var instanceResult = await instanceRepository.GetWithHistoryAsync(
            request.InstanceId, cancellationToken);
        if (instanceResult.IsFailed)
            return Result.Fail(instanceResult.Errors);

        var instance = instanceResult.Value;

        // Load definition to resolve transition and target state (by internal long Id FK)
        var defResult = await definitionRepository.GetWithStatesAndTransitionsByInternalIdAsync(
            instance.DefinitionId, cancellationToken);
        if (defResult.IsFailed)
            return Result.Fail(defResult.Errors);

        var definition = defResult.Value;

        // Find the requested transition (T1 entity: long Id)
        var transition = definition.Transitions.FirstOrDefault(t => t.Id == request.TransitionId);
        if (transition is null)
            return Result.Fail(new Error($"Transition '{request.TransitionId}' not found in definition.")
                .WithMetadata("WorkflowErrorCode", WorkflowErrorCodes.InvalidTransition));

        setTags(instance.TenantId, transition.ActionName);

        // Validate: transition must be valid from current state (both long)
        if (transition.FromStateId != instance.CurrentStateId)
            return Result.Fail(new Error($"Action '{transition.ActionName}' is not valid from current state.")
                .WithMetadata("WorkflowErrorCode", WorkflowErrorCodes.InvalidTransition));

        // ── Fork-exit guard ──────────────────────────────────────────────────────────────────────
        // While a parallel branch is mid-flight (an InstanceBranchStatus is "InProgress"), the
        // instance is "inside" the fork: its children are resolved through the branch-resolution
        // endpoint, and the continuation transition is applied internally by the join — never through
        // this handler. Any ExecuteTransition on such an instance is an attempt to leave the branching
        // state via an ordinary transition, which would bypass the parallel approval entirely. Reject
        // it fail-CLOSED. One indexed lookup per transition; transitions are human-paced approval acts,
        // so the cost is acceptable for the safety guarantee.
        var inProgressBranch = await branchRepository.GetInstanceBranchStatusAsync(
            instance.PublicId, cancellationToken);
        if (inProgressBranch is not null)
            return Result.Fail(WorkflowErrors.TransitionForbidden(transition.ActionName, "BranchInProgress"));

        // Validate: recall transition — only the instance creator may execute.
        if (transition.IsRecall && request.ActorId != instance.CreatedBy)
            return Result.Fail(WorkflowErrors.TransitionForbidden(
                transition.ActionName, "InstanceCreator"));

        // Resolve target state once; reused for authz gate, ApplyTransition, and branching.
        var targetState = definition.States.First(s => s.Id == transition.ToStateId);

        // ── Slice-5: Authz-of-T gate + WF_NO_HANDLER pre-check (non-recall only) ──────────────────
        // Recall transitions use a separate, pre-existing authz model: only the instance creator may
        // execute (enforced solely by the creator guard at :95-98 above). WorkflowHandlerResolver
        // intentionally excludes IsRecall transitions (WorkflowHandlerResolver.cs:32), so the
        // resolver gate is N/A for recall. Both Slice-5 blocks are skipped for recall paths.

        // Slice 10a DRESOLVE-a: hoist to outer scope so task-creation seam (tx-envelope else-branch)
        // can access these values. nohResolution defaults to Empty for recall + final-state paths.
        HandlerResolution nohResolution = HandlerResolution.Empty;
        // Slice 10a DLIVE-b: override target set when all 3 gates pass; null = use resolver result.
        Guid? chosenHandler = null;

        // Parallel-fork hoist: when the target is a branching state, the fork-entry gate below resolves
        // it once (and each child's handler pool) so the staging block can reuse both without a second
        // DB read. Null branch = ordinary (non-fork) target; pools null = gate not run (recall/final).
        WorkflowParallelBranch? branch = null;
        Dictionary<long, HandlerResolution>? branchChildPools = null;

        if (!transition.IsRecall)
        {
            // Authz-of-T gate (ALWAYS for non-recall — no IsFinal exception per spec line 28):
            // Load outgoing assignments for the CURRENT state (not targetState).
            // A transition T is authorized iff the actor satisfies T's OWN assignment rule.
            var currentState = definition.States.First(s => s.Id == instance.CurrentStateId);
            var currentOutgoing = await definitionRepository.GetOutgoingWithAssignmentsAsync(
                currentState.Id, cancellationToken);

            // Find the single OA for the chosen transition T. Null = no assignment row → fail-CLOSED.
            var tOA = currentOutgoing.FirstOrDefault(o => o.Transition.Id == transition.Id);
            if (tOA is null)
            {
                // Transition exists in definition graph but has no outgoing assignment record — misconfigured.
                logger.LogWarning(
                    "No outgoing assignment found for transition {TransitionId} from state {StateId}; denying (fail-CLOSED).",
                    transition.Id, currentState.Id);
                return Result.Fail(WorkflowErrors.TransitionForbidden(transition.ActionName, "Handler"));
            }

            // Resolve pool for the chosen transition — fail-CLOSED: any exception → 403, never 500.
            HandlerResolution authzResolution;
            try
            {
                authzResolution = await resolver.ResolveAsync(
                    currentState,
                    new List<OutgoingAssignment> { tOA },
                    new WorkflowResolutionContext(instance.TenantId, instance.CreatedBy, instance.Id),
                    cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Resolver threw for transition {TransitionId} from state {StateId}; denying (fail-CLOSED).",
                    transition.Id, currentState.Id);
                return Result.Fail(new ForbiddenError(
                    "Transition not permitted — handler resolution failed."));
            }

            // Actor must satisfy T's resolved pool — match by principal type (permission/role/group/
            // org-unit), in parity with inbox/claim eligibility (not permission-only).
            var actor = await ActorPrincipalsLoader.LoadAsync(
                identityClient, orgUnitClient, request.ActorId, instance.TenantId, cancellationToken);

            if (!ResolverAuthz.IsActorAuthorized(authzResolution, actor, request.ActorId))
                return Result.Fail(WorkflowErrors.TransitionForbidden(transition.ActionName, "Handler"));

            // WF_NO_HANDLER pre-check (only meaningful for non-final targets):
            // Separate second resolver call: ensures targetState has an eligible handler after transition.
            // State-unchanged by return position (atomic-tx = Slice 6, out of scope here).
            if (!targetState.IsFinal)
            {
                // Is the target a parallel-branch fork? Load once here; the staging block below reuses it.
                branch = await branchRepository.GetBranchByStateIdAsync(
                    instance.DefinitionId, targetState.Id, cancellationToken);

                if (branch is not null)
                {
                    // ── Fork-entry gate (branch-aware NOH) ──────────────────────────────────────────────
                    // A fork-only branching state has no outgoing transition, so the ordinary
                    // outgoing-NOH check would wrongly 422 it. Instead, resolve EACH child's handler pool
                    // and require every child to be resolvable. Fail-CLOSED if ANY child has neither a
                    // non-empty pool nor a concrete default assignee: an AND-join could never complete for
                    // an unhandlable child (no one can resolve its task), so we refuse to stage the fork at
                    // all rather than strand the instance in a deadlocked branch.
                    branchChildPools = new Dictionary<long, HandlerResolution>(branch.Children.Count);
                    foreach (var child in branch.Children)
                    {
                        HandlerResolution childPool;
                        try
                        {
                            var clauses = child.Assignment is null
                                ? []
                                : child.Assignment.Clauses
                                    .Select(cl => (cl.Mode, cl.RefsJson))
                                    .ToList();
                            childPool = await resolver.ResolveAssignmentAsync(
                                clauses,
                                child.Assignment?.CombineMode ?? AssignmentCombineMode.Union,
                                new WorkflowResolutionContext(instance.TenantId, instance.CreatedBy, instance.Id),
                                cancellationToken);
                        }
                        catch (Exception ex)
                        {
                            logger.LogWarning(ex,
                                "Resolver threw resolving branch child {ChildId} for fork at state {StateId}; refusing fork (fail-CLOSED).",
                                child.Id, targetState.Id);
                            return Result.Fail(WorkflowErrors.NoHandlerResolved(targetState.Id));
                        }

                        if (childPool.IsEmpty && child.DefaultAssigneeId is null)
                            return Result.Fail(WorkflowErrors.NoHandlerResolved(targetState.Id));

                        branchChildPools[child.Id] = childPool;
                    }

                    // Fork entry authorized. Skip the outgoing-NOH check and the single-next-handler
                    // override gate below — a fork fans out to N pooled children, not one overridable
                    // handler (nohResolution stays Empty; the non-branch staging block is skipped).
                }
                else
                {
                var targetOutgoing = await definitionRepository.GetOutgoingWithAssignmentsAsync(
                    targetState.Id, cancellationToken);

                try
                {
                    nohResolution = await resolver.ResolveAsync(
                        targetState,
                        targetOutgoing,
                        new WorkflowResolutionContext(instance.TenantId, instance.CreatedBy, instance.Id),
                        cancellationToken);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex,
                        "Resolver threw resolving target state {StateId}; returning WF_NO_HANDLER (fail-CLOSED).",
                        targetState.Id);
                    return Result.Fail(WorkflowErrors.NoHandlerResolved(targetState.Id));
                }

                if (nohResolution.IsEmpty)
                    return Result.Fail(WorkflowErrors.NoHandlerResolved(targetState.Id));

                // ── Slice 10a: Override gate (DLIVE-b — 3-gate ordering) ────────────────────────────────
                // Gate (1): skip when no override requested.
                if (request.NextHandlerUserId is not null)
                {
                    // Gate (2): AllowAssignorOverride must be true on this transition's assignment.
                    // DGATE-b: false → silent fallthrough + LogInformation (NOT 403).
                    if (tOA.Assignment?.AllowAssignorOverride != true)
                    {
                        logger.LogInformation(
                            "Override requested but denied by assignment config; falling back to normal resolution. InstanceId={InstanceId} TransitionId={TransitionId} ActorId={ActorId}",
                            instance.PublicId, transition.Id, request.ActorId);
                        // chosenHandler remains null → normal resolution path below
                    }
                    else
                    {
                        // Gate (3): override target must ∈ Pool(targetState).
                        // DPOOL-a: reuse already-loaded nohResolution; match the target's principals
                        // by type (parity with claim eligibility, not permission-only).
                        var overrideActor = await ActorPrincipalsLoader.LoadAsync(
                            identityClient, orgUnitClient, request.NextHandlerUserId.Value,
                            instance.TenantId, cancellationToken);
                        if (!ResolverAuthz.IsActorAuthorized(nohResolution, overrideActor, request.NextHandlerUserId.Value))
                            return Result.Fail(WorkflowErrors.TransitionForbidden(
                                transition.ActionName, "OverrideTarget"));

                        // Gate (4): override target must be live-active (no cache per RT2-4).
                        var activeStatus = await identityClient.GetUserActiveStatusAsync(
                            request.NextHandlerUserId.Value, cancellationToken);
                        if (!activeStatus.IsActive || activeStatus.IsLockedOut)
                            return Result.Fail(WorkflowErrors.TransitionForbidden(
                                transition.ActionName, "OverrideTargetInactive"));

                        // All gates passed — override accepted (DASSIGN-a).
                        chosenHandler = request.NextHandlerUserId.Value;
                    }
                }
                } // end else (ordinary non-fork target)
            }
        }

        // Evaluate declarative conditions (if any)
        if (transition.ConditionsJson is not null)
        {
            var metadata = request.Metadata ?? new Dictionary<string, object?>();
            var conditionResult = conditionEvaluator.Evaluate(transition.ConditionsJson, metadata);
            if (conditionResult.IsFailed)
                return Result.Fail(conditionResult.Errors);
        }

        // ApplyTransition gained IGuidGenerator param (Phase-4a B4)
        instance.ApplyTransition(gen, transition, targetState.IsFinal, request.ActorId, request.Comment);

        // Route FormSubmission approve/reject through the outbox (at-least-once, durable) — replaces the
        // former in-process WorkflowTransitionedFormSubmissionHandler. Raised on the aggregate BEFORE the
        // SaveChanges below so OutboxInterceptor writes the workflow.OutboxMessages row in the same tx.
        if (targetState.IsFinal)
            instance.RaiseFinalizationEvent(targetState.Name, request.ActorId, gen);

        // 7C: Persist file attachments for this transition (if any)
        if (request.FileIds is { Count: > 0 })
        {
            // The last history entry is the one just appended by ApplyTransition
            // T3 entity: use InternalId (long PK) to link attachment
            var historyEntry = instance.History[^1];
            var attachments = request.FileIds.Select(fileId =>
                WorkflowTransitionAttachment.Create(historyEntry.InternalId, fileId, fileId.ToString(), null));
            await attachmentRepository.AddRangeAsync(attachments, cancellationToken);
        }

        // ── Slice 6: Ambient-enlist tx envelope (RT2-1 / RT3-FM-F4) ─────────────────────────────────
        // If a TransactionScope or explicit EF tx is already active (e.g. BP-path at ETR:109),
        // enlist into it — no new transaction opened, caller owns commit/rollback.
        // If no ambient transaction, open one ourselves and commit/rollback here.
        // Shape: try { ... commit } catch { rollback; throw/return-fail } — fail-CLOSED invariant (S5 META-L (e)).
        IWorkflowTransaction? ownsTransaction = null;
        if (!instanceRepository.HasAmbientTransaction())
            ownsTransaction = await instanceRepository.BeginTransactionAsync(cancellationToken);

        try
        {
            // ── Complete the source-state task(s) before advancing ──────────────────────────────────
            // A forward transition is the act of finishing the work item of the state being left.
            // Without this the task stays Open forever and keeps surfacing in the inbox for an instance
            // that has already moved on (it shows as several rows when one user is both the leaving-state
            // assignee and the new-state pool member; otherwise as a stale task in the initiator's inbox).
            //
            // Complete (not Cancel) so WorkflowTask.CompletedAt feeds GetLastCompletedTaskAsync — the
            // producer for the PreviousHandler assignment-clause resolver. CompletedBy = the actor who
            // executed the transition (already authorized for this state's transition by the gate above).
            //
            // Scope assumption: a workflow-driven instance has exactly one open non-branch task at a time
            // (the current state's work item, created by StageFirstOrNextTask / instance-creation). The
            // task model carries no state FK, so "the source-state task" is expressed as "the open
            // non-branch task(s)". If a future feature creates supplementary out-of-band tasks on a live
            // instance (e.g. an ad-hoc POST /workflow/tasks with no flow semantics), it must add a
            // discriminator and exclude itself here, or it will be auto-completed on the next transition.
            //
            // Branch-child tasks (BranchChildStatusId set) are owned by the parallel-branch lifecycle
            // (auto-resolved on their own completion) and are left untouched.
            //
            // Recall is excluded: it returns the instance to a prior state and creates no forward task.
            // KNOWN GAP: recall does NOT currently complete or cancel its source task either, so the
            // recall direction can still orphan a task — a pre-existing condition this forward-only fix
            // deliberately does not change (recall task semantics — complete vs cancel vs reopen — are a
            // separate decision).
            //
            // Runs BEFORE the staging block so the newly-staged next task stays Open, and covers final
            // targets too (the staging block is skipped when targetState.IsFinal). Tracked read +
            // in-place Complete() flush in the SAME SaveChanges as the instance state update and any
            // newly-staged task (S7 single-SaveChanges invariant).
            if (!transition.IsRecall)
            {
                var sourceTasks = await taskRepository.GetOpenNonBranchByInstanceAsync(
                    instance.Id, cancellationToken);
                foreach (var sourceTask in sourceTasks)
                {
                    // Ownership gate: a claimed work item may be advanced only by its assignee.
                    // The actor already passed the assignment-rule check above, but claiming a pooled
                    // task is what reserves it — a reservation must hold against other eligible members,
                    // so an eligible non-assignee is rejected here. RowVersion on the task is the DB
                    // race tiebreak for the auto-claim below (a simultaneous loser surfaces as the
                    // outer-save concurrency conflict). Single authoritative read inside the tx avoids
                    // a read-then-act gap.
                    if (sourceTask.AssigneeId is { } owner && owner != request.ActorId)
                    {
                        if (ownsTransaction is not null)
                            await ownsTransaction.RollbackAsync(cancellationToken);
                        try { instanceRepository.ClearChanges(); }
                        catch (Exception clearEx)
                        {
                            logger.LogWarning(clearEx,
                                "ChangeTracker.Clear failed after ownership-gate reject; scoped DbContext may carry stale entries (instance {InstanceId}).",
                                instance.PublicId);
                        }
                        return Result.Fail(WorkflowErrors.AlreadyClaimed(sourceTask.Id));
                    }

                    // Auto-claim on act: an unclaimed pool task is reserved to the acting (already-
                    // authorized) user before completion, so ownership + PreviousHandler routing reflect
                    // who actually advanced it. Claimed-by-self falls straight through to Complete.
                    if (sourceTask.AssigneeId is null)
                        sourceTask.Claim(request.ActorId, request.ActorId);

                    sourceTask.Complete(request.ActorId);
                }
            }

            // Phase 5: Stage parallel branch children + tasks into change-tracker (NO save yet).
            // Must happen BEFORE UpdateWithStateGuardAsync so all entities are flushed in one SaveChanges.
            // BranchFanOutException is a sentinel thrown below on staging failure — caught separately.
            if (!targetState.IsFinal)
            {
                // branch + branchChildPools were resolved once by the fork-entry gate above. branch is
                // non-null ONLY on the non-recall fork path (the gate runs inside `if (!IsRecall)`), so a
                // recall never forks — it returns the instance to a prior simple state and stages nothing.
                if (!transition.IsRecall && branch is not null)
                {
                    try
                    {
                        // InitiateBranchingAsync stages entities on context — NO internal SaveChanges (P2b).
                        // branchChildPools (set alongside branch by the fork-entry gate) carries each
                        // child's resolved handler pool so the fan-out writes the candidate-index, making
                        // each child task pooled / claimable rather than orphaned.
                        await branchService.InitiateBranchingAsync(
                            instance.PublicId, branch, branchChildPools!, instance.TenantId,
                            request.ActorId, cancellationToken);
                    }
                    catch (Exception stagingEx)
                    {
                        // Wrap in sentinel so the outer catch can distinguish branch-staging failures
                        // (→ GOV_WFL_015 422) from infrastructure failures (→ re-throw 500).
                        throw new BranchFanOutException(instance.PublicId, stagingEx);
                    }
                }
                else if (!transition.IsRecall)
                {
                    // ── Phase 5a: Non-branching task-creation via IWorkflowTaskFactory (DSEAM-a, DRY) ──
                    // nohResolution was computed and hoisted to outer HandleCore scope (DRESOLVE-a).
                    // Recall transitions skip this block — they return the instance to a prior state for the
                    // creator and do not produce a forward-progress task.
                    //
                    // Factory writes two complementary artifacts in the SAME SaveChanges (S7 invariant):
                    //   • task.CandidateRefsJson — stable audit/equality snapshot of the predicate list.
                    //   • WorkflowTaskCandidateIndex rows — queryable read-model consumed by inbox + claim.
                    // Both derive from nohResolution.CandidatePredicates; neither can drift from the other.

                    // Title: action name executed (DTITLE sub-decision recommends transition.ActionName).
                    // Factory stages task in shared WorkflowDbContext ChangeTracker — same SaveChanges boundary (S7).
                    taskFactory.StageFirstOrNextTask(
                        instanceInternalId: instance.Id,
                        title: transition.ActionName,
                        tenantId: instance.TenantId,
                        createdBy: request.ActorId,
                        overrideAssignee: chosenHandler,        // DASSIGN-a: override wins if set by DLIVE-b gate
                        resolution: nohResolution);
                }
            }

            // Slice 7: Defer-save split (D1-α + D2-a).
            // Step 1: Stage WorkflowInstance changes WITHOUT saving (history-state fix + tracking).
            //         All branch entities (staged above) + attachments (7C) remain in change-tracker.
            var stageResult = await instanceRepository.UpdateWithStateGuardNoSaveAsync(
                instance, cancellationToken);
            if (stageResult.IsFailed)
            {
                // Staging failure (non-concurrency) — rollback own tx.
                if (ownsTransaction is not null)
                    await ownsTransaction.RollbackAsync(cancellationToken);
                // M1 close (Slice 7.1): clear ChangeTracker on early-return rollback path too
                // (reviewer Medium-1: original Slice 7 patched only catches, not IsFailed branches).
                try { instanceRepository.ClearChanges(); }
                catch (Exception clearEx)
                {
                    logger.LogWarning(clearEx,
                        "ChangeTracker.Clear failed after stage IsFailed; scoped DbContext may carry stale entries (instance {InstanceId}).",
                        instance.PublicId);
                }
                return Result.Fail(stageResult.Errors);
            }

            // Step 2: Single SaveChanges — persists WorkflowInstance state, history, attachments (7C),
            // and all staged branch entities atomically. DB-layer concurrency token on
            // WorkflowInstance.CurrentStateId catches simultaneous races; translated to
            // Result.Fail(StateConflictAtBoundary) inside the repo — no EF types cross the boundary.
            var saveResult = await instanceRepository.SaveChangesAsync(cancellationToken);
            if (saveResult.IsFailed)
            {
                // Concurrency conflict at outer-save boundary → GOV_WFL_013 409.
                // Rollback own tx — connection is in an error state after DbUpdateConcurrencyException.
                if (ownsTransaction is not null)
                    await ownsTransaction.RollbackAsync(cancellationToken);
                // M1 close (Slice 7.1): the concurrency-fail surface this branch handles is EXACTLY
                // the scenario M1 was designed to seal — stale Added/Modified entries left in tracker
                // after a failed save would re-flush on next scoped DbContext use (BP cross-module path).
                try { instanceRepository.ClearChanges(); }
                catch (Exception clearEx)
                {
                    logger.LogWarning(clearEx,
                        "ChangeTracker.Clear failed after save IsFailed; scoped DbContext may carry stale entries (instance {InstanceId}).",
                        instance.PublicId);
                }
                return Result.Fail(saveResult.Errors);
            }

            // Commit own transaction (no-op when enlisting into ambient scope — caller commits).
            if (ownsTransaction is not null)
                await ownsTransaction.CommitAsync(cancellationToken);

            return Result.Ok(MapToDto(instance));
        }
        catch (BranchFanOutException bfex)
        {
            // Branch staging failed → rollback + GOV_WFL_015 422 (ops-actionable, RT3-FM-F4).
            logger.LogWarning(bfex.InnerException,
                "Branch fan-out staging failed for instance {InstanceId}; rolling back transition (RT3-FM-F4).",
                bfex.InstanceId);
            if (ownsTransaction is not null)
            {
                try { await ownsTransaction.RollbackAsync(cancellationToken); }
                catch (Exception rbEx) { logger.LogError(rbEx, "Rollback failed for instance {InstanceId}.", bfex.InstanceId); }
            }
            // M1 close (D3-a / Slice 7): clear ChangeTracker after rollback attempt (always — success OR fail)
            // to prevent stale Added/Modified entries from leaking into future scoped DbContext reuse.
            try { instanceRepository.ClearChanges(); }
            catch (Exception clearEx)
            {
                logger.LogWarning(clearEx,
                    "ChangeTracker.Clear failed; scoped DbContext may carry stale entries (instance {InstanceId}).",
                    instance.PublicId);
            }
            return Result.Fail(WorkflowErrors.BranchFanOutFailed(instance.PublicId));
        }
        catch (Exception ex)
        {
            // fail-CLOSED: rollback own transaction on unexpected exceptions (CommitAsync throws, etc.).
            // When ownsTransaction is null (ambient scope), the caller's TransactionScope decides rollback.
            if (ownsTransaction is not null)
            {
                logger.LogWarning(ex,
                    "ETR write-phase threw; rolling back own transaction for instance {InstanceId}.",
                    instance.PublicId);
                try { await ownsTransaction.RollbackAsync(cancellationToken); }
                catch (Exception rbEx) { logger.LogError(rbEx, "Rollback failed for instance {InstanceId}.", instance.PublicId); }
            }
            // M1 close (D3-a / Slice 7): clear ChangeTracker after rollback attempt (always — success OR fail).
            // Placed BEFORE throw so cleanup runs even if caller swallows the exception.
            try { instanceRepository.ClearChanges(); }
            catch (Exception clearEx)
            {
                logger.LogWarning(clearEx,
                    "ChangeTracker.Clear failed; scoped DbContext may carry stale entries (instance {InstanceId}).",
                    instance.PublicId);
            }
            throw;
        }
        finally
        {
            ownsTransaction?.Dispose();
        }
    }

    private static WorkflowInstanceDto MapToDto(WorkflowInstance i) => new(
        i.PublicId,       // T2: PublicId as external Id
        i.DefinitionId,   // long internal FK
        i.EntityType,
        i.EntityId,
        i.CurrentStateId, // long internal FK
        i.TenantId,
        i.StartedAt,
        i.CompletedAt,
        i.DueAt,
        i.CurrentStateEnteredAt,
        i.History.Select(h => new WorkflowInstanceHistoryDto
        {
            Id = h.MessageId,           // T3: MessageId as public AK
            FromStateId = h.FromStateId,
            ToStateId = h.ToStateId,
            ActionName = h.ActionName,
            TransitionedBy = h.TransitionedBy,
            Comment = h.Comment,
            TransitionedAt = h.TransitionedAt,
        }).ToList());

    /// <summary>
    /// Sentinel exception for branch fan-out staging failure (Slice 6).
    /// Used to distinguish staging errors (→ GOV_WFL_015 422) from unexpected infrastructure
    /// exceptions (→ re-throw 500) within the outer try/catch envelope.
    /// </summary>
    private sealed class BranchFanOutException(Guid instanceId, Exception inner)
        : Exception($"Branch fan-out staging failed for instance {instanceId}.", inner)
    {
        public Guid InstanceId { get; } = instanceId;
    }
}
