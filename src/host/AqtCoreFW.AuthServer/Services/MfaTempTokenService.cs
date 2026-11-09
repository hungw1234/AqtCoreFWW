using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Memory;
using System.Text.Json;

namespace AqtCoreFW.AuthServer.Services;

/// <summary>
/// Issues and validates single-use MFA challenge temp tokens (QĐ742 1.6.b).
///
/// Flow:
///   1. ROPC succeeds but user has MFA enrolled (TotpEnrolledAt != null) →
///      Exchange calls IssueAsync → returns opaque token string + tokenId.
///   2. FE calls POST /connect/mfa-challenge with temp_token + TOTP code.
///   3. MfaChallengeController calls ValidateAsync → returns userId if valid.
///   4. Cache entry keyed on tokenId is set on consume — prevents replay.
///
/// Security properties:
///   - ITimeLimitedDataProtector: 5-min TTL, AES-CBC + HMAC via ASP.NET Core Data Protection.
///   - Audience binding: payload includes "aud=mfa-challenge" — rejected if absent.
///   - Single-use: IMemoryCache marks tokenId consumed on first validation.
///   - Both issue and consume happen in AuthServer — same key ring, no cross-host risk.
/// </summary>
public sealed class MfaTempTokenService
{
    private const string Purpose = "MfaChallenge";
    private const string Audience = "mfa-challenge";
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan CacheConsumedTtl = TimeSpan.FromMinutes(10); // outlives token TTL

    private readonly ITimeLimitedDataProtector _protector;
    private readonly IMemoryCache _cache;

    public MfaTempTokenService(IDataProtectionProvider dpProvider, IMemoryCache cache)
    {
        _protector = dpProvider
            .CreateProtector(Purpose)
            .ToTimeLimitedDataProtector();
        _cache = cache;
    }

    /// <summary>
    /// Issues a new temp_token for the given user.
    /// Returns the opaque token string (base64url-encoded protected payload).
    /// </summary>
    public string Issue(Guid userId)
    {
        var tokenId = Guid.NewGuid();
        var payload = new TempTokenPayload(tokenId, userId, Audience, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        var json = JsonSerializer.Serialize(payload);
        return _protector.Protect(json, Ttl);
    }

    /// <summary>
    /// Validates a temp_token: verifies TTL, audience, and single-use constraint.
    /// Returns the userId on success, null on any failure (expired / wrong-aud / replayed).
    /// </summary>
    public Guid? Validate(string token)
    {
        string json;
        try
        {
            json = _protector.Unprotect(token, out _);
        }
        catch (Exception)
        {
            // Covers expired tokens (CryptographicException) and tampered payloads.
            return null;
        }

        TempTokenPayload payload;
        try
        {
            payload = JsonSerializer.Deserialize<TempTokenPayload>(json)
                ?? throw new InvalidOperationException("Null payload.");
        }
        catch (Exception)
        {
            return null;
        }

        // Audience check — defense in depth against purpose confusion.
        if (payload.Aud != Audience)
            return null;

        // Single-use: check consumed cache key.
        var consumedKey = ConsumedKey(payload.TokenId);
        if (_cache.TryGetValue(consumedKey, out _))
            return null;

        // Mark consumed — set before returning to close race window.
        // Size=1 required for IMemoryCache instances configured with SizeLimit (test fixtures).
        var consumedOpts = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = CacheConsumedTtl,
            Size = 1,
        };
        _cache.Set(consumedKey, true, consumedOpts);
        return payload.UserId;
    }

    private static string ConsumedKey(Guid tokenId) => $"mfa:temp_token:consumed:{tokenId}";

    private sealed record TempTokenPayload(
        Guid TokenId,
        Guid UserId,
        string Aud,
        long IssuedAt);
}
