using AqtCoreFW.Files.Application.DTOs;
using AqtCoreFW.Files.Domain.Entities;
using AqtCoreFW.Files.Domain.Repositories;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Files.Application.Queries.GetFileMetadata;

public sealed class GetFileMetadataQueryHandler(
    IFileRepository fileRepository) : IRequestHandler<GetFileMetadataQuery, Result<FileMetadataDto>>
{
    public async Task<Result<FileMetadataDto>> Handle(
        GetFileMetadataQuery request,
        CancellationToken cancellationToken)
    {
        // FileId.From wraps the PublicId Guid; FileRepository overrides GetByIdAsync to do PublicId lookup
        var fileResult = await fileRepository.GetByIdAsync(
            FileId.From(request.FileId), cancellationToken);

        if (fileResult.IsFailed)
            return Result.Fail(new NotFoundError($"File {request.FileId} not found."));

        var file = fileResult.Value;

        if (file.TenantId != request.TenantId)
            return Result.Fail(new ForbiddenError("File does not belong to tenant."));

        // Phase-4b B3b: Id = PublicId (Guid AK), not long internal Id.
        // CreatedAt/UpdatedAt: Entity uses DateTime; cast to DateTimeOffset at serialization boundary.
        var dto = new FileMetadataDto(
            file.PublicId,
            file.OriginalFileName,
            file.ContentType,
            file.SizeBytes,
            file.ChecksumSha256,
            file.Status,
            file.TenantId,
            file.UploadedBy,
            file.CaseId,
            new DateTimeOffset(file.CreatedAt, TimeSpan.Zero),
            file.UpdatedAt.HasValue ? new DateTimeOffset(file.UpdatedAt.Value, TimeSpan.Zero) : null);

        return Result.Ok(dto);
    }
}
