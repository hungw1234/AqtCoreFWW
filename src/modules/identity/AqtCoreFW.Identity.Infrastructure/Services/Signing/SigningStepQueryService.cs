using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Identity.Infrastructure.Services.Signing;

/// <summary>
/// Read-only query surface for per-step signing history, consumed by <see cref="InProcessSigningRequestClient"/>.
///
/// Operates directly on <see cref="IdentityDbContext"/> (same pattern as SignatureOrchestrator).
/// Every query explicitly scopes to tenantId because the connection runs as db_owner, which bypasses
/// SQL Server row-level security — the tenant guard must live in the predicate.
///
/// <para>A signing request is backed by one of two underlying state machines (<see cref="SigningUnderlyingKind"/>):
/// a <see cref="SigningDocFlowRecord"/> (USB-token / VGCA DocFlow, addressed by a long record id) OR a
/// <see cref="SigningPendingTransaction"/> (server-side embedded PAdES via the remote hash-signer, addressed by the
/// provider's string TransactionId). Both produce a Completed signed PDF in their own bucket. This surface resolves
/// completion/status/signed-key uniformly across both kinds so the per-step history, the signed badge, and the
/// per-step download behave the same regardless of which provider signed the document.</para>
///
/// SignerName lookup reuses the same pattern as ListFilesQueryHandler (batch IIdentityModuleClient).
/// For simplicity in this thin read surface, signer name is left null (the FE can resolve from
/// the SignerUserId if needed); see code comment below.
/// </summary>
public sealed class SigningStepQueryService(IdentityDbContext db)
{
    // Buckets where each underlying kind stores its final signed PDF (mirror the writer adapters).
    private const string SigningDocFlowBucket = "signing-docflow";
    private const string SigningPadesBucket = "signing-pades";

    /// <summary>
    /// Uniform projection of a signing request's underlying state, resolved from whichever record kind backs it.
    /// <paramref name="HasSignedPdf"/> is the display flag (a signed artifact exists); <paramref name="SignedBucket"/>
    /// and <paramref name="SignedKey"/> are populated only when the signed PDF is actually retrievable (Completed).
    /// </summary>
    private readonly record struct UnderlyingState(
        bool IsCompleted,
        string Status,
        DateTime? CompletedAt,
        bool HasSignedPdf,
        string? SignedBucket,
        string? SignedKey);

    /// <summary>
    /// Returns signing steps for all signing requests linked to a specific file, one row per genuine
    /// signing attempt. Steps are gathered across all SigningRequest rows whose SourceFileId matches.
    /// Empty list = no signing request has been created for this file (valid state).
    ///
    /// <para>Visibility rule (replaces the old collapse-per-sequence behaviour): every <b>Completed</b>
    /// request surfaces as its own step, because each independent sign of the same file is a separate
    /// request that produced its own signed PDF blob — collapsing them by sequence number hid every
    /// successful re-sign except the latest (a single-signer file re-signed N times has N requests all at
    /// sequence 1). Non-Completed attempts (Initiated / Failed / Expired) are only shown for a sequence that
    /// has <b>no</b> Completed request — this keeps the original intent of hiding a failed attempt that was
    /// subsequently re-signed to success at the same sequence.</para>
    /// </summary>
    public async Task<IReadOnlyList<SigningStepDto>> GetStepsForFileAsync(
        Guid tenantId, Guid sourceFileId, CancellationToken ct)
    {
        // Load all SigningRequests for this (tenant, file) ordered deterministically.
        // Explicit TenantId in all predicates (db_owner RLS bypass).
        var requests = await db.SigningRequests.AsNoTracking()
            .Where(r => r.TenantId == tenantId && r.SourceFileId == sourceFileId && !r.IsDeleted)
            .OrderBy(r => r.SignerSequenceNumber)
            .ThenBy(r => r.Id) // chronological within the same sequence
            .ToListAsync(ct);

        // Resolve every request's underlying state once, then reuse for both visibility and projection.
        var states = await BuildUnderlyingStatesAsync(requests, tenantId, ct);

        // Keep every Completed attempt; show a non-Completed attempt only when its sequence has no
        // Completed sibling (so a failed-then-re-signed step shows only the success).
        var visibleRequests = SelectVisibleAttempts(requests, states);

        return ProjectSteps(visibleRequests, states);
    }

