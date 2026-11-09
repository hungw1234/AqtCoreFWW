using FluentResults;
using MediatR;

namespace AqtCoreFW.Forms.Application.Queries.GetSignatureDocHash;

/// <summary>
/// Returns the Mode-2 document hash for an authenticated user to sign at form-fill time.
///
/// The FE fetches this hash via <c>POST /api/v1/forms/{formTemplatePublicId}/signature-doc-hash</c>
/// with the draft form data in the body. The returned <c>HashBase64</c> is then passed directly
/// to the signing SDK as the payload to sign.
/// </summary>
/// <param name="TenantId">Caller's tenant — resolved server-side, never from the client.</param>
/// <param name="FormTemplatePublicId">T2 Guid AK for the template.</param>
/// <param name="Data">Draft form data (same dict that will be submitted).</param>
public sealed record GetSignatureDocHashQuery(
    Guid TenantId,
    Guid FormTemplatePublicId,
    Dictionary<string, object?> Data) : IRequest<Result<SignatureDocHashDto>>;

/// <summary>Response DTO carrying the document hash the user should sign.</summary>
/// <param name="HashBase64">Base64-encoded 32-byte SHA-256 of the Mode-2 wrapper.</param>
/// <param name="HashAlg">Hash algorithm identifier — always "SHA256" for Mode-2 v1.</param>
public sealed record SignatureDocHashDto(
    string HashBase64,
    string HashAlg = "SHA256");
