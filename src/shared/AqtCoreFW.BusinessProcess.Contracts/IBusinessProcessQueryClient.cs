namespace AqtCoreFW.SharedKernel.Contracts.Clients;

/// <summary>
/// Read-only query surface for BusinessProcess module.
/// In monolith: project-referenced, resolved via <see cref="InProcessBusinessProcessQueryClient"/>.
/// In microservice extraction: implement over gRPC/HTTP.
/// </summary>
public interface IBusinessProcessQueryClient
{
    /// <summary>
    /// Lists all non-deleted BusinessProcessDefinitions that reference the given FormTemplate
    /// (as intake form, state form, or transition form) within the caller's tenant.
    /// Accepts the FormTemplate's internal bigint Id — controller resolves PublicId via IFormTemplateQueryClient.
    /// Returns empty list for cross-tenant or non-existent ids (no existence leak).
    /// </summary>
    Task<IReadOnlyList<BusinessProcessReferenceDto>> ListBpsReferencingFormTemplateAsync(
        long formTemplateId,
        Guid tenantId,
        CancellationToken ct = default);

    /// <summary>
    /// Lists all non-deleted BusinessProcessDefinitions that reference the given WorkflowDefinition
    /// within the caller's tenant.
    /// Returns empty list for cross-tenant or non-existent ids (no existence leak).
    /// </summary>
    Task<IReadOnlyList<BusinessProcessReferenceDto>> ListBpsReferencingWorkflowDefinitionAsync(
        long workflowDefinitionId,
        Guid tenantId,
        CancellationToken ct = default);
}
