using AqtCoreFW.Files.Domain.Services;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using Hangfire;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Api.Jobs;

/// <summary>
/// Hangfire recurring job — reclaims orphaned signed-PDF blobs from the "signing-docflow" bucket.
///
/// <para>The doc-flow upload path writes the signed PDF to storage under a random-GUID key
/// <c>{tenantId}/{docId}/{guid}.signed.pdf</c> BEFORE it commits the record's Completed transition.
/// When that commit loses a concurrency race (or the process dies mid-flight), the blob is never
/// referenced — its key only ever existed in the losing request's memory, so it cannot be found from
/// the database alone. This sweep is the only thing that can reclaim it: enumerate the bucket and
/// delete blobs that no record references and that are older than a grace window.</para>
///
/// <para><b>Tenant scoping &amp; RLS:</b> blob keys are tenant-prefixed, so the sweep works one tenant
/// at a time — it lists the whole bucket, groups objects by their <c>{tenantId}/</c> prefix, and for
/// each tenant pins the DB SESSION_CONTEXT to that tenant before reading its referenced keys. Without
/// that pin, a future least-privilege (non-db_owner) runtime would have RLS hide every row, the
/// referenced-set would come back empty, and the sweep would delete EVERY blob. The per-tenant read is
/// wrapped so any failure skips that tenant rather than risking deletion against an incomplete set.</para>
/// </summary>
[DisableConcurrentExecution(timeoutInSeconds: 300)]
[AutomaticRetry(Attempts = 0)]
public sealed class SigningDocFlowOrphanBlobSweepJob(
    IdentityDbContext db,
    IFileStorageService fileStorage,
    ILogger<SigningDocFlowOrphanBlobSweepJob> logger)
{
    private const string SignedBucket = "signing-docflow";

    /// <summary>
    /// A blob younger than this is never reclaimed — it may be an in-flight upload whose record has
    /// not yet committed its Completed transition (blob written, SignedStorageKey not yet persisted).
    /// </summary>
    private const int GraceHours = 24;

    private const string SqlServerProvider = "Microsoft.EntityFrameworkCore.SqlServer";

    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        var cutoff = DateTimeOffset.UtcNow.AddHours(-GraceHours);

        var allObjects = await fileStorage.ListObjectsAsync(SignedBucket, prefix: string.Empty, ct);
        if (allObjects.Count == 0)
        {
            logger.LogDebug("DocFlowOrphanBlobSweep: bucket {Bucket} empty — nothing to reclaim.", SignedBucket);
            return;
        }

        // Group by the leading {tenantId}/ path segment; a key that does not start with a valid tenant
        // Guid is unexpected and is left untouched (logged), never deleted.
        var byTenant = new Dictionary<Guid, List<StorageObjectInfo>>();
        var unparseable = 0;
        foreach (var obj in allObjects)
        {
            var firstSegment = obj.Key.Split('/', 2)[0];
            if (!Guid.TryParse(firstSegment, out var tenantId))
            {
                unparseable++;
                continue;
            }
            if (!byTenant.TryGetValue(tenantId, out var list))
                byTenant[tenantId] = list = [];
            list.Add(obj);
        }

        if (unparseable > 0)
            logger.LogWarning(
                "DocFlowOrphanBlobSweep: {Count} object(s) skipped — key does not start with a tenant Guid.",
                unparseable);

        var isSqlServer = db.Database.ProviderName == SqlServerProvider;
        var totalDeleted = 0;
        var tenantsSkipped = 0;

        foreach (var (tenantId, objects) in byTenant)
        {
            try
            {
                var referenced = await LoadReferencedKeysAsync(tenantId, isSqlServer, ct);
                var orphans = SelectOrphanKeys(objects, referenced, cutoff);

                // Defense-in-depth against a silent empty reference-read: a tenant that has deletion
                // candidates yet ZERO referenced keys is far more likely a reference-load gap (e.g. the
                // tenant SESSION_CONTEXT pin failed to engage RLS and the read returned nothing) than a
                // tenant that is genuinely 100% orphan. Because the action here is permanent deletion,
                // skip-and-warn rather than risk wiping every signed blob for the tenant. A real all-
                // orphan tenant is left for an operator to reclaim from the warning.
                if (orphans.Count > 0 && referenced.Count == 0)
                {
                    tenantsSkipped++;
                    logger.LogWarning(
                        "DocFlowOrphanBlobSweep: tenant {TenantId} has {Candidates} deletion candidate(s) but zero referenced keys — skipping as a safety guard (possible reference-load/RLS gap).",
                        tenantId, orphans.Count);
                    continue;
                }

                foreach (var key in orphans)
                {
                    await fileStorage.DeleteAsync(SignedBucket, key, ct);
                    totalDeleted++;
                }
            }
            catch (Exception ex)
            {
                // Never delete against a reference-set we could not fully load — skip the tenant.
                tenantsSkipped++;
                logger.LogError(ex,
                    "DocFlowOrphanBlobSweep: skipped tenant {TenantId} — failed to load referenced keys.",
                    tenantId);
            }
        }

        logger.LogInformation(
            "DocFlowOrphanBlobSweep: reclaimed {Deleted} orphan blob(s) across {Tenants} tenant(s); {Skipped} tenant(s) skipped (cutoff={Cutoff:o}).",
            totalDeleted, byTenant.Count - tenantsSkipped, tenantsSkipped, cutoff);
    }

    /// <summary>
    /// Pure selection: a blob is an orphan when it has a known timestamp older than <paramref name="cutoff"/>
    /// and no record references its key. A null timestamp is treated as "too new to judge" (kept).
    /// </summary>
    public static IReadOnlyList<string> SelectOrphanKeys(
        IReadOnlyCollection<StorageObjectInfo> objects,
        ISet<string> referencedKeys,
        DateTimeOffset cutoff)
        => objects
            .Where(o => o.LastModified is { } modified
                        && modified < cutoff
                        && !referencedKeys.Contains(o.Key))
            .Select(o => o.Key)
            .ToList();

    /// <summary>
    /// Loads every signed-blob key referenced by the tenant's records (any status, including
    /// soft-deleted — their blobs are still real artifacts). Pins SESSION_CONTEXT to the tenant first
    /// so RLS returns the full set under a least-privilege login.
    /// </summary>
    private async Task<HashSet<string>> LoadReferencedKeysAsync(Guid tenantId, bool isSqlServer, CancellationToken ct)
    {
        if (isSqlServer)
        {
            var conn = db.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open)
                await db.Database.OpenConnectionAsync(ct);

            await db.Database.ExecuteSqlRawAsync(
                "EXEC sp_set_session_context N'TenantId', {0}",
                parameters: [tenantId.ToString()], cancellationToken: ct);
        }

        var keys = await db.SigningDocFlowRecords
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(r => r.TenantId == tenantId && r.SignedStorageKey != null)
            .Select(r => r.SignedStorageKey!)
            .ToListAsync(ct);

        return new HashSet<string>(keys, StringComparer.Ordinal);
    }
}
