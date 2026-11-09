using AqtCoreFW.Forms.Application.Commands.SubmitForm;
using AqtCoreFW.Forms.Domain.Entities;
using AqtCoreFW.Forms.Domain.Enums;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;

namespace AqtCoreFW.Forms.Application.Services;

/// <summary>
/// Shared digital-signature verify-at-ingest + evidence-recording logic for the form-submit
/// command handlers (SubmitForm / SubmitPublicForm). Extracted to remove the duplicated QĐ-742
/// signing-path code; the two callers differ only in the TenantId source and whether an
/// authenticated submitter is bound — both passed as parameters, so behaviour is identical to the
/// previously inline code.
///
/// <para>Kept as a static helper (not an injectable service) so the handler constructors are
/// unchanged: callers still own the <see cref="IIdentitySigningClient"/> dependency and pass it in.</para>
/// </summary>
public static class FormSignatureIngest
{
    /// <summary>
    /// Resolves the (optional) DigitalSignature field, hard-blocks a missing-but-required signature
    /// (GOV_FRM_021), and — when a proof bundle is supplied — verifies it BEFORE any persistence
    /// (GOV_FRM_022 on failure, QĐ-742 all-or-nothing). Returns the verify result, or null when no
    /// signature applies, so the caller records evidence after creating the submission entity.
    /// </summary>
    /// <param name="signerUserId">
    /// The authenticated submitter to bind the signing cert against (anti-impersonation). Pass the
    /// submitter's id on the authenticated path; pass null on the anonymous public path (binding skipped).
    /// </param>
    public static async Task<Result<SignatureVerifyResult?>> VerifyAtIngestAsync(
        IIdentitySigningClient signingClient,
        FormTemplate template,
        IReadOnlyList<FormField> activeFields,
        IDictionary<string, object?> data,
        SignaturePayload? signature,
        Guid tenantId,
        Guid? signerUserId,
        CancellationToken ct)
    {
        var digSigField = activeFields.FirstOrDefault(f =>
            f.Type == FormFieldType.DigitalSignature && f.IsActive);

        // Hard-block: required DigitalSignature field but no proof bundle supplied — reject BEFORE
        // any persistence side-effect.
        if (digSigField is not null && digSigField.Required && signature is null)
            return Result.Fail<SignatureVerifyResult?>(new ValidationError(
                "GOV_FRM_021: Chữ ký số bắt buộc.", "Signature"));

        // No signature field on the template, or no bundle supplied → nothing to verify.
        if (digSigField is null || signature is null)
            return Result.Ok<SignatureVerifyResult?>(null);

        // Determine signing mode from the DigitalSignature field's ValidationRulesJson.
        // Default (absent or unparseable) = CanonicalJson (Mode-1) to preserve existing behaviour.
        // Mode-2 (ServerDocHash): Payload = 32-byte SHA-256 of the server-constructed document wrapper.
        var signedBytes = SignatureModeParser.Parse(digSigField.ValidationRulesJson) == SignatureMode.ServerDocHash
            ? FormDataCanonicalizer.ComputeMode2DocHash(template.Code, template.Version, data, activeFields)
            : FormDataCanonicalizer.CanonicalizeMode1(data, activeFields);

        var vr = await signingClient.VerifySignatureAsync(
            new SignatureVerifyRequest
            {
                Payload       = signedBytes,
                Signature     = signature.Signature,
                SignerCertDer = signature.SignerCertDer,
                Format        = signature.Format,
                HashAlg       = signature.HashAlg,
                Encode        = signature.Encode,
                ProviderKey   = signature.ProviderKey,
                TenantId      = tenantId,
                SignerUserId  = signerUserId
            },
            ct);

        if (!vr.IsValid)
            return Result.Fail<SignatureVerifyResult?>(new ValidationError(
                $"GOV_FRM_022: Xác minh chữ ký thất bại: {vr.FailureReason}", "Signature"));

        return Result.Ok<SignatureVerifyResult?>(vr);
    }

    /// <summary>
    /// Records verified signature evidence onto the submission before first persist. Multi-signer
    /// templates (SignerRoles.Count &gt; 1) route to the per-role child collection under the first
    /// required role; single-signer templates use the scalar evidence block (behaviour unchanged).
    /// </summary>
    public static void RecordEvidence(
        FormSubmission submission,
        FormTemplate template,
        SignatureVerifyResult vr,
        Guid? signerUserId)
    {
        if (template.SignerRoles.Count > 1)
        {
            // Multi-signer: record this submitter's evidence under the first required role.
            // Subsequent signers (roles 2..N) are appended by cross-module completion handlers.
            var firstRole = template.SignerRoles
                .OrderBy(r => r.DisplayOrder)
                .FirstOrDefault(r => r.IsRequired)
                ?? template.SignerRoles.OrderBy(r => r.DisplayOrder).First();

            var evidence = FormSubmissionSignerEvidence.Create(
                formSubmissionId: 0, // EF back-fill after SaveChanges; FK set via navigation
                signerRoleKey: firstRole.RoleKey,
                signatureVerdict: true,
                signerSubject: vr.SignerSubject,
                signerSerial: vr.SignerSerial,
                signatureVerifiedAt: vr.VerifiedAt,
                signatureChainEvidence: vr.ChainEvidenceJson,
                signerCertDer: vr.SignerCertDer,
                signerUserId: signerUserId == Guid.Empty ? null : signerUserId,
                signedAt: vr.VerifiedAt);

            submission.RecordSignerEvidence(evidence);
        }
        else
        {
            // Single-signer path: scalar evidence block — behaviour unchanged.
            submission.RecordSignatureEvidence(
                true,
                vr.SignerSubject,
                vr.SignerSerial,
                vr.VerifiedAt,
                vr.ChainEvidenceJson,
                vr.SignerCertDer);
        }
    }
}
