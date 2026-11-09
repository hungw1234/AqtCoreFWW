using AqtCoreFW.Rules.Application.DTOs;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Rules.Application.Queries.GetRuleSet;

public sealed class GetRuleSetQueryHandler(
    IReadDbConnection db,
    ITenantContext tenantContext,
    ILogger<GetRuleSetQueryHandler> logger)
    : IRequestHandler<GetRuleSetQuery, Result<RuleSetDto>>
{
    public async Task<Result<RuleSetDto>> Handle(
        GetRuleSetQuery request,
        CancellationToken cancellationToken)
    {
        // Defence-in-depth: reject cross-tenant RuleSet read.
        // request.TenantId comes from controller's ResolveTenantId() (JWT claim).
        // Malicious/buggy call path that supplies a different value is blocked here.
        if (!tenantContext.IsSystemAdmin && request.TenantId != tenantContext.TenantId)
        {
            logger.LogWarning(
                "Cross-tenant GetRuleSet denied: currentTenantId={CurrentTenantId}, attemptedTenantId={AttemptedTenantId}",
                tenantContext.TenantId, request.TenantId);
            return Result.Fail(new ValidationError("Cross-tenant access denied"));
        }

        // T2 RuleSet: external addressing via PublicId — project PublicId AS Id.
        // Inner long Id used only for joining child Rules; not surfaced to DTO.
        const string ruleSetSql = """
            SELECT Id AS InternalId, PublicId AS Id, TenantId, Name, Description, Category, Version,
                   Status, TargetEntityType, CreatedAt, UpdatedAt
            FROM rules.RuleSets
            WHERE PublicId = @RuleSetId AND TenantId = @TenantId AND IsDeleted = 0
            """;

        // T2 Rule: project PublicId AS Id; RuleSetId field surfaces parent's Guid PublicId
        // (filter via internal long FK to avoid second SELECT).
        const string rulesSql = """
            SELECT r.PublicId AS Id, @RuleSetPublicId AS RuleSetId, r.Name, r.Expression, r.Priority,
                   r.Enabled, r.ErrorMessage, r.FailureAction
            FROM rules.Rules r
            WHERE r.RuleSetId = @RuleSetInternalId AND r.IsDeleted = 0
            ORDER BY r.Priority
            """;

        var row = await db.QueryFirstOrDefaultAsync<RuleSetRow>(
            ruleSetSql, new { request.RuleSetId, request.TenantId }, cancellationToken);

        if (row is null)
            return Result.Fail(new NotFoundError($"RuleSet {request.RuleSetId} not found."));

        var rules = (await db.QueryAsync<RuleDto>(
            rulesSql, new { RuleSetInternalId = row.InternalId, RuleSetPublicId = row.Id }, cancellationToken)).ToList();

        return Result.Ok(new RuleSetDto(
            row.Id, row.TenantId, row.Name, row.Description, row.Category,
            row.Version, row.Status, row.TargetEntityType,
            row.CreatedAt, row.UpdatedAt, rules));
    }

    // Dapper requires parameterless ctor — positional records with nullable params fail materialization
    private sealed class RuleSetRow
    {
        public long InternalId { get; set; }
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? Category { get; set; }
        public int Version { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? TargetEntityType { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset? UpdatedAt { get; set; }
    }
}
