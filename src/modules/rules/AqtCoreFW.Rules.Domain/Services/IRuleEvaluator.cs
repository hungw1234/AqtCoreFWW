using System.Text.Json;

namespace AqtCoreFW.Rules.Domain.Services;

/// <summary>
/// Domain-level contract for evaluating a RuleSet against a JSON input.
/// Returns a lightweight domain result — Infrastructure maps to RuleEvaluationOutput for cross-module use.
/// </summary>
public interface IRuleEvaluator
{
    /// <summary>Evaluates all enabled rules in the RuleSet against the given input.</summary>
    Task<RuleEvalDomainResult> EvaluateAsync(
        Guid ruleSetId,
        JsonDocument input,
        CancellationToken ct = default);

    /// <summary>
    /// Evicts the compiled-engine cache entry for a RuleSet across all pods.
    /// Caller clears the local IMemoryCache first, then fire-and-forget publishes
    /// a Redis pub/sub message so other pods drop their local copy.
    /// </summary>
    void InvalidateCache(Guid ruleSetId);
}

/// <summary>Domain-owned evaluation result — avoids Domain depending on SharedKernel.Contracts.</summary>
public sealed record RuleEvalDomainResult(
    bool IsSuccess,
    IReadOnlyList<RuleEvalItemResult> Results);

public sealed record RuleEvalItemResult(
    string RuleName,
    bool IsSuccess,
    string? ExceptionMessage);
