using AqtCoreFW.BusinessProcess.Application.Abstractions;
using AqtCoreFW.SharedKernel.Application.Data;

namespace AqtCoreFW.BusinessProcess.Infrastructure.Queries;

/// <summary>
/// P4 Slice 2C: Dapper read helpers for BP runtime handlers.
/// All queries restricted to <c>businessprocess.*</c> schema; <c>TenantId</c> filter on every WHERE.
/// Cross-schema joins are forbidden by <c>CrossSchemaSqlScanner</c> arch test — Forms/Workflow
/// data resolution flows through their respective cross-module clients.
/// </summary>
public sealed class BusinessProcessRuntimeQueries(IReadDbConnection db) : IBusinessProcessRuntimeQueries
{
    public async Task<BpRuntimeContext?> GetRuntimeContextAsync(
        Guid bpPublicId, Guid tenantId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT Id AS BusinessProcessDefinitionId,
                   WorkflowDefinitionId,
                   IntakeFormTemplateId,
                   IntakeFormTemplateVersion AS IntakeFormVersion,
                   IsActive
            FROM businessprocess.BusinessProcessDefinitions
            WHERE PublicId = @PublicId AND TenantId = @TenantId AND IsDeleted = 0
            """;

        return await db.QueryFirstOrDefaultAsync<BpRuntimeContext>(
            sql, new { PublicId = bpPublicId, TenantId = tenantId }, ct);
    }

    public async Task<IReadOnlyList<BpStateFormBinding>> GetStateFormTemplateIdsAsync(
        Guid bpPublicId, long workflowStateId, Guid tenantId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT sf.FormTemplateId, sf.FormTemplateVersion, sf.IsRequired
            FROM businessprocess.BusinessProcessStateForms sf
            INNER JOIN businessprocess.BusinessProcessDefinitions bp
                ON bp.Id = sf.BusinessProcessDefinitionId
                AND bp.TenantId = sf.TenantId
            WHERE bp.PublicId = @PublicId
              AND sf.TenantId = @TenantId
              AND bp.TenantId = @TenantId
              AND sf.WorkflowStateId = @WorkflowStateId
              AND bp.IsDeleted = 0
            ORDER BY sf.DisplayOrder, sf.Id
            """;

        var rows = await db.QueryAsync<BpStateFormBinding>(
            sql,
            new { PublicId = bpPublicId, TenantId = tenantId, WorkflowStateId = workflowStateId },
            ct);
        return rows.ToList();
    }

    public async Task<IReadOnlyList<BpTransitionFormBinding>> GetTransitionFormTemplateIdsAsync(
        Guid bpPublicId, long workflowTransitionId, Guid tenantId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT tf.FormTemplateId, tf.FormTemplateVersion, tf.IsRequired
            FROM businessprocess.BusinessProcessTransitionForms tf
            INNER JOIN businessprocess.BusinessProcessDefinitions bp
                ON bp.Id = tf.BusinessProcessDefinitionId
                AND bp.TenantId = tf.TenantId
            WHERE bp.PublicId = @PublicId
              AND tf.TenantId = @TenantId
              AND bp.TenantId = @TenantId
              AND tf.WorkflowTransitionId = @WorkflowTransitionId
              AND bp.IsDeleted = 0
            ORDER BY tf.DisplayOrder, tf.Id
            """;

        var rows = await db.QueryAsync<BpTransitionFormBinding>(
            sql,
            new { PublicId = bpPublicId, TenantId = tenantId, WorkflowTransitionId = workflowTransitionId },
            ct);
        return rows.ToList();
    }
}
