using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Infrastructure.Persistence;

/// <summary>
/// Idempotent batch upgrader that re-encrypts legacy (no-prefix) AES-256-GCM ciphertexts
/// to the versioned <c>v{N}:</c> format in the database (D10.2 one-time migration).
///
/// Design decision (deviates from EF migration Up() spec):
/// SQL Server does not support AES-256-GCM UDFs; CLR-based re-encryption cannot run
/// inside <c>MigrationBuilder.Sql()</c>. The data migration is therefore implemented here
/// as a C# post-Migrate hook invoked directly by each host's startup sequence.
/// The companion EF migration <c>AddCipherVersionPrefix</c> is a no-op schema marker
/// only (tracking that data migration was introduced in this deployment).
///
/// Idempotency: WHERE clause filters rows NOT LIKE 'v%:%' — second run touches 0 rows.
///
/// Batching: 500 rows per round-trip; retry-on-deadlock up to 3 times per batch.
/// </summary>
public static class LegacyCipherUpgrader
{
    // Matches the prefix written by EncryptedStringConverter (e.g. "v1:", "v123:")
    private static readonly Regex VersionPrefixRegex =
        new(@"^v(\d+):", RegexOptions.Compiled, matchTimeout: TimeSpan.FromMilliseconds(100));

    private const int BatchSize = 500;
    private const int MaxDeadlockRetries = 3;

    /// <summary>
    /// Describes a column to migrate — schema + table + column triplet.
    /// </summary>
    public sealed record ColumnTarget(string Schema, string Table, string Column);

    /// <summary>
    /// Upgrade all legacy (no-prefix) ciphertexts in the given column by re-encrypting them
    /// with the supplied key resolver. Rows already carrying a <c>v{N}:</c> prefix are skipped.
    /// </summary>
    /// <param name="connectionString">SQL Server connection string.</param>
    /// <param name="target">Schema + table + column to upgrade.</param>
    /// <param name="keyV1">The v1 AES-256 key bytes used to decrypt legacy ciphers.</param>
    /// <param name="keyResolver">Key resolver to encrypt into the new versioned format.</param>
    /// <param name="currentVersion">Current active key version to write.</param>
    /// <param name="logger">Optional logger for progress and deadlock events.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <summary>Result of a column upgrade — counts for ops visibility (N4).</summary>
    public sealed record UpgradeResult(int Upgraded, int SkippedDueToCryptoFailure);

    /// <returns>Tuple of rows successfully re-encrypted and rows skipped due to decryption failure.</returns>
    public static async Task<UpgradeResult> UpgradeColumnAsync(
        string connectionString,
        ColumnTarget target,
        byte[] keyV1,
        Func<int, byte[]> keyResolver,
        int currentVersion,
        ILogger? logger = null,
        CancellationToken ct = default)
    {
        var qualifiedTable = $"[{target.Schema}].[{target.Table}]";
        var col = $"[{target.Column}]";

        int totalUpgraded = 0;
        int totalSkipped = 0;

        // M2 fix: open ONE SqlConnection per batch (not per row). Connection pooling alone
        // doesn't avoid 500 open/close cycles per batch — explicit reuse is required for tenants
        // with 10K+ encrypted rows. Each batch fetch + N updates share the same connection.
        while (true)
        {
            ct.ThrowIfCancellationRequested();

            await using var conn = new Microsoft.Data.SqlClient.SqlConnection(connectionString);
            await conn.OpenAsync(ct);

            var batch = await FetchLegacyBatchAsync(conn, qualifiedTable, col, ct);

            if (batch.Count == 0)
                break;

            int batchUpgraded = 0;

            foreach (var (rowId, legacyCipher) in batch)
            {
                ct.ThrowIfCancellationRequested();

                if (VersionPrefixRegex.IsMatch(legacyCipher))
                    continue;

                string newCipher;
                try
                {
                    var plaintext = DecryptLegacy(legacyCipher, keyV1);
                    newCipher = EncryptVersioned(plaintext, keyResolver(currentVersion), currentVersion);
                }
                catch (CryptographicException ex)
                {
                    logger?.LogWarning(ex,
                        "Skipping row id={RowId} in {Table}.{Column} — decryption failed (key mismatch?)",
                        rowId, target.Table, target.Column);
                    totalSkipped++;
                    continue;
                }

                await UpdateRowWithRetryAsync(conn, qualifiedTable, col, rowId, newCipher, logger, ct);
                batchUpgraded++;
            }

            totalUpgraded += batchUpgraded;
            logger?.LogInformation(
                "CipherUpgrade: {Table}.{Column} — batch {Count} rows re-encrypted (total so far: {Total})",
                target.Table, target.Column, batchUpgraded, totalUpgraded);

            if (batch.Count < BatchSize)
                break;
        }

        logger?.LogInformation(
            "CipherUpgrade: {Table}.{Column} complete — {Upgraded} upgraded, {Skipped} skipped",
            target.Table, target.Column, totalUpgraded, totalSkipped);

        return new UpgradeResult(totalUpgraded, totalSkipped);
    }

