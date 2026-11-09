using FluentResults;

namespace AqtCoreFW.Workflow.Application.Services;

/// <summary>
/// Resolves the best-matching WorkflowDefinitionId for a given tenant + entity type + optional sub-type.
/// Uses 4-level specificity with cache-aside (24h TTL).
/// </summary>
public interface IWorkflowAssignmentResolver
{
    Task<Result<Guid>> ResolveAsync(
        Guid tenantId,
        string entityType,
        string? entitySubType = null,
        CancellationToken ct = default);
}