    /// <summary>
    /// Returns ordered signing steps for the signing request identified by its public id.
    /// The Forms module stores FormSubmission.SigningRequestId = request PublicId.
    /// </summary>
    public async Task<IReadOnlyList<SigningStepDto>> GetStepsForRequestPublicIdAsync(
        Guid tenantId, Guid requestPublicId, CancellationToken ct)
    {
        var request = await db.SigningRequests.AsNoTracking()
            .FirstOrDefaultAsync(
                r => r.TenantId == tenantId && r.PublicId == requestPublicId && !r.IsDeleted,
                ct);

        if (request is null)
            return Array.Empty<SigningStepDto>();

        var states = await BuildUnderlyingStatesAsync([request], tenantId, ct);
        return ProjectSteps([request], states);
    }

    /// <summary>
    /// Resolves the signed storage key for a specific step of an uploaded file.
    /// Returns Found=false when: request not found, wrong tenant, step not Completed, or no key recorded.
    ///
    /// Deterministic resolution: when multiple SigningRequest rows share the same
    /// (TenantId, SourceFileId, SequenceNumber) — possible after re-sign — prefer the row
    /// whose underlying record is Completed. Among Completed rows, prefer the latest
    /// (highest Id). This ensures a successful re-sign always surfaces over a prior failure.
    /// </summary>
    public async Task<SignedStepResolution> ResolveSignedStepForFileAsync(
        Guid tenantId, Guid sourceFileId, int sequenceNumber, CancellationToken ct)
    {
        // Fetch all candidates ordered by Id descending so the latest attempt comes first.
        // Explicit TenantId scope (db_owner bypasses RLS).
        var candidates = await db.SigningRequests.AsNoTracking()
            .Where(r => r.TenantId == tenantId
                     && r.SourceFileId == sourceFileId
                     && r.SignerSequenceNumber == sequenceNumber
                     && !r.IsDeleted)
            .OrderByDescending(r => r.Id)
            .ToListAsync(ct);

        if (candidates.Count == 0)
            return new SignedStepResolution(false, null, null, null, "NOT_FOUND");

        // Prefer Completed rows: try each in latest-first order; return on first Completed hit.
        foreach (var candidate in candidates)
        {
            var resolution = await ResolveSignedKeyAsync(candidate, tenantId, ct);
            if (resolution.Found)
                return resolution;
        }

        // No Completed row found — return the NOT_COMPLETED result from the latest attempt.
        return await ResolveSignedKeyAsync(candidates[0], tenantId, ct);
    }

    /// <summary>
    /// Resolves the signed storage key for one specific signing attempt of an uploaded file, addressed by
    /// the SigningRequest PublicId. Unlike <see cref="ResolveSignedStepForFileAsync"/> (which resolves the
    /// latest Completed at a sequence), this returns the blob of the exact request the caller named — the
    /// addressing the Files download surface uses so each independent sign of the same file is retrievable.
    /// The request must belong to both <paramref name="tenantId"/> and <paramref name="sourceFileId"/>
    /// (cross-tenant / cross-file → NOT_FOUND); only Completed attempts resolve to a key.
    /// </summary>
    public async Task<SignedStepResolution> ResolveSignedStepForFileByRequestAsync(
        Guid tenantId, Guid sourceFileId, Guid requestPublicId, CancellationToken ct)
    {
        // Explicit TenantId + SourceFileId scope: a requestId from another tenant or another file must not
        // resolve (db_owner bypasses RLS). Matching both pins the attempt to the file the caller guarded.
        var request = await db.SigningRequests.AsNoTracking()
            .FirstOrDefaultAsync(
                r => r.TenantId == tenantId
                     && r.SourceFileId == sourceFileId
                     && r.PublicId == requestPublicId
                     && !r.IsDeleted,
                ct);

        if (request is null)
            return new SignedStepResolution(false, null, null, null, "NOT_FOUND");

        return await ResolveSignedKeyAsync(request, tenantId, ct);
    }

    /// <summary>
    /// Resolves the signed storage key for a specific step of a signing request (by public id).
    /// Used by the Forms module.
    /// </summary>
    public async Task<SignedStepResolution> ResolveSignedStepForRequestAsync(
        Guid tenantId, Guid requestPublicId, int sequenceNumber, CancellationToken ct)
    {
        var request = await db.SigningRequests.AsNoTracking()
            .FirstOrDefaultAsync(
                r => r.TenantId == tenantId
                     && r.PublicId == requestPublicId
                     && r.SignerSequenceNumber == sequenceNumber
                     && !r.IsDeleted,
                ct);

        if (request is null)
            return new SignedStepResolution(false, null, null, null, "NOT_FOUND");

        return await ResolveSignedKeyAsync(request, tenantId, ct);
    }

