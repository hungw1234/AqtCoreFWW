using AqtCoreFW.Rules.Application.DTOs;
using AqtCoreFW.Rules.Domain.Services;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Rules.Application.Queries.SimulateRules;

/// <summary>
/// Runs evaluation with trace enabled but does NOT publish an audit event.
/// Safe for UI previewing / what-if analysis without polluting the audit log.
/// </summary>
public sealed class SimulateRulesQueryHandler(
    IRuleEvaluator evaluator,
    ITenantContext tenantContext,
    ILogger<SimulateRulesQueryHandler> logger)
    : IRequestHandler<SimulateRulesQuery, Result<RuleEvaluationResultDto>>
{
    public async Task<Result<RuleEvaluationResultDto>> Handle(
        SimulateRulesQuery request,
        CancellationToken cancellationToken)
    {
        // Defence-in-depth: reject cross-tenant simulation request.
        if (!tenantContext.IsSystemAdmin && request.TenantId != tenantContext.TenantId)
        {
            logger.LogWarning(
                "Cross-tenant SimulateRules denied: currentTenantId={CurrentTenantId}, attemptedTenantId={AttemptedTenantId}",
                tenantContext.TenantId, request.TenantId);
            return Result.Fail(new ValidationError("Cross-tenant access denied"));
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var output = await evaluator.EvaluateAsync(request.RuleSetId, request.Input, cancellationToken);
        sw.Stop();

        var items = output.Results
            .Select(r => new RuleResultItemDto(r.RuleName, r.IsSuccess, r.ExceptionMessage))
            .ToList();

        // Build a simple trace string from the per-rule results
        var trace = string.Join("; ", items.Select(i =>
            $"{i.RuleName}={( i.IsSuccess ? "PASS" : "FAIL" )}" +
            (i.ExceptionMessage is not null ? $"({i.ExceptionMessage})" : string.Empty)));

        return Result.Ok(new RuleEvaluationResultDto(
            output.IsSuccess, items, sw.ElapsedMilliseconds, ExplainTrace: trace));
    }
}
