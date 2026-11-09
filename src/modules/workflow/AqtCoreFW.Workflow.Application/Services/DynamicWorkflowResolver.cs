using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Application.Workflow;

namespace AqtCoreFW.Workflow.Application.Services;

/// <summary>
/// Queries WorkflowInstances to determine if a given entity has an active
/// dynamic workflow instance. Used by Cases and other modules to decide
/// whether to route through static or dynamic workflow engine.
/// </summary>
public sealed class DynamicWorkflowResolver : IWorkflowResolver
{
    private readonly IReadDbConnection _db;

    public DynamicWorkflowResolver(IReadDbConnection db)
    {
        _db = db;
    }

    /// <inheritdoc />
    public async Task<bool> HasDynamicWorkflowAsync(
        string entityType,
        Guid entityId,
        Guid tenantId,
        CancellationToken ct = default)
    {
        var id = await GetDynamicInstanceIdAsync(entityType, entityId, tenantId, ct);
        return id is not null;
    }

    /// <inheritdoc />
    public async Task<Guid?> GetDynamicInstanceIdAsync(
        string entityType,
        Guid entityId,
        Guid tenantId,
        CancellationToken ct = default)
    {
        const string sql = """
            SELECT Id
            FROM workflow.WorkflowInstances
            WHERE EntityType = @EntityType
              AND EntityId   = @EntityId
              AND TenantId   = @TenantId
              AND CompletedAt IS NULL
            """;

        return await _db.QueryFirstOrDefaultAsync<Guid?>(
            sql,
            new { EntityType = entityType, EntityId = entityId, TenantId = tenantId },
            ct);
    }
}