    /// <summary>
    /// Batch: returns the subset of file ids that have ≥1 Completed signed step.
    /// Used by ListFiles to populate the isDigitallySigned badge without N+1 queries.
    /// Recognises completion across both underlying kinds (DocFlow + remote PAdES).
    /// </summary>
    public async Task<IReadOnlyCollection<Guid>> GetSignedFileIdsAsync(
        Guid tenantId, IReadOnlyCollection<Guid> sourceFileIds, CancellationToken ct)
    {
        if (sourceFileIds.Count == 0)
            return Array.Empty<Guid>();

        // Load all completion-bearing requests for the file batch (both kinds).
        // Explicit TenantId scope (db_owner bypasses RLS).
        var requests = await db.SigningRequests.AsNoTracking()
            .Where(r => r.TenantId == tenantId
                     && r.SourceFileId != null
                     && sourceFileIds.Contains(r.SourceFileId!.Value)
                     && !r.IsDeleted
                     && (r.UnderlyingKind == SigningUnderlyingKind.DocFlow
                         || r.UnderlyingKind == SigningUnderlyingKind.PendingTransaction))
            .ToListAsync(ct);

        if (requests.Count == 0)
            return Array.Empty<Guid>();

        var states = await BuildUnderlyingStatesAsync(requests, tenantId, ct);

        var result = new HashSet<Guid>();
        foreach (var req in requests)
        {
            if (req.SourceFileId.HasValue && states[req.PublicId].IsCompleted)
                result.Add(req.SourceFileId.Value);
        }

        return result;
    }

    // ─── Private helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Batch-loads the underlying records for a set of requests (both DocFlow records and PendingTransactions in
    /// one query each, no N+1) and projects each request to a uniform <see cref="UnderlyingState"/> keyed by the
    /// request PublicId. Resolving both kinds here is what lets every downstream helper stay kind-agnostic.
    /// </summary>
    private async Task<Dictionary<Guid, UnderlyingState>> BuildUnderlyingStatesAsync(
        IReadOnlyList<SigningRequest> requests, Guid tenantId, CancellationToken ct)
    {
        var states = new Dictionary<Guid, UnderlyingState>(requests.Count);
        if (requests.Count == 0)
            return states;

        // DocFlow records are addressed by a long id parsed from UnderlyingRecordId.
        var docFlowIds = requests
            .Where(r => r.UnderlyingKind == SigningUnderlyingKind.DocFlow)
            .Select(r => long.TryParse(r.UnderlyingRecordId, out var id) ? id : (long?)null)
            .OfType<long>()
            .Distinct()
            .ToList();

        Dictionary<long, SigningDocFlowRecord> docFlowMap = [];
        if (docFlowIds.Count > 0)
        {
            docFlowMap = await db.SigningDocFlowRecords.AsNoTracking()
                .Where(d => d.TenantId == tenantId && docFlowIds.Contains(d.Id))
                .ToDictionaryAsync(d => d.Id, ct);
        }

        // PendingTransactions are addressed by the provider's string TransactionId (a Viettel UUID, NOT a long) —
        // a long.TryParse over it would silently drop every remote attempt, which is exactly the bug this fixes.
        var txnIds = requests
            .Where(r => r.UnderlyingKind == SigningUnderlyingKind.PendingTransaction
                     && !string.IsNullOrEmpty(r.UnderlyingRecordId))
            .Select(r => r.UnderlyingRecordId)
            .Distinct()
            .ToList();

        Dictionary<string, SigningPendingTransaction> txnMap = [];
        if (txnIds.Count > 0)
        {
            txnMap = await db.SigningPendingTransactions.AsNoTracking()
                .Where(t => t.TenantId == tenantId && txnIds.Contains(t.TransactionId))
                .ToDictionaryAsync(t => t.TransactionId, ct);
        }

        foreach (var req in requests)
            states[req.PublicId] = ResolveUnderlyingState(req, docFlowMap, txnMap);

        return states;
    }

