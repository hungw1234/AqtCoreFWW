using AqtCoreFW.Identity.Application.Signing;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using FluentResults;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Identity.Infrastructure.Services.Signing;

/// <summary>
/// Handles <see cref="ProcessDocFlowDownloadCommand"/>. In Infrastructure (like
/// <c>CompleteMysignCallbackCommandHandler</c>) so it can use <see cref="IdentityDbContext"/> directly;
/// blob bytes go through the <see cref="ISigningDocFlowBlobStore"/> port (host-bound to Files) so there
/// is no Identity→Files compile dependency. Preserves the fetch-before-commit invariant: the nonce
/// consumption + Downloaded transition are persisted ONLY after the bytes are in hand, so a missing
/// object leaves the nonce slot + record untouched for retry.
/// </summary>
public sealed class ProcessDocFlowDownloadCommandHandler(
    IdentityDbContext db,
    ISigningDocFlowBlobStore blobStore,
    ILogger<ProcessDocFlowDownloadCommandHandler> logger)
    : IRequestHandler<ProcessDocFlowDownloadCommand, Result<DocFlowDownloadResult>>
{
    public async Task<Result<DocFlowDownloadResult>> Handle(
        ProcessDocFlowDownloadCommand cmd, CancellationToken ct)
    {
        // Pin RLS to the JWT tenant before any read of the signing tables.
        await DocFlowTenantSession.PinAsync(db, cmd.JwtTenant, ct).ConfigureAwait(false);

        var nonceRow = await db.SigningDocFlowNonces
            .FirstOrDefaultAsync(n => n.Nonce == cmd.Nonce, ct).ConfigureAwait(false);
        if (nonceRow is null)
            return Fail(401, "Nonce not found.");
        if (nonceRow.TenantId != cmd.JwtTenant)
            return Fail(401, "Nonce does not belong to the token tenant.");
        if (!nonceRow.TryUse())
            return Fail(401, "Nonce exhausted or expired.");

        var record = await db.SigningDocFlowRecords
            .FirstOrDefaultAsync(r => r.DocId == cmd.DocId && r.JwtNonce == cmd.Nonce && !r.IsDeleted, ct)
            .ConfigureAwait(false);
        if (record is null)
            return Fail(404, "Signing record not found.");
        if (record.TenantId != cmd.JwtTenant)
            return Fail(401, "Record does not belong to the token tenant.");

        // First signer downloads the original; a subsequent signer downloads signer N-1's signed PDF so
        // the desktop tool appends an incremental PAdES signature. isFirst is server-derived from the
        // sequence number, never caller-trusted.
        var isFirst = record.SignerSequenceNumber <= 1;
        if (!isFirst && string.IsNullOrEmpty(record.PreviousSignedStorageKey))
            return Fail(500, "Previous signed PDF key is not set for this signing round.");

        // Fetch BEFORE committing the nonce consumption: a missing/unreachable object returns an error
        // with the nonce slot + record untouched, so the desktop service can retry.
        Stream pdfStream;
        try
        {
            pdfStream = isFirst
                ? await blobStore.DownloadOriginalAsync(record.OriginalStorageKey, ct).ConfigureAwait(false)
                : await blobStore.DownloadPreviousSignedAsync(record.PreviousSignedStorageKey!, ct).ConfigureAwait(false);
        }
        catch (FileNotFoundException)
        {
            logger.LogWarning("DocFlow download: PDF object missing for record {RecordId}", record.Id);
            return Fail(404, "PDF to be signed is no longer available.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "DocFlow download: storage error for record {RecordId}", record.Id);
            return Fail(500, "Failed to retrieve PDF for signing.");
        }

        // Bytes in hand — commit the nonce consumption + Downloaded transition. The rowversion token
        // rejects a concurrent replay that consumed the same nonce first.
        record.MarkDownloaded();
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            pdfStream.Dispose();
            logger.LogWarning("DocFlow download: concurrent nonce consume for {Nonce} — rejecting replay", cmd.Nonce);
            return Fail(401, "Nonce already consumed by a concurrent request.");
        }

        return Result.Ok(new DocFlowDownloadResult(pdfStream, $"{cmd.DocId}.pdf"));
    }

    private static Result<DocFlowDownloadResult> Fail(int status, string message) =>
        Result.Fail(new DocFlowError(status, message));
}
