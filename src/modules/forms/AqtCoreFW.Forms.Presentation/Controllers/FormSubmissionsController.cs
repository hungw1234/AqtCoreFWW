using System.ComponentModel.DataAnnotations;
using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.Forms.Application.Commands.ApproveSubmission;
using AqtCoreFW.Forms.Application.Commands.RejectSubmission;
using AqtCoreFW.Forms.Application.Commands.SubmitForm;
using AqtCoreFW.Forms.Application.Queries.ExportSubmissions;
using AqtCoreFW.Forms.Application.Queries.GetFormSubmission;
using AqtCoreFW.Forms.Application.Queries.GetSignatureDocHash;
using AqtCoreFW.Forms.Application.Queries.ListFormSubmissions;
using AqtCoreFW.SharedKernel.Presentation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AqtCoreFW.Forms.Presentation.Controllers;

/// <summary>
/// Form submissions REST API â€" submit and query submitted data.
/// Tenant ID and user identity resolved from JWT claims, not request body.
/// </summary>
[Route("api/v1/forms/submissions")]
[Authorize]
public sealed class FormSubmissionsController(ISender mediator) : ApiControllerBase
{
    /// <summary>Submit data against an Active form template.</summary>
    [HttpPost]
    [Authorize(Policy = PermCodes.Forms.Submit.Create)]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> Submit(
        [FromBody] SubmitFormRequest request,
        CancellationToken cancellationToken = default) =>
        ToApiResponse(await mediator.Send(
            new SubmitFormCommand(
                ResolveTenantId(), request.FormTemplatePublicId,
                ResolveUserId(), request.Data,
                ConsentGiven: request.ConsentGiven,
                Signature: request.Signature is null ? null : new SignaturePayload(
                    Convert.FromBase64String(request.Signature.SignatureBase64),
                    request.Signature.SignerCertBase64 is null
                        ? null
                        : Convert.FromBase64String(request.Signature.SignerCertBase64),
                    request.Signature.Format,
                    request.Signature.HashAlg,
                    request.Signature.Encode,
                    request.Signature.ProviderKey)),
            cancellationToken));

    /// <summary>Get a single form submission by ID (dual-mode: Json or Table storage).</summary>
    [HttpGet("{id:guid}")]
    [Authorize(Policy = PermCodes.Forms.Submit.View)]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetById(
        Guid id,
        CancellationToken cancellationToken = default) =>
        ToApiResponse(await mediator.Send(
            new GetFormSubmissionQuery(id, ResolveTenantId()), cancellationToken));