    /// <summary>
    /// Projects one request to its uniform state by branching on <see cref="SigningRequest.UnderlyingKind"/>.
    /// An unresolved/missing record is treated as a failed display state (never as Completed).
    /// </summary>
    private static UnderlyingState ResolveUnderlyingState(
        SigningRequest request,
        Dictionary<long, SigningDocFlowRecord> docFlowMap,
        Dictionary<string, SigningPendingTransaction> txnMap)
    {
        switch (request.UnderlyingKind)
        {
            case SigningUnderlyingKind.DocFlow:
                if (long.TryParse(request.UnderlyingRecordId, out var docFlowId)
                    && docFlowMap.TryGetValue(docFlowId, out var record))
                {
                    var status = record.Status switch
                    {
                        DocFlowStatus.Initiated  => "Pending",
                        DocFlowStatus.Downloaded => "InProgress",
                        DocFlowStatus.Completed  => "Completed",
                        DocFlowStatus.Failed     => "Failed",
                        DocFlowStatus.Expired    => "Expired",
                        _                        => "Failed",
                    };
                    bool completed = record.Status == DocFlowStatus.Completed && record.SignedStorageKey is not null;
                    return new UnderlyingState(
                        IsCompleted:  completed,
                        Status:       status,
                        CompletedAt:  record.CompletedAt,
                        HasSignedPdf: record.SignedStorageKey is not null,
                        SignedBucket: completed ? SigningDocFlowBucket : null,
                        SignedKey:    completed ? record.SignedStorageKey : null);
                }
                // DocFlow record not found or unresolvable — treat as failed state for display.
                return new UnderlyingState(false, "Failed", null, false, null, null);

            case SigningUnderlyingKind.PendingTransaction:
                if (request.UnderlyingRecordId is not null
                    && txnMap.TryGetValue(request.UnderlyingRecordId, out var txn))
                {
                    var status = txn.Status switch
                    {
                        PendingTransactionStatus.Initiated => "Pending",
                        PendingTransactionStatus.Completed => "Completed",
                        PendingTransactionStatus.Failed    => "Failed",
                        PendingTransactionStatus.Expired   => "Expired",
                        _                                  => "Failed",
                    };
                    bool completed = txn.Status == PendingTransactionStatus.Completed
                                  && txn.PadesSignedStorageKey is not null;
                    return new UnderlyingState(
                        IsCompleted:  completed,
                        Status:       status,
                        CompletedAt:  txn.CompletedAt,
                        HasSignedPdf: txn.PadesSignedStorageKey is not null,
                        SignedBucket: completed ? SigningPadesBucket : null,
                        SignedKey:    completed ? txn.PadesSignedStorageKey : null);
                }
                // Pending transaction not found or unresolvable — treat as failed state for display.
                return new UnderlyingState(false, "Failed", null, false, null, null);

            default: // SigningUnderlyingKind.None — not yet linked to any execution surface.
                var noneStatus = request.FailureReason is not null ? "Failed" : "Pending";
                return new UnderlyingState(false, noneStatus, null, false, null, null);
        }
    }

    /// <summary>
    /// Selects which SigningRequest rows are surfaced as steps. Input must already be ordered by
    /// (SequenceNumber ASC, Id ASC) so the latest attempt within a sequence is encountered last.
    ///
    /// Rule:
    ///   1. Every Completed attempt (underlying record Completed with a signed key) is kept — each is
    ///      a distinct signed artifact, so two successful signs at the same sequence yield two rows.
    ///   2. For a sequence that has NO Completed attempt, the latest non-Completed attempt is kept so
    ///      the FE can show its in-progress / failed state.
    ///
    /// This shows every genuine sign while still hiding a failed attempt that was later re-signed to
    /// success at the same sequence (the failed row is dropped in favour of its Completed sibling).
    /// Completion is resolved uniformly across DocFlow and remote PAdES (see <see cref="BuildUnderlyingStatesAsync"/>),
    /// so a remote attempt no longer collapses to latest-per-sequence the way a DocFlow-only check would force.
    /// Output is ordered by (SequenceNumber ASC, Id ASC) — chronological, stable.
    /// </summary>
    private static IReadOnlyList<SigningRequest> SelectVisibleAttempts(
        IReadOnlyList<SigningRequest> requests, Dictionary<Guid, UnderlyingState> states)
    {
        if (requests.Count == 0)
            return requests;

        bool IsCompleted(SigningRequest r) => states[r.PublicId].IsCompleted;

        // Sequences that have at least one Completed attempt — non-Completed siblings are hidden there.
        var sequencesWithCompleted = requests
            .Where(IsCompleted)
            .Select(r => r.SignerSequenceNumber)
            .ToHashSet();

        var visible = new List<SigningRequest>(requests.Count);
        var latestNonCompletedPerSeq = new Dictionary<int, SigningRequest>();

        foreach (var req in requests)
        {
            if (IsCompleted(req))
            {
                visible.Add(req); // keep every successful sign
            }
            else if (!sequencesWithCompleted.Contains(req.SignerSequenceNumber))
            {
                // No Completed sibling: track the latest non-Completed (input ordered Id ASC).
                latestNonCompletedPerSeq[req.SignerSequenceNumber] = req;
            }
        }

        visible.AddRange(latestNonCompletedPerSeq.Values);

        return [.. visible.OrderBy(r => r.SignerSequenceNumber).ThenBy(r => r.Id)];
    }

