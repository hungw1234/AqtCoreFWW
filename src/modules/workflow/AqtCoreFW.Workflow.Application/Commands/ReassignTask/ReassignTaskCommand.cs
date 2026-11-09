using AqtCoreFW.Workflow.Application.DTOs;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Workflow.Application.Commands.ReassignTask;

/// <summary>
/// Reassigns a workflow task to a different user.
/// Phase-4b B4c: TaskId is long (T1 WorkflowTask.Id bigint PK).
/// </summary>
public sealed record ReassignTaskCommand(
    long TaskId,
    Guid NewAssigneeId,
    Guid ReassignedById,
    string? Reason,
    Guid TenantId) : IRequest<Result<WorkflowTaskDto>>;
