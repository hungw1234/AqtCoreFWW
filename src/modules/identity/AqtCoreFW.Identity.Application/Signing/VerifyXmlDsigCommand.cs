using System.Security.Cryptography;
using System.Text.Json;
using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.Identity.Domain.Services;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using FluentValidation;
using MediatR;

namespace AqtCoreFW.Identity.Application.Signing;

/// <summary>
/// Verifies an XMLDSig-signed XML document for the given tenant.
/// The signing certificate is embedded in the document's <c>KeyInfo/X509Data</c> node.
/// OcspMode override: caller can override to <c>Hard</c>; null falls back to tenant config value.
///
/// [SuppressAutoAudit]: handler emits structured audit with same fields as <see cref="VerifySignatureCommand"/>
/// (providerKey, certSerial, certSubject, payloadHash, signatureHash, outcome, failureReason, ocspMode,
/// ocspChecked, ocspSucceeded, format, tenantId). Actions: SIGNATURE_VERIFIED / SIGNATURE_VERIFICATION_FAILED.
/// </summary>
[AuditCategory(AuditCategory.AppAccess)]
[SuppressAutoAudit]
public sealed record VerifyXmlDsigCommand(
    Guid TenantId,
    byte[] XmlDsigBytes,
    OcspMode? OcspModeOverride) : ICommand<VerifySignatureResultDto>;

public sealed class VerifyXmlDsigCommandValidator : AbstractValidator<VerifyXmlDsigCommand>
{
    public VerifyXmlDsigCommandValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.XmlDsigBytes)
            .NotEmpty()
            .WithMessage("XmlDsigBytes must not be empty.");
    }
}

public sealed class VerifyXmlDsigCommandHandler(
    ITenantSignatureConfigRepository repository,
    IStructuredSignatureService structuredService,
    IAuditLogger auditLogger)
    : IRequestHandler<VerifyXmlDsigCommand, Result<VerifySignatureResultDto>>
{
    public async Task<Result<VerifySignatureResultDto>> Handle(
        VerifyXmlDsigCommand cmd, CancellationToken ct)
    {
        // Load tenant config — needed for providerKey (audit) + OcspMode fallback
        var cfg = await repository.GetByTenantIdAsync(cmd.TenantId, ct);
        if (cfg is null)
            return Result.Fail(new NotFoundError(
                $"Signature provider not configured for tenant {cmd.TenantId}."));

        if (!cfg.IsEnabled)
            return Result.Fail(new ConflictError(
                "Digital signature integration is disabled for this tenant."));

        var ocspMode = cmd.OcspModeOverride ?? cfg.OcspMode;

        // Verify XMLDSig document — cert is embedded; no provider resolution needed
        var verifyResult = await structuredService.VerifyXmlDsigAsync(
            cmd.XmlDsigBytes, ocspMode, ct).ConfigureAwait(false);

        // Structured audit entry — same field set as VerifySignatureCommand for auditor grep consistency
        var payloadHash = Convert.ToHexString(SHA256.HashData(cmd.XmlDsigBytes)).ToLowerInvariant();
        var outcome     = verifyResult.IsValid ? "valid" : "invalid";

        var snapshot = JsonSerializer.Serialize(new
        {
            providerKey   = cfg.ProviderKey,
            certSerial    = verifyResult.SigningCertificate?.SerialNumber,
            certSubject   = verifyResult.SigningCertificate?.Subject,
            payloadHash,
            // signatureHash not computable separately — embedded in document; payloadHash covers the signed doc
            signatureHash = (string?)null,
            outcome,
            failureReason = verifyResult.FailureReason,
            ocspMode      = ocspMode.ToString(),
            ocspChecked   = verifyResult.ChainResult?.OcspChecked,
            ocspSucceeded = verifyResult.ChainResult?.OcspSucceeded,
            format        = "XmlDsig",
            tenantId      = cmd.TenantId.ToString(),
        });

        var auditAction = verifyResult.IsValid ? "SIGNATURE_VERIFIED" : "SIGNATURE_VERIFICATION_FAILED";

        await auditLogger.LogCommandAsync(
            action:       auditAction,
            moduleName:   "Identity",
            resourceType: "DigitalSignature",
            resourceId:   cmd.TenantId.ToString(),
            commandType:  typeof(VerifyXmlDsigCommand),
            dataSnapshot: snapshot,
            cancellationToken: ct);

        return Result.Ok(new VerifySignatureResultDto(
            verifyResult.IsValid,
            verifyResult.FailureReason,
            verifyResult.SigningCertificate,
            verifyResult.ChainResult));
    }
}
