using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.Identity.Application.Authorization;
using AqtCoreFW.SharedKernel.Application.Caching;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace AqtCoreFW.Identity.Infrastructure.Authorization;

/// <summary>
/// Invalidates ABAC rule cache trÃªn Two-Tier (L1 IMemoryCache + L2 Redis) vÃ  phÃ¡t
/// tÃ­n hiá»‡u cross-pod qua Redis channel <c>abac-invalidate</c> Ä‘á»ƒ <see cref="AbacCacheSubscriber"/>
/// trÃªn cÃ¡c pod khÃ¡c evict L1 cá»§a chÃºng.
///
/// Singleton DI: <see cref="ICacheService"/> + <see cref="IConnectionMultiplexer"/> Ä‘á»u Singleton â†’ OK.
/// Key format dÃ¹ng <see cref="AbacCacheKeys"/> Ä‘á»ƒ handler (Scoped) + invalidator (Singleton) khÃ´ng drift.
/// </summary>
public sealed class AbacCacheInvalidator : IAbacCacheInvalidator
{
    private const string ChannelName = "abac-invalidate";

    private readonly ICacheService _cache;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<AbacCacheInvalidator> _logger;

    public AbacCacheInvalidator(
        ICacheService cache,
        IConnectionMultiplexer redis,
        ILogger<AbacCacheInvalidator> logger)
    {
        _cache = cache;
        _redis = redis;
        _logger = logger;
    }

    public async Task InvalidateAsync(Guid? tenantId, string attributeValue, CancellationToken ct = default)
    {
        var key = AbacCacheKeys.ForDepartment(tenantId, attributeValue);

        // (1) Evict L1 + L2 trÃªn pod hiá»‡n táº¡i (L2 lÃ  Redis shared â†’ evict 1 láº§n).
        await _cache.RemoveAsync(key, ct);

        // (2) Broadcast cho cÃ¡c pod khÃ¡c â€” chá»‰ L1 cáº§n evict (L2 Ä‘Ã£ gone tá»« bÆ°á»›c 1).
        try
        {
            await _redis.GetSubscriber().PublishAsync(RedisChannel.Literal(ChannelName), key);
        }
        catch (Exception ex)
        {
            // Publish fail khÃ´ng critical â€” L2 Ä‘Ã£ evict, L1 khÃ¡c pod sáº½ expire tá»± nhiÃªn (~10 min).
            _logger.LogWarning(ex,
                "ABAC cache publish failed for key '{Key}' â€” other pods' L1 may be stale until natural expiry",
                key);
        }
    }
}
