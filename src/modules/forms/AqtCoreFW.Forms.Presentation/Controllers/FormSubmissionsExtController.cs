using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.Forms.Application.Commands.BulkApproveSubmissions;
using AqtCoreFW.Forms.Application.Commands.BulkRejectSubmissions;
using AqtCoreFW.Forms.Application.Commands.SignSubmissionDocument;
using AqtCoreFW.Forms.Application.Queries.DownloadSubmissionSignedStep;
using AqtCoreFW.Forms.Application.Queries.ExportSubmissionPdf;
using AqtCoreFW.Forms.Application.Queries.GetSubmissionSigningSteps;
using AqtCoreFW.SharedKernel.Presentation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AqtCoreFW.Forms.Presentation.Controllers;

/// <summary>
/// Submission extension endpoints: bulk approve/reject + per-submission PDF export + USB signing.
/// Kept in separate file to stay under 200 lines.
/// </summary>
[Route("api/v1/forms/submissions")]
[Authorize]
public sealed class FormSubmissionsExtController(ISender mediator) : ApiControllerBase
{
    /// <summary>Bulk approve multiple Pending submissions.</summary>
    [HttpPost("bulk-approve")]
    [Authorize(Policy = PermCodes.Forms.Submit.Approve)]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(200)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> BulkApprove(
        [FromBody] BulkApproveRequest request,
        CancellationToken cancellationToken = default) =>
        ToApiResponse(await mediator.Send(
            new BulkApproveSubmissionsCommand(
                request.Ids, ResolveTenantId(), ResolveUserId(), request.Comment),
            cancellationToken));

    /// <summary>Bulk reject multiple Pending submissions. Comment required for audit trail.</summary>
    [HttpPost("bulk-reject")]
    [Authorize(Policy = PermCodes.Forms.Submit.Reject)]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(200)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> BulkReject(
        [FromBody] BulkRejectRequest request,
        CancellationToken cancellationToken = default) =>
        ToApiResponse(await mediator.Send(
            new BulkRejectSubmissionsCommand(
                request.Ids, ResolveTenantId(), ResolveUserId(), request.Comment),
            cancellationToken));

    /// <summary>Export a single submission as PDF. Returns application/pdf binary.</summary>
    [HttpGet("{id:guid}/export-pdf")]
    [Authorize(Policy = PermCodes.Forms.Submit.Export)]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> ExportPdf(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var result = await mediator.Send(
            new ExportSubmissionPdfQuery(id, ResolveTenantId()), cancellationToken);

        if (result.IsFailed)
            return ToApiResponse(result);

        return File(result.Value, "application/pdf", "submission-" + id + ".pdf");
    }

    /// <summary>
    /// List signing steps for a submission's signed PDF.
    /// Returns ordered steps (sequence, status, completedAt, hasSignedPdf).
    /// Null SigningRequestId (no signing initiated) → empty list. Cross-tenant publicId → 404.
    /// Tenant always resolved from JWT — never from query param (IDOR prevention).
    /// </summary>
    [HttpGet("{publicId:guid}/signing-steps")]
    [Authorize(Policy = PermCodes.Forms.Submit.View)]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetSigningSteps(
        Guid publicId,
        CancellationToken cancellationToken = default) =>
        ToApiResponse(await mediator.Send(
            new GetSubmissionSigningStepsQuery(publicId, ResolveTenantId()),
            cancellationToken));

    /// <summary>
    /// Download the signed PDF for a specific signing step of a form submission.
    /// Storage key is resolved server-side from (submissionPublicId, sequence) — client never passes bucket/key.
    /// Null SigningRequestId or step not yet Completed → 404.
    /// Tenant always resolved from JWT — never from query param (IDOR prevention).
    /// </summary>
    [HttpGet("{publicId:guid}/signed")]
    [Authorize(Policy = PermCodes.Forms.Submit.Export)]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> DownloadSignedStep(
        Guid publicId,
        [FromQuery] int sequence = 1,
        CancellationToken cancellationToken = default)
    {
        var result = await mediator.Send(
            new DownloadSubmissionSignedStepQuery(publicId, ResolveTenantId(), sequence),
            cancellationToken);

        if (result.IsFailed)
            return ToApiResponse(result);

        var download = result.Value;
        return File(download.FileStream, download.ContentType,
            download.FileName, enableRangeProcessing: true);
    }

    /// <summary>
    /// Initiate a USB DocFlow signing request for a submission's server-generated PDF.
    ///
    /// The BE generates the QuestPDF document, stages it to the signing-original bucket, then
    /// calls the signing orchestrator. On success, returns RequestId and ClientHandoff for the
    /// FE to pass to the VGCA plugin (handlerUrl / jwt / docId).
    ///
    /// Poll GET /api/signing/request/{requestId}/status to track signing completion.
    ///
    /// [RequiresMfa]: signing is a high-risk action per QD 742 SS1.6.b.
    /// Tenant + caller resolved from JWT -- never from route or body (IDOR prevention).
    /// </summary>
    [HttpPost("{publicId:guid}/sign-document")]
    [RequiresMfa]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(typeof(GenerateAndInitiateSubmissionSigningResult), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> SignDocument(
        Guid publicId,
        [FromBody] SignDocumentRequest request,
        CancellationToken cancellationToken = default)
    {
        var tenantId = ResolveTenantId();
        var userId   = ResolveUserId();
        // BaseUrl is derived from the server's own request -- not from the FE body --
        // to prevent SSRF via malicious BaseUrl injection in the VGCA callback URL.
        var baseUrl  = Request.Scheme + "://" + Request.Host;

        return ToApiResponse(await mediator.Send(
            new GenerateAndInitiateSubmissionSigningCommand(
                SubmissionPublicId: publicId,
                TenantId:           tenantId,
                InitiatedBy:        userId,
                SignType:           request.SignType,
                DocNumber:          request.DocNumber,
                IssuedDate:         request.IssuedDate,
                ProviderKey:        request.ProviderKey,
                BaseUrl:            baseUrl),
            cancellationToken));
    }
}

// -- Request / response records --

// Bulk operation bodies -- TenantId/ReviewedBy resolved from JWT, not accepted from body.
public sealed record BulkApproveRequest(
    List<Guid> Ids,
    string? Comment = null);

public sealed record BulkRejectRequest(
    List<Guid> Ids,
    string Comment);

/// <summary>
/// POST body for POST /api/v1/forms/submissions/{publicId}/sign-document.
/// BaseUrl is NOT accepted from the client -- derived server-side from the HTTP request.
/// TenantId and InitiatedBy are NOT in the body -- resolved from JWT to prevent IDOR.
/// </summary>
public sealed record SignDocumentRequest(
    /// <summary>VGCA plugin function: Issued | Approved | Income | Comment | Appendix | Copy | Files.</summary>
    string SignType,
    /// <summary>Document reference number shown in the visible VGCA stamp.</summary>
    string DocNumber,
    /// <summary>Issuance date shown in the visible VGCA stamp.</summary>
    DateTime IssuedDate,
    /// <summary>Tenant signing-provider key (e.g. usb-vgca) from the tenant provider config.</summary>
    string ProviderKey);
