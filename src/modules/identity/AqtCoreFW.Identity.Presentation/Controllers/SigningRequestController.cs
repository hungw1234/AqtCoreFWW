using AqtCoreFW.Identity.Application.Signing;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Services;
using AqtCoreFW.SharedKernel.Api;
using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.SharedKernel.Presentation;
using FluentResults;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AqtCoreFW.Identity.Presentation.Controllers;

/// <summary>
/// Provider-agnostic signing entry point. Creates a durable, pollable <c>SigningRequest</c> and routes it to the
/// strategy that owns its <c>(artifact kind × provider capabilities)</c> cell, then returns the request id plus
/// the strategy's client hand-off (for USB doc-flow: handler URL / JWT / docId for the VGCA plugin).
///
/// <para>The signer is always the authenticated caller (self-sign) — BP-assigned signing (a different expected
/// signer) is a later phase. Tenant + user come from the auth context, never the request body; the API base URL
/// for the doc-flow callback is derived server-side.</para>
///
/// [RequiresMfa]: signing is a high-risk action per QĐ 742 §1.6.b.
/// </summary>
// Route is under the versioned /api/v1 prefix because this is a front-end-facing endpoint:
// the SPA apiClient has baseURL "/api/v1" and calls the relative path "/signing/request".
// (The doc-flow / mysign-callback controllers stay at /api/signing/... — they are machine-to-machine
//  callbacks reached via server-minted URLs, not the SPA apiClient.)
[Route("api/v1/signing/request")]
[Authorize]
[RequiresMfa]
[EnableRateLimiting("write-ops")]
public sealed class SigningRequestController(
    ISender mediator,
    ISignatureOrchestrator orchestrator) : ApiControllerBase
{
    /// <summary>POST /api/v1/signing/request — create + route + execute a signing request.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<CreateSigningRequestResultDto>), 200)]
    [ProducesResponseType(typeof(ApiResponse<object>), 400)]
    [ProducesResponseType(typeof(ApiResponse<object>), 401)]
    public async Task<IActionResult> Create([FromBody] CreateSigningRequestRequest request, CancellationToken ct = default)
    {
        var tenantId = ResolveTenantId();
        var userId   = ResolveUserId();
        var baseUrl  = $"{Request.Scheme}://{Request.Host}";

        var input = new RequestSignatureInput(
            TenantId:         tenantId,
            ArtifactKind:     request.ArtifactKind,
            ArtifactRef:      request.ArtifactRef,
            ProviderKey:      request.ProviderKey,
            AssignedSignerId: userId, // self-sign; BP-assigned (different signer) is a later phase
            InitiatedBy:      userId,
            Placement:        request.Placement,
            DocFlow: request.DocFlow is null
                ? null
                : new DocFlowSignParameters(
                    SignType:           request.DocFlow.SignType,
                    OriginalStorageKey: request.DocFlow.OriginalStorageKey,
                    DocNumber:          request.DocFlow.DocNumber,
                    IssuedDate:         request.DocFlow.IssuedDate,
                    BaseUrl:            baseUrl,
                    FileName:           request.DocFlow.FileName,
                    Extra:              request.DocFlow.Extra),
            SourceFileId:     request.SourceFileId,
            Pades: request.Pades is null
                ? null
                : new PadesSignParameters(
                    OriginalStorageKey:  request.Pades.OriginalStorageKey,
                    DocumentName:        request.Pades.DocumentName,
                    SignFlowModeOverride: request.Pades.SignFlowMode,
                    Reason:              request.Pades.Reason));

        // Dispatch through the audited command pipeline (AuditBehavior) instead of calling the
        // orchestrator directly — the create-signing-request write previously produced no audit row
        // on the pades/remote routes. The orchestrator still owns the create+route+execute transaction.
        var result = await mediator.Send(new RequestSignatureCommand(input), ct);
        return ToApiResponse(result.Map(r =>
            new CreateSigningRequestResultDto(r.PublicId, r.ClientHandoff)));
    }

    /// <summary>GET /api/v1/signing/request/{publicId}/status — read-time status projection for polling.</summary>
    [HttpGet("{publicId:guid}/status")]
    [ProducesResponseType(typeof(ApiResponse<SigningRequestStatusDto>), 200)]
    [ProducesResponseType(typeof(ApiResponse<object>), 404)]
    public async Task<IActionResult> GetStatus(Guid publicId, CancellationToken ct = default)
    {
        var tenantId = ResolveTenantId();
        return ToApiResponse(await orchestrator.GetStatusAsync(publicId, tenantId, ct));
    }

    /// <summary>
    /// GET /api/v1/signing/request/{publicId}/signed-pdf — download the BE-assembled signed PDF for a Completed
    /// server-side PAdES request. 404 if the request is not in the caller's tenant; 400 (typed) if it is not a
    /// PAdES request or the signature is still pending. The signed-PDF key is resolved server-side from the linked
    /// transaction — the client never names a storage key.
    /// </summary>
    [HttpGet("{publicId:guid}/signed-pdf")]
    [ProducesResponseType(typeof(FileResult), 200)]
    [ProducesResponseType(typeof(ApiResponse<object>), 400)]
    [ProducesResponseType(typeof(ApiResponse<object>), 404)]
    public async Task<IActionResult> GetSignedPdf(Guid publicId, CancellationToken ct = default)
    {
        var tenantId = ResolveTenantId();
        var result = await orchestrator.GetSignedPdfAsync(publicId, tenantId, ct);
        if (result.IsFailed)
            return ToApiResponse(result); // preserves typed error → 404 / 400, never an opaque 500
        return File(result.Value.Content, "application/pdf", result.Value.FileName);
    }
}

