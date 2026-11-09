using System.Text.Json;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Models;
using AqtCoreFW.Identity.Domain.Services;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace AqtCoreFW.Identity.Infrastructure.Services;

/// <summary>
/// Evaluates policy rules for a permission code against a caller-supplied context dictionary.
///
/// Algorithm:
///   1. Load active rules for permissionCode from Redis (key: rule:{permissionCode}, TTL 5 min),
///      falling back to IdentityDbContext on cache miss.
///   2. Sort rules by Priority descending — highest priority evaluated first.
///   3. Parse ConditionExpression JSON: [{"field":"X","op":"eq","value":"Y"}].
///      Supported ops: eq | neq | in | notin. All conditions within a rule are ANDed.
///   4. First matching Deny → return denied. First matching Allow → return allowed.
///      No match → return allowed (open by default).
/// </summary>
public sealed class PolicyRuleEvaluator : IPolicyRuleEvaluator
{
    private const int CacheTtlSeconds = 300; // 5 min
    private const string CacheKeyPrefix = "rule:";

    private readonly IdentityDbContext _db;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<PolicyRuleEvaluator> _logger;

    public PolicyRuleEvaluator(
        IdentityDbContext db,
        IConnectionMultiplexer redis,
        ILogger<PolicyRuleEvaluator> logger)
    {
        _db = db;
        _redis = redis;
        _logger = logger;
    }

    public async Task<PolicyDecision> EvaluateAsync(
        string permissionCode,
        IReadOnlyDictionary<string, string> context,
        CancellationToken ct = default)
    {
        var rules = await GetRulesAsync(permissionCode, ct);

        foreach (var rule in rules.OrderByDescending(r => r.Priority))
        {
            if (!MatchesConditions(rule, context))
                continue;

            if (rule.Effect == PolicyEffect.Deny)
                return new PolicyDecision(false, $"Denied by policy rule '{rule.Code}'", rule.Code);

            return new PolicyDecision(true, RuleCode: rule.Code);
        }

        // No rule matched — allow by default
        return new PolicyDecision(true);
    }

    // --- Rule loading (Redis cache → DB fallback) ---

    private async Task<IReadOnlyList<PolicyRule>> GetRulesAsync(string permissionCode, CancellationToken ct)
    {
        var cacheKey = $"{CacheKeyPrefix}{permissionCode}";
        try
        {
            var redisDb = _redis.GetDatabase();
            var cached = await redisDb.StringGetAsync(cacheKey);
            if (cached.HasValue)
            {
                var deserialized = JsonSerializer.Deserialize<List<PolicyRuleDto>>(cached.ToString());
                if (deserialized is not null)
                    return deserialized.Select(MapFromDto).ToList();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis unavailable reading rule:{PermissionCode}, falling back to DB", permissionCode);
        }

        var rules = await _db.Set<PolicyRule>()
            .Where(r => r.PermissionCode == permissionCode && r.IsActive)
            .AsNoTracking()
            .ToListAsync(ct);

        await TryCacheRulesAsync(cacheKey, rules);
        return rules;
    }

    private async Task TryCacheRulesAsync(string cacheKey, IReadOnlyList<PolicyRule> rules)
    {
        try
        {
            var redisDb = _redis.GetDatabase();
            var dtos = rules.Select(MapToDto).ToList();
            var json = JsonSerializer.Serialize(dtos);
            await redisDb.StringSetAsync(cacheKey, json, TimeSpan.FromSeconds(CacheTtlSeconds));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis unavailable writing rule cache key {Key}", cacheKey);
        }
    }

    // --- Condition matching ---

    private static bool MatchesConditions(PolicyRule rule, IReadOnlyDictionary<string, string> context)
    {
        if (string.IsNullOrWhiteSpace(rule.ConditionExpression))
            return true; // Unconditional rule always matches

        List<ConditionDto>? conditions;
        try
        {
            conditions = JsonSerializer.Deserialize<List<ConditionDto>>(
                rule.ConditionExpression,
                JsonOptions);
        }
        catch
        {
            return false; // Malformed condition = no match (fail safe)
        }

        if (conditions is null || conditions.Count == 0)
            return true;

        // All conditions must be satisfied (AND logic)
        return conditions.All(c => EvaluateCondition(c, context));
    }

    private static bool EvaluateCondition(ConditionDto cond, IReadOnlyDictionary<string, string> ctx)
    {
        if (!ctx.TryGetValue(cond.Field, out var actual))
            return false;

        return cond.Op switch
        {
            "eq"    => string.Equals(actual, cond.Value, StringComparison.OrdinalIgnoreCase),
            "neq"   => !string.Equals(actual, cond.Value, StringComparison.OrdinalIgnoreCase),
            "in"    => cond.Value.Split(',', StringSplitOptions.TrimEntries)
                           .Any(v => string.Equals(actual, v, StringComparison.OrdinalIgnoreCase)),
            "notin" => !cond.Value.Split(',', StringSplitOptions.TrimEntries)
                           .Any(v => string.Equals(actual, v, StringComparison.OrdinalIgnoreCase)),
            _       => false
        };
    }

    // --- DTO helpers (lightweight serializable form for Redis) ---

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static PolicyRuleDto MapToDto(PolicyRule r) => new()
    {
        Code = r.Code,
        PermissionCode = r.PermissionCode,
        ConditionExpression = r.ConditionExpression,
        Effect = (int)r.Effect,
        Priority = r.Priority,
        LogOnDeny = r.LogOnDeny
    };

    // Id is not needed for evaluation — only evaluation fields are mapped back from Redis.
    private static PolicyRule MapFromDto(PolicyRuleDto dto) => new()
    {
        Code = dto.Code,
        PermissionCode = dto.PermissionCode,
        ConditionExpression = dto.ConditionExpression,
        Effect = (PolicyEffect)dto.Effect,
        Priority = dto.Priority,
        IsActive = true,
        LogOnDeny = dto.LogOnDeny
    };

    // Minimal DTO for Redis — avoids pulling EF nav-prop graph into cache.
    // Id omitted — not needed for evaluation; PolicyRule.Id has private set (T1).
    private sealed class PolicyRuleDto
    {
        public string Code { get; set; } = string.Empty;
        public string PermissionCode { get; set; } = string.Empty;
        public string? ConditionExpression { get; set; }
        public int Effect { get; set; }
        public int Priority { get; set; }
        public bool LogOnDeny { get; set; }
    }

    private sealed class ConditionDto
    {
        public string Field { get; set; } = string.Empty;
        public string Op { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }
}
