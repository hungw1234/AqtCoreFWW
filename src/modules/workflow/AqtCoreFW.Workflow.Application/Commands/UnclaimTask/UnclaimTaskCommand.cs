using AqtCoreFW.Workflow.Application.DTOs;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Workflow.Application.Commands.UnclaimTask;

/// <summary>
/// Current assignee releases a claimed pool task back to the pool.
/// Domain Unclaim() is idempotent (no-op when AssigneeId is already null).
/// TenantId and ActorId resolved from JWT by controller; never from request body.
/// P4 Slice 4-B.
/// </summary>
public sealed record UnclaimTaskCommand(
    long TaskId,
    Guid ActorId,
    Guid TenantId) : IRequest<Result<WorkflowTaskDto>>;
