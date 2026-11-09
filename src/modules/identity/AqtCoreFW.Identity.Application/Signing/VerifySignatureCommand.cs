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
/// Verifies a digital signature against a payload using the tenant's configured provider.
/// OcspMode is per-request — callers can override at call time (e.g. Soft for user-facing,
/// Hard for compliance validation). Tenant's TenantSignatureConfig.OcspMode is the default
/// when the caller does not specify (null → use tenant config value).
/// AuditCategory.AppAccess — verify is a read/validation action, not a config mutation.
///
/// [SuppressAutoAudit]: handler emits structured audit including providerKey, certSerial,
/// certSubject, payloadHash, signatureHash, outcome, ocspMode, ocspChecked, ocspSucceeded,
/// format, encode, hashAlg per QĐ 742 spec lines 53 + 373.
/// Severity.Info on valid; Severity.Warning on invalid.
///
/// <para><b>W1 new fields:</b>
/// <list type="bullet">
///   <item><term><see cref="SignerCertBase64"/></term>
///     <description>Base64-encoded DER certificate from VGCA <c>CertBase64</c> callback field.
///     Required when <see cref="Format"/> is <see cref="SignatureFormat.Raw"/>; null for Pkcs7.</description></item>
///   <item><term><see cref="HashAlg"/></term><description>Hash algorithm (default "SHA256").</description></item>
///   <item><term><see cref="Encode"/></term><description>Asymmetric algorithm (default "RSA").</description></item>
///   <item><term><see cref="Format"/></term>
///     <description>Signature binary layout — default <see cref="SignatureFormat.Pkcs7"/> preserves
///     backward compat with all existing callers.</description></item>
/// </list>
/// </para>
/// </summary>
[AuditCategory(AuditCategory.AppAccess)]
[SuppressAutoAudit]
public sealed record VerifySignatureCommand(
    Guid TenantId,
    byte[] Payload,
    byte[] Signature,
    OcspMode? OcspModeOverride,
    string? SignerCertBase64 = null,
    string HashAlg = "SHA256",
    string Encode = "RSA",
    SignatureFormat Format = SignatureFormat.Pkcs7) : ICommand<VerifySignatureResultDto>;

public sealed class VerifySignatureCommandValidator : AbstractValidator<VerifySignatureCommand>
{
    public VerifySignatureCommandValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.Payload).NotEmpty().WithMessage("Payload must not be empty.");
        RuleFor(x => x.Signature).NotEmpty().WithMessage("Signature bytes must not be empty.");
    }
}

public sealed class VerifySignatureCommandHandler(
    ITenantSignatureConfigRepository repository,
    ISignatureProviderResolver resolver,
    IAuditLogger auditLogger)
    : IRequestHandler<VerifySignatureCommand, Result<VerifySignatureResultDto>>
{
    public async Task<Result<VerifySignatureResultDto>> Handle(
        VerifySignatureCommand cmd, CancellationToken ct)
    {
        // Load tenant config — needed for ProviderKey + fallback OcspMode
        var cfg = await repository.GetByTenantIdAsync(cmd.TenantId, ct);
        if (cfg is null)
            return Result.Fail(new NotFoundError(
                $"Signature provider not configured for tenant {cmd.TenantId}."));

        if (!cfg.IsEnabled)
            return Result.Fail(new ConflictError(
                "Digital signature integration is disabled for this tenant."));

        // Resolve provider
        IDigitalSignatureProvider provider;
        try
        {
            provider = resolver.Resolve(cfg.ProviderKey);
        }
        catch (KeyNotFoundException ex)
        {
            return Result.Fail(new Error(ex.Message));
        }

        // OcspMode: use caller override if supplied, else fall back to tenant config
        var ocspMode = cmd.OcspModeOverride ?? cfg.OcspMode;

        // Decode signer cert for Raw format (W1 Tier-1)
        byte[]? signerCertDer = null;
        if (cmd.SignerCertBase64 is not null)
        {
            try
            {
                signerCertDer = Convert.FromBase64String(cmd.SignerCertBase64);
            }
            catch (FormatException)
            {
                return Result.Fail(new Error("SignerCertBase64 is not valid base64."));
            }
        }

        var verifyRequest = new VerifyRequest(
            Payload: cmd.Payload,
            Signature: cmd.Signature,
            OcspMode: ocspMode,
            SignerCertDer: signerCertDer,
            HashAlg: cmd.HashAlg,
            Encode: cmd.Encode,
            Format: cmd.Format);

        var verifyResult = await provider.VerifyAsync(verifyRequest, ct);

        // Structured audit entry per QĐ 742 spec lines 53 + 373.
        // payloadHash + signatureHash are SHA-256 fingerprints — no raw bytes in audit log.
        // format/encode/hashAlg included per W1 extension for compliance trail.
        var payloadHash    = Convert.ToHexString(SHA256.HashData(cmd.Payload)).ToLowerInvariant();
        var signatureHash  = Convert.ToHexString(SHA256.HashData(cmd.Signature)).ToLowerInvariant();
        var outcome        = verifyResult.IsValid ? "valid" : "invalid";

        var snapshot = JsonSerializer.Serialize(new
        {
            providerKey         = cfg.ProviderKey,
            certSerial          = verifyResult.SigningCertificate?.SerialNumber,
            certSubject         = verifyResult.SigningCertificate?.Subject,
            payloadHash,
            signatureHash,
            outcome,
            failureReason       = verifyResult.FailureReason,
            ocspMode            = ocspMode.ToString(),
            ocspChecked         = verifyResult.ChainResult?.OcspChecked,
            ocspSucceeded       = verifyResult.ChainResult?.OcspSucceeded,
            format              = cmd.Format.ToString(),
            encode              = cmd.Encode,
            hashAlg             = cmd.HashAlg,
            tenantId            = cmd.TenantId.ToString(),
        });

        // Warning on invalid signature; Info on valid — consistent with spec §5.1 evidence level.
        var auditAction = verifyResult.IsValid ? "SIGNATURE_VERIFIED" : "SIGNATURE_VERIFICATION_FAILED";

        await auditLogger.LogCommandAsync(
            action:       auditAction,
            moduleName:   "Identity",
            resourceType: "DigitalSignature",
            resourceId:   cmd.TenantId.ToString(),
            commandType:  typeof(VerifySignatureCommand),
            dataSnapshot: snapshot,
            cancellationToken: ct);

        return Result.Ok(new VerifySignatureResultDto(
            verifyResult.IsValid,
            verifyResult.FailureReason,
            verifyResult.SigningCertificate,
            verifyResult.ChainResult));
    }
}