// ── Request DTOs ───────────────────────────────────────────────────────────────

/// <summary>POST body for /api/v1/signing/request. BaseUrl is NOT accepted from the client (derived server-side).</summary>
public sealed record CreateSigningRequestRequest(
    SigningArtifactKind ArtifactKind,
    string ArtifactRef,
    string ProviderKey,
    string? Placement = null,
    CreateSigningRequestDocFlowRequest? DocFlow = null,
    /// <summary>
    /// For UploadedPdf kind only: the FileRecord PublicId returned by the stage-for-signing response.
    /// Forwarded to the signing orchestrator so the Files module can query steps for this file.
    /// Not required for FormPdf or DetachedData kinds (those are linked via submission id).
    /// </summary>
    Guid? SourceFileId = null,
    /// <summary>PAdES-specific parameters (required when the artifact routes to the server-side Mysign strategy).</summary>
    CreateSigningRequestPadesRequest? Pades = null);

/// <summary>DocFlow-specific parameters (required when the artifact routes to the USB doc-flow strategy).</summary>
public sealed record CreateSigningRequestDocFlowRequest(
    DocFlowSignType SignType,
    string OriginalStorageKey,
    string DocNumber,
    DateTime IssuedDate,
    string? FileName = null,
    IReadOnlyDictionary<string, string>? Extra = null);

/// <summary>PAdES-specific parameters (required when the artifact routes to the remote Mysign server-side strategy).</summary>
/// <param name="SignFlowMode">
/// Optional per-request override of the tenant's remote signing flow (Sync / Async). Null → use the tenant config
/// (default Async). Lets a caller force a specific flow for this one request without mutating tenant settings.
/// </param>
public sealed record CreateSigningRequestPadesRequest(
    string OriginalStorageKey,
    string? DocumentName = null,
    SignatureFlowMode? SignFlowMode = null,
    string? Reason = null);

// ── Result DTO ─────────────────────────────────────────────────────────────────

/// <summary>Result of creating a signing request: the public id to poll + the strategy's client hand-off.</summary>
public sealed record CreateSigningRequestResultDto(
    Guid RequestId,
    IReadOnlyDictionary<string, string> ClientHandoff);
