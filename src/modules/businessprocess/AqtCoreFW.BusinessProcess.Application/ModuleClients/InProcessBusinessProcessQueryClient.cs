using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using Dapper;

namespace AqtCoreFW.BusinessProcess.Application.ModuleClients;

/// <summary>
/// In-process implementation of <see cref="IBusinessProcessQueryClient"/> (BP MVP P7-FU2/FU3).
/// Dapper reads on <c>businessprocess.*</c> schema only (same-schema; CrossSchemaSqlScanner green).
/// Defense-in-depth: TenantId filter on every table touched, mirroring Pattern-A repo rationale
/// (RLS may be bypassed by db_owner login per [[phase6-rls-restore-shipped-260512]]).
/// Cross-tenant or missing ids return empty list (no existence leak, per D-cross-3).
/// </summary>
public sealed class InProcessBusinessProcessQueryClient(IReadDbConnection db)
    : IBusinessProcessQueryClient
{
    /// <inheritdoc/>
    public async Task<IReadOnlyList<BusinessProcessReferenceDto>> ListBpsReferencingFormTemplateAsync(
        long formTemplateId,
        Guid tenantId,
        CancellationToken ct = default)
    {
        // 3-way UNION ALL: intake form on root definition, state forms, transition forms.
        // IsDeleted=0 on every BP root join — soft-deleted BPs must not appear in blocking dialog.
        // TenantId filter on both sides of every JOIN (defense-in-depth, same as InProcessFormTemplateQueryClient).
        // formTemplateId is internal bigint — caller (controller) resolves PublicId via IFormTemplateQueryClient.
        const string sql = """
            SELECT bp.PublicId AS BusinessProcessPublicId, bp.Code, bp.Name,
                   'intake' AS [Role],
                   CAST(NULL AS bigint) AS WorkflowStateId,
                   CAST(NULL AS bigint) AS WorkflowTransitionId,
                   bp.IsActive
            FROM businessprocess.BusinessProcessDefinitions bp
            WHERE bp.TenantId = @TenantId
              AND bp.IsDeleted = 0
              AND bp.IntakeFormTemplateId = @FormTemplateId

            UNION ALL

            SELECT bp.PublicId, bp.Code, bp.Name,
                   'state-form',
                   sf.WorkflowStateId,
                   CAST(NULL AS bigint),
                   bp.IsActive
            FROM businessprocess.BusinessProcessStateForms sf
            INNER JOIN businessprocess.BusinessProcessDefinitions bp
                    ON bp.Id = sf.BusinessProcessDefinitionId
                   AND bp.TenantId = sf.TenantId
            WHERE sf.TenantId = @TenantId
              AND sf.FormTemplateId = @FormTemplateId
              AND bp.IsDeleted = 0

            UNION ALL

            SELECT bp.PublicId, bp.Code, bp.Name,
                   'transition-form',
                   CAST(NULL AS bigint),
                   tf.WorkflowTransitionId,
                   bp.IsActive
            FROM businessprocess.BusinessProcessTransitionForms tf
            INNER JOIN businessprocess.BusinessProcessDefinitions bp
                    ON bp.Id = tf.BusinessProcessDefinitionId
                   AND bp.TenantId = tf.TenantId
            WHERE tf.TenantId = @TenantId
              AND tf.FormTemplateId = @FormTemplateId
              AND bp.IsDeleted = 0

            ORDER BY Code
            """;

        var rows = await db.QueryAsync<BusinessProcessReferenceDto>(
            sql,
            new { FormTemplateId = formTemplateId, TenantId = tenantId },
            ct);

        return rows.ToList();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<BusinessProcessReferenceDto>> ListBpsReferencingWorkflowDefinitionAsync(
        long workflowDefinitionId,
        Guid tenantId,
        CancellationToken ct = default)
    {
        // Single-table query — WorkflowDefinitionId lives only on the root BP definition.
        const string sql = """
            SELECT bp.PublicId AS BusinessProcessPublicId, bp.Code, bp.Name,
                   'workflow' AS [Role],
                   CAST(NULL AS bigint) AS WorkflowStateId,
                   CAST(NULL AS bigint) AS WorkflowTransitionId,
                   bp.IsActive
            FROM businessprocess.BusinessProcessDefinitions bp
            WHERE bp.TenantId = @TenantId
              AND bp.IsDeleted = 0
              AND bp.WorkflowDefinitionId = @WorkflowDefinitionId
            ORDER BY bp.Code
            """;

        var rows = await db.QueryAsync<BusinessProcessReferenceDto>(
            sql,
            new { WorkflowDefinitionId = workflowDefinitionId, TenantId = tenantId },
            ct);

        return rows.ToList();
    }
}
