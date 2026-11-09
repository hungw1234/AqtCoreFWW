using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.Workflow.Application.Repositories;
using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Domain.Errors;
using FluentResults;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Workflow.Application.Services;

/// <inheritdoc/>
public sealed class BranchJoinCompletionService(
    IWorkflowDefinitionRepository definitionRepository,
    IWorkflowInstanceRepository instanceRepository,
    IParallelBranchRepository branchRepository,
    IWorkflowHandlerResolver resolver,
    IWorkflowTaskFactory taskFactory,
    IGuidGenerator gen,
    ILogger<BranchJoinCompletionService> logger)
    : IBranchJoinCompletionService
{
    /// <inheritdoc/>
    /// <remarks>
    /// <para><b>Precondition:</b> the caller must have no other unflushed pending changes in the scoped
    /// DbContext when invoking this — the rollback path clears the WHOLE change-tracker, so anything
    /// staged-but-unsaved by the caller would be silently dropped. Today's three callers all flush their
    /// prior work first (the task save in CompleteTask/CancelTask; the child-resolution save in
    /// ResolveBranchChildAsync), so only this method's own entities are tracked at entry.</para>
    /// <para><b>Known interaction:</b> the WorkflowInstance concurrency token is on CurrentStateId only,
    /// so a concurrent instance-level SLA write (MarkSlaBreachNotified — does not change CurrentStateId)
    /// does not conflict with this join's full-row write and can be reverted by it; harmless (the next SLA
    /// cycle re-detects). A durable fix would need a row-version on the instance, deliberately avoided.</para>
    /// </remarks>
    public async Task<Result> CompleteJoinAsync(
        WorkflowInstance instance,
        long continuationStateId,
        Guid actorId,
        CancellationToken ct)
    {
        // ── Continuation pre-check (read-only, BEFORE the atomic window) ────────────────────────────
        // Pulling the deterministic failure (a handler-less continuation = config error) out of the
        // transaction means the only failures that can occur inside the atomic block are transient
        // concurrency conflicts. Fail-CLOSED on ANY failure: def-load, missing continuation, resolver throw.
        var defResult = await definitionRepository.GetWithStatesAndTransitionsByInternalIdAsync(
            instance.DefinitionId, ct);
        if (defResult.IsFailed)
        {
            logger.LogWarning(
                "Definition load failed for DefinitionId={DefinitionId}; blocking continuation (fail-CLOSED).",
                instance.DefinitionId);
            return Result.Fail(WorkflowErrors.NoHandlerResolved(continuationStateId));
        }

        var continuationState = defResult.Value.States.FirstOrDefault(s => s.Id == continuationStateId);
        if (continuationState is null)
        {
            logger.LogWarning(
                "Continuation state {StateId} not found in definition {DefinitionId}; blocking (fail-CLOSED).",
                continuationStateId, instance.DefinitionId);
            return Result.Fail(WorkflowErrors.NoHandlerResolved(continuationStateId));
        }

        // Resolve the continuation pool ONCE — reused both for the fail-CLOSED gate and (when non-final)
        // for staging the continuation work item (RT3) so the predicate snapshot can never drift.
        HandlerResolution? continuationResolution = null;
        if (!continuationState.IsFinal)
        {
            var outgoing = await definitionRepository.GetOutgoingWithAssignmentsAsync(
                continuationState.Id, ct);
            try
            {
                continuationResolution = await resolver.ResolveAsync(
                    continuationState,
                    outgoing,
                    new WorkflowResolutionContext(instance.TenantId, instance.CreatedBy, instance.Id),
                    ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Resolver threw for continuation state {StateId}; blocking (fail-CLOSED).",
                    continuationStateId);
                return Result.Fail(WorkflowErrors.NoHandlerResolved(continuationStateId));
            }

            if (continuationResolution.IsEmpty)
                return Result.Fail(WorkflowErrors.NoHandlerResolved(continuationStateId));
        }

        // ── Winner-election (step 1) ────────────────────────────────────────────────────────────────
        // Reload the still-in-progress branch status. Null ⇒ another thread already completed the join
        // (the row is now Resolved/TimedOut) ⇒ this caller is a clean no-op (NOT an error — otherwise the
        // losing sibling's task-complete would spuriously fail).
        var branchStatus = await branchRepository.GetInstanceBranchStatusAsync(instance.PublicId, ct);
        if (branchStatus is null)
            return Result.Ok();

        // ── Atomic join-completion ──────────────────────────────────────────────────────────────────
        // MarkResolved + continuation advance + continuation task all flush in ONE SaveChanges, enlisting
        // into any ambient (BP) transaction. The branch status' RowVersion is the concurrency token: if a
        // sibling wins the race, our SaveChanges throws and we resolve to a no-op rather than stranding.
        IWorkflowTransaction? ownsTransaction = null;
        if (!instanceRepository.HasAmbientTransaction())
            ownsTransaction = await instanceRepository.BeginTransactionAsync(ct);

        try
        {
            branchStatus.MarkResolved();
            instance.ApplyParallelJoinTransition(gen, continuationStateId, continuationState.IsFinal, actorId);

            // A final continuation completes the run — mirror the direct-transition path
            // (ExecuteTransitionCommandHandler / SlaBreachCheckerJob): route the FormSubmission
            // approve/reject through the outbox so a parallel-join-to-final no longer strands the
            // linked submission in Pending. No-op for non-FormSubmission entities (guarded inside
            // RaiseFinalizationEvent). Captured by OutboxInterceptor in the SaveChanges below, atomic
            // with the transition write. ApplyParallelJoinTransition already stamped CompletedAt.
            if (continuationState.IsFinal)
                instance.RaiseFinalizationEvent(continuationState.Name, actorId, gen);

            // RT3: stage the continuation work item so something lands in the inbox to advance the
            // instance after the join (mirrors ExecuteTransition's StageFirstOrNextTask). A final
            // continuation has no resolution and stages no task — the instance has reached its end.
            if (continuationResolution is not null)
                taskFactory.StageFirstOrNextTask(
                    instanceInternalId: instance.Id,
                    title: continuationState.Name,
                    tenantId: instance.TenantId,
                    createdBy: actorId,
                    overrideAssignee: null,
                    resolution: continuationResolution);

            var staged = await instanceRepository.UpdateWithStateGuardNoSaveAsync(instance, ct);
            if (staged.IsFailed)
            {
                await RollbackAsync(ownsTransaction, instance.PublicId, ct);
                return Result.Fail(staged.Errors);
            }

            var saved = await instanceRepository.SaveChangesAsync(ct);
            if (saved.IsFailed)
            {
                await RollbackAsync(ownsTransaction, instance.PublicId, ct);

                // Winner-election (step 2): a concurrent sibling beat us to MarkResolved (its SaveChanges
                // bumped the branch RowVersion). Re-read: if the branch is no longer in progress, the join
                // was completed by the winner ⇒ clean no-op. Otherwise it is a genuine transient conflict
                // — surface it so the caller loud-logs and an async reconciler can finish the join.
                var stillInProgress = await branchRepository.GetInstanceBranchStatusAsync(
                    instance.PublicId, ct);
                if (stillInProgress is null)
                    return Result.Ok();

                return Result.Fail(saved.Errors);
            }

            if (ownsTransaction is not null)
                await ownsTransaction.CommitAsync(ct);

            return Result.Ok();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Join-completion write threw for instance {InstanceId}; rolling back (fail-CLOSED).",
                instance.PublicId);
            await RollbackAsync(ownsTransaction, instance.PublicId, ct);
            throw;
        }
        finally
        {
            ownsTransaction?.Dispose();
        }
    }

    // Rollback the owned transaction (no-op when enlisted in an ambient scope — the caller owns it) and
    // clear the change-tracker so stale Added/Modified entries never leak into later scoped-context reuse.
    private async Task RollbackAsync(IWorkflowTransaction? ownsTransaction, Guid instancePublicId, CancellationToken ct)
    {
        if (ownsTransaction is not null)
        {
            try { await ownsTransaction.RollbackAsync(ct); }
            catch (Exception ex) { logger.LogError(ex, "Rollback failed for instance {InstanceId}.", instancePublicId); }
        }

        try { instanceRepository.ClearChanges(); }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "ChangeTracker.Clear failed; scoped DbContext may carry stale entries (instance {InstanceId}).",
                instancePublicId);
        }
    }
}
