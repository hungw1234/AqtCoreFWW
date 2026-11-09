using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace AqtCoreFW.Ai.Infrastructure.Services;

/// <summary>
/// Per-tenant monthly token budget tracker backed by Redis.
/// Key pattern: ai:tokens:{tenantId}:{yyyy-MM}
/// Uses INCRBY for atomic increment. TTL set to 32 days on first write.
/// </summary>
public interface ITokenBudgetTracker
{
    /// <summary>Increments token count for tenant. Returns false if budget is exceeded.</summary>
    Task<bool> TrackAsync(Guid tenantId, int tokens, long monthlyLimit, CancellationToken ct = default);

    /// <summary>Returns remaining tokens for current month. Returns long.MaxValue if Redis unavailable.</summary>
    Task<long> GetRemainingAsync(Guid tenantId, long monthlyLimit, CancellationToken ct = default);
}

public sealed class TokenBudgetTracker(
    IConnectionMultiplexer redis,
    ILogger<TokenBudgetTracker> logger)
    : ITokenBudgetTracker
{
    private static string BudgetKey(Guid tenantId) =>
        $"ai:tokens:{tenantId}:{DateTimeOffset.UtcNow:yyyy-MM}";

    public async Task<bool> TrackAsync(
        Guid tenantId,
        int tokens,
        long monthlyLimit,
        CancellationToken ct = default)
    {
        try
        {
            var db = redis.GetDatabase();
            var key = BudgetKey(tenantId);

            var newTotal = await db.StringIncrementAsync(key, tokens);

            // Set TTL on first write (key didn't exist before increment)
            if (newTotal == tokens)
                await db.KeyExpireAsync(key, TimeSpan.FromDays(32));

            if (newTotal > monthlyLimit)
            {
                logger.LogWarning(
                    "Token budget exceeded: tenant={TenantId} used={Used} limit={Limit}",
                    tenantId, newTotal, monthlyLimit);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            // Redis unavailable — fail open (allow request, log warning)
            logger.LogWarning(ex,
                "TokenBudgetTracker Redis unavailable for tenant={TenantId}. Allowing request.", tenantId);
            return true;
        }
    }

    public async Task<long> GetRemainingAsync(
        Guid tenantId,
        long monthlyLimit,
        CancellationToken ct = default)
    {
        try
        {
            var db = redis.GetDatabase();
            var used = (long?)await db.StringGetAsync(BudgetKey(tenantId)) ?? 0L;
            return Math.Max(0, monthlyLimit - used);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "TokenBudgetTracker Redis unavailable for tenant={TenantId}. Returning MaxValue.", tenantId);
            return long.MaxValue;
        }
    }
}
