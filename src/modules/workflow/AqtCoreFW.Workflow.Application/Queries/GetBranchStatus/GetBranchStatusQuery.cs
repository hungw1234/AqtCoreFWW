using AqtCoreFW.Workflow.Application.DTOs;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Workflow.Application.Queries.GetBranchStatus;

/// <summary>Returns all branch statuses (with child statuses) for a workflow instance.</summary>
public sealed record GetBranchStatusQuery(Guid InstanceId, Guid TenantId)
    : IRequest<Result<IReadOnlyList<WorkflowBranchStatusDto>>>;
