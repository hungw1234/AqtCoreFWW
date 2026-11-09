using AqtCoreFW.Identity.Application.Signing;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Services;
using AqtCoreFW.SharedKernel.Api;
using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.SharedKernel.Presentation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AqtCoreFW.Identity.Presentation.Controllers;

/// <summary>
/// Initiates a Tier-3 VGCA doc-flow signing session (W3, QĐ 742 mục 5.1).
///
/// <para>POST /api/signing/docflow/initiate — authenticated user, MFA required.
/// Returns {handlerUrl, jwt, docId} for the FE to pass into the VGCA plugin call.</para>
///
/// [RequiresMfa]: signing is a high-risk action per QĐ 742 §1.6.b.
/// Rate-limit: "write-ops" policy (30/min per user via existing rate-limit middleware).
/// </summary>
[Route("api/signing/docflow")]
[Authorize]
[RequiresMfa]
[EnableRateLimiting("write-ops")]
public sealed class DocFlowInitiateController(ISender mediator) : ApiControllerBase
{
    /// <summary>
    /// POST /api/signing/docflow/initiate
    /// Creates a doc-flow signing session and returns the handler URL + JWT for VGCA plugin.
    /// </summary>
    [HttpPost("initiate")]
    [ProducesResponseType(typeof(ApiResponse<InitiateDocFlowResultDto>), 200)]
    [ProducesResponseType(typeof(ApiResponse<object>), 400)]
    [ProducesResponseType(typeof(ApiResponse<object>), 401)]
    public async Task<IActionResult> Initiate(
        [FromBody] InitiateDocFlowRequest request,
        CancellationToken ct = default)
    {
        var tenantId = ResolveTenantId();
        var userId   = ResolveUserId();

        // Build base URL from the current request for HandlerUrl construction
        var baseUrl = $"{Request.Scheme}://{Request.Host}";

        var metadata = new DocFlowMetadata(
            FileName:    request.FileName,
            DocNumber:   request.DocNumber,
            IssuedDate:  request.IssuedDate,
            Extra:       request.Extra);

        return ToApiResponse(await mediator.Send(new InitiateDocFlowCommand(
            TenantId:           tenantId,
            DocId:              request.DocId,
            SignType:           request.SignType,
            OriginalStorageKey: request.OriginalStorageKey,
            Metadata:           metadata,
            UserId:             userId,
            BaseUrl:            baseUrl), ct));
    }
}

// ── Request DTO ─────────────────────────────────────────────────────────────

/// <summary>
/// POST body for /api/signing/docflow/initiate.
/// OriginalStorageKey: IFileStorageService key in bucket "signing-original" where the PDF to be signed lives.
/// </summary>
public sealed record InitiateDocFlowRequest(
    Guid DocId,
    DocFlowSignType SignType,
    string OriginalStorageKey,
    string DocNumber,
    DateTime IssuedDate,
    string? FileName = null,
    IReadOnlyDictionary<string, string>? Extra = null);