    // ── Private SQL helpers ──────────────────────────────────────────────────

    private static async Task<List<(string RowId, string Cipher)>> FetchLegacyBatchAsync(
        Microsoft.Data.SqlClient.SqlConnection conn,
        string qualifiedTable,
        string col,
        CancellationToken ct)
    {
        // Fetch rows where column is NOT NULL and does NOT start with "v<digits>:"
        // NOT LIKE 'v[0-9]%:%' is a conservative SQL filter; in-process regex is authoritative.
        var sql = $"""
            SELECT TOP ({BatchSize}) CAST(Id AS NVARCHAR(50)), {col}
            FROM {qualifiedTable}
            WHERE {col} IS NOT NULL
              AND {col} NOT LIKE 'v[0-9]%:%'
            """;

        using var cmd = new Microsoft.Data.SqlClient.SqlCommand(sql, conn);
        cmd.CommandTimeout = 60;

        var result = new List<(string, string)>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            result.Add((reader.GetString(0), reader.GetString(1)));
        }

        return result;
    }

    private static async Task UpdateRowWithRetryAsync(
        Microsoft.Data.SqlClient.SqlConnection conn,
        string qualifiedTable,
        string col,
        string rowId,
        string newCipher,
        ILogger? logger,
        CancellationToken ct)
    {
        // N1 clarification: on attempt == MaxDeadlockRetries the `when` filter evaluates false,
        // so the SqlException propagates out of the method (NOT silently swallowed).
        for (int attempt = 1; attempt <= MaxDeadlockRetries; attempt++)
        {
            try
            {
                var sql = $"UPDATE {qualifiedTable} SET {col} = @cipher WHERE CAST(Id AS NVARCHAR(50)) = @id";
                using var cmd = new Microsoft.Data.SqlClient.SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@cipher", newCipher);
                cmd.Parameters.AddWithValue("@id", rowId);
                cmd.CommandTimeout = 30;

                await cmd.ExecuteNonQueryAsync(ct);
                return;
            }
            catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number == 1205 && attempt < MaxDeadlockRetries)
            {
                logger?.LogWarning("Deadlock on row {RowId}, retry {Attempt}/{Max}", rowId, attempt, MaxDeadlockRetries);
                await Task.Delay(TimeSpan.FromMilliseconds(100 * attempt), ct);
            }
        }
    }

    // ── Crypto helpers (mirrors EncryptedStringConverter private methods) ────

    private static string DecryptLegacy(string base64, byte[] key)
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

    private static string EncryptVersioned(string plaintext, byte[] key, int version)
    {
        if (string.IsNullOrEmpty(plaintext)) return plaintext;

        var nonce = new byte[AesGcm.NonceByteSizes.MaxSize];
        RandomNumberGenerator.Fill(nonce);

        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var cipherBytes = new byte[plaintextBytes.Length];
        var tag = new byte[AesGcm.TagByteSizes.MaxSize];

        using var aesGcm = new AesGcm(key, tag.Length);
        aesGcm.Encrypt(nonce, plaintextBytes, cipherBytes, tag);

        var combined = new byte[nonce.Length + tag.Length + cipherBytes.Length];
        Buffer.BlockCopy(nonce, 0, combined, 0, nonce.Length);
        Buffer.BlockCopy(tag, 0, combined, nonce.Length, tag.Length);
        Buffer.BlockCopy(cipherBytes, 0, combined, nonce.Length + tag.Length, cipherBytes.Length);

        return $"v{version}:{Convert.ToBase64String(combined)}";
    }
}