    /// <summary>
    /// Projects a list of signing requests into step DTOs using their pre-resolved underlying states.
    /// SignerName is left null — the FE panel resolves display names from SignerUserId via a
    /// separate user lookup if needed (avoids a cross-collection join on the read path here).
    /// </summary>
    private static IReadOnlyList<SigningStepDto> ProjectSteps(
        IReadOnlyList<SigningRequest> requests, Dictionary<Guid, UnderlyingState> states)
    {
        if (requests.Count == 0)
            return Array.Empty<SigningStepDto>();

        var steps = new List<SigningStepDto>(requests.Count);
        foreach (var req in requests)
        {
            var state = states[req.PublicId];
            steps.Add(new SigningStepDto(
                SigningRequestId: req.PublicId,
                SequenceNumber:   req.SignerSequenceNumber,
                Status:           state.Status,
                SignerUserId:     req.AssignedSignerId,
                SignerName:       null,  // resolved client-side from SignerUserId if needed
                CompletedAt:      state.CompletedAt,
                HasSignedPdf:     state.HasSignedPdf));
        }

        return steps;
    }

    /// <summary>
    /// Resolves the signed storage key from a SigningRequest's underlying record, across both kinds.
    /// Only returns the key when the underlying record is Completed and has a non-null signed key.
    /// </summary>
    private async Task<SignedStepResolution> ResolveSignedKeyAsync(
        SigningRequest request, Guid tenantId, CancellationToken ct)
    {
        switch (request.UnderlyingKind)
        {
            case SigningUnderlyingKind.DocFlow:
            {
                if (!long.TryParse(request.UnderlyingRecordId, out var docFlowId))
                    return new SignedStepResolution(false, null, null, null, "NOT_COMPLETED");

                // Explicit TenantId scope (db_owner bypasses RLS).
                var record = await db.SigningDocFlowRecords.AsNoTracking()
                    .FirstOrDefaultAsync(d => d.Id == docFlowId && d.TenantId == tenantId, ct);

                if (record is null)
                    return new SignedStepResolution(false, null, null, null, "NOT_FOUND");

                if (record.Status != DocFlowStatus.Completed || string.IsNullOrEmpty(record.SignedStorageKey))
                    return new SignedStepResolution(false, null, null, null, "NOT_COMPLETED");

                // Derive a download filename from the doc id and sequence for Content-Disposition.
                var fileName = $"signed-{record.DocId:N}-seq{record.SignerSequenceNumber}.pdf";

                return new SignedStepResolution(
                    Found:      true,
                    Bucket:     SigningDocFlowBucket,
                    StorageKey: record.SignedStorageKey,
                    FileName:   fileName,
                    ErrorCode:  null);
            }

            case SigningUnderlyingKind.PendingTransaction:
            {
                // Explicit TenantId scope (db_owner bypasses RLS); TransactionId is the string addressing key.
                var txn = await db.SigningPendingTransactions.AsNoTracking()
                    .FirstOrDefaultAsync(
                        t => t.TransactionId == request.UnderlyingRecordId && t.TenantId == tenantId, ct);

                if (txn is null)
                    return new SignedStepResolution(false, null, null, null, "NOT_FOUND");

                if (txn.Status != PendingTransactionStatus.Completed
                    || string.IsNullOrEmpty(txn.PadesSignedStorageKey))
                    return new SignedStepResolution(false, null, null, null, "NOT_COMPLETED");

                // No DocId on a remote transaction — address the download by the request PublicId + sequence.
                var fileName = $"signed-{request.PublicId:N}-seq{request.SignerSequenceNumber}.pdf";

                return new SignedStepResolution(
                    Found:      true,
                    Bucket:     SigningPadesBucket,
                    StorageKey: txn.PadesSignedStorageKey,
                    FileName:   fileName,
                    ErrorCode:  null);
            }

            default: // None — not linked to any execution surface, nothing to download.
                return new SignedStepResolution(false, null, null, null, "NOT_COMPLETED");
        }
    }
}
