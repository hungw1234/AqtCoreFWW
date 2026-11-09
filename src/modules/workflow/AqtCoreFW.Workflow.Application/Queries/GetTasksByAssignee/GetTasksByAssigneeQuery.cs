using AqtCoreFW.Workflow.Application.DTOs;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Workflow.Application.Queries.GetTasksByAssignee;

/// <summary>Returns open/in-progress tasks for a specific assignee within a tenant.</summary>
public sealed record GetTasksByAssigneeQuery(
    Guid AssigneeId,
    Guid TenantId) : IRequest<Result<IReadOnlyList<WorkflowTaskDto>>>;
