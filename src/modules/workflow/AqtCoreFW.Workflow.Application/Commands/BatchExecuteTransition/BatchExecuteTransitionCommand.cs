using AqtCoreFW.Workflow.Application.DTOs;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Workflow.Application.Commands.BatchExecuteTransition;

/// <summary>
/// Executes the same actor's transition on multiple workflow instances.
/// Partial-failure semantics: each item succeeds or fails independently via MediatR.Send.
/// NOT a single transaction — each item calls SaveChanges independently.
/// Max 50 items per batch (validator-enforced).
/// Phase-02: permission check delegated to per-item ExecuteTransitionCommand handler
/// (permission-set membership via IIdentityModuleClient) — no role field on this command.
/// </summary>
public sealed record BatchExecuteTransitionCommand(
    IReadOnlyList<BatchTransitionItem> Items,
    Guid ActorId,
    string? Comment) : IRequest<Result<BatchTransitionResult>>;

/// <summary>
/// Single (instance, transition) pair within a batch request.
/// InstanceId = WorkflowInstance.PublicId (T2 Guid AK).
/// TransitionId = WorkflowTransition.Id (T1 long bigint PK — no Guid AK).
/// ACCEPT-LONG: used as opaque round-trip handle; FE consumes via orval <c>number | string</c>
/// permissive union with no parsing/introspection.
/// </summary>
// arch:accept-long T1 WorkflowTransition: aggregate-internal id, FE round-trips opaque
public sealed record BatchTransitionItem(Guid InstanceId, long TransitionId);
