using AqtCoreFW.Rules.Application.DTOs;
using AqtCoreFW.Rules.Domain.Services;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace AqtCoreFW.Rules.Application.Queries.ExplainRules;

/// <summary>
/// Returns full explain tree: each rule, whether it fired, condition values, and outcome.
/// No audit event — read-only diagnostic operation.
/// </summary>
public sealed class ExplainRulesQueryHandler(
    IRuleEvaluator evaluator,
    ITenantContext tenantContext,
    ILogger<ExplainRulesQueryHandler> logger)
    : IRequestHandler<ExplainRulesQuery, Result<RuleEvaluationResultDto>>
{
    public async Task<Result<RuleEvaluationResultDto>> Handle(
        ExplainRulesQuery request,
        CancellationToken cancellationToken)
    {
        // Defence-in-depth: reject cross-tenant explain request.
        if (!tenantContext.IsSystemAdmin && request.TenantId != tenantContext.TenantId)
        {
            logger.LogWarning(
                "Cross-tenant ExplainRules denied: currentTenantId={CurrentTenantId}, attemptedTenantId={AttemptedTenantId}",
                tenantContext.TenantId, request.TenantId);
            return Result.Fail(new ValidationError("Cross-tenant access denied"));
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var output = await evaluator.EvaluateAsync(request.RuleSetId, request.Input, cancellationToken);
        sw.Stop();

        var items = output.Results
            .Select(r => new RuleResultItemDto(r.RuleName, r.IsSuccess, r.ExceptionMessage))
            .ToList();

        // Detailed explain tree serialised as JSON string
        var explainTree = JsonSerializer.Serialize(new
        {
            RuleSetId = request.RuleSetId,
            IsSuccess = output.IsSuccess,
            DurationMs = sw.ElapsedMilliseconds,
            Rules = items.Select(i => new
            {
                i.RuleName,
                Fired = i.IsSuccess,
                Reason = i.ExceptionMessage ?? (i.IsSuccess ? "condition met" : "condition not met")
            })
        });

        return Result.Ok(new RuleEvaluationResultDto(
            output.IsSuccess, items, sw.ElapsedMilliseconds, ExplainTrace: explainTree));
    }
}
