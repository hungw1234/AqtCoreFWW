using System.Text.Json.Serialization;
using AqtCoreFW.Identity.Application.Signing;
using AqtCoreFW.SharedKernel.Presentation;
using FluentResults;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Identity.Presentation.Controllers;

/// <summary>
/// VGCA Tier-3 FileUploadHandler endpoints (W3, QĐ 742 mục 5.1).
///
/// <para>Called directly by the VGCA SignService desktop process, NOT the browser — therefore
/// [AllowAnonymous] with custom JWT validation via <see cref="IDocFlowJwtService"/> (inline; a filter
/// would never run under [AllowAnonymous]). The desktop service authenticates solely via the
/// short-lived JWT issued by /initiate.</para>
///
/// <para>Thin controller: JWT validation + multipart extraction + PDF-format request validation are
/// transport concerns here; the nonce/record state machine, RLS tenant pinning, blob I/O and audit live
/// in <see cref="ProcessDocFlowDownloadCommand"/> / <see cref="CompleteDocFlowUploadCommand"/>.</para>
///
/// <para>
/// Download: GET  /api/signing/docflow/file?docId={guid}&amp;token={jwt}
/// Upload:   POST /api/signing/docflow/file  (multipart/form-data, field "uploadfile")
/// </para>
/// </summary>
[Route("api/signing/docflow")]
[AllowAnonymous] // JWT validated manually below — desktop service has no session cookie
public sealed class FileUploadHandlerController(
    IDocFlowJwtService jwtService,
    ISender mediator,
    ILogger<FileUploadHandlerController> logger) : ApiControllerBase
{
    private const long MaxUploadBytes = 50L * 1024 * 1024; // 50 MB

    // ─── Download ─────────────────────────────────────────────────────────────

    /// <summary>
    /// GET /api/signing/docflow/file?docId={guid}&amp;token={jwt}
    /// VGCA service downloads the PDF to be signed. Nonce slot 1 of 2 consumed here.
    /// </summary>
    [HttpGet("file")]
    [ProducesResponseType(typeof(FileStreamResult), 200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Download([FromQuery] Guid docId, CancellationToken ct = default)
    {
        var jwt = ExtractJwt();
        if (jwt is null)
            return Unauthorized(new { error = "Missing JWT — provide via Authorization header or ?token query param." });

        if (!jwtService.TryValidate(jwt, out var userId, out var jwtDocId, out var nonce, out var jwtTenant, out var jwtError))
        {
            logger.LogWarning("DocFlow download: JWT validation failed — {Error}", jwtError);
            return Unauthorized(new { error = jwtError });
        }

        if (jwtDocId != docId)
        {
            logger.LogWarning("DocFlow download: docId mismatch — JWT={JwtDocId} query={QueryDocId}", jwtDocId, docId);
            return Unauthorized(new { error = "JWT docId does not match query docId." });
        }

        var result = await mediator.Send(
            new ProcessDocFlowDownloadCommand(docId, jwtTenant, userId, nonce), ct);

        if (result.IsFailed)
            return MapError(result.Errors);

        return File(result.Value.Content, "application/pdf", result.Value.FileName);
    }

    // ─── Upload ───────────────────────────────────────────────────────────────

    /// <summary>
    /// POST /api/signing/docflow/file
    /// VGCA service uploads the signed PDF (multipart/form-data, field "uploadfile").
    /// Nonce slot 2 of 2 consumed here. Returns the VGCA PascalCase contract.
    /// </summary>
    [HttpPost("file")]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadBytes)]
    [RequestSizeLimit(MaxUploadBytes)]
    [ProducesResponseType(200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> Upload(CancellationToken ct = default)
    {
        var jwt = ExtractJwt();
        if (jwt is null)
            return Unauthorized(new { error = "Missing JWT." });

        if (!jwtService.TryValidate(jwt, out var userId, out var jwtDocId, out var nonce, out var jwtTenant, out var jwtError))
        {
            logger.LogWarning("DocFlow upload: JWT validation failed — {Error}", jwtError);
            return Unauthorized(new { error = jwtError });
        }

        // ── PDF-format request validation (transport) ──────────────────────────
        // These format checks run here, BEFORE the command consumes the nonce (the original ran them
        // after the nonce/record checks). Intentional: reject obviously-malformed transport without
        // touching the RLS-protected state. The desktop tool sends one well-formed multipart per
        // ceremony, so the paths do not realistically overlap — do not "restore" the old order.
        IFormFile? uploadedFile = Request.Form.Files["uploadfile"] ?? Request.Form.Files.FirstOrDefault();
        if (uploadedFile is null)
            return BadRequest(new { error = "No file found in multipart form (expected field 'uploadfile')." });

        var contentType = uploadedFile.ContentType;
        if (!contentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase)
            && !contentType.Equals("application/octet-stream", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = $"Unexpected content type '{contentType}'; expected application/pdf." });

        if (uploadedFile.Length > MaxUploadBytes)
            return BadRequest(new { error = $"File exceeds maximum size of {MaxUploadBytes / 1024 / 1024} MB." });

        // Server-side content sniff — the client ContentType is spoofable. This only proves it is a PDF,
        // NOT that it carries a valid signature (full crypto verification happens in the sandbox stage).
        if (!await IsPdfMagicHeaderAsync(uploadedFile, ct))
            return BadRequest(new { error = "Uploaded file is not a PDF (missing %PDF- header)." });

        var baseUrl = $"{Request.Scheme}://{Request.Host}";

        // Open a fresh stream (the magic check above consumed its own) and hand it to the handler, which
        // owns the nonce/record state machine, RLS pinning, blob upload and audit.
        await using var stream = uploadedFile.OpenReadStream();
        var result = await mediator.Send(
            new CompleteDocFlowUploadCommand(
                jwtDocId, jwtTenant, userId, nonce, stream, uploadedFile.FileName, baseUrl), ct);

        if (result.IsFailed)
            return MapError(result.Errors);

        // The VGCA desktop tool parses these keys case-sensitively (PascalCase from the vendor sample);
        // VgcaUploadResponse pins the wire casing over the host's camelCase policy.
        return Ok(new VgcaUploadResponse(
            Status:     true,
            Message:    string.Empty,
            FileName:   result.Value.FileName,
            FileServer: result.Value.FileServer));
    }

    /// <summary>Maps a <see cref="DocFlowError"/> to its exact HTTP status + raw {error} body.</summary>
    private IActionResult MapError(IReadOnlyList<IError> errors)
    {
        var docFlowError = errors.OfType<DocFlowError>().FirstOrDefault();
        var status = docFlowError?.StatusCode ?? 500;
        var message = docFlowError?.Message ?? errors.FirstOrDefault()?.Message ?? "Unexpected error.";
        return StatusCode(status, new { error = message });
    }

    /// <summary>
    /// Upload response body the VGCA desktop tool parses. The <see cref="JsonPropertyName"/> casing
    /// matches the vendor SDK sample verbatim and overrides the host's camelCase naming policy.
    /// </summary>
    private sealed record VgcaUploadResponse(
        [property: JsonPropertyName("Status")]     bool Status,
        [property: JsonPropertyName("Message")]    string Message,
        [property: JsonPropertyName("FileName")]   string FileName,
        [property: JsonPropertyName("FileServer")] string FileServer);

    // ─── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>Extracts JWT from Authorization: Bearer header or ?token query parameter.</summary>
    private string? ExtractJwt()
    {
        var authHeader = Request.Headers.Authorization.FirstOrDefault();
        if (authHeader?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == true)
            return authHeader["Bearer ".Length..].Trim();

        return Request.Query.TryGetValue("token", out var tokenValues)
            ? tokenValues.FirstOrDefault()
            : null;
    }

    /// <summary>
    /// Server-side content sniff: true when the uploaded file begins with the "%PDF-" magic header.
    /// Reads only the first 5 bytes (no PDF parsing — that is the sandbox's job).
    /// </summary>
    private static async Task<bool> IsPdfMagicHeaderAsync(IFormFile file, CancellationToken ct)
    {
        const int magicLen = 5; // "%PDF-"
        var header = new byte[magicLen];

        await using var stream = file.OpenReadStream();
        var read = await stream.ReadAtLeastAsync(header, magicLen, throwOnEndOfStream: false, ct);

        return read == magicLen && header.AsSpan().SequenceEqual("%PDF-"u8);
    }
}
