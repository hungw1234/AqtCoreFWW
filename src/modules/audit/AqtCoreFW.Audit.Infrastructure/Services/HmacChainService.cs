using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AqtCoreFW.Audit.Application.DTOs;
using AqtCoreFW.Audit.Application.Services;
using AqtCoreFW.Audit.Domain.Entities;
using AqtCoreFW.Audit.Domain.Repositories;
using AqtCoreFW.Audit.Domain.ValueObjects;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Compliance;
using Hangfire;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Audit.Infrastructure.Services;

/// <summary>
/// Computes and verifies the HMAC-SHA256 chain for audit log entries.
/// Chain: HmacSignature[n] = HMAC-SHA256(key, PreviousSignature[n-1] + EntryFingerprint[n])
/// Key source: <see cref="IAuditHmacKeyResolver"/> (Vault path <c>aqt/audit/hmac-v{N}</c> with legacy
/// <c>Audit:HmacKey</c> config fallback for one release).
///
/// Ordering invariant: chain ordering uses signer-assigned SequenceId, NOT InternalId.
/// InternalId is assigned at INSERT time but only visible after COMMIT — two concurrent transactions
/// can produce lower InternalId committing later than a higher one, making InternalId an unreliable
/// chain key that generates permanent false-tamper on verify. SequenceId is assigned by this service
/// at signing time in InternalId order (best-effort temporal), so sign-order is always stable.
///
/// Concurrency invariant: per-tenant sp_getapplock on the raw batch connection serializes concurrent
/// SignTenantChainAsync calls that share the same tenantKey. Different tenants acquire different lock
/// resources and run in parallel. [DisableConcurrentExecution] is intentionally NOT used — it locks
/// by type.method with no awareness of arguments, so it cannot key per-tenant.
/// </summary>
public sealed class HmacChainService : IHmacChainVerifier, IHmacChainBackfiller
{
    private readonly string _connectionString;

    // Elevated (db_owner-capable) connection string used ONLY for cross-tenant TenantId enumeration.
    // Under a least-privilege (non-db_owner) runtime login, the RLS FILTER predicate on
    // AuditLogEntries exposes named-tenant rows only when SESSION_CONTEXT N'TenantId' matches the
    // row's TenantId, and NULL-tenant rows only via audit_admin_bypass. There is no single bypass
    // that makes DISTINCT TenantId return ALL distinct values under a non-db_owner login — you would
    // need to set TenantId context for each tenant before querying, which requires knowing the tenant
    // list first (circular). A db_owner-capable connection bypasses the FILTER predicate entirely
    // (IS_MEMBER('db_owner')=1 short-circuits), returning the complete tenant list in one query.
    // Set via ConnectionStrings:AuditAdmin; falls back to the runtime Default when unset.
    private readonly string _adminConnectionString;

    private readonly IAuditHmacKeyResolver _keyResolver;
    private readonly ILogger<HmacChainService> _logger;

    // EF-backed repository is retained for VerifyAsync (read-only, no RLS write concern)
    // and for the legacy ComputeAsync path. SignTenantChainAsync uses the raw connection exclusively.
    private readonly IAuditLogRepository? _repository;

    // Used by RunBackfillJobAsync to write the audit trail entry for the backfill operation.
    // Null on the test-only (constrained-login) ctor path — RunBackfillJobAsync is not called there.
    private readonly IGuidGenerator? _guidGenerator;
    private readonly IBackgroundAuditJobEnqueuer? _jobEnqueuer;

    /// <summary>
    /// Raw row projection used by the batch signer's raw-SQL reader path.
    /// AuditLogEntry properties have private setters (append-only guard) so we project into this
    /// plain struct for fingerprint computation instead of trying to set private fields.
    /// </summary>
    private readonly record struct AuditRow(
        Guid MessageId,
        Guid? TenantId,
        Guid? UserId,
        string UserName,
        string Action,
        string ModuleName,
        string ResourceType,
        string? ResourceId,
        DateTimeOffset OccurredAt,
        string HmacSignature,
        long SequenceId,
        AuditCategory Category,
        AuditSeverity Severity,
        int SchemaVersion);

    /// <summary>Result of one signing pass: rows written + the chain head before and after.</summary>
    private readonly record struct SigningPassResult(long Count, string? PreHead, string? PostHead);

    /// <summary>Per-tenant backfill evidence aggregated for the chain-backfill audit entry.</summary>
    private readonly record struct TenantChainEvidence(Guid? TenantId, long Rows, string? PreHead, string? PostHead);

    /// <summary>
    /// Tail-attestation watermark row read back at verify time — projected from
    /// <c>[audit].[ChainWatermarks]</c> by <see cref="ReadWatermarkAsync"/>. See
    /// <see cref="BuildWatermarkPayload"/>/<see cref="UpsertWatermarkAsync"/> for the writer side.
    /// </summary>
    private readonly record struct WatermarkRow(long LastSequenceId, string ChainHead, string WatermarkHmac);

    /// <summary>
    /// Primary runtime constructor (DI path).
    /// EF repository is available for VerifyAsync and legacy ComputeAsync.
    /// Connection string is taken explicitly (not from EF DbContext) to avoid
    /// TenantSessionContextInterceptor interference during the raw batch.
    /// adminConnectionString should be a db_owner-capable connection used only for cross-tenant
    /// tenant enumeration in BackfillAllTenantsAsync; falls back to connectionString when null.
    /// </summary>
    public HmacChainService(
        IAuditLogRepository? repository,
        IAuditHmacKeyResolver keyResolver,
        ILogger<HmacChainService> logger,
        string connectionString,
        string? adminConnectionString = null,
        IGuidGenerator? guidGenerator = null,
        IBackgroundAuditJobEnqueuer? jobEnqueuer = null)
    {
        _repository = repository;
        _keyResolver = keyResolver;
        _logger = logger;
        _connectionString = connectionString;
        _adminConnectionString = adminConnectionString ?? connectionString;
        _guidGenerator = guidGenerator;
        _jobEnqueuer = jobEnqueuer;
    }

    /// <summary>
    /// Test / constrained-login constructor: accepts an explicit connection string only.
    /// Used by TenantIsolation integration tests to run the signer under a non-db_owner login
    /// (rls_test_app) to prove RLS bypass-before-read works under prod least-privilege credentials.
    /// VerifyAsync uses the raw connection path when _repository is null.
    /// </summary>
    public HmacChainService(
        string connectionString,
        IAuditHmacKeyResolver keyResolver,
        ILogger<HmacChainService> logger)
    {
        _connectionString = connectionString;
        _adminConnectionString = connectionString;
        _keyResolver = keyResolver;
        _logger = logger;
        _repository = null;
    }

    private byte[] GetKey() => _keyResolver.GetKey();

