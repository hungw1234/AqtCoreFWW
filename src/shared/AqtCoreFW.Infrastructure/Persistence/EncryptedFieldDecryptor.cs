using System.Security.Cryptography;
using AqtCoreFW.Infrastructure.Configuration;
using AqtCoreFW.SharedKernel.Abstractions;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Infrastructure.Persistence;

/// <summary>
/// Read-model decryptor for application-level encrypted fields. Wraps <see cref="IKeyResolver"/> and
/// delegates to <see cref="EncryptedStringConverter.DecryptString"/> so Dapper read paths decrypt PII
/// (e.g. AspNetUsers.Email) through the exact same crypto as the EF write-side converter.
/// </summary>
public sealed class EncryptedFieldDecryptor(IKeyResolver keyResolver, ILogger<EncryptedFieldDecryptor> logger)
    : IEncryptedFieldDecryptor
{
    public string? Decrypt(string? cipher)
    {
        if (string.IsNullOrEmpty(cipher))
            return cipher;

        try
        {
            return EncryptedStringConverter.DecryptString(cipher, keyResolver.GetKey);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            // ONLY per-value decode failures are swallowed: not-valid-ciphertext (already-plaintext
            // legacy data) or a tampered blob. Return unchanged rather than faulting the whole
            // read-model list on one bad row. Debug-level so a column mid-migration doesn't spam warnings.
            // DELIBERATELY NOT caught: KeyNotFoundException from the key resolver (e.g. Vault down /
            // missing key version) propagates and faults the call — a platform-wide key outage must fail
            // loud, never silently render ciphertext on every screen. Do NOT widen this catch to Exception.
            logger.LogDebug("Encrypted field decrypt skipped — value is not decodable ciphertext.");
            return cipher;
        }
    }
}
