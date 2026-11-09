using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Identity.Infrastructure.Services;

/// <summary>
/// One-time synchronous startup backfill that populates all email hash columns
/// for ApplicationUser rows where the global hash column is NULL.
///
/// Must run AFTER PlaintextToEncryptedUpgrader has encrypted Email/NormalizedEmail,
/// and BEFORE <c>app.Run()</c>. Login is dead for any user whose NormalizedEmailHashGlobal
/// is NULL because EmailHashedUserStore.FindByEmailAsync queries that column exclusively
/// (AES-GCM ciphertext cannot be used in a WHERE clause — non-deterministic encryption).
///
/// Uses EF to load users so EncryptedStringConverter decrypts Email/NormalizedEmail
/// transparently. Hashes are computed via EmailHashService (HMAC-SHA-256 + pepper).
///
/// Idempotency: batches filtered by WHERE NormalizedEmailHashGlobal IS NULL — second run 0 rows.
///
/// Hash semantics (Fixup P0-1 — two-column split):
///   • NormalizedEmailHashGlobal = ComputeHash(NormalizedEmail, Guid.Empty)
///     — login lookup key; matches FindByEmailAsync
///   • EmailHashGlobal = ComputeHash(Email canonical, Guid.Empty)
///   • NormalizedEmailHashTenant = ComputeHash(NormalizedEmail, user.TenantId)
///     — NULL when TenantId is null or Guid.Empty (system/global users)
///   • EmailHashTenant = ComputeHash(Email canonical, user.TenantId) — same rule
/// </summary>
public static class EmailHashBackfillService
{
    private const int BatchSize = 500;

    /// <summary>Result counts for ops visibility.</summary>
    public sealed record BackfillResult(int Backfilled);

    /// <summary>
    /// Backfills all 4 email hash columns for users where NormalizedEmailHashGlobal is NULL.
    /// Loads users via EF (decrypts Email/NormalizedEmail via EncryptedStringConverter),
    /// computes hashes for both global and tenant columns, then saves via EF SaveChanges.
    /// </summary>
    /// <param name="dbContext">IdentityDbContext — must have IKeyResolver wired for decryption.</param>
    /// <param name="emailHashService">Hash service.</param>
    /// <param name="logger">Optional progress logger.</param>
    /// <param name="ct">Cancellation token.</param>
    public static async Task<BackfillResult> BackfillAsync(
        IdentityDbContext dbContext,
        EmailHashService emailHashService,
        ILogger? logger = null,
        CancellationToken ct = default)
    {
        int totalBackfilled = 0;
        int totalSkipped = 0;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            // Load users missing the global hash (either fresh rows or rows where interceptor
            // used wrong tenant pepper before Fixup P0-1). Using NormalizedEmailHashGlobal IS NULL
            // ensures both old-name renamed rows and truly new rows are caught.
            var batch = await dbContext.Users
                .Where(u => u.NormalizedEmailHashGlobal == null && u.NormalizedEmail != null)
                .OrderBy(u => u.Id) // stable ordering for reproducible batches
                .Take(BatchSize)
                .ToListAsync(ct);

            if (batch.Count == 0)
                break;

            int batchOk = 0;
            int batchFail = 0;
            foreach (var user in batch)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    PopulateHashes(user, emailHashService);
                    batchOk++;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    // P4 defense: per-user wrap — one bad row (e.g. missing tenant pepper)
                    // must not abort the entire batch and lock out other tenants. Revert this
                    // user's tracked mutations so SaveChangesAsync still persists the survivors;
                    // the user remains "hash NULL" and will be retried on the next startup.
                    // Log user.Id only — never decrypted Email (PII redaction).
                    dbContext.Entry(user).State = EntityState.Unchanged;
                    batchFail++;
                    logger?.LogError(ex,
                        "EmailHashBackfill: failed to hash user {UserId} (tenant {TenantId}) — continuing",
                        user.Id, user.TenantId);
                }
            }

            await dbContext.SaveChangesAsync(ct);
            totalBackfilled += batchOk;
            totalSkipped += batchFail;

            if (batchFail > 0)
            {
                logger?.LogError(
                    "EmailHashBackfill: {Ok}/{Total} hashed, {Failed} skipped due to errors (running total: ok={TotalOk}, skipped={TotalFailed})",
                    batchOk, batch.Count, batchFail, totalBackfilled, totalSkipped);
            }
            else
            {
                logger?.LogInformation(
                    "EmailHashBackfill: {Count} users hashed (total so far: {Total})",
                    batchOk, totalBackfilled);
            }

            if (batch.Count < BatchSize)
                break;
        }

        if (totalSkipped > 0)
        {
            logger?.LogError(
                "EmailHashBackfill: complete — {Total} users backfilled, {Skipped} skipped (will retry on next startup)",
                totalBackfilled, totalSkipped);
        }
        else
        {
            logger?.LogInformation(
                "EmailHashBackfill: complete — {Total} users backfilled",
                totalBackfilled);
        }

        return new BackfillResult(totalBackfilled);
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Computes and sets all 4 email hash columns on the user entity.
    /// Global columns use Guid.Empty pepper (matches FindByEmailAsync login path).
    /// Tenant columns use per-tenant pepper; NULL for users without a tenant scope.
    /// </summary>
    private static void PopulateHashes(ApplicationUser user, EmailHashService emailHashService)
    {
        // Global columns — login lookup; MUST match FindByEmailAsync which uses Guid.Empty.
        if (!string.IsNullOrWhiteSpace(user.NormalizedEmail))
            user.NormalizedEmailHashGlobal = emailHashService.ComputeHash(user.NormalizedEmail, Guid.Empty);

        if (!string.IsNullOrWhiteSpace(user.Email))
            user.EmailHashGlobal = emailHashService.ComputeHash(user.Email, Guid.Empty);

        // Tenant columns — per-tenant pepper; NULL for global/system users.
        if (user.TenantId is Guid tid && tid != Guid.Empty)
        {
            if (!string.IsNullOrWhiteSpace(user.NormalizedEmail))
                user.NormalizedEmailHashTenant = emailHashService.ComputeHash(user.NormalizedEmail, tid);

            if (!string.IsNullOrWhiteSpace(user.Email))
                user.EmailHashTenant = emailHashService.ComputeHash(user.Email, tid);
        }
        else
        {
            user.NormalizedEmailHashTenant = null;
            user.EmailHashTenant = null;
        }
    }
}
