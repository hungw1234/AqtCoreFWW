using System.Security.Cryptography;
using System.Text;
using AqtCoreFW.Identity.Application.Signing;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AqtCoreFW.Identity.Infrastructure.Services.Signing.ViettelMysign;

/// <summary>
/// Issues and validates short-lived inbound tokens for Viettel Mysign callback authentication.
///
/// <para><b>Token lifecycle:</b>
/// <list type="number">
///   <item><see cref="IssueTokenAsync"/>: generates 32 random bytes, base64-encodes to raw token,
///   SHA-256 hashes it (uppercase hex), persists <see cref="MysignInboundToken"/> row, returns raw token.</item>
///   <item><see cref="TryValidateAsync"/>: SHA-256 hashes incoming Bearer token (same uppercase hex),
///   looks up row by hash, checks <see cref="MysignInboundToken.IsValid"/>, updates LastUsedAt.</item>
/// </list>
/// Raw token is returned ONCE to the caller and never stored.
/// </para>
/// </summary>
public sealed class MysignInboundTokenService(
    IdentityDbContext db,
    IOptionsMonitor<ViettelMysignOptions> opts,
    ILogger<MysignInboundTokenService> logger)
    : IMysignInboundTokenService
{
    /// <inheritdoc/>
    public async Task<string> IssueTokenAsync(CancellationToken ct = default)
    {
        // Generate 32 cryptographically random bytes → base64 raw token
        var rawBytes = new byte[32];
        RandomNumberGenerator.Fill(rawBytes);
        var rawToken = Convert.ToBase64String(rawBytes);

        // Hash for storage — uppercase hex so lookup is case-deterministic
        var hash = HashToken(rawToken);

        var ttl = TimeSpan.FromSeconds(opts.CurrentValue.CallbackTokenTtlSeconds);
        var record = MysignInboundToken.Issue(hash, ttl);

        db.MysignInboundTokens.Add(record);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        logger.LogDebug(
            "Mysign inbound token issued — hash prefix {HashPrefix}..., expires {ExpiresAt:u}",
            hash[..8], record.ExpiresAt);

        return rawToken;
    }

    /// <inheritdoc/>
    public async Task<bool> TryValidateAsync(string token, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(token))
            return false;

        var hash = HashToken(token);
        var record = await db.MysignInboundTokens
            .FirstOrDefaultAsync(t => t.TokenHash == hash, ct)
            .ConfigureAwait(false);

        if (record is null)
        {
            logger.LogDebug("Mysign token validation: hash not found");
            return false;
        }

        if (!record.IsValid(DateTime.UtcNow))
        {
            logger.LogDebug(
                "Mysign token validation: token invalid (revoked={Revoked}, expired={Expired})",
                record.IsRevoked, DateTime.UtcNow >= record.ExpiresAt);
            return false;
        }

        record.MarkUsed();
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return true;
    }

    /// <inheritdoc/>
    public async Task<MysignCallbackAuthOutcome> AuthenticateCallbackAsync(
        string? clientId, string? clientSecret, CancellationToken ct = default)
    {
        var current = opts.CurrentValue;

        // Reject with NotConfigured (→ 503) if callback credentials are absent. Empty clientId/secret
        // would otherwise pass the constant-time compare (both sides empty → match).
        if (string.IsNullOrEmpty(current.CallbackInboundClientId) ||
            string.IsNullOrEmpty(current.CallbackInboundClientSecret))
        {
            logger.LogWarning(
                "Mysign callback auth rejected: CallbackInboundClientId/Secret not configured. " +
                "Set Signing:Providers:ViettelMysign:CallbackInboundClientId/Secret.");
            return new MysignCallbackAuthOutcome(MysignCallbackAuthStatus.NotConfigured);
        }

        // Constant-time comparison prevents timing oracle attacks.
        var clientIdMatch = CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(clientId ?? string.Empty),
            Encoding.UTF8.GetBytes(current.CallbackInboundClientId));
        var clientSecretMatch = CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(clientSecret ?? string.Empty),
            Encoding.UTF8.GetBytes(current.CallbackInboundClientSecret));

        if (!clientIdMatch || !clientSecretMatch)
            return new MysignCallbackAuthOutcome(MysignCallbackAuthStatus.InvalidCredentials);

        var token = await IssueTokenAsync(ct).ConfigureAwait(false);
        return new MysignCallbackAuthOutcome(
            MysignCallbackAuthStatus.Issued, token, current.CallbackTokenTtlSeconds);
    }

    /// <summary>
    /// Computes SHA-256 of the raw token and returns uppercase hex string (64 chars).
    /// Both IssueTokenAsync and TryValidateAsync MUST use the same algorithm.
    /// </summary>
    private static string HashToken(string rawToken)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(rawToken);
        var hash  = SHA256.HashData(bytes);
        // Convert.ToHexString returns UPPERCASE — consistent for DB lookup
        return Convert.ToHexString(hash);
    }
}
