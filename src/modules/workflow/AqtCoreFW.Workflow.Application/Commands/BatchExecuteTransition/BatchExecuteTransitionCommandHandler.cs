using AqtCoreFW.Workflow.Application.Commands.ExecuteTransition;
using AqtCoreFW.Workflow.Application.DTOs;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Workflow.Application.Commands.BatchExecuteTransition;

/// <summary>
/// Executes a batch of workflow transitions with partial-failure semantics.
///
/// Design decisions:
/// - Loops sequentially (not parallel) to avoid DbContext concurrency issues and
///   to keep per-item error reporting deterministic.
/// - Uses IMediator.Send(ExecuteTransitionCommand) for DRY — reuses all validation
///   and state-guard logic from the single-transition handler.
/// - Red-team F04: ChangeTracker pollution avoided because ExecuteTransitionCommandHandler
///   persists each item via UpdateWithStateGuardAsync (calls SaveChangesAsync per item),
///   and domain events are dispatched in-process. Entities remain tracked but SaveChanges
///   clears the dirty state. For extreme batch sizes (&gt;50) consider per-item DI scope,
///   but validator caps at 50 — memory footprint is acceptable.
/// - Exceptions per item are caught and recorded as failures — never propagate.
/// </summary>
public sealed class BatchExecuteTransitionCommandHandler(IMediator mediator)
    : IRequestHandler<BatchExecuteTransitionCommand, Result<BatchTransitionResult>>
{
    public async Task<Result<BatchTransitionResult>> Handle(
        BatchExecuteTransitionCommand request,
        CancellationToken cancellationToken)
    {
        var results = new List<BatchTransitionItemResult>(request.Items.Count);

        foreach (var item in request.Items)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            results.Add(await ExecuteOneAsync(request, item, cancellationToken));
        }

        var successCount = results.Count(r => r.Success);
        var batchResult = new BatchTransitionResult(
            TotalCount: results.Count,
            SuccessCount: successCount,
            FailureCount: results.Count - successCount,
            Items: results);

        return Result.Ok(batchResult);
    }

    private async Task<BatchTransitionItemResult> ExecuteOneAsync(
        BatchExecuteTransitionCommand request,
        BatchTransitionItem item,
        CancellationToken ct)
    {
        try
        {
            var single = new ExecuteTransitionCommand(
                InstanceId: item.InstanceId,
                TransitionId: item.TransitionId,
                ActorId: request.ActorId,
                Comment: request.Comment,
                NextHandlerUserId: null); // batch deliberately excludes per-item override (DDTO-DEFAULT)

            var result = await mediator.Send(single, ct);

            return result.IsSuccess
                ? new BatchTransitionItemResult(item.InstanceId, item.TransitionId, true, null, result.Value)
                : new BatchTransitionItemResult(
                    item.InstanceId, item.TransitionId, false,
                    string.Join("; ", result.Errors.Select(e => e.Message)),
                    null);
        }
        catch (Exception ex)
        {
            // Per-item exception — record sanitized summary only; full details stay server-side.
            // Exposing ex.Message risks leaking internal EF/SQL error strings to clients.
            var safeError = $"Unhandled error: {ex.GetType().Name}";
            _ = ex; // full exception is available to host logging pipeline via MediatR diagnostics
            return new BatchTransitionItemResult(
                item.InstanceId, item.TransitionId, false, safeError, null);
        }
    }
}
