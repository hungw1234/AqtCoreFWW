using AqtCoreFW.Files.Application.DTOs;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Files.Application.Commands.ArchiveRecord;

/// <summary>Archives a file record — moves it to cold storage and updates its lifecycle status.</summary>
public sealed record ArchiveRecordCommand(
    Guid FileRecordId,
    string ArchiveStoragePath,
    string OriginalStoragePath,
    Guid ArchivedBy,
    Guid TenantId,
    string? ArchiveReason) : IRequest<Result<RecordLifecycleDto>>;
