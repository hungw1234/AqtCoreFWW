using AqtCoreFW.Files.Application.Options;
using AqtCoreFW.Files.Domain.Entities;
using AqtCoreFW.Files.Domain.Repositories;
using AqtCoreFW.Files.Domain.Services;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;
using Microsoft.Extensions.Options;

namespace AqtCoreFW.Files.Application.Queries.DownloadFile;

/// <summary>
/// Auth check → validate file is Available → clearance check → stream from MinIO.
/// Files with Quarantined/Rejected status return 404 (GOV compliance: scan must complete before use).
/// F-02: Clearance level check — users may only download files at or below their clearance level.
///       Clearance is derived from RBAC roles: SystemAdmin/Admin=TopSecret, GovOfficer=Confidential, default=Internal.
/// </summary>
public sealed class DownloadFileQueryHandler(
    IFileRepository fileRepository,
    IFileStorageService storageService,
    ICurrentUser currentUser,
    IOptions<FilesOptions> options) : IRequestHandler<DownloadFileQuery, Result<DownloadFileResult>>
{
    public async Task<Result<DownloadFileResult>> Handle(
        DownloadFileQuery request,
        CancellationToken cancellationToken)
    {
        var fileResult = await fileRepository.GetByIdAsync(
            FileId.From(request.FileId), cancellationToken);

        if (fileResult.IsFailed)
            return Result.Fail(new NotFoundError($"File {request.FileId} not found."));

        var file = fileResult.Value;

        if (file.TenantId != request.TenantId)
            return Result.Fail(new ForbiddenError("File does not belong to tenant."));

        // GOV compliance: file must pass virus scan before download
        if (file.Status != FileStatus.Available)
            return Result.Fail(new NotFoundError(
                $"File {request.FileId} is not available (status: {file.Status})."));

        // F-02: Enforce clearance level — role-based mapping per QĐ742 cấp 4 classification
        var userClearance = ResolveUserClearance(currentUser.Roles);
        if (file.ClassificationLevel > userClearance)
            return Result.Fail(new ForbiddenError(
                $"Insufficient clearance: file requires {file.ClassificationLevel}, user has {userClearance}."));

        Stream stream;
        try
        {
            stream = await storageService.DownloadAsync(
                options.Value.BucketName, file.StorageKey, cancellationToken);
        }
        catch (FileNotFoundException)
        {
            // Metadata row exists but the bytes are gone (orphaned record after a storage reset, or an
            // upload whose object never persisted) — a 404 is honest here, not a 500. Storage-unreachable
            // errors are intentionally NOT caught: they propagate to surface as a 5xx, never a silent 200.
            return Result.Fail(new NotFoundError(
                $"File {request.FileId} content is no longer available in storage."));
        }

        return Result.Ok(new DownloadFileResult(
            stream,
            file.OriginalFileName,
            file.ContentType));
    }

    /// <summary>
    /// Resolves the maximum ClassificationLevel a user may access based on their roles.
    /// SystemAdmin / Admin → TopSecret (full access).
    /// GovOfficer → Confidential.
    /// All other authenticated users → Internal (default).
    /// </summary>
    private static ClassificationLevel ResolveUserClearance(string[] roles)
    {
        if (roles.Any(r => r is "SystemAdmin" or "Admin"))
            return ClassificationLevel.TopSecret;

        if (roles.Any(r => r == "GovOfficer"))
            return ClassificationLevel.Confidential;

        return ClassificationLevel.Internal;
    }
}
