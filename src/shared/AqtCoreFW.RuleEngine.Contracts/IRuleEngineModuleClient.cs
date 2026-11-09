namespace AqtCoreFW.SharedKernel.Contracts.Clients;

/// <summary>
/// Cross-module interface for rule engine access.
/// Monolith: InProcessRuleEngineModuleClient (direct DB query, zero overhead).
/// Microservice: GrpcRuleEngineModuleClient (when module extracted).
/// </summary>
public interface IRuleEngineModuleClient
{
    Task<RuleEvaluationOutput> EvaluateAsync(Guid ruleSetId, System.Text.Json.JsonDocument input, Guid tenantId, CancellationToken ct = default);
}

public sealed record RuleEvaluationOutput(bool IsSuccess, IReadOnlyList<RuleResultItem> Results);
public sealed record RuleResultItem(string RuleName, bool IsSuccess, string? ExceptionMessage);
