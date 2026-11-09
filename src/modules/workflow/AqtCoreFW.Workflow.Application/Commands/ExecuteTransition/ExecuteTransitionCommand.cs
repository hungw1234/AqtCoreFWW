using AqtCoreFW.Workflow.Application.DTOs;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Workflow.Application.Commands.ExecuteTransition;

/// <summary>
/// Executes a workflow transition on an instance.
/// Authorization uses the assignment-based resolver (WorkflowTransitionAssignment rules
/// for authz-of-T over current state).
/// FileIds: optional list of file IDs from the Files module to attach to this transition.
/// </summary>
/// <summary>
/// Phase-4b B4c: InstanceId = WorkflowInstance.PublicId (Guid T2 AK).
/// TransitionId = WorkflowTransition.Id (long T1 bigint PK — no Guid AK).
/// ACCEPT-LONG: used as opaque round-trip handle; FE consumes via orval <c>number | string</c>
/// permissive union with no parsing/introspection.
/// </summary>
public sealed record ExecuteTransitionCommand(
    Guid InstanceId,
    // arch:accept-long T1 WorkflowTransition: aggregate-internal id, FE round-trips opaque
    long TransitionId,
    Guid ActorId,
    string? Comment,
    IReadOnlyDictionary<string, object?>? Metadata = null,
    IReadOnlyList<Guid>? FileIds = null,
    // Slice 10a DDTO-DEFAULT: optional assignor override — null = use normal resolver result.
    // DLIVE-b: gate order (AllowAssignorOverride → ∈Pool → live active) enforced in handler.
    Guid? NextHandlerUserId = null)
    : IRequest<Result<WorkflowInstanceDto>>;