    // ── Signing ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Signs all unsigned entries for the given tenant in a single batch.
    /// NULL tenantId processes the system-level (tenant-independent) chain.
    ///
    /// Connection ordering:
    ///   1. Open a dedicated raw SqlConnection — NOT the EF AuditDbContext connection.
    ///      TenantSessionContextInterceptor on the EF connection independently sets/clears
    ///      SESSION_CONTEXT on every SaveChanges; reusing the EF connection would fight the
    ///      interceptor and risk the bypass flag being cleared between the read and the write.
    ///   2. EXEC sp_getapplock @Resource='audit-hmac:{tenantKey}' — per-tenant advisory lock
    ///      that serializes concurrent signers on the same tenant without blocking different tenants.
    ///   3. Set RLS context BEFORE any read:
    ///        - Named tenant: sp_set_session_context N'TenantId', @t
    ///        - NULL tenant:  sp_set_session_context N'audit_admin_bypass', N'true'
    ///      Without this, a non-db_owner login (prod least-privilege) cannot read the rows it
    ///      needs to sign — the RLS FILTER predicate hides them.
    ///   4. Set audit_mutate_mode='signature' to bypass the immutability trigger for UPDATEs.
    ///   5. Read anchor + unsigned rows; assign SequenceId + HMAC; UPDATE via raw SQL.
    ///      Uses a raw UPDATE with no WHERE HmacSignature='' guard — unlike the repository's
    ///      UpdateSignatureAsync (first-time single-row signing only), the batch/backfill path must
    ///      re-sign already-signed rows, so it must not skip rows that already carry a signature.
    ///   6. Finally: release applock + clear ALL session-context keys + close.
    ///      Explicit clear before pool return prevents context leaking to the next logical request
    ///      that picks up the same physical connection from the pool.
    /// </summary>
    [AutomaticRetry(Attempts = 5)]
    public async Task SignTenantChainAsync(Guid? tenantId, CancellationToken cancellationToken = default) =>
        await RunSigningPassAsync(tenantId, recomputeAll: false, cancellationToken); // count discarded — incremental sign has no caller that needs it

    /// <summary>
    /// Backfill-re-sign for a single tenant: resets the chain anchor to zero and re-signs
    /// EVERY row (including already-signed rows) in InternalId order, assigning SequenceId 1..N.
    ///
    /// This is the "recompute-all" path required to fix rows signed by the old broken chain.
    /// It differs from <see cref="SignTenantChainAsync"/> in one key way: instead of reading only
    /// unsigned rows (SequenceId=0), it reads ALL rows for the tenant regardless of their current
    /// SequenceId or HmacSignature. The per-tenant applock is held for the entire operation, so a
    /// concurrent live SignTenantChainAsync call for the same tenant waits until backfill completes.
    ///
    /// The trigger tr_AuditLog_NoMutate allows UPDATE under audit_mutate_mode='signature' without
    /// restricting which columns or what value transitions are made — verified empirically.
    /// </summary>
    public async Task BackfillTenantChainAsync(Guid? tenantId, CancellationToken cancellationToken = default) =>
        await RunSigningPassAsync(tenantId, recomputeAll: true, cancellationToken); // count discarded — single-tenant callers (tests) don't need it

    /// <summary>
    /// Backfill-re-sign for a single tenant and return the per-tenant chain evidence:
    /// rows re-signed plus the pre- and post-backfill chain-head signatures. The heads let an
    /// auditor verify whether a backfill replaced an existing chain (pre != null) or signed a
    /// fresh one, and detect a privileged signature rewrite that the row count alone would hide.
    /// </summary>
    private async Task<TenantChainEvidence> RunBackfillPassAsync(Guid? tenantId, CancellationToken cancellationToken)
    {
        var pass = await RunSigningPassAsync(tenantId, recomputeAll: true, cancellationToken);
        return new TenantChainEvidence(tenantId, pass.Count, pass.PreHead, pass.PostHead);
    }

    /// <summary>
    /// Enumerates all distinct TenantId values (including NULL) in the audit table and runs
    /// BackfillTenantChainAsync for each one. Intended for the admin-triggered full-system backfill.
    /// Returns the total count of rows re-signed across all tenants.
    ///
    /// Cross-tenant enumeration uses the admin (db_owner-capable) connection (_adminConnectionString)
    /// so the DISTINCT TenantId query bypasses the RLS FILTER predicate and returns all distinct
    /// values. Per-tenant backfill passes continue to use the runtime Default connection with the
    /// per-tenant TenantId session context — correct under prod least-privilege.
    /// </summary>
    public async Task<long> BackfillAllTenantsAsync(CancellationToken cancellationToken = default) =>
        (await BackfillAllTenantsWithEvidenceAsync(cancellationToken)).TotalRows;

    /// <summary>
    /// Sweep entry point for the recurring chain-signing safety net.
    /// Enumerates every tenant chain that either (a) has at least one unsigned row (SequenceId=0) —
    /// including the NULL system chain — or (b) is fully signed but is missing its tail watermark,
    /// and enqueues an incremental sign job per such tenant.
    ///
    /// WHY (a) exists: per-tenant signing is otherwise driven purely by append — each audit write
    /// enqueues a sign for that entry's tenant. A low-traffic chain that stops receiving new entries
    /// (e.g. the system/NULL chain) never re-fires the append-time enqueue, so any rows left at
    /// SequenceId=0 — for instance rows written before the per-tenant sequencing scheme — would sit
    /// unsigned indefinitely and trip the unsigned-backlog health check. This recurring sweep is the
    /// only path that re-signs a dormant chain without a new append or a manual full backfill.
    ///
    /// WHY (b) exists: rolling out the watermark feature onto pre-existing signed chains (or
    /// recovering from a deleted watermark row) needs the same self-heal mechanism — a chain with
    /// zero unsigned rows never re-enters (a), so it would otherwise stay watermark-less forever.
    /// Enqueuing it here routes it through an incremental sign that reads zero unsigned rows but
    /// still runs the post-loop watermark upsert, healing the watermark from the existing anchor.
    ///
    /// Cross-tenant enumeration uses the admin (db_owner-capable) connection so both DISTINCT queries
    /// bypass the RLS FILTER predicate. Only enqueues for tenants that need (a) or (b) — healthy,
    /// fully-watermarked chains are skipped. Enqueue is idempotent: it shares the same per-tenant
    /// applock as the append-time path, so a concurrent or redundant sign collapses to a no-op once
    /// rows are signed and the watermark is current.
    /// </summary>
    /// <returns>
    /// The number of distinct tenant chains found needing (a) or (b). Requires the full
    /// (enqueuer-bearing) constructor — on the constrained-login test ctor <c>_jobEnqueuer</c> is null
    /// and no sign is actually enqueued, so the count then reflects work detected rather than dispatched.
    /// </returns>
    public async Task<int> EnqueueSignForBackloggedChainsAsync(CancellationToken cancellationToken = default)
    {
        var backlogged = await ReadBackloggedTenantIdsAsync(cancellationToken);
        var watermarkMissing = await ReadWatermarkMissingTenantIdsAsync(cancellationToken);

        // Union, NULL-aware distinct — EqualityComparer<Guid?>.Default (used by LINQ's default
        // comparer) treats two nulls as equal, so the NULL/system chain collapses to one entry
        // even when it appears in both sets.
        var tenants = backlogged.Union(watermarkMissing).ToList();

        foreach (var tenantId in tenants)
            _jobEnqueuer?.EnqueueHmacChain(tenantId);

        if (tenants.Count > 0)
            _logger.LogInformation(
                "HmacChainService: chain-sweep enqueued sign for {Count} backlogged/watermark-missing tenant chain(s).",
                tenants.Count);

        return tenants.Count;
    }

