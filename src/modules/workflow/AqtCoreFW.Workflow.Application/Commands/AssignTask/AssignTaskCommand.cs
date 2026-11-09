using AqtCoreFW.Workflow.Application.DTOs;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Workflow.Application.Commands.AssignTask;

/// <summary>
/// Assigns a workflow task to a user (initial assignment).
/// Phase-4b B4c: TaskId is long (T1 WorkflowTask.Id bigint PK).
/// </summary>
public sealed record AssignTaskCommand(
    long TaskId,
    Guid AssigneeId,
    Guid AssignedById,
    string? Reason,
    Guid TenantId) : IRequest<Result<WorkflowTaskDto>>;
