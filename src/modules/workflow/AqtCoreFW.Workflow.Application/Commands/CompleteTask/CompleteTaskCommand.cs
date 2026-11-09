using AqtCoreFW.Workflow.Application.DTOs;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Workflow.Application.Commands.CompleteTask;

/// <summary>
/// Marks a workflow task as Done.
/// Phase-4b B4c: TaskId is long (T1 WorkflowTask.Id bigint PK).
/// </summary>
public sealed record CompleteTaskCommand(
    long TaskId,
    Guid CompletedBy,
    Guid TenantId) : IRequest<Result<WorkflowTaskDto>>;
