using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Infrastructure.Persistence;

/// <summary>
/// Idempotent batch upgrader that encrypts plaintext column values with AES-256-GCM
/// for the FIRST time (initialization upgrade — not re-encryption of legacy ciphers).
///
/// Distinct from <see cref="LegacyCipherUpgrader"/> which re-encrypts v0 ciphertext blobs
/// to the v{N}: format. That upgrader must never be applied to plaintext rows — the Base64
/// decode step would throw FormatException on values containing '@' or other non-Base64 chars.
///
/// This upgrader handles plaintext → v{N}: in a separate, safe code path:
///   - Fetches rows WHERE column IS NOT NULL AND column NOT LIKE 'v[0-9]%:%'
///   - Encrypts each raw value directly (no decrypt step)
///   - Idempotent: already-encrypted rows (v{N}: prefix) are skipped
///
/// Batching: 500 rows per round-trip; retry-on-deadlock up to 3 times per batch.
///
/// IMPORTANT: Run this BEFORE <see cref="LegacyCipherUpgrader"/> when a column transitions
/// from plaintext → encrypted. After all rows carry a v{N}: prefix, LegacyCipherUpgrader can
/// run safely for future key-rotation purposes.
/// </summary>
public static class PlaintextToEncryptedUpgrader
{
    // Matches the v{N}: prefix written by EncryptedStringConverter.
    // Anchored at start — a legacy base64 blob starting with 'v' does NOT match
    // unless immediately followed by digits and colon.
    private static readonly Regex VersionPrefixRegex =
        new(@"^v(\d+):", RegexOptions.Compiled, matchTimeout: TimeSpan.FromMilliseconds(100));

    private const int BatchSize = 500;
    private const int MaxDeadlockRetries = 3;

    /// <summary>
    /// Describes a column to upgrade — schema + table + column triplet plus PK column name.
    /// <paramref name="PkColumn"/> defaults to <c>Id</c> (ASP.NET Core Identity / Guid PK convention).
    /// Override for tables with non-standard PK names (e.g. <c>InternalId</c> for audit entities).
    /// </summary>
    public sealed record ColumnTarget(string Schema, string Table, string Column, string PkColumn = "Id");

    /// <summary>Result counts for ops visibility.</summary>
    public sealed record UpgradeResult(int Encrypted, int Skipped);

    /// <summary>
    /// Encrypts all plaintext values in the given column with AES-256-GCM, writing the
    /// v{N}:base64 wire format. Rows already prefixed with v{N}: are skipped.
    /// </summary>
    /// <param name="connectionString">SQL Server connection string.</param>
    /// <param name="target">Schema + table + column to upgrade.</param>
    /// <param name="keyResolver">Key resolver — called with <paramref name="currentVersion"/>.</param>
    /// <param name="currentVersion">Active key version to write into the v{N}: prefix.</param>
    /// <param name="logger">Optional logger for progress and deadlock events.</param>
    /// <param name="ct">Cancellation token.</param>
    public static async Task<UpgradeResult> EncryptColumnAsync(
        string connectionString,
        ColumnTarget target,
        Func<int, byte[]> keyResolver,
        int currentVersion,
        ILogger? logger = null,
        CancellationToken ct = default)
    {
        var qualifiedTable = $"[{target.Schema}].[{target.Table}]";
        var col = $"[{target.Column}]";
        var key = keyResolver(currentVersion);

        int totalEncrypted = 0;
        int totalSkipped = 0;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            await using var conn = new Microsoft.Data.SqlClient.SqlConnection(connectionString);
            await conn.OpenAsync(ct);

            var batch = await FetchPlaintextBatchAsync(conn, qualifiedTable, col, target.PkColumn, ct);

            if (batch.Count == 0)
                break;

            int batchEncrypted = 0;

            foreach (var (rowId, plaintext) in batch)
            {
                ct.ThrowIfCancellationRequested();

                // Double-check: skip anything that already has a v{N}: prefix
                // (handles restarts where batch partially committed).
                if (VersionPrefixRegex.IsMatch(plaintext))
                {
                    totalSkipped++;
                    continue;
                }

                var newCipher = EncryptVersioned(plaintext, key, currentVersion);
                await UpdateRowWithRetryAsync(conn, qualifiedTable, col, target.PkColumn, rowId, newCipher, logger, ct);
                batchEncrypted++;
            }

            totalEncrypted += batchEncrypted;
            logger?.LogInformation(
                "PlaintextEncrypt: {Table}.{Column} — batch {Count} rows encrypted (total so far: {Total})",
                target.Table, target.Column, batchEncrypted, totalEncrypted);

            if (batch.Count < BatchSize)
                break;
        }

        logger?.LogInformation(
            "PlaintextEncrypt: {Table}.{Column} complete — {Encrypted} encrypted, {Skipped} already encrypted (skipped)",
            target.Table, target.Column, totalEncrypted, totalSkipped);

        return new UpgradeResult(totalEncrypted, totalSkipped);
    }

    // ── Private SQL helpers ──────────────────────────────────────────────────

    private static async Task<List<(string RowId, string Plaintext)>> FetchPlaintextBatchAsync(
        Microsoft.Data.SqlClient.SqlConnection conn,
        string qualifiedTable,
        string col,
        string pkCol,
        CancellationToken ct)
    {
        // Fetch rows where column is NOT NULL and does NOT start with "v<digits>:"
        // NOT LIKE 'v[0-9]%:%' is a conservative SQL filter; in-process regex is authoritative.
        // pkCol is bracket-quoted to handle reserved words; always cast to NVARCHAR(50) for row identity.
        var sql = $"""
            SELECT TOP ({BatchSize}) CAST([{pkCol}] AS NVARCHAR(50)), {col}
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
        string pkCol,
        string rowId,
        string newCipher,
        ILogger? logger,
        CancellationToken ct)
    {
        for (int attempt = 1; attempt <= MaxDeadlockRetries; attempt++)
        {
            try
            {
                var sql = $"UPDATE {qualifiedTable} SET {col} = @cipher WHERE CAST([{pkCol}] AS NVARCHAR(50)) = @id";
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

    // ── Crypto helper (mirrors EncryptedStringConverter.Encrypt) ────────────

    private static string EncryptVersioned(string plaintext, byte[] key, int version)
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

        return $"v{version}:{Convert.ToBase64String(combined)}";
    }
}