    /// <summary>
    /// Backfill every tenant chain and collect per-tenant evidence (rows + pre/post chain heads).
    /// The public <see cref="BackfillAllTenantsAsync"/> exposes only the total (its interface
    /// contract is unchanged); the job entry point uses the richer result to write tamper-evidence
    /// into the backfill audit entry.
    /// </summary>
    private async Task<(long TotalRows, IReadOnlyList<TenantChainEvidence> PerTenant)> BackfillAllTenantsWithEvidenceAsync(
        CancellationToken cancellationToken)
    {
        var tenants = await ReadDistinctTenantIdsAsync(cancellationToken);
        var perTenant = new List<TenantChainEvidence>(tenants.Count);
        long totalRows = 0;

        foreach (var tenantId in tenants)
        {
            // Accumulate the pass count directly (not a post-lock COUNT query) to avoid a race:
            // a concurrent live signer that started after backfill released the applock could be
            // counted twice. The pass also returns the pre/post chain heads for forensic evidence.
            var evidence = await RunBackfillPassAsync(tenantId, cancellationToken);
            perTenant.Add(evidence);
            totalRows += evidence.Rows;
        }

        return (totalRows, perTenant);
    }

    /// <summary>
    /// Hangfire job entry point for the admin-triggered full-system backfill.
    /// Called by Hangfire with the triggering actor captured at enqueue time (from the HTTP-bound
    /// ICurrentUser). Hangfire runs without an HTTP context — ICurrentUser would be empty there —
    /// so the actor is passed as explicit job arguments instead of being read from ambient context.
    ///
    /// After backfill completes, writes one audit trail entry using the captured actor identity
    /// so the operation is permanently traceable (QĐ 742 §3.1 compliance evidence).
    /// The new audit entry is picked up by the next normal signing pass — not re-backfilled, which
    /// would create an infinite loop.
    /// </summary>
    public async Task RunBackfillJobAsync(
        Guid triggeredByUserId,
        Guid? triggeredByTenantId,
        string triggeredByUserName,
        string? triggeredByIp = null,
        string? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "HMAC chain backfill job started — triggered by user {UserId}", triggeredByUserId);

        var (totalRows, perTenant) = await BackfillAllTenantsWithEvidenceAsync(cancellationToken);

        _logger.LogInformation(
            "HMAC chain backfill job completed — {TotalRows} rows re-signed across all tenants", totalRows);

