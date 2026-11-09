using AqtCoreFW.Identity.Domain.Services;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace AqtCoreFW.Identity.Infrastructure.Services;

/// <summary>
/// Redis-backed permission version counter.
/// Key: perm-version:{userId} — incremented atomically on any permission change.
/// Falls back to 0 (no-op) when Redis is unavailable, matching the pattern
/// used in ClaimsEnrichmentTransformer for resilience.
/// </summary>
public sealed class PermissionVersionService : IPermissionVersionService
{
    private const string KeyPrefix = "perm-version:";

    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<PermissionVersionService> _logger;

    public PermissionVersionService(
        IConnectionMultiplexer redis,
        ILogger<PermissionVersionService> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<int> GetVersionAsync(Guid userId)
    {
        try
        {
            var db = _redis.GetDatabase();
            var value = await db.StringGetAsync($"{KeyPrefix}{userId}");
            if (value.HasValue && int.TryParse(value.ToString(), out var version))
                return version;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis unavailable reading perm-version:{UserId}, returning 0", userId);
        }

        return 0;
    }

    /// <inheritdoc />
    public async Task IncrementAsync(Guid userId)
    {
        try
        {
            var db = _redis.GetDatabase();
            await db.StringIncrementAsync($"{KeyPrefix}{userId}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis unavailable incrementing perm-version:{UserId}", userId);
        }
    }
}
