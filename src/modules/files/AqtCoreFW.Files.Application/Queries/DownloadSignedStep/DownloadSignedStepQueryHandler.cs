using AqtCoreFW.Files.Application.Queries.DownloadFile;
using AqtCoreFW.Files.Domain.Entities;
using AqtCoreFW.Files.Domain.Repositories;
using AqtCoreFW.Files.Domain.Services;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Files.Application.Queries.DownloadSignedStep;

/// <summary>
/// Streams the signed PDF for a specific signing step of an uploaded file.
///
/// Security invariants:
///  1. File ownership guard first — cross-tenant file id returns 404.
///  2. Storage key resolved server-side via ISigningRequestClient; client never supplies bucket/key.
///  3. Only Completed steps have a signed PDF; not-Completed → 404.
/// </summary>
public sealed class DownloadSignedStepQueryHandler(
    IFileRepository fileRepository,
    ISigningRequestClient signingClient,
    IFileStorageService storageService)
    : IRequestHandler<DownloadSignedStepQuery, Result<DownloadFileResult>>
{
    public async Task<Result<DownloadFileResult>> Handle(
        DownloadSignedStepQuery request,
        CancellationToken cancellationToken)
    {
        // Step 1: file ownership guard — cross-tenant → 404.
        var fileResult = await fileRepository.GetByIdAsync(
            FileId.From(request.FileId), cancellationToken);

        if (fileResult.IsFailed || fileResult.Value.TenantId != request.TenantId)
            return Result.Fail(new NotFoundError($"File {request.FileId} not found."));

        // Step 2: resolve storage key server-side (tenant-scoped inside Identity impl).
        // Prefer per-attempt addressing by RequestId so each independent sign of the same file resolves
        // to its own blob; fall back to (fileId, sequence) for legacy callers that omit RequestId.
        var resolution = request.RequestId is { } requestId
            ? await signingClient.ResolveSignedStepForFileByRequestAsync(
                request.TenantId, request.FileId, requestId, cancellationToken)
            : await signingClient.ResolveSignedStepForFileAsync(
                request.TenantId, request.FileId, request.SequenceNumber, cancellationToken);

        if (!resolution.Found)
        {
            // NOT_COMPLETED and NOT_FOUND both surface as 404 — client should poll status first.
            // Describe the attempt by whichever address the caller used (request id vs sequence).
            var attemptRef = request.RequestId is { } rid
                ? $"request {rid}"
                : $"sequence {request.SequenceNumber}";
            return Result.Fail(new NotFoundError(
                $"No completed signed PDF found for file {request.FileId} {attemptRef}. " +
                $"Reason: {resolution.ErrorCode}"));
        }

        // Step 3: stream from signing-docflow bucket. Storage key is server-minted; never caller-supplied.
        Stream stream;
        try
        {
            stream = await storageService.DownloadAsync(
                resolution.Bucket!, resolution.StorageKey!, cancellationToken);
        }
        catch (FileNotFoundException)
        {
            return Result.Fail(new NotFoundError(
                $"Signed PDF for file {request.FileId} sequence {request.SequenceNumber} is no longer in storage."));
        }

        return Result.Ok(new DownloadFileResult(
            stream,
            resolution.FileName ?? $"signed-{request.FileId}-seq{request.SequenceNumber}.pdf",
            "application/pdf"));
    }
}
