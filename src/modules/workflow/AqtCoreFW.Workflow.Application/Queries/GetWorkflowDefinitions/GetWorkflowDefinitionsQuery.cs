using AqtCoreFW.SharedKernel.Application.Pagination;
using AqtCoreFW.Workflow.Application.DTOs;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Workflow.Application.Queries.GetWorkflowDefinitions;

/// <summary>
/// Returns a paged list of WorkflowDefinitions for a tenant.
/// States/Transitions are NOT loaded — use GetWorkflowDefinitionQuery for full detail.
/// </summary>
public sealed record GetWorkflowDefinitionsQuery(
    Guid TenantId,
    bool? IsActive,
    string? SearchTerm,
    int Page,
    int PageSize)
    : IRequest<Result<PagedResult<WorkflowDefinitionSummaryDto>>>;

/// <summary>
/// Lightweight summary for list views — no nested collections.
/// Class form (not positional record) required for Dapper compatibility:
/// DB column CreatedAt is datetime2 → Dapper materializes as DateTime, not DateTimeOffset.
/// </summary>
public sealed class WorkflowDefinitionSummaryDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public bool IsActive { get; init; }
    public DateTime CreatedAt { get; init; }
    public string DefinitionKey { get; init; } = string.Empty;
    public int Version { get; init; }
    public bool IsLatest { get; init; }
    public string EntityType { get; init; } = string.Empty;
}
