using AqtCoreFW.Rules.Application.DTOs;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Rules.Application.Queries.GetDecisionTable;

public sealed class GetDecisionTableQueryHandler(
    IReadDbConnection db,
    ITenantContext tenantContext,
    ILogger<GetDecisionTableQueryHandler> logger)
    : IRequestHandler<GetDecisionTableQuery, Result<DecisionTableDto>>
{
    public async Task<Result<DecisionTableDto>> Handle(
        GetDecisionTableQuery request,
        CancellationToken cancellationToken)
    {
        // Defence-in-depth: reject cross-tenant DecisionTable read.
        if (!tenantContext.IsSystemAdmin && request.TenantId != tenantContext.TenantId)
        {
            logger.LogWarning(
                "Cross-tenant GetDecisionTable denied: currentTenantId={CurrentTenantId}, attemptedTenantId={AttemptedTenantId}",
                tenantContext.TenantId, request.TenantId);
            return Result.Fail(new ValidationError("Cross-tenant access denied"));
        }

        // T1 DecisionTable: long Id internal PK; surface parent RuleSet's Guid PublicId via JOIN.
        const string sql = """
            SELECT dt.Id, rs.PublicId AS RuleSetId, dt.TenantId, dt.Name,
                   dt.InputColumns, dt.OutputColumns, dt.Rows,
                   dt.CreatedAt, dt.UpdatedAt
            FROM rules.DecisionTables dt
            INNER JOIN rules.RuleSets rs ON rs.Id = dt.RuleSetId
            WHERE dt.Id = @TableId AND dt.TenantId = @TenantId AND dt.IsDeleted = 0
            """;

        var row = await db.QueryFirstOrDefaultAsync<DecisionTableRow>(
            sql, new { request.TableId, request.TenantId }, cancellationToken);

        if (row is null)
            return Result.Fail(new NotFoundError($"DecisionTable {request.TableId} not found."));

        // Parse storage JSON at the boundary — see ListDecisionTablesByRuleSetQueryHandler for rationale.
        return Result.Ok(new DecisionTableDto(
            row.Id, row.RuleSetId, row.TenantId, row.Name,
            DecisionTableJsonHelper.ParseColumns(row.InputColumns, logger, $"InputColumns:{row.Id}"),
            DecisionTableJsonHelper.ParseColumns(row.OutputColumns, logger, $"OutputColumns:{row.Id}"),
            DecisionTableJsonHelper.ParseRows(row.Rows, logger, $"Rows:{row.Id}"),
            row.CreatedAt, row.UpdatedAt));
    }

    // Dapper requires parameterless ctor — positional records with nullable params fail materialization
    private sealed class DecisionTableRow
    {
        public long Id { get; set; }
        public Guid RuleSetId { get; set; }
        public Guid TenantId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string InputColumns { get; set; } = string.Empty;
        public string OutputColumns { get; set; } = string.Empty;
        public string Rows { get; set; } = string.Empty;
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset? UpdatedAt { get; set; }
    }
}
