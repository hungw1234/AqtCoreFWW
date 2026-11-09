using AqtCoreFW.Workflow.Application.DTOs;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Workflow.Application.Commands.ReturnDelegatedTask;

/// <summary>
/// Returns a delegated task to its original assignee.
/// Idempotent: if the task is not currently delegated, returns success with no changes.
/// IsAdminOverride: set by controller when caller has WORKFLOW.DELEGATION.MANAGE permission (C4).
/// </summary>
public sealed record ReturnDelegatedTaskCommand(
    long TaskId,
    Guid ActorId,
    Guid TenantId,
    bool IsAdminOverride = false) : IRequest<Result<WorkflowTaskDto>>;
