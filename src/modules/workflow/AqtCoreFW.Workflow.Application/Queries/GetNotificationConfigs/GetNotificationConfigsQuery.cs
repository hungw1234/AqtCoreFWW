using AqtCoreFW.Workflow.Application.DTOs;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Workflow.Application.Queries.GetNotificationConfigs;

/// <summary>Returns all notification configs for a workflow definition (tenant-scoped).</summary>
public sealed record GetNotificationConfigsQuery(
    Guid DefinitionId,
    Guid TenantId) : IRequest<Result<IReadOnlyList<WorkflowNotificationConfigDto>>>;
