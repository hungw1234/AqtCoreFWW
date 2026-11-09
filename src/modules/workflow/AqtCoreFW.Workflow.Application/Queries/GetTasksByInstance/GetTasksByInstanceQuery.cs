using AqtCoreFW.Workflow.Application.DTOs;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Workflow.Application.Queries.GetTasksByInstance;

/// <summary>Returns all tasks belonging to a workflow instance, each enriched with the
/// resolved assignee name and (for pool tasks) the candidate principals.</summary>
public sealed record GetTasksByInstanceQuery(
    Guid InstanceId,
    Guid TenantId) : IRequest<Result<IReadOnlyList<WorkflowInstanceTaskDto>>>;
