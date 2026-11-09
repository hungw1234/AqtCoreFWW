using AqtCoreFW.Identity.Application.Signing;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Contracts;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Identity.Infrastructure.Services.Signing;

/// <summary>
/// Default <see cref="ISignatureOrchestrator"/>. Routes a provider-agnostic request to a strategy by
/// <c>(artifact kind × capabilities)</c>, persists a durable <c>SigningRequest</c>, and projects status at read
/// time from the underlying provider record. Direct <see cref="IdentityDbContext"/> access mirrors the existing
/// signing handlers (no bespoke repository).
/// </summary>
public sealed class SignatureOrchestrator(
    IdentityDbContext db,
    SigningStrategyRouter router,
    ISigningProviderAllowlistGuard allowlistGuard,
    IFeatureFlagService featureFlags,
    IGuidGenerator guidGenerator,
    ISigningPlaceholderPdfReader signedPdfReader) : ISignatureOrchestrator
{
    /// <summary>
    /// Gates the untrusted-PDF upload-sign path. Signing a user-uploaded PDF safely requires cryptographic
    /// verification of the returned artifact, which requires parsing an untrusted PDF — permitted only inside
    /// the not-yet-built parse sandbox (that whole chain is deferred to a later release). Until it ships, this
    /// deployment does not accept the upload-arbitrary-PDF-then-sign path; the flag defaults off. Read GLOBAL
    /// (no tenant): deployment-wide posture, mirroring the virus-scan flag's global-only semantics.
    /// </summary>
    private const string UploadSignFlag = "feature:signing.upload-arbitrary-pdf";

    public async Task<Result<RequestSignatureResult>> RequestSignatureAsync(
        RequestSignatureInput input, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        // Untrusted-PDF upload-sign gate at the single orchestrator chokepoint, so BOTH the MediatR command
        // AND the cross-module InProcessSigningRequestClient path are covered (form-generated PDFs and
        // detached-data signatures are unaffected). Runs before any side effect. See backlog
        // SIGN-FULL-PDF-CRYPTO-VERIFY: the untrusted-PDF crypto-verify chain is deferred to a later release.
        if (input.ArtifactKind == SigningArtifactKind.UploadedPdf
            && !featureFlags.IsEnabled(UploadSignFlag))
        {
            return Result.Fail(new ConflictError(
                "Signing an uploaded PDF is disabled in this deployment (deferred to a later release)."));
        }

        // Fail-closed provider allowlist (defense-in-depth) BEFORE routing or any side effect. This is the
        // single chokepoint for BOTH request callers — the self-sign HTTP handler AND the Forms
        // InProcessSigningRequestClient path — so an FE-supplied providerKey the assigned signer is not enrolled
        // for is denied (403) here, before a SigningRequest row exists and before the provider is invoked. An
        // unknown provider key is deferred to the route handling below (→ 404, unchanged); a 0-enrollment signer
        // is deferred to the late SignerBindingGuard (no legacy break). Keyed off AssignedSignerId, mirroring
        // that guard's subject.
        var allowlist = await allowlistGuard.EnsureAllowedAsync(
            input.ProviderKey, input.TenantId, input.AssignedSignerId, ct);
        if (allowlist.IsFailed)
            return Result.Fail(allowlist.Errors);

        // Resolve the routing cell before any side effect. Expected resolution failures become a typed Result
        // (→ 400/404), not a 500: an unsupported (artifact × capabilities) cell, or an unknown provider key. An
        // ambiguous registration is a server bug and is left to throw (→ 500), which is the correct signal.
        SigningRoute route;
        try
        {
            route = router.Route(input.ArtifactKind, input.ProviderKey);
        }
        catch (NotSupportedException ex)
        {
            return Result.Fail(new BadRequestError(ex.Message, "SIGNING_ROUTE_UNSUPPORTED"));
        }
        catch (KeyNotFoundException ex)
        {
            return Result.Fail(new NotFoundError(ex.Message));
        }

        if (route.Strategy is not ISigningExecutionStrategy executable)
            return Result.Fail(new BadRequestError(
                $"Signing strategy '{route.Strategy.Name}' has no execution surface yet (added in its owning phase).",
                "SIGNING_NO_EXECUTION_SURFACE"));

        // Sequence guard: reject initiating sequence N when the preceding signer (N-1) has not yet
        // reached a terminal-Completed state. This prevents signer N from downloading signer N-1's
        // artifact before it exists, which would produce a signature over an incomplete chain. Using
        // a typed Result failure (not throw) so the HTTP layer maps it to 4xx, never 500.
        if (input.SignerSequenceNumber > 1)
        {
            var sequenceGuardResult = await EnforceSequenceGuardAsync(
                input.TenantId, input.ArtifactRef, input.SignerSequenceNumber, ct);
            if (sequenceGuardResult.IsFailed)
                return Result.Fail(sequenceGuardResult.Errors);
        }

        // Build the request in memory first, then execute the strategy, then link + persist. The strategy creates
        // and commits the underlying provider record; we persist the SigningRequest already linked to it so the
        // request row is never left in an unlinked limbo. If the strategy fails or throws, no request row is
        // written (Add runs only after a successful execution); if the final save fails, the underlying record
        // stays independently valid (usable via its own state machine).
        var request = SigningRequest.Create(
            guidGenerator,
            input.TenantId,
            input.ArtifactKind,
            input.ArtifactRef,
            input.ProviderKey,
            input.AssignedSignerId,
            input.InitiatedBy,
            input.Placement,
            input.SignerSequenceNumber,
            input.SourceFileId);

        var execution = await executable.ExecuteAsync(new SigningExecutionContext(input, route.Provider), ct);
        if (execution.IsFailed)
            return Result.Fail(execution.Errors); // preserve the inner error type (validator → 422, etc.)

        request.LinkUnderlying(
            execution.Value.UnderlyingKind, execution.Value.UnderlyingRecordId, input.InitiatedBy);

        db.SigningRequests.Add(request);
        await db.SaveChangesAsync(ct);

        return Result.Ok(new RequestSignatureResult(request.PublicId, execution.Value.ClientHandoff));
    }

    public async Task<Result<SigningRequestStatusDto>> GetStatusAsync(
        Guid publicId, Guid tenantId, CancellationToken ct = default)
    {
        var request = await db.SigningRequests.AsNoTracking()
            .FirstOrDefaultAsync(r => r.PublicId == publicId && r.TenantId == tenantId, ct);
        if (request is null)
            return Result.Fail(new NotFoundError(
                $"Signing request {publicId} was not found in tenant {tenantId}."));

        var status = await ProjectStatusAsync(request, ct);
        return Result.Ok(new SigningRequestStatusDto(
            request.PublicId, status, request.ArtifactKind, request.ProviderKey, request.FailureReason));
    }

    public async Task<Result<SignedPdfDownload>> GetSignedPdfAsync(
        Guid publicId, Guid tenantId, CancellationToken ct = default)
    {
        var request = await db.SigningRequests.AsNoTracking()
            .FirstOrDefaultAsync(r => r.PublicId == publicId && r.TenantId == tenantId, ct);
        if (request is null)
            return Result.Fail(new NotFoundError(
                $"Signing request {publicId} was not found in tenant {tenantId}."));

        // Only the server-side PAdES path produces a downloadable, BE-assembled signed PDF. DocFlow signed PDFs
        // live under SigningDocFlowRecord (a separate download surface); a detached/unlinked request has no PDF.
        if (request.UnderlyingKind != SigningUnderlyingKind.PendingTransaction)
            return Result.Fail(new BadRequestError(
                "This signing request has no server-assembled signed PDF to download.",
                "SIGNING_PADES_NO_SIGNED_PDF"));

        // Match TenantId explicitly (not just the id): RLS is bypassed under db_owner (the integration context),
        // so an id-only read would be a latent cross-tenant read there.
        var txn = await db.SigningPendingTransactions.AsNoTracking()
            .FirstOrDefaultAsync(t => t.TransactionId == request.UnderlyingRecordId && t.TenantId == tenantId, ct);
        if (txn is null || txn.Status != PendingTransactionStatus.Completed || txn.PadesSignedStorageKey is null)
            return Result.Fail(new BadRequestError(
                "The signed PDF is not ready yet — the signature is still pending or the request did not complete.",
                "SIGNING_PADES_PDF_NOT_READY"));

        byte[] bytes = await signedPdfReader.ReadAsync(txn.PadesSignedStorageKey, ct);
        return Result.Ok(new SignedPdfDownload(bytes, $"signed-{publicId}.pdf"));
    }

    /// <summary>
    /// Sequence guard: verifies that the SigningRequest for sequence N-1 (the immediately preceding
    /// signer) on the same (TenantId, ArtifactRef) is terminal-Completed before allowing sequence N
    /// to begin. This prevents a later signer from downloading an artifact that does not yet carry
    /// the prior signer's incremental signature, which would break the PAdES chain.
    ///
    /// <para>The read is explicitly scoped to TenantId (not just ArtifactRef) so the check is
    /// db_owner-independent — RLS is bypassed under the db_owner login used in the integration suite,
    /// so an id-only or artifactRef-only query would be a latent cross-tenant read there.</para>
    /// </summary>
    private async Task<Result> EnforceSequenceGuardAsync(
        Guid tenantId, string artifactRef, int requestedSequence, CancellationToken ct)
    {
        var precedingSequence = requestedSequence - 1;

        // Locate the SigningRequest at sequence N-1 for this (tenant, artifact).
        var precedingRequest = await db.SigningRequests.AsNoTracking()
            .FirstOrDefaultAsync(
                r => r.TenantId == tenantId
                     && r.ArtifactRef == artifactRef
                     && r.SignerSequenceNumber == precedingSequence,
                ct);

        if (precedingRequest is null)
            return Result.Fail(new BadRequestError(
                $"No signing request found for signer at sequence {precedingSequence} on this artifact. " +
                "Sequence must be initiated in strict order.",
                "SIGNING_SEQUENCE_PREDECESSOR_NOT_FOUND"));

        // Project the preceding request's status to check for Completed.
        var precedingStatus = await ProjectStatusAsync(precedingRequest, ct);

        if (precedingStatus != SigningRequestStatus.Completed)
            return Result.Fail(new BadRequestError(
                $"Signer at sequence {precedingSequence} has not yet completed (current status: {precedingStatus}). " +
                "Each signer must complete before the next signer may begin.",
                "SIGNING_SEQUENCE_PREDECESSOR_NOT_COMPLETED"));

        return Result.Ok();
    }

    /// <summary>
    /// Read-time projection: maps the underlying record's state, or the unlinked/creation-failure state. A
    /// linked request whose underlying record cannot be found is treated as <see cref="SigningRequestStatus.Failed"/>
    /// (the record was expected to exist; its absence is a terminal anomaly, not a silent "pending").
    /// </summary>
    private async Task<SigningRequestStatus> ProjectStatusAsync(SigningRequest request, CancellationToken ct)
    {
        switch (request.UnderlyingKind)
        {
            case SigningUnderlyingKind.None:
                return SigningRequestStatusProjector.ForUnlinked(request.FailureReason is not null);

            case SigningUnderlyingKind.DocFlow:
            {
                if (!long.TryParse(request.UnderlyingRecordId, out var docFlowId))
                    return SigningRequestStatus.Failed;
                // Match TenantId explicitly (not just the id): RLS is bypassed under db_owner — the context the
                // integration suite runs in — so an id-only read would be a latent cross-tenant read there.
                var record = await db.SigningDocFlowRecords.AsNoTracking()
                    .FirstOrDefaultAsync(r => r.Id == docFlowId && r.TenantId == request.TenantId, ct);
                return record is null
                    ? SigningRequestStatus.Failed
                    : SigningRequestStatusProjector.FromDocFlow(record.Status);
            }

            case SigningUnderlyingKind.PendingTransaction:
            {
                var txn = await db.SigningPendingTransactions.AsNoTracking()
                    .FirstOrDefaultAsync(
                        t => t.TransactionId == request.UnderlyingRecordId && t.TenantId == request.TenantId, ct);
                return txn is null
                    ? SigningRequestStatus.Failed
                    : SigningRequestStatusProjector.FromPendingTransaction(txn.Status);
            }

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(request), request.UnderlyingKind, "Unmapped underlying kind.");
        }
    }
}
