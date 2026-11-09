using AqtCoreFW.Files.Application.Commands.DeleteFile;
using AqtCoreFW.Files.Application.Commands.StageFileForSigning;
using AqtCoreFW.Files.Application.Commands.UploadFile;
using AqtCoreFW.Files.Application.Queries.DownloadFile;
using AqtCoreFW.Files.Application.Queries.DownloadSignedStep;
using AqtCoreFW.Files.Application.Queries.GetFileMetadata;
using AqtCoreFW.Files.Application.Queries.GetFileSigningSteps;
using AqtCoreFW.Files.Application.Queries.ListFiles;
using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Presentation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IO;

namespace AqtCoreFW.Files.Presentation.Controllers;

/// <summary>
/// Files REST API — upload, download, metadata, delete.
/// POST /api/v1/files          → 202 Accepted (two-phase: quarantine → ClamAV scan async)
/// GET  /api/v1/files/{id}     → stream download (Available files only)
/// GET  /api/v1/files/{id}/metadata → file metadata + current scan status
/// DELETE /api/v1/files/{id}   → soft delete
///
/// F-01: TenantId and user identity always resolved from JWT claims (ICurrentUser/ITenantContext).
///       Never accepted from query params — prevents tenant spoofing and IDOR.
/// RecyclableMemoryStream used for upload handling — avoids LOH pressure on large files.
/// </summary>
[Route("api/v1/files")]
[Authorize]
public sealed class FilesController(
    ISender mediator,
    RecyclableMemoryStreamManager streamManager,
    ICurrentUser currentUser,
    ITenantContext tenantContext) : ApiControllerBase
{
    /// <summary>List files for the current tenant (paginated). Enriches uploader names via Identity client.</summary>
    [HttpGet]
    [ProducesResponseType(200)]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (!tenantContext.TenantId.HasValue)
            return Forbid();

        var result = await mediator.Send(
            new ListFilesQuery(tenantContext.TenantId.Value, page, pageSize), cancellationToken);

        return Ok(AqtCoreFW.SharedKernel.Api.ApiResponse<object>.Ok(result.Value));
    }

    /// <summary>Upload a file. Returns 202 with fileId for async scan polling.</summary>
    [HttpPost]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(202)]
    [ProducesResponseType(422)]
    [RequestSizeLimit(104_857_600)] // 100 MB — enforced at middleware level (NĐ68 Group F fix)
    public async Task<IActionResult> Upload(
        IFormFile file,
        [FromQuery] Guid? caseId,
        [FromQuery] ClassificationLevel classification = ClassificationLevel.Internal,
        CancellationToken cancellationToken = default)
    {
        // F-01: Resolve identity from JWT — never trust query params for security-critical fields
        if (!tenantContext.TenantId.HasValue)
            return Forbid();

        var tenantId = tenantContext.TenantId.Value;
        var uploadedBy = currentUser.UserId;

        // RecyclableMemoryStream — avoids MemoryStream LOH for large uploads
        await using var recyclableStream = streamManager.GetStream("file-upload");
        await file.CopyToAsync(recyclableStream, cancellationToken);
        recyclableStream.Position = 0;

        var command = new UploadFileCommand(
            recyclableStream,
            file.FileName,
            file.ContentType,
            file.Length,
            tenantId,
            uploadedBy,
            caseId,
            classification);

        var result = await mediator.Send(command, cancellationToken);

        if (result.IsFailed)
            return ToApiResponse(result);

        // 202 Accepted — file is quarantined, scan in progress
        return StatusCode(StatusCodes.Status202Accepted, result.Value);
    }

    /// <summary>Download file stream. Only Available (scan-passed) files returned.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(403)]
    public async Task<IActionResult> Download(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        // F-01: Resolve tenant and user from JWT
        if (!tenantContext.TenantId.HasValue)
            return Forbid();

        var tenantId = tenantContext.TenantId.Value;
        var requestedBy = currentUser.UserId;

        var result = await mediator.Send(
            new DownloadFileQuery(id, tenantId, requestedBy), cancellationToken);

        if (result.IsFailed)
            return ToApiResponse(result);

        var download = result.Value;
        return File(download.FileStream, download.ContentType,
            download.OriginalFileName, enableRangeProcessing: true);
    }

    /// <summary>Get file metadata including virus scan status (use for polling after upload).</summary>
    [HttpGet("{id:guid}/metadata")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetMetadata(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        // F-01: Resolve tenant from JWT
        if (!tenantContext.TenantId.HasValue)
            return Forbid();

        return ToApiResponse(await mediator.Send(
            new GetFileMetadataQuery(id, tenantContext.TenantId.Value), cancellationToken));
    }

    /// <summary>Soft-delete a file record.</summary>
    [HttpDelete("{id:guid}")]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    [ProducesResponseType(403)]
    public async Task<IActionResult> Delete(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        // F-01: Resolve tenant and user from JWT
        if (!tenantContext.TenantId.HasValue)
            return Forbid();

        var tenantId = tenantContext.TenantId.Value;
        var deletedBy = currentUser.UserId;

        return ToApiResponse(await mediator.Send(
            new DeleteFileCommand(id, tenantId, deletedBy), cancellationToken));
    }

    /// <summary>
    /// List signing steps for a file. Returns ordered steps (sequence, status, completedAt, hasSignedPdf).
    /// Returns an empty list for files that have never been signed. Cross-tenant file id → 404.
    /// Tenant always resolved from JWT — never from query param (IDOR prevention).
    /// </summary>
    [HttpGet("{id:guid}/signing-steps")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetSigningSteps(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        if (!tenantContext.TenantId.HasValue)
            return Forbid();

        return ToApiResponse(await mediator.Send(
            new GetFileSigningStepsQuery(id, tenantContext.TenantId.Value), cancellationToken));
    }

    /// <summary>
    /// Download the signed PDF for a specific signing attempt of an uploaded file.
    /// Storage key is resolved server-side — client never passes bucket/key.
    /// Prefer <paramref name="requestId"/> (the SigningRequest PublicId) so each independent sign of the
    /// same file is retrievable; <paramref name="sequence"/> is the legacy fallback (latest at that step).
    /// Cross-tenant / cross-file id → 404. Attempt not yet Completed → 404.
    /// Tenant always resolved from JWT — never from query param (IDOR prevention).
    /// </summary>
    [HttpGet("{id:guid}/signed")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> DownloadSignedStep(
        Guid id,
        [FromQuery] int sequence = 1,
        [FromQuery] Guid? requestId = null,
        CancellationToken cancellationToken = default)
    {
        if (!tenantContext.TenantId.HasValue)
            return Forbid();

        var result = await mediator.Send(
            new DownloadSignedStepQuery(id, tenantContext.TenantId.Value, sequence, requestId),
            cancellationToken);

        if (result.IsFailed)
            return ToApiResponse(result);

        var download = result.Value;
        return File(download.FileStream, download.ContentType,
            download.OriginalFileName, enableRangeProcessing: true);
    }

    /// <summary>
    /// Copy a tenant-owned PDF from the Files bucket into the signing-original bucket and mint a
    /// docId for the DocFlow USB signing round-trip.
    /// Returns 200 { docId, originalStorageKey } on success; 404 if not found / not Available;
    /// 422 if the file is not a PDF.
    /// [RequiresMfa]: staging is a prerequisite for signing, a high-risk action per QĐ 742 §1.6.b.
    /// </summary>
    [HttpPost("{id:guid}/stage-for-signing")]
    [RequiresMfa]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    [ProducesResponseType(403)]
    public async Task<IActionResult> StageForSigning(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        // Resolve tenant from JWT — never accept from query params (prevents tenant spoofing / IDOR)
        if (!tenantContext.TenantId.HasValue)
            return Forbid();

        var tenantId = tenantContext.TenantId.Value;

        return ToApiResponse(await mediator.Send(
            new StageFileForSigningCommand(id, tenantId), cancellationToken));
    }
}
