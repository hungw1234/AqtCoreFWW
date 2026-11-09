using AqtCoreFW.Forms.Application.Services;
using AqtCoreFW.Forms.Domain.Enums;
using AqtCoreFW.Forms.Domain.Repositories;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Forms.Application.Queries.GetSignatureDocHash;

/// <summary>
/// Returns the Mode-2 document hash for the authenticated sign-time endpoint.
///
/// Loads the template (with fields) exactly as <c>SubmitFormCommandHandler</c> does,
/// finds the active DigitalSignature field, verifies it is in ServerDocHash mode,
/// then computes and returns the 32-byte SHA-256 doc hash as Base64.
///
/// The FE uses this hash as the payload it passes to the signing SDK. At ingest time,
/// <c>SubmitFormCommandHandler</c> re-runs <c>ComputeMode2DocHash</c> over the submitted
/// data and verifies the signature over those same bytes — determinism is the invariant.
/// </summary>
public sealed class GetSignatureDocHashQueryHandler(
    IFormTemplateRepository templateRepository)
    : IRequestHandler<GetSignatureDocHashQuery, Result<SignatureDocHashDto>>
{
    public async Task<Result<SignatureDocHashDto>> Handle(
        GetSignatureDocHashQuery request,
        CancellationToken cancellationToken)
    {
        var template = await templateRepository.GetByPublicIdWithFieldsAsync(
            request.FormTemplatePublicId, request.TenantId, cancellationToken);

        if (template is null || template.Status != FormTemplateStatus.Active)
            return Result.Fail(new NotFoundError(
                $"Form template '{request.FormTemplatePublicId}' không tìm thấy hoặc chưa active."));

        var activeFields = template.Fields.Where(f => f.IsActive).ToList();

        var digSigField = activeFields.FirstOrDefault(f =>
            f.Type == FormFieldType.DigitalSignature && f.IsActive);

        if (digSigField is null)
            return Result.Fail(new ValidationError(
                "Template này không có trường chữ ký số.", "formTemplatePublicId"));

        // Only ServerDocHash fields have a server-computed document hash.
        // CanonicalJson fields are signed by the FE directly (no server hash fetch needed).
        if (SignatureModeParser.Parse(digSigField.ValidationRulesJson) != SignatureMode.ServerDocHash)
            return Result.Fail(new ValidationError(
                "Trường chữ ký số không sử dụng chế độ ServerDocHash.", "formTemplatePublicId"));

        var hashBytes = FormDataCanonicalizer.ComputeMode2DocHash(
            template.Code,
            template.Version,
            request.Data,
            activeFields);

        return Result.Ok(new SignatureDocHashDto(
            HashBase64: Convert.ToBase64String(hashBytes),
            HashAlg:    "SHA256"));
    }
}
