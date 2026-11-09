using System.Security.Cryptography;
using AqtCoreFW.Files.Application.DTOs;
using AqtCoreFW.Files.Application.Jobs;
using AqtCoreFW.Files.Application.Options;
using AqtCoreFW.Files.Domain.Entities;
using AqtCoreFW.Files.Domain.Repositories;
using AqtCoreFW.Files.Domain.Services;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AqtCoreFW.Files.Application.Commands.UploadFile;

/// <summary>
/// Phase 1 of two-phase upload pipeline:
/// validate → security check → compute SHA-256 → upload MinIO (Quarantined) → enqueue ClamAV job → 202.
/// Phase 2 (async): ClamAvScanJob → Available | Rejected.
/// Phase-4b B3b: inject IGuidGenerator; pass to FileRecord.Create(); use PublicId for Hangfire + DTO.
/// </summary>
public sealed class UploadFileCommandHandler(
    IFileRepository fileRepository,
    IFileStorageService storageService,
    IFileSecurityService securityService,
    IBackgroundJobService backgroundJobService,
    IGuidGenerator guidGen,
    IOptions<FilesOptions> options,
    ILogger<UploadFileCommandHandler> logger) : IRequestHandler<UploadFileCommand, Result<FileUploadResultDto>>
{
    public async Task<Result<FileUploadResultDto>> Handle(
        UploadFileCommand request,
        CancellationToken cancellationToken)
    {
        // 1. Extension blocklist check (fast — no I/O)
        var extResult = securityService.ValidateExtension(request.OriginalFileName);
        if (!extResult.IsValid)
            return Result.Fail(new ValidationError(extResult.Reason!, "FileName"));

        // 2. Magic bytes MIME validation (reads first 512 bytes)
        var mimeResult = await securityService.ValidateMimeTypeAsync(request.FileStream, cancellationToken);
        if (!mimeResult.IsValid)
            return Result.Fail(new ValidationError(mimeResult.Reason!, "ContentType"));

        // 3. Zip bomb check (only for archive types)
        var zipResult = await securityService.CheckZipBombAsync(request.FileStream, cancellationToken);
        if (!zipResult.IsValid)
            return Result.Fail(new ValidationError(zipResult.Reason!, "FileStream"));

        // 4. Generate UUID storage key — never use user-supplied filename
        var storageKey = securityService.GenerateStorageKey(request.TenantId, request.OriginalFileName);
        var bucket = options.Value.BucketName;

        // 5. Compute SHA-256 (rewind stream first)
        request.FileStream.Position = 0;
        var checksum = await ComputeSha256Async(request.FileStream, cancellationToken);

        // 6. Upload to MinIO (stream from beginning)
        request.FileStream.Position = 0;
        await storageService.UploadAsync(
            request.FileStream, bucket, storageKey,
            request.ContentType, cancellationToken);

        // 7. Save metadata with Quarantined status
        var fileRecord = FileRecord.Create(
            guidGen,
            request.TenantId,
            request.OriginalFileName,
            storageKey,
            request.ContentType,
            request.SizeBytes,
            checksum,
            request.UploadedBy,
            request.CaseId,
            classification: request.Classification);

        await fileRepository.AddAsync(fileRecord, cancellationToken);

        // 8. Enqueue async ClamAV scan job on its dedicated queue — pass PublicId (Guid AK) for external reference.
        //    The virus-scan lane keeps scan throughput isolated from generic background work; the processing
        //    server lists the same BackgroundJobQueues.VirusScan name so the job is actually picked up.
        backgroundJobService.Enqueue<IClamAvScanJob>(
            BackgroundJobQueues.VirusScan, job => job.ScanAsync(fileRecord.PublicId));

        logger.LogInformation(
            "File {FilePublicId} uploaded to quarantine. StorageKey={StorageKey}",
            fileRecord.PublicId, storageKey);

        var result = new FileUploadResultDto(
            fileRecord.PublicId,
            $"/api/v1/files/{fileRecord.PublicId}/metadata");

        return Result.Ok(result);
    }

    private static async Task<string> ComputeSha256Async(Stream stream, CancellationToken cancellationToken)
    {
        using var sha256 = SHA256.Create();
        var hashBytes = await sha256.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}
