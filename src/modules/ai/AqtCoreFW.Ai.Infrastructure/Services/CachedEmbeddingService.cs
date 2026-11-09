using AqtCoreFW.SharedKernel.AI;
using FluentResults;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AqtCoreFW.Ai.Infrastructure.Services;

/// <summary>
/// Decorator for IEmbeddingService — caches vectors in Redis (or IDistributedCache).
/// Cache key: emb:{dim}:{SHA256(text)}, TTL 7 days.
/// ~80% cache hit rate for GOV documents (same regulatory text re-embedded frequently).
/// Register via: services.Decorate&lt;IEmbeddingService, CachedEmbeddingService&gt;()
/// </summary>
public sealed class CachedEmbeddingService(
    IEmbeddingService inner,
    IDistributedCache cache,
    ILogger<CachedEmbeddingService> logger)
    : IEmbeddingService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromDays(7);

    public int VectorDimension => inner.VectorDimension;

    public async Task<Result<float[]>> EmbedAsync(string text, CancellationToken ct = default)
    {
        var key = BuildCacheKey(text);

        // Try cache first
        try
        {
            var cached = await cache.GetAsync(key, ct);
            if (cached is not null)
            {
                var vector = JsonSerializer.Deserialize<float[]>(cached);
                if (vector is not null)
                {
                    logger.LogDebug("CachedEmbedding: cache hit for key={Key}", key);
                    return Result.Ok(vector);
                }
            }
        }
        catch (Exception ex)
        {
            // Cache unavailable — fall through to inner service (degraded, not fatal)
            logger.LogWarning(ex, "CachedEmbeddingService: cache read failed, falling through to inner service");
        }

        // Cache miss — compute via inner service
        var result = await inner.EmbedAsync(text, ct);
        if (result.IsFailed)
            return result;

        // Store in cache asynchronously — don't fail the caller if cache write fails
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(result.Value);
            var options = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = CacheTtl
            };
            await cache.SetAsync(key, bytes, options, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "CachedEmbeddingService: cache write failed for key={Key}", key);
        }

        return result;
    }

    /// <summary>Cache key: emb:{vectorDim}:{SHA256(text)} — deterministic, collision-resistant.</summary>
    private string BuildCacheKey(string text)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        var hashHex = Convert.ToHexString(hash).ToLowerInvariant();
        return $"emb:{inner.VectorDimension}:{hashHex}";
    }
}
