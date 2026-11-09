using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Application.Caching;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Infrastructure.ApiKeys;

/// <summary>
/// API key lifecycle service — create, list, revoke, rotate.
/// Key format: aqt_{prefix8}_{random24}. SHA-256 stored; plaintext returned once.
/// Redis cache TTL 5min for hot-path M2M lookup (prefix → hash).
/// </summary>
public sealed class ApiKeyService(
    ApiKeyDbContext db,
    ICacheService cache,
    ILogger<ApiKeyService> logger,
    IGuidGenerator guidGen)
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    // ── Create ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Generates a new API key. Returns plaintext — caller must transmit once only.
    /// </summary>
    public async Task<ApiKeyCreateResult> CreateAsync(
        string name, string clientId, Guid tenantId, string createdBy,
        DateTimeOffset? expiresAt, IReadOnlyList<string>? scopes, CancellationToken ct)
    {
        var random = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24))
            .Replace("+", "A").Replace("/", "B").Replace("=", "C")[..24];
        var prefix = random[..8];
        var plaintext = $"aqt_{prefix}_{random}";
        var hash = ComputeHash(plaintext);

        var record = new ApiKeyRecord
        {
            Id = guidGen.NewIdRandom(), // auth-adjacent: v4 random (no timestamp leak)
            Name = name,
            Prefix = prefix,
            KeyHash = hash,
            ClientId = clientId,
            TenantId = tenantId,
            CreatedBy = createdBy,
            ExpiresAt = expiresAt,
            ScopesJson = JsonSerializer.Serialize(scopes ?? [])
        };

        if (scopes is { Count: > 0 })
        {
            foreach (var scope in scopes)
                record.Scopes.Add(new ApiKeyScope
                {
                    Id = guidGen.NewIdRandom(), // auth-adjacent: v4 random
                    TenantId = tenantId,
                    ScopePermission = scope
                });
        }

        db.ApiKeys.Add(record);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("API key created: {Name} prefix={Prefix} tenant={TenantId}",
            name, prefix, tenantId);

        return new ApiKeyCreateResult(record.Id, plaintext, prefix, record.CreatedAt);
    }

    // ── List ─────────────────────────────────────────────────────────────────

    public async Task<List<ApiKeyRecord>> ListAsync(Guid tenantId, CancellationToken ct) =>
        await db.ApiKeys
            .Include(k => k.Scopes)
            .Where(k => k.TenantId == tenantId && !k.IsRevoked)
            .OrderByDescending(k => k.CreatedAt)
            .ToListAsync(ct);

    // ── Revoke ───────────────────────────────────────────────────────────────

    public async Task<bool> RevokeAsync(Guid id, Guid tenantId, CancellationToken ct)
    {
        var key = await db.ApiKeys.FirstOrDefaultAsync(
            k => k.Id == id && k.TenantId == tenantId, ct);

        if (key is null) return false;

        key.IsRevoked = true;
        await db.SaveChangesAsync(ct);
        await cache.RemoveAsync($"apikey:{key.Prefix}");

        logger.LogInformation("API key revoked: {Id} prefix={Prefix}", id, key.Prefix);
        return true;
    }

    // ── Rotate ───────────────────────────────────────────────────────────────

    /// <summary>Atomically revokes old key and issues a new one. Returns new plaintext.</summary>
    public async Task<ApiKeyCreateResult?> RotateAsync(Guid id, Guid tenantId, string rotatedBy, CancellationToken ct)
    {
        var old = await db.ApiKeys
            .Include(k => k.Scopes)
            .FirstOrDefaultAsync(k => k.Id == id && k.TenantId == tenantId, ct);

        if (old is null) return null;

        old.IsRevoked = true;
        await cache.RemoveAsync($"apikey:{old.Prefix}");

        var scopeNames = old.Scopes.Select(s => s.ScopePermission).ToList();
        await db.SaveChangesAsync(ct);

        return await CreateAsync(old.Name, old.ClientId, tenantId, rotatedBy,
            old.ExpiresAt, scopeNames, ct);
    }

    // ── Validate (used by auth handler) ──────────────────────────────────────

    /// <summary>
    /// Validates a plaintext key. Returns the record if valid; null if invalid/revoked/expired.
    /// Uses Redis cache to avoid DB hit on every M2M request.
    /// </summary>
    public async Task<ApiKeyRecord?> ValidateAsync(string plaintext, CancellationToken ct)
    {
        if (!TryParseKey(plaintext, out var prefix, out _)) return null;

        var cacheKey = $"apikey:{prefix}";
        var cached = await cache.GetAsync<CachedApiKey>(cacheKey);

        ApiKeyRecord? record;
        if (cached is not null)
        {
            // Fast path — verify hash without DB round-trip
            if (cached.IsRevoked || (cached.ExpiresAt.HasValue && cached.ExpiresAt < DateTimeOffset.UtcNow))
                return null;
            if (cached.KeyHash != ComputeHash(plaintext)) return null;

            // Load full record for scopes (scopes rarely change, acceptable DB hit)
            record = await db.ApiKeys.Include(k => k.Scopes)
                .FirstOrDefaultAsync(k => k.Prefix == prefix, ct);
        }
        else
        {
            record = await db.ApiKeys.Include(k => k.Scopes)
                .FirstOrDefaultAsync(k => k.Prefix == prefix, ct);

            if (record is null) return null;

            await cache.SetAsync(cacheKey,
                new CachedApiKey(record.KeyHash, record.IsRevoked, record.ExpiresAt), CacheTtl);
        }

        if (record is null || record.IsRevoked) return null;
        if (record.ExpiresAt.HasValue && record.ExpiresAt < DateTimeOffset.UtcNow) return null;
        if (record.KeyHash != ComputeHash(plaintext)) return null;

        return record;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string ComputeHash(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static bool TryParseKey(string key, out string prefix, out string rest)
    {
        prefix = rest = string.Empty;
        // Expected: aqt_{prefix8}_{random24}
        var parts = key.Split('_');
        if (parts.Length != 3 || parts[0] != "aqt" || parts[1].Length != 8)
            return false;
        prefix = parts[1];
        rest = parts[2];
        return true;
    }
}

// ── Value objects ─────────────────────────────────────────────────────────────

public sealed record ApiKeyCreateResult(Guid Id, string Plaintext, string Prefix, DateTimeOffset CreatedAt);

internal sealed record CachedApiKey(string KeyHash, bool IsRevoked, DateTimeOffset? ExpiresAt);
