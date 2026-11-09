using AqtCoreFW.Identity.Application.Signing;
using AqtCoreFW.Identity.Domain.Services;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Identity.Infrastructure.Services.Signing.Strategies;

/// <summary>
/// USB-token (VGCA) doc-flow path: the desktop SignService downloads, stamps and re-uploads the PDF
/// client-side; the BE never holds the key or parses the PDF. Owns PDF artifacts for providers that declare
/// <see cref="SignatureProviderCapabilities.DocFlow"/> + <see cref="SignatureProviderCapabilities.ClientSideSigning"/>.
///
/// <para>Execution reuses the shipped <see cref="InitiateDocFlowCommand"/> (via MediatR) rather than calling
/// <see cref="IDocumentSigningWorkflow"/> directly — that command carries the tenant-prefix storage-key guard
/// (its validator) and the audit entry, and the unified path must NOT re-implement either. The strategy returns
/// the created record id for the orchestrator to link, plus the handler-URL/JWT/docId hand-off for the FE.</para>
/// </summary>
public sealed class DocFlowSigningStrategy(ISender mediator) : ISigningExecutionStrategy
{
    public string Name => "docflow";

    public bool CanHandle(SigningArtifactKind artifactKind, SignatureProviderCapabilities resolvedCapabilities)
        => artifactKind is SigningArtifactKind.FormPdf or SigningArtifactKind.UploadedPdf
           && resolvedCapabilities.HasFlag(SignatureProviderCapabilities.DocFlow)
           && resolvedCapabilities.HasFlag(SignatureProviderCapabilities.ClientSideSigning);

    public async Task<Result<SigningExecutionResult>> ExecuteAsync(
        SigningExecutionContext context, CancellationToken ct = default)
    {
        var input = context.Input;

        // Bad client input → 400, not 500: the caller omitted the doc-flow parameters or supplied a non-id ref.
        if (input.DocFlow is null)
            return Result.Fail(new BadRequestError(
                "DocFlow signing requires DocFlow parameters (sign type, original key, etc.).",
                "SIGNING_DOCFLOW_PARAMS_REQUIRED"));

        // For a PDF doc-flow artifact, ArtifactRef is the document identifier the VGCA round-trip operates on.
        if (!Guid.TryParse(input.ArtifactRef, out var docId))
            return Result.Fail(new BadRequestError(
                $"DocFlow ArtifactRef must be the document id (Guid); got '{input.ArtifactRef}'.",
                "SIGNING_DOCFLOW_BAD_ARTIFACT_REF"));

        var p = input.DocFlow;
        var metadata = new DocFlowMetadata(
            FileName:   p.FileName,
            DocNumber:  p.DocNumber,
            IssuedDate: p.IssuedDate,
            Extra:      p.Extra);

        // Route through the shipped command so its validator (storage-key tenant-prefix guard) and audit fire.
        // The sequence number and previous-signed key are threaded from DocFlowSignParameters so the command's
        // validator enforces the incremental chaining invariant (previous key required for sequence > 1,
        // tenant-prefix guard applied to both keys).
        var result = await mediator.Send(new InitiateDocFlowCommand(
            TenantId:                input.TenantId,
            DocId:                   docId,
            SignType:                p.SignType,
            OriginalStorageKey:      p.OriginalStorageKey,
            Metadata:                metadata,
            UserId:                  input.InitiatedBy,
            BaseUrl:                 p.BaseUrl,
            SignerSequenceNumber:    p.SignerSequenceNumber,
            PreviousSignedStorageKey: p.PreviousSignedStorageKey), ct);

        // Propagate the inner errors with their TYPE intact (the tenant-prefix guard yields a ValidationError →
        // 422; a BadRequestError → 400) instead of collapsing them into a generic 500.
        if (result.IsFailed)
            return Result.Fail(result.Errors);

        var dto = result.Value;
        // A successful initiation with no record id is an internal invariant violation, not a client error → 500.
        if (dto.RecordId is not { } recordId)
            throw new InvalidOperationException("DocFlow initiation succeeded but returned no record id to link.");

        return Result.Ok(new SigningExecutionResult(
            UnderlyingKind:     SigningUnderlyingKind.DocFlow,
            UnderlyingRecordId: recordId.ToString(),
            ClientHandoff: new Dictionary<string, string>
            {
                ["handlerUrl"] = dto.HandlerUrl,
                ["jwt"]        = dto.Jwt,
                ["docId"]      = dto.DocId.ToString(),
            }));
    }
}
