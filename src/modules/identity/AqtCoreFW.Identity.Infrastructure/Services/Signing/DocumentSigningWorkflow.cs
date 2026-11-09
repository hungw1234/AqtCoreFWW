using AqtCoreFW.Identity.Application.Signing;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Services;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Identity.Infrastructure.Services.Signing;

/// <summary>
/// Implements <see cref="IDocumentSigningWorkflow"/> — Tier-3 VGCA doc-flow PDF signing (W3, QĐ 742 mục 5.1).
///
/// <para>All 7 methods delegate to <see cref="InitiateSignAsync"/> which:
/// <list type="number">
///   <item>Creates a <see cref="SigningDocFlowRecord"/> (Status=Initiated) in the identity DB.</item>
///   <item>Creates a <see cref="SigningDocFlowNonce"/> (UseCount=0, Expires=now+TTL) linked to the record.</item>
///   <item>Issues a short-lived HS256 JWT via <see cref="IDocFlowJwtService"/>.</item>
///   <item>Returns <see cref="DocFlowResult"/> with <c>IsSuccess=true</c> and the JWT in
///     <c>DocumentNumber</c> (repurposed as JWT carrier — callers extract via
///     <see cref="DocFlowResult.DocumentNumber"/>; <c>StorageKey</c> is null until upload completes).</item>
/// </list>
/// Actual PDF download and upload are handled by <c>FileUploadHandlerController</c> — the controller
/// updates the record via <see cref="IdentityDbContext"/> after each VGCA round-trip.
/// </para>
///
/// <para>Note on <see cref="DocFlowResult"/> field repurposing for initiation response:
/// <c>DocumentNumber</c> carries the issued JWT; <c>DocumentDate</c> carries the handlerUrl base
/// (relative path — controller appends actual base URL). Callers must be aware of this convention.
/// The <see cref="InitiateDocFlowCommand"/> handler wraps this into a proper DTO before returning to FE.</para>
/// </summary>
public sealed class DocumentSigningWorkflow(
    IdentityDbContext db,
    IDocFlowJwtService jwtService,
    ILogger<DocumentSigningWorkflow> logger) : IDocumentSigningWorkflow
{
    // OriginalBucket used by FileUploadHandlerController.Download — not by this workflow.
    // The workflow does NOT pre-verify the PDF exists (avoids MinIO dependency at initiation time;
    // FileUploadHandlerController.Download returns 500 with a clear error if the key is invalid).

    /// <inheritdoc/>
    public Task<DocFlowResult> SignIssuedAsync(Guid docId, DocFlowMetadata metadata, CancellationToken ct = default)
        => InitiateSignAsync(DocFlowSignType.Issued, docId, metadata, ct);

    /// <inheritdoc/>
    public Task<DocFlowResult> SignApprovedAsync(Guid docId, DocFlowMetadata metadata, CancellationToken ct = default)
        => InitiateSignAsync(DocFlowSignType.Approved, docId, metadata, ct);

    /// <inheritdoc/>
    public Task<DocFlowResult> SignIncomeAsync(Guid docId, DocFlowMetadata metadata, CancellationToken ct = default)
        => InitiateSignAsync(DocFlowSignType.Income, docId, metadata, ct);

    /// <inheritdoc/>
    public Task<DocFlowResult> CommentAsync(Guid docId, DocFlowMetadata metadata, CancellationToken ct = default)
        => InitiateSignAsync(DocFlowSignType.Comment, docId, metadata, ct);

    /// <inheritdoc/>
    public Task<DocFlowResult> SignAppendixAsync(Guid docId, DocFlowMetadata metadata, CancellationToken ct = default)
        => InitiateSignAsync(DocFlowSignType.Appendix, docId, metadata, ct);

    /// <inheritdoc/>
    public Task<DocFlowResult> SignCopyAsync(Guid docId, DocFlowMetadata metadata, CancellationToken ct = default)
        => InitiateSignAsync(DocFlowSignType.Copy, docId, metadata, ct);

    /// <inheritdoc/>
    public async Task<DocFlowResult> SignFilesAsync(
        IReadOnlyList<Guid> docIds,
        DocFlowMetadata commonMetadata,
        CancellationToken ct = default)
    {
        if (docIds is null || docIds.Count == 0)
            return new DocFlowResult(false, null, null, null, "docIds must not be empty.");

        // Batch: initiate the first document; FE batches remaining via vgca_sign_files params
        return await InitiateSignAsync(DocFlowSignType.Files, docIds[0], commonMetadata, ct);
    }

    // ─── Core initiation logic ────────────────────────────────────────────────

    /// <summary>
    /// Shared initiation path for all 7 sign-type methods.
    ///
    /// <para>The <paramref name="metadata"/> must carry <c>OriginalStorageKey</c> via
    /// <see cref="DocFlowMetadata.Extra"/> key <c>"originalStorageKey"</c> and
    /// <c>"initiatedBy"</c> (Guid userId string), both injected by
    /// <see cref="InitiateDocFlowCommand"/> before delegating here.</para>
    /// </summary>
    private async Task<DocFlowResult> InitiateSignAsync(
        DocFlowSignType signType,
        Guid docId,
        DocFlowMetadata metadata,
        CancellationToken ct)
    {
        // Caller (InitiateDocFlowCommand) must inject these via metadata.Extra
        if (metadata.Extra is null
            || !metadata.Extra.TryGetValue("originalStorageKey", out var originalKey)
            || string.IsNullOrWhiteSpace(originalKey))
        {
            return new DocFlowResult(false, null, null, null,
                "originalStorageKey must be provided in metadata.Extra.");
        }

        if (!metadata.Extra.TryGetValue("initiatedBy", out var initiatedByStr)
            || !Guid.TryParse(initiatedByStr, out var initiatedBy))
        {
            return new DocFlowResult(false, null, null, null,
                "initiatedBy (Guid) must be provided in metadata.Extra.");
        }

        if (!metadata.Extra.TryGetValue("tenantId", out var tenantIdStr)
            || !Guid.TryParse(tenantIdStr, out var tenantId))
        {
            return new DocFlowResult(false, null, null, null,
                "tenantId (Guid) must be provided in metadata.Extra.");
        }

        // Resolve incremental chain parameters injected by InitiateDocFlowCommand.
        var signerSequenceNumber = 1;
        if (metadata.Extra.TryGetValue("signerSequenceNumber", out var seqStr)
            && int.TryParse(seqStr, out var parsedSeq)
            && parsedSeq >= 1)
        {
            signerSequenceNumber = parsedSeq;
        }

        metadata.Extra.TryGetValue("previousSignedStorageKey", out var previousSignedKey);

        // Defense-in-depth: reject a previousSignedStorageKey outside the caller's tenant prefix even on
        // paths that bypass the command validator. Any caller that injects a foreign-tenant key via Extra
        // is caught here before the record is persisted, preventing cross-tenant storage-key reads.
        if (!string.IsNullOrEmpty(previousSignedKey)
            && !previousSignedKey.StartsWith(tenantId.ToString() + "/", StringComparison.OrdinalIgnoreCase))
        {
            return new DocFlowResult(false, null, null, null,
                "previousSignedStorageKey must reside within the caller's tenant prefix ('<tenantId>/...'). " +
                "Cross-tenant previous-signed key rejected.");
        }

        // 1. Generate nonce Guid first (needed for both record and JWT)
        var nonce = Guid.NewGuid();

        // 2. Supersede any prior non-terminal record at the same (TenantId, DocId, SignerSequenceNumber)
        //    before inserting the new one. This frees the filtered-active unique slot so a retry-after-JWT-
        //    expiry does not collide. Scoped by TenantId to prevent cross-tenant reads (db_owner bypasses RLS).
        //    The supersede and the new insert must land in the same SaveChangesAsync so that if a concurrent
        //    insert wins the unique race, the whole unit of work rolls back — leaving no dangling supersede.
        // Filter must use EF-translatable predicates (no computed CLR properties).
        // Non-terminal = Status is Initiated(0) or Downloaded(1); terminal statuses are Completed(2), Failed(3), Expired(4).
        // IsDeleted is handled by the global query filter on SigningDocFlowRecords, but we also need to read
        // superseded (soft-deleted) rows to avoid double-supersede on the same record, so we bypass the filter
        // with IgnoreQueryFilters and add the IsDeleted predicate explicitly.
        var priorActiveRecord = await db.SigningDocFlowRecords
            .IgnoreQueryFilters()
            .Where(r => r.TenantId == tenantId
                     && r.DocId == docId
                     && r.SignerSequenceNumber == signerSequenceNumber
                     && !r.IsDeleted
                     && r.Status != DocFlowStatus.Completed
                     && r.Status != DocFlowStatus.Failed
                     && r.Status != DocFlowStatus.Expired)
            .FirstOrDefaultAsync(ct);

        if (priorActiveRecord is not null)
        {
            logger.LogInformation(
                "Superseding prior active DocFlow record Id={PriorRecordId} for docId={DocId} seq={Seq} tenantId={TenantId} (retry-after-expiry or abandoned attempt).",
                priorActiveRecord.Id, docId, signerSequenceNumber, tenantId);
            priorActiveRecord.Supersede(initiatedBy);
        }

        // 3. Create record (Status=Initiated, nonce FK set after SaveChanges gives us the IDENTITY Id).
        //    Two-step save: first persist the record to get Id, then create the nonce row with the FK.
        var record = SigningDocFlowRecord.Initiate(
            tenantId:                tenantId,
            docId:                   docId,
            signType:                signType,
            originalStorageKey:      originalKey,
            jwtNonce:                nonce,
            initiatedBy:             initiatedBy,
            signerSequenceNumber:    signerSequenceNumber,
            previousSignedStorageKey: string.IsNullOrEmpty(previousSignedKey) ? null : previousSignedKey);

        db.SigningDocFlowRecords.Add(record);
        try
        {
            await db.SaveChangesAsync(ct); // record.Id is now populated (IDENTITY)
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            // A concurrent caller won the active-slot race for this (TenantId, DocId, sequence).
            // Return a typed conflict result so the caller maps it to a 409-class response, never 500.
            logger.LogWarning(
                "Concurrent DocFlow initiation conflict: docId={DocId} seq={Seq} tenantId={TenantId}. " +
                "Another request won the unique-slot race; returning typed conflict.",
                docId, signerSequenceNumber, tenantId);

            return new DocFlowResult(false, null, null, null,
                "SIGNING_SEQUENCE_CONFLICT: A concurrent request is already initiating the same signing sequence. " +
                "Retry after the active session completes or expires.");
        }

        // 4. Create nonce row (FK = record.Id, now valid)
        var ttlSeconds = 300; // default; caller can override via metadata.Extra["ttlSeconds"]
        if (metadata.Extra.TryGetValue("ttlSeconds", out var ttlStr)
            && int.TryParse(ttlStr, out var parsedTtl)
            && parsedTtl > 0)
        {
            ttlSeconds = parsedTtl;
        }

        var nonceRow = SigningDocFlowNonce.Create(nonce, tenantId, record.Id, ttlSeconds);
        db.SigningDocFlowNonces.Add(nonceRow);
        await db.SaveChangesAsync(ct);

        // 5. Issue JWT — carries tenant_id so the [AllowAnonymous] handler can assert tenant ownership.
        var jwt = jwtService.IssueJwt(initiatedBy, docId, nonce, tenantId);

        logger.LogInformation(
            "DocFlow initiated: recordId={RecordId} docId={DocId} signType={SignType} tenantId={TenantId}",
            record.Id, docId, signType, tenantId);

        // Return: DocumentNumber carries the JWT; DocumentDate carries the relative handler path.
        // InitiateDocFlowCommand wraps these into a properly typed DTO before returning to FE.
        return new DocFlowResult(
            IsSuccess:      true,
            StorageKey:     null, // populated only on Completed (after upload)
            DocumentNumber: jwt,
            DocumentDate:   $"/api/signing/docflow/file",
            FailureReason:  null,
            RecordId:       record.Id); // lets the unified orchestrator link the SigningRequest to this record
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// SQL Server unique constraint violation: error number 2601 (duplicate key in unique index)
    /// or 2627 (unique constraint). Used to detect races on the filtered-active unique index so
    /// concurrent duplicate inits return a typed conflict result instead of propagating a 500.
    /// Mirrors the same detection used in <see cref="InProcessDocumentSignatureSessionClient"/>.
    /// </summary>
    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        var inner = ex.InnerException;
        while (inner is not null)
        {
            if (inner is Microsoft.Data.SqlClient.SqlException sql
                && (sql.Number == 2601 || sql.Number == 2627))
                return true;
            inner = inner.InnerException;
        }
        return false;
    }
}
