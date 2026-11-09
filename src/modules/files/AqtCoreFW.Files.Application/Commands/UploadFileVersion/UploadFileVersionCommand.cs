using AqtCoreFW.Files.Application.DTOs;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Files.Application.Commands.UploadFileVersion;

/// <summary>Adds a new version to an existing FileRecord.</summary>
public sealed record UploadFileVersionCommand(
    Guid FileRecordId,
    string StoragePath,
    long FileSize,
    string ContentHash,
    Guid UploadedBy,
    Guid TenantId,
    string? Comment) : IRequest<Result<FileVersionDto>>;
