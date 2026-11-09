using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.Workflow.Application.DTOs;
using AqtCoreFW.Workflow.Domain.Entities;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Workflow.Application.Queries.GetEscalationRules;

public sealed class GetEscalationRulesQueryHandler(IReadDbConnection db)
    : IRequestHandler<GetEscalationRulesQuery, Result<IReadOnlyList<EscalationRuleDto>>>
{
    public async Task<Result<IReadOnlyList<EscalationRuleDto>>> Handle(
        GetEscalationRulesQuery request,
        CancellationToken cancellationToken)
    {
        // Dynamic WHERE clause: optionally filter by IsActive
        var activeFilter = request.IsActive.HasValue
            ? "AND IsActive = @IsActive"
            : string.Empty;

        // T2 WorkflowDefinition: filter by PublicId (Guid AK) via JOIN.
        // er.WorkflowDefinitionId is a bigint FK — cannot compare directly against Guid request param.
        // Phase-07 PR-D2: SELECT wd.PublicId instead of er.WorkflowDefinitionId (bigint FK).
        // JOIN already present (needed for WHERE filter); just swap the SELECT column.
        var sql = $"""
            SELECT er.Id, wd.PublicId AS WorkflowDefinitionId, er.StateId, er.EscalationAfterMinutes,
                   er.EscalateTo, er.EscalateToValue, er.NotifyVia, er.IsActive, er.TenantId
            FROM workflow.EscalationRules er
            INNER JOIN workflow.WorkflowDefinitions wd ON wd.Id = er.WorkflowDefinitionId
            WHERE wd.PublicId = @WorkflowDefinitionId
              AND er.TenantId = @TenantId
              AND er.IsDeleted = 0
              {activeFilter}
            ORDER BY er.EscalationAfterMinutes
            """;

        var rows = await db.QueryAsync<RuleRow>(
            sql,
            new { request.WorkflowDefinitionId, request.TenantId, request.IsActive },
            cancellationToken);

        var result = rows
            .Select(r => new EscalationRuleDto(
                r.Id, r.WorkflowDefinitionId, r.StateId,
                r.EscalationAfterMinutes, (EscalationTarget)r.EscalateTo,
                r.EscalateToValue, r.NotifyVia, r.IsActive, r.TenantId))
            .ToList();

        return Result.Ok<IReadOnlyList<EscalationRuleDto>>(result);
    }

    // EscalationRule is T1: Id (long), WorkflowDefinitionId now Guid (T2 PublicId AK via JOIN), StateId (long? FK to T1 state)
    internal sealed class RuleRow
    {
        public long Id { get; set; }
        public Guid WorkflowDefinitionId { get; set; }
        public long? StateId { get; set; }
        public int EscalationAfterMinutes { get; set; }
        public int EscalateTo { get; set; }
        public string EscalateToValue { get; set; } = string.Empty;
        public string NotifyVia { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public Guid TenantId { get; set; }
    }
}
