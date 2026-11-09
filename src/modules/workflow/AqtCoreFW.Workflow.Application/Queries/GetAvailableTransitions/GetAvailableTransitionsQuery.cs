using AqtCoreFW.Workflow.Application.DTOs;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Workflow.Application.Queries.GetAvailableTransitions;

/// <summary>
/// Returns transitions valid from the current state of a given WorkflowInstance,
/// filtered by assignment-based resolver authz (WorkflowTransitionAssignment rules
/// determine visibility).
/// </summary>
public sealed record GetAvailableTransitionsQuery(
    Guid InstanceId,
    Guid TenantId,
    Guid UserId)
    : IRequest<Result<IReadOnlyList<WorkflowTransitionDto>>>;
