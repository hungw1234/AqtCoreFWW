using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.Workflow.Application.DTOs;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Workflow.Application.Queries.GetAssignmentRules;

public sealed class GetAssignmentRulesQueryHandler(IReadDbConnection db)
    : IRequestHandler<GetAssignmentRulesQuery, Result<IReadOnlyList<WorkflowAssignmentRuleDto>>>
{
    public async Task<Result<IReadOnlyList<WorkflowAssignmentRuleDto>>> Handle(
        GetAssignmentRulesQuery request,
        CancellationToken cancellationToken)
    {
        // Phase-07 PR-D2: JOIN WorkflowDefinitions to project PublicId (Guid AK)
        // instead of the raw bigint FK (ar.WorkflowDefinitionId).
        const string sql = """
            SELECT ar.Id, ar.TenantId, wd.PublicId AS WorkflowDefinitionId,
                   ar.EntityType, ar.EntitySubType,
                   ar.Specificity, ar.Priority, ar.IsActive,
                   ar.Description, ar.EffectiveFrom, ar.EffectiveUntil
            FROM workflow.WorkflowAssignmentRules ar
            INNER JOIN workflow.WorkflowDefinitions wd ON wd.Id = ar.WorkflowDefinitionId
            WHERE ar.TenantId = @TenantId
              AND ar.IsDeleted = 0
            ORDER BY ar.Specificity DESC, ar.Priority DESC
            """;

        var rows = await db.QueryAsync<RuleRow>(
            sql,
            new { request.TenantId },
            cancellationToken);

        var result = rows
            .Select(r => new WorkflowAssignmentRuleDto(
                r.Id, r.TenantId, r.WorkflowDefinitionId,
                r.EntityType, r.EntitySubType,
                r.Specificity, r.Priority, r.IsActive,
                r.Description, r.EffectiveFrom, r.EffectiveUntil))
            .ToList();

        return Result.Ok<IReadOnlyList<WorkflowAssignmentRuleDto>>(result);
    }

    // WorkflowAssignmentRule is T1: Id (long), WorkflowDefinitionId now Guid (T2 PublicId AK via JOIN)
    internal sealed class RuleRow
    {
        public long Id { get; set; }
        public Guid TenantId { get; set; }
        public Guid WorkflowDefinitionId { get; set; }
        public string? EntityType { get; set; }
        public string? EntitySubType { get; set; }
        public int Specificity { get; set; }
        public int Priority { get; set; }
        public bool IsActive { get; set; }
        public string? Description { get; set; }
        public DateTimeOffset EffectiveFrom { get; set; }
        public DateTimeOffset? EffectiveUntil { get; set; }
    }
}
