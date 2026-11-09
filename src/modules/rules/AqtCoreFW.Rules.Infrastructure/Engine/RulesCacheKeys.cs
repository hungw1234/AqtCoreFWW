namespace AqtCoreFW.Rules.Infrastructure.Engine;

/// <summary>
/// DRY helper for RulesEngine cache key generation — used by evaluator and subscriber.
/// </summary>
internal static class RulesCacheKeys
{
    public static string ForEngine(Guid ruleSetId) => $"rules:engine:{ruleSetId}";
}
