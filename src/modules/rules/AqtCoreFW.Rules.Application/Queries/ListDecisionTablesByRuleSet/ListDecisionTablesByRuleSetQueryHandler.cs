using AqtCoreFW.Rules.Application.DTOs;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Rules.Application.Queries.ListDecisionTablesByRuleSet;

public sealed class ListDecisionTablesByRuleSetQueryHandler(
    IReadDbConnection db,
    ITenantContext tenantContext,
    ILogger<ListDecisionTablesByRuleSetQueryHandler> logger)
    : IRequestHandler<ListDecisionTablesByRuleSetQuery, Result<IReadOnlyList<DecisionTableDto>>>
{
    public async Task<Result<IReadOnlyList<DecisionTableDto>>> Handle(
        ListDecisionTablesByRuleSetQuery request,
        CancellationToken cancellationToken)
    {
        // Defence-in-depth: reject cross-tenant list reads. Mirrors GetRuleSet/GetDecisionTable.
        if (!tenantContext.IsSystemAdmin && request.TenantId != tenantContext.TenantId)
        {
            logger.LogWarning(
                "Cross-tenant ListDecisionTablesByRuleSet denied: currentTenantId={CurrentTenantId}, attemptedTenantId={AttemptedTenantId}",
                tenantContext.TenantId, request.TenantId);
            return Result.Fail(new ValidationError("Cross-tenant access denied"));
        }

        // T1 DecisionTable: long Id internal PK; T2 RuleSet addressed by Guid PublicId via JOIN.
        // Filter parent by PublicId (external addressing); surface parent's PublicId on each row.
        const string sql = """
            SELECT dt.Id, rs.PublicId AS RuleSetId, dt.TenantId, dt.Name,
                   dt.InputColumns, dt.OutputColumns, dt.Rows,
                   dt.CreatedAt, dt.UpdatedAt
            FROM rules.DecisionTables dt
            INNER JOIN rules.RuleSets rs ON rs.Id = dt.RuleSetId
            WHERE rs.PublicId = @RuleSetId AND dt.TenantId = @TenantId AND dt.IsDeleted = 0
            ORDER BY dt.CreatedAt
            """;

        var rows = (await db.QueryAsync<DecisionTableRow>(
            sql, new { request.RuleSetId, request.TenantId }, cancellationToken)).ToList();

        // Parse raw JSON storage columns once at the boundary so FE/mobile/batch consumers
        // never re-implement the deserialisation. See ADR DTO contract drift fix (v2.132p2).
        var tables = rows.Select(r => new DecisionTableDto(
            r.Id, r.RuleSetId, r.TenantId, r.Name,
            DecisionTableJsonHelper.ParseColumns(r.InputColumns, logger, $"InputColumns:{r.Id}"),
            DecisionTableJsonHelper.ParseColumns(r.OutputColumns, logger, $"OutputColumns:{r.Id}"),
            DecisionTableJsonHelper.ParseRows(r.Rows, logger, $"Rows:{r.Id}"),
            r.CreatedAt, r.UpdatedAt)).ToList();

        return Result.Ok<IReadOnlyList<DecisionTableDto>>(tables);
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
