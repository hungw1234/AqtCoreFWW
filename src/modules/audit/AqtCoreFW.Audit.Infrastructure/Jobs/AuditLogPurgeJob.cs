using AqtCoreFW.Audit.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Compliance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Audit.Infrastructure.Jobs;

/// <summary>
/// Hangfire recurring job — purges <c>AuditLogEntries</c> rows past their per-(tenant,
/// category) retention window. QĐ 742 §3.2.b. Drift R3 in phase-01 plan: this is a
/// NEW job inside the Audit module — it does NOT touch <c>SodAuditLogPurgeJob</c>
/// (Identity module, scoped to <c>SodViolationLogs</c>).
///
/// <para>Retention resolution per (tenant, category) follows the precedence in
/// <c>IAuditRetentionPolicyRepository.ResolveAsync</c>:
/// (tenant + category) → (tenant + null) → (null + null). The job pulls all policies
/// once per run (admin-curated table, handful of rows), then evaluates per (tenant,
/// category) bucket discovered in <c>AuditLogEntries</c>.</para>
///
/// <para>Trigger bypass: M2 added <c>tr_AuditLog_NoMutate</c>, which blocks DELETE
/// unless <c>SESSION_CONTEXT(N'audit_mutate_mode') = N'archive'</c> on the same
/// physical connection. The job opens a connection up-front, sets the context once,
/// runs all batches, then clears + closes — guaranteeing the bypass survives across
/// every <c>ExecuteSqlRawAsync</c> call.</para>
///
/// <para>Cron: 03:00 UTC daily (10:00 ICT, off-peak; 1h after SodAuditLogPurgeJob to
/// avoid contending for the audit DB during peak admin reads).</para>
/// </summary>
public sealed class AuditLogPurgeJob
{
    private const int BatchSize = 10_000;
    private const string BypassMode = "archive";
    private static readonly Guid GlobalTenantSentinel = Guid.Empty;

    /// <summary>(tenant, category) bucket key discovered in AuditLogEntries.</summary>
    private readonly record struct BucketKey(Guid? TenantId, AuditCategory Category);

    private readonly AuditDbContext _db;
    private readonly ILogger<AuditLogPurgeJob> _logger;

