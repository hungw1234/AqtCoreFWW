using AqtCoreFW.Rules.Domain.Services;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.SharedKernel.Domain;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace AqtCoreFW.Rules.Infrastructure.Clients;

/// <summary>
/// In-process implementation of IRuleEngineModuleClient.
/// Maps domain RuleEvalDomainResult → SharedKernel RuleEvaluationOutput.
/// Swap for GrpcRuleEngineModuleClient when the module is extracted to a microservice.
/// </summary>
public sealed class InProcessRuleEngineModuleClient(
    IRuleEvaluator evaluator,
    ITenantContext tenantContext,
    ILogger<InProcessRuleEngineModuleClient> logger)
    : IRuleEngineModuleClient
{
    public async Task<RuleEvaluationOutput> EvaluateAsync(
        Guid ruleSetId,
        JsonDocument input,
        Guid tenantId,
        CancellationToken ct = default)
    {
        // Defence-in-depth: internal invariant breach detection.
        // Cross-module callers (e.g., Forms) pass tenantId from their context.
        // If tenantId doesn't match tenantContext.TenantId, it indicates a bug
        // in the calling code — throw to surface as HTTP 500 (not silent fail).
        if (!tenantContext.IsSystemAdmin && tenantId != tenantContext.TenantId)
        {
            logger.LogWarning(
                "Cross-tenant IRuleEngineModuleClient.EvaluateAsync rejected: currentTenantId={CurrentTenantId}, passedTenantId={PassedTenantId}",
                tenantContext.TenantId, tenantId);
            throw new InvalidOperationException("Cross-tenant evaluator call rejected");
        }

        var domainResult = await evaluator.EvaluateAsync(ruleSetId, input, ct);

        var items = domainResult.Results
            .Select(r => new RuleResultItem(r.RuleName, r.IsSuccess, r.ExceptionMessage))
            .ToList();

        return new RuleEvaluationOutput(domainResult.IsSuccess, items);
    }
}
