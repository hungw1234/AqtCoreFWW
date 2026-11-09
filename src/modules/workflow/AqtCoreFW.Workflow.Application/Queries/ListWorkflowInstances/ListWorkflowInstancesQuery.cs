using AqtCoreFW.SharedKernel.Application.Pagination;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Workflow.Application.Queries.ListWorkflowInstances;

/// <summary>
/// Returns a cursor-paginated list of workflow instances for a tenant.
/// PageSize is clamped server-side to [1, 100] — client cannot DoS via large values. [RT-11]
/// Cursor null → first page (newest first). Non-null → resume from encoded position.
/// </summary>
public sealed record ListWorkflowInstancesQuery(
    Guid TenantId,
    string? Cursor,
    int PageSize,
    InstanceFilter Filter) : IRequest<Result<PagedResult<WorkflowInstanceListItemDto>>>;
