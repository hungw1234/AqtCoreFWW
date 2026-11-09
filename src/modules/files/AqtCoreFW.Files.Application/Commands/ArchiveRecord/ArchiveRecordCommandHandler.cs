using AqtCoreFW.Files.Application.DTOs;
using AqtCoreFW.Files.Domain.Entities;
using AqtCoreFW.Files.Domain.Repositories;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Domain.Repositories;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Files.Application.Commands.ArchiveRecord;

/// <summary>
/// Phase-4b B3b refactor:
/// - ArchiveRecordCommand.FileRecordId is Guid (PublicId) — look up FileRecord.Id (long) via IFileRepository.
/// - RecordLifecycle is T3 plain-class: InternalId (long PK) + MessageId (Guid AK).
/// - IRepository uses long TId (InternalId) for T3.
/// - RecordLifecycle.Archive() takes no args (no AuditableEntity base).
/// </summary>
public sealed class ArchiveRecordCommandHandler(
    IFileRepository fileRepository,
    IRepository<RecordLifecycle, long> lifecycleRepository,
    IGuidGenerator guidGen)
    : IRequestHandler<ArchiveRecordCommand, Result<RecordLifecycleDto>>
{
    public async Task<Result<RecordLifecycleDto>> Handle(
        ArchiveRecordCommand request,
        CancellationToken cancellationToken)
    {
        // Resolve FileRecord PublicId → internal long FK
        var fileResult = await fileRepository.GetByIdAsync(
            FileId.From(request.FileRecordId), cancellationToken);

        if (fileResult.IsFailed)
            return Result.Fail(new NotFoundError($"FileRecord {request.FileRecordId} not found."));

        var fileRecordInternalId = fileResult.Value.Id;

        // Create lifecycle entry with long FK
        var lifecycle = RecordLifecycle.Create(guidGen, fileRecordInternalId, retentionPolicyId: null);
        lifecycle.Archive();

        await lifecycleRepository.AddAsync(lifecycle, cancellationToken);

        return Result.Ok(MapToDto(lifecycle));
    }

    internal static RecordLifecycleDto MapToDto(RecordLifecycle l) => new(
        l.MessageId, l.FileRecordId, l.CurrentStatus, l.RetentionPolicyId,
        l.ArchivedAt, l.ScheduledDestroyAt, l.DestroyedAt, l.DestroyedBy);
}
