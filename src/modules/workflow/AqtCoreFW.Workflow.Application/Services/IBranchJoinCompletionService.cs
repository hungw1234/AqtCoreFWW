using AqtCoreFW.Workflow.Domain.Entities;
using FluentResults;

namespace AqtCoreFW.Workflow.Application.Services;

/// <summary>
/// Completes a parallel-branch join once the join condition is satisfied: validates the continuation
/// state has an eligible handler (fail-CLOSED), advances the instance to the continuation state, and
/// stages the continuation work item. Extracted so all three resolution entry points
/// (<c>ResolveBranchChildCommandHandler</c> explicit resolve, <c>CompleteTaskCommandHandler</c> and
/// <c>CancelTaskCommandHandler</c> auto-resolve) share ONE join-completion path — previously each
/// re-implemented it and the auto-resolve paths both omitted the continuation pre-check and swallowed
/// failures, so a join could advance to a handler-less continuation and silently strand.
/// </summary>
public interface IBranchJoinCompletionService
{
    /// <summary>
    /// Applies the continuation transition for a completed join.
    /// </summary>
    /// <param name="instance">The instance whose branch just satisfied its join condition (already loaded).</param>
    /// <param name="continuationStateId">The branch's continuation state (long T1 PK) to advance to.</param>
    /// <param name="actorId">The user whose resolution completed the join (history + audit attribution).</param>
    /// <returns>
    /// Ok when the instance advanced to the continuation state; Fail with WF_NO_HANDLER (422) when the
    /// continuation state has no eligible handler (config error — fail-CLOSED), or with the state-guard
    /// conflict error. Callers on a user-facing task action (complete/cancel) treat a failure as
    /// non-fatal to that action and log it loudly rather than propagating it.
    /// </returns>
    Task<Result> CompleteJoinAsync(
        WorkflowInstance instance,
        long continuationStateId,
        Guid actorId,
        CancellationToken ct);
}
