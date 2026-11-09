using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace AqtCoreFW.Infrastructure.Persistence;

/// <summary>
/// EF Core value converter — AES-256-GCM application-level encryption for PII columns.
/// No SQL Server Enterprise or pgcrypto required.
///
/// Wire format written (base64): v{N}:base64([12-byte nonce][16-byte GCM tag][ciphertext])
///
/// Read (dual-decode — permanent per D10.2):
///   • Matches ^v(\d+): → extract version → decrypt with keyResolver(version)
///   • No prefix match  → legacy v1 format → decrypt with keyResolver(1)
///
/// NOTE: base64 characters include A-Z a-z 0-9 + / =, and the letter 'v' CAN appear
/// as the first character of a legacy base64 blob. Regex ^v(\d+): is used (NOT
/// StartsWith("v")) to avoid misclassifying legacy ciphers as prefixed.
///
/// Usage in entity config:
///   builder.Property(x => x.TotpSecret)
///       .HasConversion((ValueConverter)new EncryptedStringConverter(resolver.GetKey, resolver.CurrentVersion));
///
/// Legacy ctor (single byte[] key) kept for design-time / test-only scenarios without IKeyResolver.
/// </summary>
public sealed class EncryptedStringConverter : ValueConverter<string, string>
{
    // Matches: "v" + one-or-more digits + ":" — e.g. "v1:", "v2:", "v123:"
    // Anchored at start (^) so a legacy base64 starting with 'v' does NOT match
    // unless followed immediately by digits and colon.
    private static readonly Regex VersionPrefixRegex =
        new(@"^v(\d+):", RegexOptions.Compiled, matchTimeout: TimeSpan.FromMilliseconds(100));

    /// <summary>
    /// Runtime ctor — versioned write + dual-decode read.
    /// <paramref name="keyResolver"/> must throw <see cref="KeyNotFoundException"/> on missing version.
    /// </summary>
    public EncryptedStringConverter(Func<int, byte[]> keyResolver, int currentVersion)
        : base(
            plain => Encrypt(plain, keyResolver(currentVersion), currentVersion),
            cipher => DecryptString(cipher, keyResolver))
    {
    }

    /// <summary>
    /// Legacy single-key ctor — design-time scaffolding / tests that don't need IKeyResolver.
    /// Writes WITHOUT version prefix; reads as v1.
    /// </summary>
    public EncryptedStringConverter(byte[] keyBytes)
        : base(
            plain => Encrypt(plain, keyBytes, version: null),
            cipher => DecryptLegacy(cipher, keyBytes))
    {
    }

    // ── Write ────────────────────────────────────────────────────────────────

    private static string Encrypt(string plaintext, byte[] key, int? version)
    {
        if (string.IsNullOrEmpty(plaintext)) return plaintext;

        var nonce = new byte[AesGcm.NonceByteSizes.MaxSize]; // 12 bytes
        RandomNumberGenerator.Fill(nonce);

        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var cipherBytes = new byte[plaintextBytes.Length];
        var tag = new byte[AesGcm.TagByteSizes.MaxSize]; // 16 bytes

        using var aesGcm = new AesGcm(key, tag.Length);
        aesGcm.Encrypt(nonce, plaintextBytes, cipherBytes, tag);

        // Pack: nonce(12) + tag(16) + ciphertext
        var combined = new byte[nonce.Length + tag.Length + cipherBytes.Length];
        Buffer.BlockCopy(nonce, 0, combined, 0, nonce.Length);
        Buffer.BlockCopy(tag, 0, combined, nonce.Length, tag.Length);
        Buffer.BlockCopy(cipherBytes, 0, combined, nonce.Length + tag.Length, cipherBytes.Length);

        var base64 = Convert.ToBase64String(combined);
        return version.HasValue ? $"v{version.Value}:{base64}" : base64;
    }

    // ── Read (dual-decode permanent) ─────────────────────────────────────────

    /// <summary>
    /// Dual-decode an encrypted field value: <c>v{N}:</c>-prefixed → version-aware, otherwise legacy v1.
    /// Public so read-model decryptors (Dapper paths that bypass the EF converter) reuse the exact same
    /// crypto path as the converter — single source of truth, no duplicated AES-GCM logic.
    /// Empty/null passes through. Throws on malformed base64 / GCM auth failure (caller decides handling).
    /// </summary>
    public static string DecryptString(string encoded, Func<int, byte[]> keyResolver)
    {
        if (string.IsNullOrEmpty(encoded)) return encoded;

        var match = VersionPrefixRegex.Match(encoded);
        if (match.Success)
        {
            // v{N}: prefix found — version-aware path
            var version = int.Parse(match.Groups[1].Value);
            var base64Part = encoded[(match.Length)..];
            return DecryptBlob(base64Part, keyResolver(version));
        }

        // No prefix — legacy format, assume v1 (D10.2 permanent dual-decode)
        return DecryptBlob(encoded, keyResolver(1));
    }

    /// <summary>Single-key decrypt used by the legacy ctor only.</summary>
    private static string DecryptLegacy(string encoded, byte[] key)
    {
        if (string.IsNullOrEmpty(encoded)) return encoded;

        // Strip version prefix if present (tolerates rows migrated to new format)
        var match = VersionPrefixRegex.Match(encoded);
        var blob = match.Success ? encoded[(match.Length)..] : encoded;
        return DecryptBlob(blob, key);
    }

    private static string DecryptBlob(string base64, byte[] key)
    {
        var combined = Convert.FromBase64String(base64);
        var nonceSize = AesGcm.NonceByteSizes.MaxSize;  // 12
        var tagSize = AesGcm.TagByteSizes.MaxSize;       // 16

        var nonce = combined[..nonceSize];
        var tag = combined[nonceSize..(nonceSize + tagSize)];
        var cipherBytes = combined[(nonceSize + tagSize)..];
        var plaintext = new byte[cipherBytes.Length];

        using var aesGcm = new AesGcm(key, tagSize);
        aesGcm.Decrypt(nonce, cipherBytes, tag, plaintext);

        return Encoding.UTF8.GetString(plaintext);
    }
}