    public AuditLogPurgeJob(AuditDbContext db, ILogger<AuditLogPurgeJob> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>Hangfire entry point — purge rows older than their resolved retention window.</summary>
    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        // 1. Snapshot all policies once. Admin-curated, so a single materialised list is cheap
        //    and avoids re-querying inside the per-bucket loop.
        var policies = await _db.AuditRetentionPolicies.AsNoTracking()
            .ToListAsync(cancellationToken);
        if (policies.Count == 0)
        {
            _logger.LogWarning("AuditLogPurgeJob: no AuditRetentionPolicies seeded — skipping run.");
            return;
        }

        // 2. Open a single physical connection for the entire purge run so SESSION_CONTEXT
        //    bypass survives across every query/ExecuteSqlRawAsync call.
        var conn = _db.Database.GetDbConnection();
        var openedHere = false;
        if (conn.State != System.Data.ConnectionState.Open)
        {
            await _db.Database.OpenConnectionAsync(cancellationToken);
            openedHere = true;
        }

        var totalDeleted = 0;
        var buckets = new List<BucketKey>();
        try
        {
            // Set audit_mutate_mode='archive' to bypass the tr_AuditLog_NoMutate immutability
            // trigger, which otherwise blocks all DELETE statements on AuditLogEntries.
            await _db.Database.ExecuteSqlRawAsync(
                $"EXEC sp_set_session_context @key=N'audit_mutate_mode', @value=N'{BypassMode}', @read_only=0;",
                cancellationToken);

            // RLS: audit_admin_bypass — admin read of system/global audit rows
            // AuditLogEntries has an asymmetric RLS FILTER predicate (PHASE6-RLS-NULLABLE-TENANT
            // Option A): without this flag, NULL-tenant (system event) rows are invisible to
            // a connection with no ambient TenantId, so the purge job would silently skip them —
            // including the bucket-discovery GroupBy below (code-review M-1: must run AFTER this).
            // The flag is cleared in the finally block below (or by pool reset if connection faults).
            // Do NOT pass @read_only=1 — we rely on pool reset (sp_reset_connection) as safety net.
            await _db.Database.ExecuteSqlRawAsync(
                "EXEC sp_set_session_context @key=N'audit_admin_bypass', @value=N'true', @read_only=0;",
                cancellationToken);

            // 3. Discover (tenant, category) buckets present in the log — on the same connection,
            //    after the bypass flag is set, so NULL-tenant buckets are visible. Result set is
            //    tiny (one row per tenant×category combination).
            buckets = await _db.AuditLogEntries.AsNoTracking()
                .GroupBy(e => new { e.TenantId, e.Category })
                .Select(g => new BucketKey(g.Key.TenantId, g.Key.Category))
                .ToListAsync(cancellationToken);

            foreach (var bucket in buckets)
            {
                if (cancellationToken.IsCancellationRequested) break;

                var policy = ResolvePolicy(policies, bucket.TenantId, bucket.Category);
                if (policy is null)
                {
                    // Should be impossible if the seed migration ran (global default exists).
                    _logger.LogWarning(
                        "AuditLogPurgeJob: no retention policy resolved for tenant={TenantId} category={Category} — skipping bucket.",
                        bucket.TenantId, bucket.Category);
                    continue;
                }

                var cutoff = DateTimeOffset.UtcNow.AddDays(-policy.RetentionDays);
                try
                {
                    var deletedForBucket = await PurgeBucketAsync(
                        bucket.TenantId, bucket.Category, cutoff, cancellationToken);
                    totalDeleted += deletedForBucket;

                    if (deletedForBucket > 0)
                    {
                        _logger.LogInformation(
                            "AuditLogPurgeJob: tenant={TenantId} category={Category} retention={RetentionDays}d cutoff={Cutoff:u} deleted={Deleted}",
                            bucket.TenantId, bucket.Category, policy.RetentionDays, cutoff, deletedForBucket);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Code-review M3: per-bucket isolation. One failing tenant must not abort
                    // the whole run; log and continue so the remaining buckets still purge.
                    _logger.LogError(ex,
                        "AuditLogPurgeJob: bucket failed tenant={TenantId} category={Category} — continuing with next bucket.",
                        bucket.TenantId, bucket.Category);
                }
            }
        }
        finally
        {
            // Defensive clear (code-review H3): if any bucket throw escaped, this still runs
            // before the connection returns to the pool — preventing a leaked 'archive' bypass
            // or audit_admin_bypass from being inherited by an unrelated request.
            try
            {
                await _db.Database.ExecuteSqlRawAsync(
                    "EXEC sp_set_session_context @key=N'audit_mutate_mode', @value=NULL, @read_only=0;",
                    CancellationToken.None);
                // RLS: audit_admin_bypass — clear alongside audit_mutate_mode (belt-and-braces;
                // pool reset also clears it, but explicit clear is safer when connection is reused).
                await _db.Database.ExecuteSqlRawAsync(
                    "EXEC sp_set_session_context @key=N'audit_admin_bypass', @value=NULL, @read_only=0;",
                    CancellationToken.None);
            }
            catch
            {
                // Connection already faulted — pool reset clears SESSION_CONTEXT on its own
                // when Connection Reset=true (default). Best-effort here.
            }

            if (openedHere)
                await _db.Database.CloseConnectionAsync();
        }

        _logger.LogInformation(
            "AuditLogPurgeJob: run complete — totalDeleted={Total} buckets={Buckets} policies={Policies}",
            totalDeleted, buckets.Count, policies.Count);
    }

    private async Task<int> PurgeBucketAsync(
        Guid? tenantId,
        AuditCategory category,
        DateTimeOffset cutoff,
        CancellationToken cancellationToken)
    {
        // Batched DELETE via raw SQL — avoids EF change tracking overhead and ensures
        // the trigger's session-context check sees the still-open connection.
        // Schema bracketed because [audit] is fine but kept consistent with codebase pattern.
        // Parameters: @cutoff (datetimeoffset), @tenantId (uniqueidentifier or NULL), @category (int)
        const string sqlWithTenant = @"
WITH batch AS (
    SELECT TOP (@batchSize) InternalId
    FROM [audit].[AuditLogEntries]
    WHERE OccurredAt < @cutoff
      AND TenantId = @tenantId
      AND Category = @category
    ORDER BY OccurredAt ASC
)
DELETE FROM [audit].[AuditLogEntries]
WHERE InternalId IN (SELECT InternalId FROM batch);";

        const string sqlGlobal = @"
WITH batch AS (
    SELECT TOP (@batchSize) InternalId
    FROM [audit].[AuditLogEntries]
    WHERE OccurredAt < @cutoff
      AND TenantId IS NULL
      AND Category = @category
    ORDER BY OccurredAt ASC
)
DELETE FROM [audit].[AuditLogEntries]
WHERE InternalId IN (SELECT InternalId FROM batch);";

        var bucketTotal = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            int affected;
            if (tenantId is null)
            {
                affected = await _db.Database.ExecuteSqlRawAsync(
                    sqlGlobal,
                    [
                        new Microsoft.Data.SqlClient.SqlParameter("@batchSize", BatchSize),
                        new Microsoft.Data.SqlClient.SqlParameter("@cutoff", cutoff),
                        new Microsoft.Data.SqlClient.SqlParameter("@category", (int)category)
                    ],
                    cancellationToken);
            }
            else
            {
                affected = await _db.Database.ExecuteSqlRawAsync(
                    sqlWithTenant,
                    [
                        new Microsoft.Data.SqlClient.SqlParameter("@batchSize", BatchSize),
                        new Microsoft.Data.SqlClient.SqlParameter("@cutoff", cutoff),
                        new Microsoft.Data.SqlClient.SqlParameter("@tenantId", tenantId.Value),
                        new Microsoft.Data.SqlClient.SqlParameter("@category", (int)category)
                    ],
                    cancellationToken);
            }

            bucketTotal += affected;
            if (affected < BatchSize) break;
        }

        return bucketTotal;
    }

    /// <summary>
    /// Mirrors <c>IAuditRetentionPolicyRepository.ResolveAsync</c> for in-memory list:
    /// (tenant + category) → (tenant + null cat) → (null tenant + null cat) → null.
    /// Kept inline to avoid a second DB round-trip per bucket.
    /// </summary>
    internal static Domain.Entities.AuditRetentionPolicy? ResolvePolicy(
        IReadOnlyList<Domain.Entities.AuditRetentionPolicy> policies,
        Guid? tenantId,
        AuditCategory category)
    {
        // tenantId comes from log row (nullable). Treat null tenant rows as "global".
        return policies
            .Where(p => (p.TenantId == tenantId || p.TenantId == null)
                     && (p.Category == category || p.Category == null))
            .OrderByDescending(p => p.TenantId == tenantId && tenantId.HasValue ? 2 : 0)
            .ThenByDescending(p => p.Category == category ? 1 : 0)
            .FirstOrDefault();
    }
}