        // Write audit trail with the captured actor identity.
        // _repository and _guidGenerator are non-null on the DI path (only null on the constrained-
        // login test ctor, which never calls this method).
        if (_repository is not null && _guidGenerator is not null)
        {
            // Record richer tamper-evidence than the row count alone: caller IP + per-tenant pre/post
            // chain heads. Because the immutability trigger permits HMAC rewrites under
            // audit_mutate_mode='signature', this single entry is the only forensic control over a
            // privileged signature rewrite — capturing who/where + the chain transition lets an
            // auditor detect a rewrite that a bare "N rows re-signed" would conceal.
            var dataSnapshot = BuildBackfillEvidenceJson(totalRows, triggeredByIp, correlationId, perTenant);

            var entry = AuditLogEntry.Create(
                _guidGenerator,
                triggeredByTenantId,
                triggeredByUserId,
                triggeredByUserName,
                action: "chain-backfill",
                moduleName: "Audit",
                resourceType: "AuditChain",
                resourceId: null,
                dataSnapshot: dataSnapshot,
                ipAddress: triggeredByIp,
                correlationId: correlationId);

            await _repository.AddAsync(entry, cancellationToken);

            // Enqueue chain signing for the new audit entry so it joins the signed chain.
            _jobEnqueuer?.EnqueueHmacChain(entry.TenantId);
        }
    }

    /// <summary>
    /// Serializes the backfill tamper-evidence into the audit entry's DataSnapshot (nvarchar(max)).
    /// Shape: { rowsReSignedTotal, callerIp, correlationId, tenantChains:[{ tenantId, rows, preHead, postHead }] }.
    /// callerIp + correlationId are also stored in dedicated columns; they are duplicated here so the
    /// evidence blob is self-contained for an offline auditor. System.Text.Json escapes values
    /// correctly — unlike the prior hand-built interpolation.
    /// </summary>
    private static string BuildBackfillEvidenceJson(
        long totalRows, string? callerIp, string? correlationId, IReadOnlyList<TenantChainEvidence> perTenant)
    {
        var snapshot = new
        {
            rowsReSignedTotal = totalRows,
            callerIp,
            correlationId,
            tenantChains = perTenant.Select(t => new
            {
                tenantId = t.TenantId?.ToString(),
                rows = t.Rows,
                preHead = t.PreHead,
                postHead = t.PostHead,
            }),
        };

        return JsonSerializer.Serialize(snapshot);
    }

    /// <summary>
    /// Core signing pass — shared by the normal incremental path (recomputeAll=false) and the
    /// backfill path (recomputeAll=true). Returns the rows signed/re-signed in this pass plus the
    /// pre- and post-pass chain-head signatures (forensic evidence for the backfill path; the
    /// incremental callers discard the result).
    ///
    /// When recomputeAll=false: reads only unsigned rows (SequenceId=0), appends to the existing
    ///   chain starting from the current max SequenceId anchor.
    /// When recomputeAll=true: reads ALL rows (SequenceId any), resets anchor to empty/1 so every
    ///   row gets a fresh SequenceId and a fresh HMAC — effectively replacing the entire chain.
    /// </summary>
    private async Task<SigningPassResult> RunSigningPassAsync(Guid? tenantId, bool recomputeAll, CancellationToken cancellationToken)
    {
        var tenantKey = tenantId?.ToString() ?? "system";

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        try
        {
            // Per-tenant advisory lock — return code checked, THROW on <0.
            // A negative return code (-1 timeout, -3 deadlock) means another signer holds the lock.
            // THROW lets [AutomaticRetry] re-queue the job rather than silently proceeding without
            // the lock, which would let two signers assign overlapping SequenceIds and fork the chain.
            await ExecNonQueryAsync(conn,
                "DECLARE @rc int; " +
                "EXEC @rc = sp_getapplock @Resource=@res, @LockMode=N'Exclusive', @LockOwner=N'Session', @LockTimeout=30000; " +
                "IF @rc < 0 THROW 50000, N'audit-hmac applock not granted (timeout/deadlock)', 1;",
                ("@res", $"audit-hmac:{tenantKey}"),
                cancellationToken);

            // Set RLS context BEFORE any read so a non-db_owner login can see the rows.
            if (tenantId.HasValue)
            {
                await ExecNonQueryAsync(conn,
                    "EXEC sp_set_session_context @key=N'TenantId', @value=@t, @read_only=0",
                    ("@t", tenantId.Value.ToString()),
                    cancellationToken);
            }
            else
            {
                await ExecNonQueryAsync(conn,
                    "EXEC sp_set_session_context @key=N'audit_admin_bypass', @value=N'true', @read_only=0",
                    cancellationToken: cancellationToken);
            }

            // Allow UPDATE on the immutability-protected table.
            await ExecNonQueryAsync(conn,
                "EXEC sp_set_session_context @key=N'audit_mutate_mode', @value=N'signature', @read_only=0",
                cancellationToken: cancellationToken);

            string previousHash;
            long nextSeq;
            List<AuditRow> rows;
            // Chain head as it stood before this pass — null when the tenant chain was never signed.
            // For the backfill path this is the head being REPLACED; surfacing it is what lets an
            // auditor see the chain transition rather than just "N rows re-signed".
            string? preHead;
            // Captured from the incremental branch only — the post-loop watermark upsert below needs
            // it to compute lastSeq even when this pass signs zero rows (0-row heal). Unused by the
            // recomputeAll branch: a 0-row backfill means an empty chain regardless of the prior anchor.
            long anchorSeq = 0L;

            if (recomputeAll)
            {
                // Backfill: discard the existing chain — reset anchor so every row gets a
                // fresh SequenceId starting at 1. Read ALL rows regardless of current SequenceId.
                // Read the prior head first (for evidence) before resetting the anchor.
                var (_, oldSig) = await ReadAnchorAsync(conn, tenantId, cancellationToken);
                preHead = oldSig;
                previousHash = string.Empty;
                nextSeq = 1L;
                rows = await ReadAllRowsAsync(conn, tenantId, cancellationToken);
            }
            else
            {
                // Incremental sign: pick up from the last signed row (anchor).
                (anchorSeq, var anchorSig) = await ReadAnchorAsync(conn, tenantId, cancellationToken);
                preHead = anchorSig;
                previousHash = anchorSig ?? string.Empty;
                nextSeq = anchorSeq + 1L;
                rows = await ReadUnsignedAsync(conn, tenantId, cancellationToken);
            }

            long count = 0;
            foreach (var row in rows)
            {
                var fingerprint = BuildFingerprintFromRow(row);
                var signature = ComputeHmac(previousHash + fingerprint);

                await UpdateSequenceAndSignatureRawAsync(conn, row.MessageId, nextSeq, signature, cancellationToken);

                previousHash = signature;
                nextSeq++;
                count++;
            }

            if (recomputeAll)
            {
                _logger.LogInformation(
                    "HmacChainService: backfill re-signed {Count} entries for tenant {TenantId}",
                    count, tenantKey);
            }
            else
            {
                _logger.LogInformation(
                    "HmacChainService: signed {Count} entries for tenant {TenantId}",
                    count, tenantKey);
            }

            // Post-pass chain head = the last signature written this pass. When no rows were
            // processed the head is unchanged, so report the pre-pass head.
            var postHead = count > 0 ? previousHash : preHead;

            // Tail-attestation watermark — written last, still inside the applock, on this same
            // connection. lastSeq is the highest SequenceId this pass attests to: rows signed this
            // pass (nextSeq - 1) when count > 0; otherwise 0 for an empty backfill (no rows exist at
            // all) or the pre-existing anchor for a 0-row incremental pass (heals a dormant chain's
            // watermark without changing its attested length). Skipped when lastSeq == 0 — a chain
            // with no signed rows gets no watermark row, keeping "no watermark" in sync with "no
            // chain to attest to" for whatever reads this row to verify the tail.
            var lastSeq = count > 0 ? nextSeq - 1 : (recomputeAll ? 0L : anchorSeq);
            if (lastSeq > 0)
            {
                // lastSeq > 0 guarantees postHead is non-null here: either count > 0 (postHead =
                // previousHash, built from at least one signed row this pass) or count == 0 with
                // anchorSeq > 0 (postHead = preHead = the pre-existing anchor signature, which is
                // only null when anchorSeq == 0 — excluded by lastSeq > 0 in that branch).
                var head = postHead!;
                var watermarkHmac = ComputeWatermarkHmac(tenantId, lastSeq, head);
                await UpsertWatermarkAsync(conn, tenantId, lastSeq, head, watermarkHmac, cancellationToken);
            }

            return new SigningPassResult(count, preHead, postHead);
        }
        finally
        {
            await ClearContextAndReleaseLockAsync(conn, tenantId);
        }
    }

    /// <summary>
    /// Legacy single-entry HMAC compute — superseded by the per-tenant batch job.
    /// Delegates to SignTenantChainAsync so both paths share identical chain logic.
    /// Retained for backward compatibility with any Hangfire job records serialized before Phase 2.
    /// </summary>
    public async Task ComputeAsync(Guid entryId, CancellationToken cancellationToken = default)
    {
        if (_repository is not null)
        {
            var entry = await _repository.GetByIdAsync(entryId, cancellationToken);
            if (entry is null)
            {
                _logger.LogWarning("HmacChainService: AuditLogEntry {EntryId} not found", entryId);
                return;
            }

            if (!string.IsNullOrEmpty(entry.HmacSignature))
            {
                _logger.LogDebug("HmacChainService: entry {EntryId} already has HMAC — skipping", entryId);
                return;
            }

            await SignTenantChainAsync(entry.TenantId, cancellationToken);
        }
        else
        {
            // Test path (no EF repository): fall back to null-tenant chain.
            await SignTenantChainAsync(null, cancellationToken);
        }

        _logger.LogDebug("HmacChainService: signed entry {EntryId} via tenant batch", entryId);
    }

    // ── Verification ──────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies HMAC chain integrity for all signed entries belonging to the given tenant.
    /// Streams entries in SequenceId order (sign-order), recomputing each HMAC against the previous.
    ///
    /// Mid-chain hole detection: once a gap in SequenceId is detected between consecutive signed
    /// rows, all rows from the gap point are treated as violations — silently resetting previousHash
    /// would allow spliced-in signed sub-chains to pass verification undetected.
    /// </summary>
    public async Task<ChainVerificationResultDto> VerifyAsync(
        Guid? tenantId, CancellationToken cancellationToken = default)
    {
        // Use EF repository when available (runtime DI path — db_owner connection).
        if (_repository is not null)
            return await VerifyViaRepositoryAsync(tenantId, cancellationToken);

        // Test path (constrained-login ctor): verify via raw connection with RLS context.
        return await VerifyViaRawConnAsync(tenantId, cancellationToken);
    }

    private async Task<ChainVerificationResultDto> VerifyViaRepositoryAsync(
        Guid? tenantId, CancellationToken cancellationToken)
    {
        long entriesChecked = 0;
        string previousHash = string.Empty;
        long expectedNextSeq = 1;

        // Tail-attestation invariant — READ THE WATERMARK BEFORE STREAMING THE CHAIN, not after.
        // The signer commits chain rows one-by-one then writes the watermark last, both inside the
        // same per-tenant applock (RunSigningPassAsync), so for any committed snapshot the actual max
        // SequenceId is always >= the watermark's LastSequenceId. Verify itself runs under READ
        // COMMITTED with no transactional isolation from a concurrently running signer. Reading the
        // watermark FIRST means the chain — streamed afterwards — can only have grown by the time it
        // is read, so maxSeenSeq >= LastSequenceId stays true for the common case of a concurrent
        // append (every append triggers a sign). Reading the watermark AFTER the stream would let a
        // signing pass that completes mid-verify push LastSequenceId above the already-captured
        // maxSeenSeq, producing a false "tail truncation" report on ordinary traffic. Do not move
        // this read below the loop.
        var wm = await ReadWatermarkAsync(tenantId, cancellationToken);
        long maxSeenSeq = 0;
        string? sigAtWatermarkSeq = null;

        await foreach (var entry in _repository!.GetByTenantOrderedAsync(tenantId, cancellationToken))
        {
            entriesChecked++;

            // Mid-chain hole: gap in SequenceId means unsigned rows sit between signed rows.
            if (entry.SequenceId > expectedNextSeq)
            {
                _logger.LogWarning(
                    "HmacChainService: chain hole at entry {EntryId} — expected seq={Expected}, got seq={Actual} for tenant {TenantId}",
                    entry.MessageId, expectedNextSeq, entry.SequenceId, tenantId?.ToString() ?? "(null)");

                return new ChainVerificationResultDto(
                    IsValid: false,
                    EntriesChecked: entriesChecked,
                    FirstTamperedEntryId: entry.MessageId,
                    TamperDetail: $"Chain hole: expected seq={expectedNextSeq}, got seq={entry.SequenceId}");
            }

            var fingerprint = BuildFingerprint(entry);
            var expected = ComputeHmac(previousHash + fingerprint);

            if (!string.Equals(expected, entry.HmacSignature, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "HmacChainService: HMAC mismatch at entry {EntryId} seq={Seq} for tenant {TenantId}",
                    entry.MessageId, entry.SequenceId, tenantId?.ToString() ?? "(null)");

                return new ChainVerificationResultDto(
                    IsValid: false,
                    EntriesChecked: entriesChecked,
                    FirstTamperedEntryId: entry.MessageId,
                    TamperDetail: $"HMAC mismatch at seq={entry.SequenceId}");
            }

            previousHash = entry.HmacSignature;
            expectedNextSeq = entry.SequenceId + 1;

            if (entry.SequenceId > maxSeenSeq)
                maxSeenSeq = entry.SequenceId;
            if (wm is { } w && entry.SequenceId == w.LastSequenceId)
                sigAtWatermarkSeq = entry.HmacSignature;
        }

        // Final gate for tail-level attacks the stream itself cannot see. The per-row HMAC/hole
        // checks above already ran clean at this point — an in-chain tamper would have returned
        // earlier with its precise FirstTamperedEntryId, so this check never overrides that detail.
        var watermarkFailure = EvaluateWatermark(tenantId, wm, maxSeenSeq, sigAtWatermarkSeq, entriesChecked);
        if (watermarkFailure is not null)
            return watermarkFailure;

        _logger.LogInformation(
            "HmacChainService: chain verified for tenant {TenantId}, {Count} entries checked",
            tenantId?.ToString() ?? "(null)", entriesChecked);

        return new ChainVerificationResultDto(
            IsValid: true,
            EntriesChecked: entriesChecked,
            FirstTamperedEntryId: null,
            TamperDetail: null);
    }

    private async Task<ChainVerificationResultDto> VerifyViaRawConnAsync(
        Guid? tenantId, CancellationToken cancellationToken)
    {
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        // Set RLS context so a non-db_owner connection can see the rows.
        if (tenantId.HasValue)
        {
            await ExecNonQueryAsync(conn,
                "EXEC sp_set_session_context @key=N'TenantId', @value=@t, @read_only=0",
                ("@t", tenantId.Value.ToString()),
                cancellationToken);
        }
        else
        {
            await ExecNonQueryAsync(conn,
                "EXEC sp_set_session_context @key=N'audit_admin_bypass', @value=N'true', @read_only=0",
                cancellationToken: cancellationToken);
        }

        try
        {
            long entriesChecked = 0;
            string previousHash = string.Empty;
            long expectedNextSeq = 1;

            // Tail-attestation invariant — read the watermark BEFORE streaming the chain. Same
            // invariant as VerifyViaRepositoryAsync (see the comment there): the signer commits rows
            // then the watermark last inside the applock, so reading the watermark first keeps
            // maxSeenSeq >= LastSequenceId race-free against a concurrent append. Do not move this
            // read below the SELECT/stream that follows.
            var wm = await ReadWatermarkAsync(tenantId, cancellationToken);
            long maxSeenSeq = 0;
            string? sigAtWatermarkSeq = null;

            var (tenantFilter, tenantParam) = BuildTenantFilter(tenantId);
            var sql = $"""
                SELECT MessageId, TenantId, UserId, UserName, Action, ModuleName, ResourceType,
                       ResourceId, OccurredAt, HmacSignature, SequenceId, Category, Severity, SchemaVersion
                FROM [audit].[AuditLogEntries]
                WHERE SequenceId > 0
                  {tenantFilter}
                ORDER BY SequenceId
                """;

            await using var cmd = new SqlCommand(sql, conn);
            if (tenantParam.HasValue)
                cmd.Parameters.AddWithValue("@tenantId", tenantParam.Value);

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var row = ReadRowFromReader(reader);
                entriesChecked++;

                if (row.SequenceId > expectedNextSeq)
                {
                    return new ChainVerificationResultDto(
                        IsValid: false,
                        EntriesChecked: entriesChecked,
                        FirstTamperedEntryId: row.MessageId,
                        TamperDetail: $"Chain hole: expected seq={expectedNextSeq}, got seq={row.SequenceId}");
                }

                var fingerprint = BuildFingerprintFromRow(row);
                var expected = ComputeHmac(previousHash + fingerprint);

                if (!string.Equals(expected, row.HmacSignature, StringComparison.OrdinalIgnoreCase))
                {
                    return new ChainVerificationResultDto(
                        IsValid: false,
                        EntriesChecked: entriesChecked,
                        FirstTamperedEntryId: row.MessageId,
                        TamperDetail: $"HMAC mismatch at seq={row.SequenceId}");
                }

                previousHash = row.HmacSignature;
                expectedNextSeq = row.SequenceId + 1;

                if (row.SequenceId > maxSeenSeq)
                    maxSeenSeq = row.SequenceId;
                if (wm is { } w && row.SequenceId == w.LastSequenceId)
                    sigAtWatermarkSeq = row.HmacSignature;
            }

            // Final gate for tail-level attacks the stream itself cannot see — see
            // VerifyViaRepositoryAsync for the full rationale (identical logic, shared helper).
            var watermarkFailure = EvaluateWatermark(tenantId, wm, maxSeenSeq, sigAtWatermarkSeq, entriesChecked);
            if (watermarkFailure is not null)
                return watermarkFailure;

            return new ChainVerificationResultDto(
                IsValid: true,
                EntriesChecked: entriesChecked,
                FirstTamperedEntryId: null,
                TamperDetail: null);
        }
        finally
        {
            await ClearContextAsync(conn);
        }
    }

    // ── Tail-attestation watermark (verify side) ─────────────────────────────

    /// <summary>
    /// Pure decision helper — the SINGLE source of watermark-evaluation logic shared by both
    /// <see cref="VerifyViaRepositoryAsync"/> and <see cref="VerifyViaRawConnAsync"/>, mirroring the
    /// single-source rule already applied to <see cref="BuildFingerprintCore"/> so the two verify
    /// paths cannot diverge. Returns null when the watermark check passes; a failing
    /// <see cref="ChainVerificationResultDto"/> otherwise. Callers run this only AFTER their stream
    /// loop completes without an earlier in-chain tamper/hole return.
    ///
    /// Check order (this is the final gate, run after the existing per-row HMAC/hole checks):
    ///   (b) a non-empty chain with no watermark row at all — the watermark itself was deleted to
    ///       revert to pre-rollout "no tail check" (or the chain has never had a signing pass write
    ///       one — see the rollout/first-sign race documented on the callers).
    ///   (a) the watermark was tampered — HMAC over (tenant, LastSequenceId, ChainHead) no longer
    ///       matches WatermarkHmac (e.g. LastSequenceId lowered by hand without recomputing the HMAC).
    ///   (c) LOAD-BEARING tail-truncation check — the highest SequenceId actually observed while
    ///       streaming the chain is below what the watermark attests to. This is the check that
    ///       detects an attacker deleting the K newest rows without the key to update the watermark.
    ///   (d) defense-in-depth: the signature observed at SequenceId == LastSequenceId does not match
    ///       the watermark's recorded ChainHead (a keyless splice at exactly the watermarked
    ///       position). Largely redundant with (c) plus the in-chain HMAC check; kept because it is
    ///       cheap and ties the watermark to the observed head.
    /// </summary>
    private ChainVerificationResultDto? EvaluateWatermark(
        Guid? tenantId, WatermarkRow? wm, long maxSeenSeq, string? sigAtWatermarkSeq, long entriesChecked)
    {
        if (wm is null)
            return entriesChecked > 0                                   // (b) non-empty chain must have a watermark
                ? Fail(entriesChecked, null, "Missing chain watermark for a signed chain")
                : null;                                                  // empty chain, ok

        var w = wm.Value;
        var expected = ComputeWatermarkHmac(tenantId, w.LastSequenceId, w.ChainHead);
        if (!string.Equals(expected, w.WatermarkHmac, StringComparison.OrdinalIgnoreCase)) // (a)
            return Fail(entriesChecked, null, "Chain watermark tampered (HMAC mismatch)");

        if (maxSeenSeq < w.LastSequenceId)                              // (c) LOAD-BEARING tail-truncation check
            return Fail(entriesChecked, null,
                $"Tail truncation: watermark seq={w.LastSequenceId}, chain max seq={maxSeenSeq}");

        // (d) defense-in-depth head match — largely redundant w/ (c) + in-chain HMAC; cheap.
        if (sigAtWatermarkSeq is not null &&
            !string.Equals(sigAtWatermarkSeq, w.ChainHead, StringComparison.OrdinalIgnoreCase))
            return Fail(entriesChecked, null, "Chain watermark head mismatch (splice)");

        return null;
    }

    private static ChainVerificationResultDto Fail(long entriesChecked, Guid? firstTamperedEntryId, string detail) =>
        new(IsValid: false, EntriesChecked: entriesChecked, FirstTamperedEntryId: firstTamperedEntryId, TamperDetail: detail);

    /// <summary>
    /// Reads the tail-attestation watermark row for a tenant (or the NULL/system chain). Opens its
    /// own short-lived connection — <c>[audit].[ChainWatermarks]</c> carries no RLS policy (it holds
    /// only a signed chain-length attestation, no tenant business data), so no SESSION_CONTEXT setup
    /// is needed regardless of which verify path calls this. A dedicated connection also avoids
    /// coupling this single cheap row-read to the EF streaming enumerator's connection lifetime on
    /// the repository path.
    /// </summary>
    private async Task<WatermarkRow?> ReadWatermarkAsync(Guid? tenantId, CancellationToken ct)
    {
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var (tenantFilter, tenantParam) = BuildTenantFilter(tenantId);
        var sql = $"""
            SELECT LastSequenceId, ChainHead, WatermarkHmac
            FROM [audit].[ChainWatermarks]
            WHERE 1=1
              {tenantFilter}
            """;

        await using var cmd = new SqlCommand(sql, conn);
        if (tenantParam.HasValue)
            cmd.Parameters.AddWithValue("@tenantId", tenantParam.Value);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;

        return new WatermarkRow(reader.GetInt64(0), reader.GetString(1), reader.GetString(2));
    }

    // ── HMAC helpers ──────────────────────────────────────────────────────────

    private string ComputeHmac(string input)
    {
        using var hmac = new HMACSHA256(GetKey());
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Watermark payload — single source shared by the writer (here) and whatever verifies the tail
    /// against this row, so the two paths cannot diverge on the byte layout being signed. tenantKey
    /// uses the same "system" fallback as the applock resource key (<see cref="RunSigningPassAsync"/>)
    /// so the payload is stable and unambiguous for the NULL/system chain.
    /// </summary>
    private static string BuildWatermarkPayload(Guid? tenantId, long lastSeq, string head) =>
        $"wm|{(tenantId?.ToString() ?? "system")}|{lastSeq}|{head}";

    /// <summary>HMAC-SHA256 (hex) over the watermark payload, using the same key/algorithm as the chain.</summary>
    private string ComputeWatermarkHmac(Guid? tenantId, long lastSeq, string head) =>
        ComputeHmac(BuildWatermarkPayload(tenantId, lastSeq, head));

    /// <summary>
    /// HMAC payload schema branches on <see cref="AuditLogEntry.SchemaVersion"/>:
    ///   v1 (legacy) — pre-QĐ 742 fields only; existing rows keep their original sig.
    ///   v2 (current) — adds Category, Severity, SchemaVersion so the QĐ 742 classification
    ///   becomes tamper-protected. <see cref="AuditLogEntry.DataSnapshot"/> is intentionally
    ///   excluded so <c>AuditPiiAnonymizer</c> can scrub PII (RTBF / Law 91/2025) without
    ///   invalidating the chain.
    /// SequenceId is intentionally excluded: it is assigned by this service after creation,
    /// so including it in the fingerprint would create a circular dependency.
    /// Verifier and computer MUST agree on the version, hence both call this method.
    /// </summary>
    internal static string BuildFingerprint(AuditLogEntry e) => BuildFingerprintCore(
        e.SchemaVersion, e.MessageId, e.TenantId, e.UserId, e.Action,
        e.Category, e.Severity, e.ModuleName, e.ResourceType, e.ResourceId, e.OccurredAt);

    private static string BuildFingerprintFromRow(AuditRow r) => BuildFingerprintCore(
        r.SchemaVersion, r.MessageId, r.TenantId, r.UserId, r.Action,
        r.Category, r.Severity, r.ModuleName, r.ResourceType, r.ResourceId, r.OccurredAt);

    /// <summary>
    /// Single source of truth for the fingerprint string. Both the EF-entity path (verify) and the
    /// raw-reader path (signer) route through here, so they cannot diverge. This matters because at
    /// runtime the signer uses the raw path while verify uses the EF path — any byte difference
    /// between two separate builders would make every verify falsely report tamper. One method
    /// makes that divergence impossible by construction.
    /// </summary>
    private static string BuildFingerprintCore(
        int schemaVersion, Guid messageId, Guid? tenantId, Guid? userId, string action,
        AuditCategory category, AuditSeverity severity, string moduleName,
        string resourceType, string? resourceId, DateTimeOffset occurredAt) => schemaVersion switch
    {
        <= 1 => $"{messageId}|{tenantId}|{userId}|{action}|{moduleName}|{resourceType}|{resourceId}|{occurredAt:O}",
        _    => $"v2|{messageId}|{tenantId}|{userId}|{action}|{(int)category}|{(int)severity}|" +
                $"{moduleName}|{resourceType}|{resourceId}|{occurredAt:O}|{schemaVersion}"
    };

    // ── Raw SQL helpers ───────────────────────────────────────────────────────

    private async Task<(long AnchorSeq, string? AnchorSig)> ReadAnchorAsync(
        SqlConnection conn, Guid? tenantId, CancellationToken ct)
    {
        var (tenantFilter, tenantParam) = BuildTenantFilter(tenantId);
        var sql = $"""
            SELECT TOP 1 SequenceId, HmacSignature
            FROM [audit].[AuditLogEntries]
            WHERE SequenceId > 0
              {tenantFilter}
            ORDER BY SequenceId DESC
            """;

        await using var cmd = new SqlCommand(sql, conn);
        if (tenantParam.HasValue)
            cmd.Parameters.AddWithValue("@tenantId", tenantParam.Value);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return (0L, null);

        return (reader.GetInt64(0), reader.IsDBNull(1) ? null : reader.GetString(1));
    }

    private static async Task<List<AuditRow>> ReadUnsignedAsync(
        SqlConnection conn, Guid? tenantId, CancellationToken ct)
    {
        var (tenantFilter, tenantParam) = BuildTenantFilter(tenantId);
        var sql = $"""
            SELECT MessageId, TenantId, UserId, UserName, Action, ModuleName, ResourceType,
                   ResourceId, OccurredAt, HmacSignature, SequenceId, Category, Severity, SchemaVersion
            FROM [audit].[AuditLogEntries]
            WHERE SequenceId = 0
              {tenantFilter}
            ORDER BY InternalId
            """;

        await using var cmd = new SqlCommand(sql, conn);
        if (tenantParam.HasValue)
            cmd.Parameters.AddWithValue("@tenantId", tenantParam.Value);

        var results = new List<AuditRow>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            results.Add(ReadRowFromReader(reader));

        return results;
    }

    /// <summary>
    /// Reads ALL rows for the given tenant ordered by InternalId — used by the backfill path.
    /// Unlike <see cref="ReadUnsignedAsync"/>, there is NO SequenceId=0 filter: the backfill must
    /// re-sign every row regardless of whether it was previously signed (possibly with a wrong chain).
    /// The caller holds the per-tenant applock before calling this, so no concurrent signer
    /// can write new rows or modify SequenceIds during the read.
    /// </summary>
    private static async Task<List<AuditRow>> ReadAllRowsAsync(
        SqlConnection conn, Guid? tenantId, CancellationToken ct)
    {
        var (tenantFilter, tenantParam) = BuildTenantFilter(tenantId);
        var sql = $"""
            SELECT MessageId, TenantId, UserId, UserName, Action, ModuleName, ResourceType,
                   ResourceId, OccurredAt, HmacSignature, SequenceId, Category, Severity, SchemaVersion
            FROM [audit].[AuditLogEntries]
            WHERE 1=1
              {tenantFilter}
            ORDER BY InternalId
            """;

        await using var cmd = new SqlCommand(sql, conn);
        if (tenantParam.HasValue)
            cmd.Parameters.AddWithValue("@tenantId", tenantParam.Value);

        var results = new List<AuditRow>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            results.Add(ReadRowFromReader(reader));

        return results;
    }

    /// <summary>
    /// Reads the list of distinct TenantId values (including NULL) present in the audit table.
    /// Used by <see cref="BackfillAllTenantsAsync"/> to enumerate which tenant chains need backfill.
    ///
    /// Uses _adminConnectionString (db_owner-capable) so the FILTER predicate on AuditLogEntries is
    /// bypassed entirely (IS_MEMBER('db_owner')=1 short-circuits). Under a non-db_owner login, there
    /// is no single SESSION_CONTEXT combination that makes all distinct TenantIds visible: named-tenant
    /// rows require TenantId context matching each row's value (circular — you must know the list to
    /// set context, but you need context to get the list), and audit_admin_bypass alone exposes only
    /// NULL-tenant rows. Only a db_owner-capable connection resolves this without circular dependency.
    /// </summary>
    private async Task<List<Guid?>> ReadDistinctTenantIdsAsync(CancellationToken ct)
    {
        // Use the elevated (db_owner-capable) connection for cross-tenant enumeration.
        // No SESSION_CONTEXT needed — db_owner bypasses the RLS FILTER predicate entirely.
        // Per-tenant backfill passes continue to use the runtime Default connection with
        // per-tenant TenantId session context set in RunSigningPassAsync.
        await using var conn = new SqlConnection(_adminConnectionString);
        await conn.OpenAsync(ct);

        const string sql = """
            SELECT DISTINCT TenantId
            FROM [audit].[AuditLogEntries]
            """;

        await using var cmd = new SqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var tenants = new List<Guid?>();
        while (await reader.ReadAsync(ct))
            tenants.Add(reader.IsDBNull(0) ? null : reader.GetGuid(0));

        return tenants;
    }

    /// <summary>
    /// Enumerates the distinct TenantId values (including NULL) that currently have at least one
    /// unsigned row (SequenceId=0). Mirrors <see cref="ReadDistinctTenantIdsAsync"/> but filters to
    /// the backlog so the recurring sweep only enqueues work where there is something to sign.
    /// Filters on SequenceId=0 specifically — that is exactly the set the incremental signer
    /// (<see cref="ReadUnsignedAsync"/>) can heal. This is intentionally NARROWER than the
    /// unsigned-backlog health check, which flags <c>SequenceId=0 OR HmacSignature=''</c>: a row with
    /// a SequenceId but an empty signature is a separate state that only a full backfill recomputes
    /// (incremental sign reads SequenceId=0, so enqueuing for it would be a no-op). That divergent
    /// state is unreachable by construction — <see cref="UpdateSequenceAndSignatureRawAsync"/> always
    /// writes SequenceId and HmacSignature together, and AuditLogEntry.Create defaults both to 0/''
    /// together — so it can only arise from corruption / a manual DB edit / an interrupted backfill.
    /// The recurring sweep deliberately does NOT chase that case (it would mean routing those tenants
    /// to a full recompute on every tick); the correct operator response to such corruption is the
    /// admin chain-backfill endpoint. If that state ever occurs, the health check stays Unhealthy
    /// (by design — it surfaces the corruption) rather than being silently masked.
    /// Uses the elevated (db_owner-capable) connection — db_owner bypasses the RLS FILTER
    /// predicate, so no per-tenant SESSION_CONTEXT is needed to see the full cross-tenant backlog.
    /// </summary>
    private async Task<List<Guid?>> ReadBackloggedTenantIdsAsync(CancellationToken ct)
    {
        await using var conn = new SqlConnection(_adminConnectionString);
        await conn.OpenAsync(ct);

        const string sql = """
            SELECT DISTINCT TenantId
            FROM [audit].[AuditLogEntries]
            WHERE SequenceId = 0
            """;

        await using var cmd = new SqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var tenants = new List<Guid?>();
        while (await reader.ReadAsync(ct))
            tenants.Add(reader.IsDBNull(0) ? null : reader.GetGuid(0));

        return tenants;
    }

    /// <summary>
    /// Enumerates distinct TenantId values (including NULL) that have at least one signed row
    /// (SequenceId > 0) but no corresponding <see cref="ChainWatermark"/> row yet. Feeds the
    /// rollout/self-heal path: a signed-but-unwatermarked chain (pre-existing chain signed before
    /// this feature shipped, or a watermark row manually deleted) gets enqueued for an incremental
    /// sign, which — even reading zero unsigned rows — still runs the post-loop watermark upsert and
    /// heals it from the anchor.
    /// Uses the elevated (db_owner-capable) connection, same rationale as
    /// <see cref="ReadBackloggedTenantIdsAsync"/> — the NOT EXISTS join needs to see every tenant's
    /// signed rows and every existing watermark row regardless of RLS.
    /// </summary>
    private async Task<List<Guid?>> ReadWatermarkMissingTenantIdsAsync(CancellationToken ct)
    {
        await using var conn = new SqlConnection(_adminConnectionString);
        await conn.OpenAsync(ct);

        const string sql = """
            SELECT DISTINCT a.TenantId
            FROM [audit].[AuditLogEntries] a
            WHERE a.SequenceId > 0
              AND NOT EXISTS (
                SELECT 1 FROM [audit].[ChainWatermarks] w
                WHERE (w.TenantId = a.TenantId) OR (w.TenantId IS NULL AND a.TenantId IS NULL))
            """;

        await using var cmd = new SqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var tenants = new List<Guid?>();
        while (await reader.ReadAsync(ct))
            tenants.Add(reader.IsDBNull(0) ? null : reader.GetGuid(0));

        return tenants;
    }

    private static async Task UpdateSequenceAndSignatureRawAsync(
        SqlConnection conn, Guid messageId, long sequenceId, string signature, CancellationToken ct)
    {
        // Raw UPDATE without WHERE HmacSignature='' guard — the per-tenant applock ensures no
        // other signer is active on this tenant, and audit_mutate_mode='signature' is already
        // set on this connection (step 4 in SignTenantChainAsync).
        const string sql = """
            UPDATE [audit].[AuditLogEntries]
            SET SequenceId = @seq, HmacSignature = @sig
            WHERE MessageId = @id
            """;

        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@seq", sequenceId);
        cmd.Parameters.AddWithValue("@sig", signature);
        cmd.Parameters.AddWithValue("@id", messageId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Update-else-insert for the tail watermark. Safe as a plain two-statement upsert (no MERGE,
    /// no race window) because the caller holds the per-tenant sp_getapplock for the entire signing
    /// pass — only one writer per tenant can ever reach here at a time.
    ///
    /// Two separate UPDATE statements (named-tenant vs NULL-tenant) rather than one WHERE with
    /// "@t IS NULL AND TenantId IS NULL OR TenantId=@t" — that single-clause form is ambiguous
    /// (AND binds tighter than OR, so it does not evaluate as "either the null case or the named
    /// case" the way it reads). NULL comparisons always use IS NULL, never "= NULL", mirroring
    /// <see cref="BuildTenantFilter"/>.
    /// </summary>
    private static async Task UpsertWatermarkAsync(
        SqlConnection conn, Guid? tenantId, long lastSeq, string head, string watermarkHmac, CancellationToken ct)
    {
        var updateSql = tenantId.HasValue
            ? """
              UPDATE [audit].[ChainWatermarks]
                 SET LastSequenceId = @seq, ChainHead = @head, WatermarkHmac = @hmac, UpdatedAtUtc = SYSDATETIMEOFFSET()
               WHERE TenantId = @t
              """
            : """
              UPDATE [audit].[ChainWatermarks]
                 SET LastSequenceId = @seq, ChainHead = @head, WatermarkHmac = @hmac, UpdatedAtUtc = SYSDATETIMEOFFSET()
               WHERE TenantId IS NULL
              """;

        await using (var updateCmd = new SqlCommand(updateSql, conn))
        {
            updateCmd.Parameters.AddWithValue("@seq", lastSeq);
            updateCmd.Parameters.AddWithValue("@head", head);
            updateCmd.Parameters.AddWithValue("@hmac", watermarkHmac);
            if (tenantId.HasValue)
                updateCmd.Parameters.AddWithValue("@t", tenantId.Value);

            var updated = await updateCmd.ExecuteNonQueryAsync(ct);
            if (updated > 0)
                return;
        }

        const string insertSql = """
            INSERT INTO [audit].[ChainWatermarks] (TenantId, LastSequenceId, ChainHead, WatermarkHmac, UpdatedAtUtc)
            VALUES (@t, @seq, @head, @hmac, SYSDATETIMEOFFSET())
            """;

        await using var insertCmd = new SqlCommand(insertSql, conn);
        insertCmd.Parameters.AddWithValue("@t", (object?)tenantId ?? DBNull.Value);
        insertCmd.Parameters.AddWithValue("@seq", lastSeq);
        insertCmd.Parameters.AddWithValue("@head", head);
        insertCmd.Parameters.AddWithValue("@hmac", watermarkHmac);
        await insertCmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task ClearContextAndReleaseLockAsync(SqlConnection conn, Guid? tenantId)
    {
        // Release the per-tenant advisory lock first so the next waiter can proceed immediately,
        // then clear all session-context keys before the connection returns to the pool.
        try
        {
            var tenantKey = tenantId?.ToString() ?? "system";
            await using var releaseCmd = new SqlCommand(
                "EXEC sp_releaseapplock @Resource=@res, @LockOwner=N'Session'", conn);
            releaseCmd.Parameters.AddWithValue("@res", $"audit-hmac:{tenantKey}");
            await releaseCmd.ExecuteNonQueryAsync(CancellationToken.None);
        }
        catch
        {
            // Connection faulted or lock already released — pool reset handles cleanup.
        }

        await ClearContextAsync(conn);
    }

    private static async Task ClearContextAsync(SqlConnection conn)
    {
        // Clear all three context keys set during the batch.
        // sp_reset_connection also clears SESSION_CONTEXT when the connection is returned to the pool
        // (assuming @read_only=0, which we always use), but explicit clear is belt-and-braces
        // against Connection Reset=false misconfig and any connection-pinning scenario.
        try
        {
            const string clearSql = """
                EXEC sp_set_session_context @key=N'TenantId',           @value=NULL, @read_only=0;
                EXEC sp_set_session_context @key=N'audit_admin_bypass', @value=NULL, @read_only=0;
                EXEC sp_set_session_context @key=N'audit_mutate_mode',  @value=NULL, @read_only=0;
                """;
            await using var cmd = new SqlCommand(clearSql, conn);
            await cmd.ExecuteNonQueryAsync(CancellationToken.None);
        }
        catch
        {
            // Connection already faulted — pool reset clears SESSION_CONTEXT on return.
        }
    }

    /// <summary>
    /// Tenant filter fragment + optional parameter value.
    /// Uses IS NULL (not = NULL) for the null-tenant case — SQL NULL comparisons require IS NULL.
    /// </summary>
    private static (string Filter, Guid? Param) BuildTenantFilter(Guid? tenantId) =>
        tenantId.HasValue
            ? ("AND TenantId = @tenantId", tenantId)
            : ("AND TenantId IS NULL", (Guid?)null);

    private static async Task ExecNonQueryAsync(
        SqlConnection conn,
        string sql,
        (string Name, string Value)? param = null,
        CancellationToken cancellationToken = default)
    {
        await using var cmd = new SqlCommand(sql, conn);
        if (param.HasValue)
            cmd.Parameters.AddWithValue(param.Value.Name, param.Value.Value);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    // ── Reader projection ─────────────────────────────────────────────────────

    /// <summary>
    /// Projects the current SqlDataReader row into <see cref="AuditRow"/>.
    /// Column order must exactly match the SELECT list used in ReadUnsignedAsync and VerifyViaRawConnAsync:
    ///   0=MessageId, 1=TenantId, 2=UserId, 3=UserName, 4=Action, 5=ModuleName,
    ///   6=ResourceType, 7=ResourceId, 8=OccurredAt, 9=HmacSignature,
    ///   10=SequenceId, 11=Category, 12=Severity, 13=SchemaVersion
    /// </summary>
    private static AuditRow ReadRowFromReader(SqlDataReader reader) => new(
        MessageId:    reader.GetGuid(0),
        TenantId:     reader.IsDBNull(1)  ? null          : reader.GetGuid(1),
        UserId:       reader.IsDBNull(2)  ? null          : reader.GetGuid(2),
        UserName:     reader.IsDBNull(3)  ? string.Empty  : reader.GetString(3),
        Action:       reader.GetString(4),
        ModuleName:   reader.GetString(5),
        ResourceType: reader.GetString(6),
        ResourceId:   reader.IsDBNull(7)  ? null          : reader.GetString(7),
        OccurredAt:   reader.GetDateTimeOffset(8),
        HmacSignature: reader.IsDBNull(9) ? string.Empty  : reader.GetString(9),
        SequenceId:   reader.GetInt64(10),
        Category:     (AuditCategory)reader.GetInt32(11),
        Severity:     (AuditSeverity)reader.GetInt32(12),
        SchemaVersion: reader.GetInt32(13));
}
