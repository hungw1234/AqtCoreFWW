namespace AqtCoreFW.Workflow.Application.Validation;

/// <summary>
/// Lightweight lookup service to retrieve EntityType from a WorkflowDefinition row.
/// Used by SaveDefinitionGraphCommandValidator to validate state/transition names
/// against the correct entity's allowed states.
/// Implemented via Dapper query in Infrastructure layer.
/// </summary>
public interface IWorkflowDefinitionEntityTypeLookup
{
    /// <summary>
    /// Returns the EntityType for the given definition ID if visible to the tenant
    /// (tenant-specific row OR global template with TenantId=Guid.Empty), else null.
    /// Mirrors the fallback semantics of <c>GetWorkflowDefinitionQueryHandler</c>.
    /// </summary>
    Task<string?> GetEntityTypeAsync(Guid definitionId, Guid tenantId, CancellationToken cancellationToken = default);
}
