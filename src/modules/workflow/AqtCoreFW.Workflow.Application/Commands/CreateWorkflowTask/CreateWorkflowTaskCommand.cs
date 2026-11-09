using AqtCoreFW.Workflow.Application.DTOs;
using AqtCoreFW.Workflow.Domain.Entities;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Workflow.Application.Commands.CreateWorkflowTask;

/// <summary>
/// Creates a new WorkflowTask within a workflow instance.
/// Phase-07 PR-2: WorkflowInstancePublicId is Guid (T2 external AK); handler resolves to long FK via IWorkflowInstanceRepository.GetByIdAsync.
/// </summary>
public sealed record CreateWorkflowTaskCommand(
    Guid WorkflowInstancePublicId,
    string Title,
    string? Description,
    Guid? AssigneeId,
    WorkflowTaskPriority Priority,
    DateTime? DueDate,
    Guid TenantId,
    Guid CreatedBy) : IRequest<Result<WorkflowTaskDto>>;
