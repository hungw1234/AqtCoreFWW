using AqtCoreFW.Forms.Application.Commands.SubmitForm;
using AqtCoreFW.Forms.Application.Commands.SubmitPublicForm;
using AqtCoreFW.Forms.Application.Queries.EvaluateFormulas;
using AqtCoreFW.Forms.Application.Queries.GetPublicForm;
using AqtCoreFW.Forms.Application.Queries.GetPublicFormFieldOptions;
using AqtCoreFW.Forms.Application.Queries.GetSignatureDocHash;
using AqtCoreFW.SharedKernel.Presentation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AqtCoreFW.Forms.Presentation.Controllers;

/// <summary>
/// Public (anonymous) form endpoints — no [Authorize] required.
/// GET returns the Active template schema for embedding.
/// POST accepts anonymous submissions (rate-limited: 5/min per IP).
/// </summary>
[Route("api/v1/public/forms")]
[AllowAnonymous]
public sealed class PublicFormsController(ISender mediator, IHttpContextAccessor httpCtx) : ApiControllerBase
{
    /// <summary>Get an Active form template by its code (slug) — no auth required.</summary>
    [HttpGet("{code}")]
    [EnableRateLimiting("anonymous")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(429)]
    public async Task<IActionResult> GetForm(
        string code,
        CancellationToken cancellationToken = default) =>
        ToApiResponse(await mediator.Send(
            new GetPublicFormQuery(code), cancellationToken));

    /// <summary>Submit form data anonymously. Stored with SubmittedBy = Guid.Empty.</summary>
    [HttpPost("{code}/submit")]
    [EnableRateLimiting("public-form-submit")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(422)]
    [ProducesResponseType(429)]
    public async Task<IActionResult> Submit(
        string code,
        [FromBody] PublicSubmitRequest request,
        CancellationToken cancellationToken = default)
    {
        var ctx = httpCtx.HttpContext;
        var clientIp = ctx?.Connection.RemoteIpAddress?.ToString();
        var userAgent = ctx?.Request.Headers.UserAgent.ToString();

        return ToApiResponse(await mediator.Send(
            new SubmitPublicFormCommand(
                code, request.Data, request.ConsentGiven,
                clientIp, userAgent,
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
    }

    /// <summary>
    /// Compute the Mode-2 (ServerDocHash) document hash for an anonymous public form.
    /// The caller signs the returned <c>hashBase64</c>, then includes it in the Signature
    /// bundle when submitting via <c>POST {code}/submit</c>.
    /// Only valid for templates with an active DigitalSignature field in ServerDocHash mode.
    /// </summary>
    [HttpPost("{code}/signature-doc-hash")]
    [EnableRateLimiting("anonymous")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(422)]
    [ProducesResponseType(429)]
    public async Task<IActionResult> GetSignatureDocHash(
        string code,
        [FromBody] PublicSignatureDocHashRequest request,
        CancellationToken cancellationToken = default) =>
        ToApiResponse(await mediator.Send(
            new GetPublicSignatureDocHashQuery(code, request.Data),
            cancellationToken));

    /// <summary>
    /// Batch-evaluate every active Formula field of a public Active template against caller
    /// draft values — the same realtime compute path as the authenticated evaluate-formulas
    /// endpoint, reached anonymously so a public form can render computed fields while filling.
    /// Rate-limited (<c>formula-evaluate</c> policy) since it is an unauthenticated, high-frequency
    /// (realtime typing) surface.
    /// </summary>
    [HttpPost("{code}/evaluate-formulas")]
    [EnableRateLimiting("formula-evaluate")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(429)]
    public async Task<IActionResult> EvaluateFormulas(
        string code,
        [FromBody] PublicEvaluateFormulasRequest request,
        CancellationToken cancellationToken = default) =>
        ToApiResponse(await mediator.Send(
            new EvaluatePublicFormulasQuery(code, request.Data),
            cancellationToken));

    /// <summary>
    /// Get selectable options for one reference/dropdown field of a PUBLIC form, addressed by the
    /// form's <paramref name="code"/> plus the field's stable <paramref name="fieldKey"/> (never the
    /// internal PK). Cascade: pass parent values as <c>?p[filterParamName]=value</c> to filter the
    /// option-set (omitting <c>p</c> returns the full list). Anonymous surface — the field is scoped to
    /// the resolved template's own active fields and the tenant is server-resolved, so no cross-tenant
    /// field is addressable. Stricter rate-limit than formula-evaluate (a reference field can trigger an
    /// outbound external fetch).
    /// </summary>
    [HttpGet("{code}/fields/{fieldKey}/options")]
    [EnableRateLimiting("public-field-options")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(429)]
    public async Task<IActionResult> GetFieldOptions(
        string code,
        string fieldKey,
        [FromQuery] Dictionary<string, string>? p = null,
        CancellationToken cancellationToken = default) =>
        ToApiResponse(await mediator.Send(
            new GetPublicFormFieldOptionsQuery(
                code, fieldKey,
                p is { Count: > 0 } ? p : null),
            cancellationToken));
}

/// <summary>Public submit request body — data payload + consent flag.</summary>
public sealed record PublicSubmitRequest(
    Dictionary<string, object?> Data,
    bool ConsentGiven = false,
    SignaturePayloadRequest? Signature = null);

/// <summary>
/// Request body for the anonymous Mode-2 document-hash endpoint.
/// Contains the draft form data the caller intends to submit.
/// </summary>
public sealed record PublicSignatureDocHashRequest(
    Dictionary<string, object?> Data);

/// <summary>Request body for the anonymous batch formula-evaluate endpoint.</summary>
public sealed record PublicEvaluateFormulasRequest(
    Dictionary<string, object?> Data);
