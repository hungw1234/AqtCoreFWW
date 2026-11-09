using AqtCoreFW.Files.Application.DTOs;
using AqtCoreFW.Files.Application.Options;
using AqtCoreFW.Files.Application.Pdf;
using AqtCoreFW.Files.Domain.Entities;
using AqtCoreFW.Files.Domain.Repositories;
using AqtCoreFW.Files.Domain.Services;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;
using Microsoft.Extensions.Options;

namespace AqtCoreFW.Files.Application.Commands.StageFileForSigning;

/// <summary>
/// Stages a Files-bucket PDF into the signing-original bucket so the DocFlow USB handler can
/// retrieve it. Uploads live in "aqtcorefw-files"; the DocFlow download reads from
/// "signing-original" — without this copy every signing attempt returns 404.
///
/// Guards: file must exist, belong to the caller's tenant, be Available (virus scan passed),
/// and have content-type "application/pdf". Non-PDF uploads are rejected server-side regardless
/// of FE gate (locked decision per architecture spec).
/// </summary>
public sealed class StageFileForSigningCommandHandler(
    IFileRepository fileRepository,
    IFileStorageService storageService,
    IOptions<FilesOptions> options,
    IGuidGenerator guidGen) : IRequestHandler<StageFileForSigningCommand, Result<StageFileForSigningResult>>
{
    // Bucket where DocFlow download reads the original document for USB signing.
    // Must match the key used by FileUploadHandlerController's GET /api/signing/docflow/file.
    private const string SigningOriginalBucket = "signing-original";

    public async Task<Result<StageFileForSigningResult>> Handle(
        StageFileForSigningCommand request,
        CancellationToken cancellationToken)
    {
        var fileResult = await fileRepository.GetByIdAsync(
            FileId.From(request.FileId), cancellationToken);

        if (fileResult.IsFailed)
            return Result.Fail(new NotFoundError($"File {request.FileId} not found."));

        var file = fileResult.Value;

        if (file.TenantId != request.TenantId)
            return Result.Fail(new ForbiddenError("File does not belong to tenant."));

        // Only virus-scanned (Available) files may be signed.
        if (file.Status != FileStatus.Available)
            return Result.Fail(new NotFoundError(
                $"File {request.FileId} is not available (status: {file.Status})."));

        // Server-side PDF gate — non-PDF content must be rejected here, not only in the FE,
        // because the DocFlow magic-byte sniff on upload would otherwise be the first rejection
        // point (too late: bytes have already been copied to signing-original).
        if (!string.Equals(file.ContentType, "application/pdf", StringComparison.OrdinalIgnoreCase))
            return Result.Fail(new ValidationError(
                $"Only PDF files can be staged for signing (content-type: {file.ContentType}).",
                "ContentType"));

        // Download bytes from the Files upload bucket. Buffer them so we can inspect the document
        // before it is copied into the signing bucket (the /Encrypt marker lives in the trailer at the
        // end of the file, so a full read is required to gate reliably). Uploads are size-capped, so
        // buffering a single document for an interactive signing action is acceptable.
        var filesBucket = options.Value.BucketName;
        var sourceStream = await storageService.DownloadAsync(filesBucket, file.StorageKey, cancellationToken);

        await using (sourceStream)
        {
            using var buffer = new MemoryStream();
            await sourceStream.CopyToAsync(buffer, cancellationToken);

            // Reject encrypted / password-protected PDFs at ingest. The USB signing tool (iText
            // PdfSigner) cannot open an owner-password-protected document for signing and fails deep
            // in the desktop process with a cryptic error; surface a clear, actionable rejection here
            // instead of staging a document that can never be signed.
            // GetBuffer() is publicly visible on the no-arg MemoryStream (no zero-copy concern), and
            // (int)Length is safe because the upload size cap keeps documents well under int.MaxValue.
            if (PdfEncryptionDetector.IsEncrypted(buffer.GetBuffer().AsSpan(0, (int)buffer.Length)))
                return Result.Fail(new ValidationError(
                    "Tệp PDF đã được mã hoá hoặc đặt mật khẩu bảo vệ nên không thể ký số. " +
                    "Vui lòng tải lên tệp PDF không mã hoá (gỡ bỏ mật khẩu/giới hạn quyền) rồi thử lại.",
                    "Encrypted"));

            // Mint a fresh document id and build the tenant-prefixed key.
            // The InitiateDocFlowCommand validator requires the key to start with "{tenantId}/"
            // so that cross-tenant key guessing is structurally impossible.
            var docId = guidGen.NewIdMonotonic();
            var originalKey = $"{request.TenantId}/{docId}.pdf";

            buffer.Position = 0;
            await storageService.UploadAsync(
                buffer, SigningOriginalBucket, originalKey, "application/pdf", cancellationToken);

            return Result.Ok(new StageFileForSigningResult(docId, originalKey));
        }
    }
}
