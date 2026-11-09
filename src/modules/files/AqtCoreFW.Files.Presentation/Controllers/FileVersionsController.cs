using AqtCoreFW.Files.Application.Commands.UploadFileVersion;
using AqtCoreFW.Files.Application.Queries.GetFileVersions;
using AqtCoreFW.SharedKernel.Presentation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AqtCoreFW.Files.Presentation.Controllers;

/// <summary>
/// File Versions REST API — upload a new version of a file and list versions.
/// Version numbers are auto-incremented per FileRecord.
/// </summary>
[Route("api/v1/files/{fileRecordId:guid}/versions")]
[Authorize]
public sealed class FileVersionsController(ISender mediator) : ApiControllerBase
{
    /// <summary>List all versions of a file record for the resolved tenant.</summary>
    [HttpGet]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> List(
        Guid fileRecordId,
        CancellationToken cancellationToken = default) =>
        ToApiResponse(await mediator.Send(
            new GetFileVersionsQuery(fileRecordId, ResolveTenantId()),
            cancellationToken));

    /// <summary>Upload a new version of an existing file record.</summary>
    [HttpPost]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(200)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> Upload(
        Guid fileRecordId,
        [FromBody] UploadFileVersionRequest request,
        CancellationToken cancellationToken = default) =>
        ToApiResponse(await mediator.Send(
            new UploadFileVersionCommand(
                fileRecordId,
                request.StoragePath,
                request.FileSize,
                request.ContentHash,
                ResolveUserId(),
                ResolveTenantId(),
                request.Comment),
            cancellationToken));
}

public sealed record UploadFileVersionRequest(
    string StoragePath,
    long FileSize,
    string ContentHash,
    string? Comment);
