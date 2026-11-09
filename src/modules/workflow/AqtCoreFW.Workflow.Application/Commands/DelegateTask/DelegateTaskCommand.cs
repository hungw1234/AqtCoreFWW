using AqtCoreFW.Workflow.Application.DTOs;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Workflow.Application.Commands.DelegateTask;

/// <summary>
/// Delegates an assigned task to another user with an optional expiry and reason.
/// ActorId must be the current assignee or IsAdminOverride = true.
/// IsAdminOverride: set by controller when caller has WORKFLOW.DELEGATION.MANAGE permission (C4).
/// </summary>
public sealed record DelegateTaskCommand(
    long TaskId,
    Guid DelegateToUserId,
    DateTimeOffset ExpiresAt,
    string? Reason,
    Guid ActorId,
    Guid TenantId,
    bool IsAdminOverride = false) : IRequest<Result<WorkflowTaskDto>>;
