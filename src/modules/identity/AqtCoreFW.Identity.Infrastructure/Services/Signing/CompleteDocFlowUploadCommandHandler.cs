using System.Text.Json;
using AqtCoreFW.Identity.Application.Signing;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Application;
using FluentResults;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Identity.Infrastructure.Services.Signing;

/// <summary>
/// Handles <see cref="CompleteDocFlowUploadCommand"/> — the doc-flow signing commit. Mirrors the
/// download handler's pin → nonce → record → state ordering, stores the uploaded bytes via the
/// <see cref="ISigningDocFlowBlobStore"/> port (no Identity→Files dependency), then completes the
/// record and audits the commit exactly once. The PDF format checks already ran in the controller.
/// </summary>
public sealed class CompleteDocFlowUploadCommandHandler(
    IdentityDbContext db,
    ISigningDocFlowBlobStore blobStore,
    IAuditLogger auditLogger,
    ILogger<CompleteDocFlowUploadCommandHandler> logger,
    IGuidGenerator guidGen)
    : IRequestHandler<CompleteDocFlowUploadCommand, Result<DocFlowUploadResult>>
{
    public async Task<Result<DocFlowUploadResult>> Handle(
        CompleteDocFlowUploadCommand cmd, CancellationToken ct)
    {
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
        if (record.Status != DocFlowStatus.Downloaded)
            return Fail(400, $"Record is in state '{record.Status}'; expected Downloaded before upload.");

        // Random-GUID key — on a late concurrency conflict below this blob is orphaned (record never
        // reaches Completed, so SignedStorageKey is never persisted); a sweep reclaims it.
        var storageKey = $"{record.TenantId}/{cmd.DocId}/{guidGen.NewIdRandom()}.signed.pdf";
        try
        {
            await blobStore.UploadSignedAsync(cmd.Content, storageKey, "application/pdf", ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "DocFlow upload: storage upload failed for key {Key}", storageKey);
            record.Fail("Storage upload failed: " + ex.Message, cmd.UserId);
            try
            {
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Fail(401, "Nonce already consumed by a concurrent request.");
            }
            return Fail(500, "Failed to store signed PDF.");
        }

        record.Complete(storageKey, cmd.UserId);
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            logger.LogWarning("DocFlow upload: concurrent nonce consume for {Nonce} — rejecting replay", cmd.Nonce);
            return Fail(401, "Nonce already consumed by a concurrent request.");
        }

        // Audit the signing commit exactly once, attributed to the signing tenant (anonymous call).
        var snapshot = JsonSerializer.Serialize(new
        {
            recordId   = record.Id,
            docId      = cmd.DocId,
            tenantId   = record.TenantId,
            userId     = cmd.UserId,
            storageKey,
        });
        await auditLogger.LogCommandAsync(
            action:       "DOCFLOW_UPLOAD_COMPLETED",
            moduleName:   "Identity",
            resourceType: "SigningDocFlowRecord",
            resourceId:   record.Id.ToString(),
            commandType:  typeof(CompleteDocFlowUploadCommand),
            dataSnapshot: snapshot,
            tenantIdOverride: record.TenantId,
            cancellationToken: ct).ConfigureAwait(false);

        var fileServer = $"{cmd.BaseUrl}/api/signing/docflow/download/{record.TenantId}/{storageKey}";
        return Result.Ok(new DocFlowUploadResult(cmd.FileName, fileServer));
    }

    private static Result<DocFlowUploadResult> Fail(int status, string message) =>
        Result.Fail(new DocFlowError(status, message));
}
