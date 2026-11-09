using AqtCoreFW.Identity.Application.Signing.Profiles;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.SharedKernel.Presentation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace AqtCoreFW.Identity.Presentation.Controllers;

/// <summary>
/// Self-service signature-profile management for the authenticated user. Every action resolves the
/// caller's UserId + TenantId from the JWT server-side (<see cref="ApiControllerBase.ResolveUserId"/> /
/// <see cref="ApiControllerBase.ResolveTenantId"/>) and never binds identity from the request body or
/// route — a caller can only see/mutate their own profiles. <c>ResolveTenantId</c> throws (→ 401) when
/// the tenant claim is missing (fail-closed).
/// </summary>
[Route("api/v1/me/signature-profiles")]
[Authorize]
public sealed class UserSignatureProfileController(ISender mediator) : ApiControllerBase
{
    /// <summary>Lists the caller's own signature profiles, ordered by display name.</summary>
    [HttpGet]
    [ProducesResponseType(200)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> List(CancellationToken ct = default)
    {
        var tenantId = ResolveTenantId();
        var userId = ResolveUserId();
        if (userId == Guid.Empty)
            return Unauthorized();

        var result = await mediator.Send(new ListMySignatureProfilesQuery(tenantId, userId), ct);
        return ToApiResponse(result);
    }

    /// <summary>Creates a new (non-default) signature profile for the caller. Returns the new PublicId.</summary>
    [HttpPost]
    [ProducesResponseType(200)]
    [ProducesResponseType(422)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> Create(
        [FromBody] CreateSignatureProfileRequest request, CancellationToken ct = default)
    {
        var tenantId = ResolveTenantId();
        var userId = ResolveUserId();
        if (userId == Guid.Empty)
            return Unauthorized();

        var result = await mediator.Send(
            new CreateSignatureProfileCommand(
                tenantId, userId, request.DisplayName, request.ShowSerial, request.ShowIssuer), ct);
        return ToApiResponse(result);
    }

    /// <summary>Renames one of the caller's profiles. 404 if the profile is not theirs.</summary>
    [HttpPut("{publicId:guid}")]
    [ProducesResponseType(204)]
    [ProducesResponseType(422)]
    [ProducesResponseType(401)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Update(
        Guid publicId, [FromBody] UpdateSignatureProfileRequest request, CancellationToken ct = default)
    {
        var tenantId = ResolveTenantId();
        var userId = ResolveUserId();
        if (userId == Guid.Empty)
            return Unauthorized();

        var result = await mediator.Send(
            new UpdateSignatureProfileCommand(
                tenantId, userId, publicId, request.DisplayName, request.ShowSerial, request.ShowIssuer), ct);
        return ToApiResponse(result);
    }

    /// <summary>Promotes one of the caller's profiles to be their default (clears the prior default).</summary>
    [HttpPost("{publicId:guid}/default")]
    [ProducesResponseType(204)]
    [ProducesResponseType(422)]
    [ProducesResponseType(401)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> SetDefault(Guid publicId, CancellationToken ct = default)
    {
        var tenantId = ResolveTenantId();
        var userId = ResolveUserId();
        if (userId == Guid.Empty)
            return Unauthorized();

        var result = await mediator.Send(
            new MarkDefaultSignatureProfileCommand(tenantId, userId, publicId), ct);
        return ToApiResponse(result);
    }

    /// <summary>Soft-deletes one of the caller's profiles. 404 if the profile is not theirs.</summary>
    [HttpDelete("{publicId:guid}")]
    [ProducesResponseType(204)]
    [ProducesResponseType(422)]
    [ProducesResponseType(401)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Delete(Guid publicId, CancellationToken ct = default)
    {
        var tenantId = ResolveTenantId();
        var userId = ResolveUserId();
        if (userId == Guid.Empty)
            return Unauthorized();

        var result = await mediator.Send(
            new DeleteSignatureProfileCommand(tenantId, userId, publicId), ct);
        return ToApiResponse(result);
    }

    /// <summary>
    /// Streams one visual signature image slot (PNG/JPEG) of the caller's profile so the UI can preview an
    /// already-stored image. Returns the raw bytes (NOT the {success,data} envelope) with the content-type
    /// derived from the stored magic bytes. 404 when the profile is not theirs or the slot is empty.
    /// </summary>
    [HttpGet("{publicId:guid}/images/{visualType}")]
    [ProducesResponseType(200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetImage(
        Guid publicId,
        SignatureVisualType visualType,
        CancellationToken ct = default)
    {
        var tenantId = ResolveTenantId();
        var userId = ResolveUserId();
        if (userId == Guid.Empty)
            return Unauthorized();

        var result = await mediator.Send(
            new GetMySignatureImageQuery(tenantId, userId, publicId, visualType), ct);
        if (result.IsFailed)
            return NotFound();

        // Binary payload — bypass ToApiResponse (which JSON-envelopes) and return the image bytes directly.
        return File(result.Value.Content, result.Value.ContentType);
    }

    /// <summary>
    /// Uploads a visual signature image (PNG/JPEG) into one slot of the caller's profile. The bytes are
    /// validated and stored server-side (content-type ignored — magic-byte narrowed to PNG/JPEG, SVG
    /// rejected); the stored key replaces only the targeted slot. 404 if the profile is not theirs.
    /// </summary>
    [HttpPost("{publicId:guid}/images")]
    [ProducesResponseType(200)]
    [ProducesResponseType(422)]
    [ProducesResponseType(401)]
    [ProducesResponseType(404)]
    [RequestSizeLimit(1_048_576)] // 1 MB hard cap at the pipe; the command validator enforces the 500 KB rule
    public async Task<IActionResult> UploadImage(
        Guid publicId,
        [FromForm] SignatureVisualType visualType,
        IFormFile file,
        CancellationToken ct = default)
    {
        var tenantId = ResolveTenantId();
        var userId = ResolveUserId();
        if (userId == Guid.Empty)
            return Unauthorized();

        // Signature images are small (≤500 KB) — buffer to a byte[] for double-pass validation (magic-byte
        // probe + store) rather than a forward-only stream.
        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, ct);

        var result = await mediator.Send(new UploadSignatureImageCommand(
            tenantId, userId, publicId, visualType, buffer.ToArray(), file.FileName), ct);
        return ToApiResponse(result);
    }
}

/// <summary>Request body for creating a signature profile (identity comes from JWT, not the body).</summary>
/// <param name="ShowSerial">Whether the visible stamp shows the "Serial" line (default true).</param>
/// <param name="ShowIssuer">Whether the visible stamp shows the "Nhà CC" (issuer) line (default true).</param>
public sealed record CreateSignatureProfileRequest(
    string DisplayName, bool ShowSerial = true, bool ShowIssuer = true);

/// <summary>Request body for renaming a signature profile + toggling its stamp serial/issuer lines.</summary>
public sealed record UpdateSignatureProfileRequest(
    string DisplayName, bool ShowSerial = true, bool ShowIssuer = true);