    /// <summary>Export all submissions for a template as CSV. Admin/SystemAdmin only.</summary>
    /// <remarks>
    /// Streams the CSV line-by-line and holds one pooled DB connection open for the whole download, so
    /// it is capped with the heavier "batch-ops" rate-limit (not "read-ops") to bound how many large
    /// exports can pin connections concurrently.
    /// </remarks>
    [HttpGet("export")]
    [Authorize(Policy = PermCodes.Forms.Submit.Export)]
    [EnableRateLimiting("batch-ops")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Export(
        [FromQuery] Guid templatePublicId,
        [FromQuery] string format = "csv",
        CancellationToken ct = default)
    {
        // Header/field validation (incl. 404s) runs inside the handler BEFORE any line is streamed, so
        // a NotFound still surfaces as a normal error response here — nothing has been written yet.
        var result = await mediator.Send(
            new ExportSubmissionsQuery(ResolveTenantId(), templatePublicId, format), ct);

        if (result.IsFailed)
            return ToApiResponse(result);

        // Stream CSV lines straight to the response body so the export is not buffered server-side.
        // The handler's IAsyncEnumerable owns the DB connection for the duration of this write loop.
        var fileName = $"{templatePublicId}_{DateTime.Now:yyyyMMdd}.csv";
        Response.ContentType = "text/csv";
        Response.Headers.ContentDisposition = $"attachment; filename=\"{fileName}\"";

        // No BOM — preserves byte-parity with the previous Encoding.UTF8.GetBytes output.
        await using var writer = new StreamWriter(Response.Body, new System.Text.UTF8Encoding(false));
        await foreach (var line in result.Value.WithCancellation(ct))
        {
            await writer.WriteLineAsync(line);
        }
        await writer.FlushAsync(ct);

        return new EmptyResult();
    }

    /// <summary>List submissions filtered by templatePublicId, status and/or field values (paged).
    /// filters: JSON-encoded array of [{fieldKey, value}] for field-value search.
    /// search: substring match on ReviewComment.</summary>
    [HttpGet]
    [Authorize(Policy = PermCodes.Forms.Submit.View)]
    [ProducesResponseType(200)]
    public async Task<IActionResult> List(
        [FromQuery] Guid? templatePublicId,
        [FromQuery] string? status = null,
        [FromQuery][StringLength(200)] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? filters = null,
        CancellationToken cancellationToken = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        return ToApiResponse(await mediator.Send(
            new ListFormSubmissionsQuery(ResolveTenantId(), templatePublicId, status, search, page, pageSize, filters),
            cancellationToken));
    }

    /// <summary>Approve a Pending submission. Reviewer identity + optional comment required.</summary>
    [HttpPost("{id:guid}/approve")]
    [Authorize(Policy = PermCodes.Forms.Submit.Approve)]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> Approve(
        Guid id,
        [FromBody] ApproveSubmissionRequest request,
        CancellationToken cancellationToken = default) =>
        ToApiResponse(await mediator.Send(
            new ApproveSubmissionCommand(id, ResolveTenantId(), ResolveUserId(), request.Comment),
            cancellationToken));

    /// <summary>Reject a Pending submission. Comment is mandatory for audit trail.</summary>
    [HttpPost("{id:guid}/reject")]
    [Authorize(Policy = PermCodes.Forms.Submit.Reject)]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> Reject(
        Guid id,
        [FromBody] RejectSubmissionRequest request,
        CancellationToken cancellationToken = default) =>
        ToApiResponse(await mediator.Send(
            new RejectSubmissionCommand(id, ResolveTenantId(), ResolveUserId(), request.Comment),
            cancellationToken));

    /// <summary>
    /// Compute the Mode-2 (ServerDocHash) document hash for the given draft data.
    /// The caller signs the returned <c>hashBase64</c> with their signing provider, then
    /// includes it in the <c>Signature</c> bundle when submitting the form.
    /// Only valid for templates that have an active DigitalSignature field in ServerDocHash mode.
    /// </summary>
    [HttpPost("/api/v1/forms/{formTemplatePublicId:guid}/signature-doc-hash")]
    [Authorize(Policy = Policies.SignatureDocHashAccess)]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> GetSignatureDocHash(
        Guid formTemplatePublicId,
        [FromBody] SignatureDocHashRequest request,
        CancellationToken cancellationToken = default) =>
        ToApiResponse(await mediator.Send(
            new GetSignatureDocHashQuery(
                ResolveTenantId(),
                formTemplatePublicId,
                request.Data),
            cancellationToken));
}

// Request body records — no entity types (GOV_SEC_004 mass-assignment prevention)
// TenantId, SubmittedBy, ReviewedBy removed — resolved from JWT via base class
// Data is Dictionary<string, object?> to support typed values from API consumers
/// <summary>FormTemplatePublicId = FormTemplate.PublicId (T2 Guid AK — external addressing).</summary>
public sealed record SubmitFormRequest(
    Guid FormTemplatePublicId,
    Dictionary<string, object?> Data,
    // PDPL consent flag — mirrors PublicSubmitRequest so the authenticated path
    // carries the consent the FE modal collects (RequiresConsent + PII templates).
    bool ConsentGiven = false,
    SignaturePayloadRequest? Signature = null);

/// <summary>
/// Wire-format signature proof bundle.
/// Signature and cert bytes are base64-encoded for JSON transport.
/// TenantId / mode are resolved server-side — never supplied by the client.
/// </summary>
public sealed record SignaturePayloadRequest(
    string SignatureBase64,
    string? SignerCertBase64 = null,
    string Format = "Raw",
    string HashAlg = "SHA256",
    string Encode = "RSA",
    string? ProviderKey = null);

public sealed record ApproveSubmissionRequest(
    string? Comment = null);

public sealed record RejectSubmissionRequest(
    string Comment);

/// <summary>
/// Request body for the Mode-2 document-hash endpoint.
/// Contains the draft form data the caller intends to submit.
/// TenantId is resolved from the JWT — never supplied by the client.
/// </summary>
public sealed record SignatureDocHashRequest(
    Dictionary<string, object?> Data);
