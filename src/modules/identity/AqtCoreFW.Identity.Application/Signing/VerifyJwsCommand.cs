using System.Security.Cryptography;
using System.Text;
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
/// Verifies a JWS compact serialization for the given tenant.
/// The signing certificate is embedded in the JWS <c>x5c</c> header claim.
/// OcspMode override: null falls back to tenant config value.
///
/// [SuppressAutoAudit]: handler emits structured audit with the same field set
/// as <see cref="VerifySignatureCommand"/>. Actions: SIGNATURE_VERIFIED / SIGNATURE_VERIFICATION_FAILED.
/// </summary>
[AuditCategory(AuditCategory.AppAccess)]
[SuppressAutoAudit]
public sealed record VerifyJwsCommand(
    Guid TenantId,
    string JwsCompact,
    OcspMode? OcspModeOverride) : ICommand<VerifySignatureResultDto>;

public sealed class VerifyJwsCommandValidator : AbstractValidator<VerifyJwsCommand>
{
    public VerifyJwsCommandValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.JwsCompact)
            .NotEmpty()
            .WithMessage("JwsCompact must not be empty.")
            .Must(s => s.Split('.').Length == 3)
            .WithMessage("JwsCompact must have exactly 3 segments (header.payload.signature).");
    }
}

public sealed class VerifyJwsCommandHandler(
    ITenantSignatureConfigRepository repository,
    IStructuredSignatureService structuredService,
    IAuditLogger auditLogger)
    : IRequestHandler<VerifyJwsCommand, Result<VerifySignatureResultDto>>
{
    public async Task<Result<VerifySignatureResultDto>> Handle(
        VerifyJwsCommand cmd, CancellationToken ct)
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

        // Verify JWS — cert is embedded in x5c header; no provider resolution needed
        var verifyResult = await structuredService.VerifyJwsAsync(
            cmd.JwsCompact, ocspMode, ct).ConfigureAwait(false);

        // Structured audit entry — payloadHash computed over UTF-8 bytes of the compact string
        var payloadHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(cmd.JwsCompact))).ToLowerInvariant();
        var outcome = verifyResult.IsValid ? "valid" : "invalid";

        var snapshot = JsonSerializer.Serialize(new
        {
            providerKey   = cfg.ProviderKey,
            certSerial    = verifyResult.SigningCertificate?.SerialNumber,
            certSubject   = verifyResult.SigningCertificate?.Subject,
            payloadHash,
            // signatureHash not computable separately — embedded in JWS compact; payloadHash covers full token
            signatureHash = (string?)null,
            outcome,
            failureReason = verifyResult.FailureReason,
            ocspMode      = ocspMode.ToString(),
            ocspChecked   = verifyResult.ChainResult?.OcspChecked,
            ocspSucceeded = verifyResult.ChainResult?.OcspSucceeded,
            format        = "Jws",
            tenantId      = cmd.TenantId.ToString(),
        });

        var auditAction = verifyResult.IsValid ? "SIGNATURE_VERIFIED" : "SIGNATURE_VERIFICATION_FAILED";

        await auditLogger.LogCommandAsync(
            action:       auditAction,
            moduleName:   "Identity",
            resourceType: "DigitalSignature",
            resourceId:   cmd.TenantId.ToString(),
            commandType:  typeof(VerifyJwsCommand),
            dataSnapshot: snapshot,
            cancellationToken: ct);

        return Result.Ok(new VerifySignatureResultDto(
            verifyResult.IsValid,
            verifyResult.FailureReason,
            verifyResult.SigningCertificate,
            verifyResult.ChainResult));
    }
}
