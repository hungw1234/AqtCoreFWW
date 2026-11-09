using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.Workflow.Application.Validation;

namespace AqtCoreFW.Workflow.Infrastructure.Validation;

/// <summary>
/// Dapper-based lookup for WorkflowDefinition.EntityType.
/// Used by SaveDefinitionGraphCommandValidator to resolve EntityType from the existing definition row.
/// Tenant-filtered: visible when TenantId matches the requester OR TenantId=Guid.Empty (global template).
/// Mirrors the fallback semantics of <c>GetWorkflowDefinitionQueryHandler</c>.
/// </summary>
public sealed class WorkflowDefinitionEntityTypeLookup : IWorkflowDefinitionEntityTypeLookup
{
    private readonly IReadDbConnection _db;

    public WorkflowDefinitionEntityTypeLookup(IReadDbConnection db)
    {
        _db = db;
    }

    public async Task<string?> GetEntityTypeAsync(Guid definitionId, Guid tenantId, CancellationToken cancellationToken = default)
    {
        // T2 entity (long Id PK + Guid PublicId AK): URL/command uses PublicId, not internal Id.
        // Querying by Id with a Guid value caused "Operand type clash: uniqueidentifier vs bigint".
        const string sql = """
            SELECT EntityType
            FROM workflow.WorkflowDefinitions
            WHERE PublicId = @DefinitionId
              AND (TenantId = @TenantId OR TenantId = @GlobalTenantId)
              AND IsDeleted = 0
            """;

        return await _db.QueryFirstOrDefaultAsync<string>(
            sql,
            new { DefinitionId = definitionId, TenantId = tenantId, GlobalTenantId = Guid.Empty },
            cancellationToken);
    }
}
