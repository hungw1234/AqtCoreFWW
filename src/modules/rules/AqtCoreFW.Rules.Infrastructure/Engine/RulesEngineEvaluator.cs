using AqtCoreFW.Rules.Domain.Services;
using AqtCoreFW.Rules.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using RulesEngine.Models;
using StackExchange.Redis;
using System.Text.Json;

namespace AqtCoreFW.Rules.Infrastructure.Engine;

/// <summary>
/// IRuleEvaluator implementation backed by Microsoft.RulesEngine (open-source).
/// Workflows are compiled once and cached by ruleSetId — cache invalidated on activation.
/// </summary>
public sealed class RulesEngineEvaluator(
    RulesDbContext dbContext,
    IMemoryCache cache,
    IConnectionMultiplexer redis,
    ILogger<RulesEngineEvaluator> logger)
    : IRuleEvaluator
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);

    public async Task<RuleEvalDomainResult> EvaluateAsync(
        Guid ruleSetId,
        JsonDocument input,
        CancellationToken ct = default)
    {
        var engine = await GetOrBuildEngineAsync(ruleSetId, ct);

        // Convert JsonDocument to dynamic — RulesEngine resolves property paths via reflection
        var inputJson = input.RootElement.GetRawText();
        var inputObj = JsonSerializer.Deserialize<dynamic>(inputJson) ?? new { };

        List<RulesEngine.Models.RuleResultTree> results =
            await engine.ExecuteAllRulesAsync(ruleSetId.ToString(), inputObj);

        var items = results
            .Select(r => new RuleEvalItemResult(r.Rule.RuleName, r.IsSuccess, r.ExceptionMessage))
            .ToList();

        return new RuleEvalDomainResult(items.All(r => r.IsSuccess), items);
    }

    /// <summary>
    /// Removes cached workflow from local pod and publishes invalidation to other pods via Redis pub/sub.
    /// Fire-and-forget publish — Redis failure degrades to local-only invalidation (acceptable per design).
    /// </summary>
    public void InvalidateCache(Guid ruleSetId)
    {
        // Clear local first — callers see fresh state immediately even if Redis is slow
        cache.Remove(RulesCacheKeys.ForEngine(ruleSetId));

        // Notify other pods (fire-and-forget)
        try
        {
            redis.GetSubscriber().Publish(
                RedisChannel.Literal("rules:cache-invalidate"),
                ruleSetId.ToString());
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "RulesEngineEvaluator: Redis publish failed for {RuleSetId} — local invalidation only", ruleSetId);
        }
    }

    private async Task<RulesEngine.RulesEngine> GetOrBuildEngineAsync(Guid ruleSetId, CancellationToken ct)
    {
        // ruleSetId is the external Guid PublicId (T2 addressing — cross-module exposed).
        var key = RulesCacheKeys.ForEngine(ruleSetId);
        if (cache.TryGetValue(key, out RulesEngine.RulesEngine? cached) && cached is not null)
            return cached;

        var ruleSet = await dbContext.RuleSets
            .AsNoTracking()
            .Include(rs => rs.Rules)
            .FirstOrDefaultAsync(rs => rs.PublicId == ruleSetId && !rs.IsDeleted, ct)
            ?? throw new InvalidOperationException($"RuleSet {ruleSetId} not found.");

        var workflow = new Workflow
        {
            WorkflowName = ruleSetId.ToString(),
            Rules = ruleSet.Rules
                .Where(r => r.Enabled)
                .OrderBy(r => r.Priority)
                .Select(r => new Rule
                {
                    RuleName = r.Name,
                    Expression = r.Expression,
                    ErrorMessage = r.ErrorMessage ?? $"Rule '{r.Name}' failed."
                })
                .ToArray()
        };

        var engine = new RulesEngine.RulesEngine([workflow]);
        cache.Set(key, engine, CacheTtl);
        return engine;
    }

}
