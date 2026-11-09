using System.Security.Cryptography;
using System.Text;

namespace AqtCoreFW.Infrastructure.Webhooks;

/// <summary>
/// Computes and verifies HMAC-SHA256 signatures for webhook payloads.
/// Signature format: sha256={hex}
/// Header sent: X-Webhook-Signature: sha256={hex}
///              X-Webhook-Timestamp: {unix_seconds}
///
/// Receivers verify by:
///   1. Reject if |now - X-Webhook-Timestamp| > 300s (replay guard)
///   2. Recompute HMAC-SHA256(secret, "{timestamp}.{body}") and compare
/// </summary>
public sealed class WebhookSigningService
{
    private const string SignaturePrefix = "sha256=";

    /// <summary>
    /// Generates a cryptographically-random plaintext secret (32 bytes → 64 hex chars).
    /// </summary>
    public static string GenerateSecret()
        => Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

    /// <summary>
    /// Computes HMAC-SHA256 of the plaintext secret for storage.
    /// Stored hash is used at delivery time to sign payloads.
    /// NOTE: We store the hash of the secret, not the secret itself.
    /// At delivery we re-derive the signing key from the stored hash value directly.
    /// </summary>
    public static string HashSecret(string plaintextSecret)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(plaintextSecret));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// Computes X-Webhook-Signature for the given payload body + timestamp.
    /// signKey is the stored SecretHash value (HMAC key derived from it).
    /// Message signed: "{unixTimestamp}.{body}"
    /// </summary>
    public static string ComputeSignature(string signKey, string body, long unixTimestamp)
    {
        var message = $"{unixTimestamp}.{body}";
        var keyBytes = Encoding.UTF8.GetBytes(signKey);
        var msgBytes = Encoding.UTF8.GetBytes(message);
        var hash = HMACSHA256.HashData(keyBytes, msgBytes);
        return SignaturePrefix + Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Adds X-Webhook-Signature and X-Webhook-Timestamp headers to an outgoing request.
    /// </summary>
    public static void ApplySignatureHeaders(
        HttpRequestMessage request,
        string signKey,
        string body,
        long unixTimestamp)
    {
        var signature = ComputeSignature(signKey, body, unixTimestamp);
        request.Headers.TryAddWithoutValidation("X-Webhook-Signature", signature);
        request.Headers.TryAddWithoutValidation("X-Webhook-Timestamp", unixTimestamp.ToString());
    }
}
