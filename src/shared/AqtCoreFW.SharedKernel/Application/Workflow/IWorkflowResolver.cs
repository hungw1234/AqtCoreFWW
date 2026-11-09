namespace AqtCoreFW.SharedKernel.Application.Workflow;

/// <summary>
/// Resolves which workflow engine to use for a given entity.
/// Returns true if dynamic workflow is configured; false = use static engine.
/// Placed in SharedKernel so Cases and other modules can depend on it
/// without taking a direct reference to the Workflow module.
/// </summary>
public interface IWorkflowResolver
{
    /// <summary>Check if entity has an active dynamic workflow instance.</summary>
    Task<bool> HasDynamicWorkflowAsync(string entityType, Guid entityId, Guid tenantId, CancellationToken ct = default);

    /// <summary>Get the dynamic workflow instance ID for an entity (null if none).</summary>
    Task<Guid?> GetDynamicInstanceIdAsync(string entityType, Guid entityId, Guid tenantId, CancellationToken ct = default);
}
