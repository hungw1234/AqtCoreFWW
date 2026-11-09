using AqtCoreFW.Workflow.Application.DTOs;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Workflow.Application.Commands.CancelTask;

/// <summary>
/// Cancels a workflow task with an optional reason.
/// Phase-4b B4c: TaskId is long (T1 WorkflowTask.Id bigint PK).
/// </summary>
public sealed record CancelTaskCommand(
    long TaskId,
    string? Reason,
    Guid CancelledBy,
    Guid TenantId) : IRequest<Result<WorkflowTaskDto>>;
