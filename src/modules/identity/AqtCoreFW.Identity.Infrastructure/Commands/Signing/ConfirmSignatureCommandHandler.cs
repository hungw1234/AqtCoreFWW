using System.Text.Json;
using AqtCoreFW.Identity.Application.Signing;
using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.Identity.Domain.Services;
using AqtCoreFW.Identity.Infrastructure.Services.Signing;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Identity.Infrastructure.Commands.Signing;

/// <summary>
/// Handles <see cref="ConfirmSignatureCommand"/> — queries Viettel Mysign /requests/status
/// for a previously deferred async transaction and returns the current signing state.
///
/// <para>Lives in Infrastructure (not Application) because it has a direct dependency on
/// <see cref="ViettelMysignProvider"/> for the async-poll confirm method. Application layer
/// holds only the command record and validator.</para>
///
/// TODO(W6): When async-callback lands and a second polling provider exists, extract
/// <c>IPollingSignatureProvider</c> to Domain and move the handler back to Application.
/// </summary>
public sealed class ConfirmSignatureCommandHandler(
    ITenantSignatureConfigRepository repository,
    ISignatureProviderResolver resolver,
    IAuditLogger auditLogger,
    ISignerBindingGuard signerBindingGuard)
    : IRequestHandler<ConfirmSignatureCommand, Result<SignPayloadResultDto>>
{
    public async Task<Result<SignPayloadResultDto>> Handle(
        ConfirmSignatureCommand cmd, CancellationToken ct)
    {
        // Load tenant config — same guard as SignPayloadCommandHandler.
        var cfg = await repository.GetByTenantIdAsync(cmd.TenantId, ct);
        if (cfg is null)
            return Result.Fail(new NotFoundError(
                $"Signature provider not configured for tenant {cmd.TenantId}."));

        if (!cfg.IsEnabled)
            return Result.Fail(new ConflictError(
                "Digital signature integration is disabled for this tenant."));

        // Resolve provider.
        IDigitalSignatureProvider provider;
        try
        {
            provider = resolver.Resolve(cfg.ProviderKey);
        }
        catch (KeyNotFoundException ex)
        {
            return Result.Fail(new Error(ex.Message));
        }

        // Only ViettelMysignProvider supports async-poll confirm.
        if (provider is not ViettelMysignProvider mysignProvider)
        {
            return Result.Fail(new ConflictError(
                $"Provider '{cfg.ProviderKey}' does not support async transaction confirmation."));
        }

        var signResult = await mysignProvider.ConfirmAsync(cmd.TransactionId, cmd.UserId, ct);

        // Anti-impersonation: when the deferred transaction completes, the certificate it signed with
        // MUST belong to the assigned signer — same binding the synchronous path enforces post-sign.
        if (signResult.IsSuccess && !signResult.IsPending
            && signResult.SigningCertificate is not null)
        {
            // A completed deferred signature came back with a certificate — bind it or fail closed.
            if (!Guid.TryParse(cmd.UserId, out var signerGuid))
                return Result.Fail(new BadRequestError(
                    "Signer id is not resolvable — cannot bind the signing certificate.",
                    "SIGNER_IDENTITY_UNRESOLVED"));

            var verdict = await signerBindingGuard.VerifyAsync(
                signResult.SigningCertificate, signerGuid, cmd.TenantId, ct);
            if (!verdict.IsBound)
            {
                await auditLogger.LogCommandAsync(
                    action:            "SIGN_CONFIRM_BINDING_REJECTED",
                    moduleName:        "Identity",
                    resourceType:      "DigitalSignature",
                    resourceId:        cmd.TenantId.ToString(),
                    commandType:       typeof(ConfirmSignatureCommand),
                    dataSnapshot:      JsonSerializer.Serialize(new
                    {
                        providerKey   = cfg.ProviderKey,
                        transactionId = cmd.TransactionId,
                        certSerial    = signResult.SigningCertificate.SerialNumber,
                        certSubject   = signResult.SigningCertificate.Subject,
                        userId        = cmd.UserId,
                        tenantId      = cmd.TenantId.ToString(),
                        reason        = verdict.Reason,
                    }),
                    cancellationToken: ct);

                return Result.Fail(new BadRequestError(
                    $"Signing certificate is not bound to the assigned signer: {verdict.Reason}",
                    "SIGNER_CERT_MISMATCH"));
            }
        }

        // Structured audit — mirrors SignPayloadCommandHandler snapshot shape.
        var outcome = signResult.IsPending ? "pending"
            : signResult.IsSuccess         ? "success"
            :                                "failure";

        var snapshot = JsonSerializer.Serialize(new
        {
            providerKey   = cfg.ProviderKey,
            transactionId = cmd.TransactionId,
            certSerial    = signResult.SigningCertificate?.SerialNumber,
            certSubject   = signResult.SigningCertificate?.Subject,
            isPending     = signResult.IsPending,
            outcome,
            failureReason = signResult.FailureReason,
            userId        = cmd.UserId,
            tenantId      = cmd.TenantId.ToString(),
        });

        var auditAction = outcome == "failure" ? "SIGN_CONFIRM_FAILED" : "SIGN_PAYLOAD_CONFIRMED";

        await auditLogger.LogCommandAsync(
            action:            auditAction,
            moduleName:        "Identity",
            resourceType:      "DigitalSignature",
            resourceId:        cmd.TenantId.ToString(),
            commandType:       typeof(ConfirmSignatureCommand),
            dataSnapshot:      snapshot,
            cancellationToken: ct);

        return Result.Ok(new SignPayloadResultDto(
            signResult.IsSuccess,
            signResult.IsPending,
            signResult.Signature,
            signResult.TransactionId,
            signResult.FailureReason,
            signResult.SigningCertificate));
    }
}
