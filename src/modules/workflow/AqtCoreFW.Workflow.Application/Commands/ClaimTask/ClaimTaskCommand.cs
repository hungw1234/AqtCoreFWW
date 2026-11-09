using AqtCoreFW.Workflow.Application.DTOs;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Workflow.Application.Commands.ClaimTask;

/// <summary>
/// Pool-eligible member claims an unclaimed pool task (RT2-5).
/// TenantId and ActorId resolved from JWT by controller; never from request body.
/// P4 Slice 4-B.
/// </summary>
public sealed record ClaimTaskCommand(
    long TaskId,
    Guid ActorId,
    Guid TenantId) : IRequest<Result<WorkflowTaskDto>>;
