using FluentResults;
using MediatR;

namespace AqtCoreFW.Forms.Application.Queries.GetSignatureDocHash;

/// <summary>
/// Returns the Mode-2 document hash for an anonymous (public) user to sign at form-fill time.
///
/// Mirrors <see cref="GetSignatureDocHashQuery"/> but resolves the template by Code (slug)
/// instead of by PublicId + TenantId, matching the public submit path used by
/// <c>SubmitPublicFormCommandHandler</c>.
/// </summary>
/// <param name="TemplateCode">Public form code (slug) — same identifier used by <c>GET /api/v1/public/forms/{code}</c>.</param>
/// <param name="Data">Draft form data (same dict that will be submitted).</param>
public sealed record GetPublicSignatureDocHashQuery(
    string TemplateCode,
    Dictionary<string, object?> Data) : IRequest<Result<SignatureDocHashDto>>;
