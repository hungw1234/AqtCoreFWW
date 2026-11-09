using AqtCoreFW.Files.Application.DTOs;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Files.Application.Queries.GetFileVersions;

/// <summary>Returns all versions of a file record ordered by version number.</summary>
public sealed record GetFileVersionsQuery(
    Guid FileRecordId,
    Guid TenantId) : IRequest<Result<IReadOnlyList<FileVersionDto>>>;
