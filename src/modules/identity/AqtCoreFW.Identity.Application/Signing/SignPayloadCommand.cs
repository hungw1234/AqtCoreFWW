using System.Security.Cryptography;
using System.Text.Json;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.Identity.Domain.Services;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Identity;

namespace AqtCoreFW.Identity.Application.Signing;

/// <summary>
/// Dispatches a server-side sign request to the tenant's configured IDigitalSignatureProvider.
/// Returns 503-equivalent failure when the provider lacks ServerSideSigning capability (e.g. USB token).
/// AuditCategory.AppAccess — signing is a user action, not an admin config change.
///
/// [SuppressAutoAudit]: handler emits structured audit including providerKey, certSerial,
/// certSubject, SHA-256 payloadHash, isPending, outcome per QĐ 742 spec lines 53 + 339.
/// Severity.Info on success/pending; Severity.Warning on provider failure.
/// </summary>
/// <param name="SkipBindingGuard">
/// When true, the post-sign anti-impersonation <see cref="ISignerBindingGuard"/> check is skipped.
/// This is for the ENROLLMENT possession-proof path only: proving possession is what CREATES the
/// binding, so requiring the cert to already be bound is circular (the signer has no approved
/// enrollment for the cert being enrolled → the guard fail-closes and enrollment can never succeed).
/// Attribution is still enforced upstream — the sign is issued against the caller's own
/// SigningNationalId — and the resulting enrollment is recorded Pending for admin approval, so
/// skipping the guard here opens no impersonation path. Defaults to false: normal signing keeps the guard.
/// </param>
[AuditCategory(AuditCategory.AppAccess)]
[SuppressAutoAudit]
public sealed record SignPayloadCommand(
    Guid TenantId,
    byte[] Payload,
    string UserId,
    string? Reason,
    Dictionary<string, string>? ExtraParameters,
    bool SkipBindingGuard = false) : ICommand<SignPayloadResultDto>;

public sealed class SignPayloadCommandValidator : AbstractValidator<SignPayloadCommand>
{
    public SignPayloadCommandValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.Payload).NotEmpty().WithMessage("Payload must not be empty.");
        RuleFor(x => x.UserId).NotEmpty();
    }
}

public sealed class SignPayloadCommandHandler(
    ITenantSignatureConfigRepository repository,
    ISignatureProviderResolver resolver,
    IAuditLogger auditLogger,
    UserManager<ApplicationUser> userManager,
    ISignerBindingGuard signerBindingGuard)
    : IRequestHandler<SignPayloadCommand, Result<SignPayloadResultDto>>
{
    // Provider-param keys that are SERVER-SET and must never be honored from the public /sign body:
    // mode-selection (admin-controlled) and identity attribution (anti-impersonation). Stripped from
    // caller-supplied ExtraParameters before the server sets its authoritative values.
    private static readonly string[] ReservedProviderParamKeys =
        ["mysignMode", "signFlowMode", "userId", "tenantId"];

    public async Task<Result<SignPayloadResultDto>> Handle(
        SignPayloadCommand cmd, CancellationToken ct)
    {
        // Load tenant config — 503 when not configured or disabled
        var cfg = await repository.GetByTenantIdAsync(cmd.TenantId, ct);
        if (cfg is null)
            return Result.Fail(new NotFoundError(
                $"Signature provider not configured for tenant {cmd.TenantId}."));

        if (!cfg.IsEnabled)
            return Result.Fail(new ConflictError(
                "Digital signature integration is disabled for this tenant."));

        // Resolve provider — throws KeyNotFoundException if key is unknown
        IDigitalSignatureProvider provider;
        try
        {
            provider = resolver.Resolve(cfg.ProviderKey);
        }
        catch (KeyNotFoundException ex)
        {
            return Result.Fail(new Error(ex.Message));
        }

        // Reject providers that require FE-side signing (e.g. USB token)
        if (!provider.Capabilities.HasFlag(SignatureProviderCapabilities.ServerSideSigning))
            return Result.Fail(new ConflictError(
                $"Provider '{cfg.ProviderKey}' uses client-side signing — " +
                "call the FE plugin directly and use /verify to validate the result."));

        // Resolve the signer's CA-side identity (CCCD) for the provider's user_id.
        // cmd.UserId is the internal user Guid (the audit actor); remote providers
        // (Viettel Mysign) are keyed on the national-id, not the Guid. Fail closed when the signer
        // has no national id: the provider's configured default-account fallback MUST NOT attribute
        // a signature to the wrong identity on a user-attributed signing path (anti-impersonation).
        var signer = await userManager.FindByIdAsync(cmd.UserId);
        var signerNationalId = signer?.SigningNationalId;
        if (string.IsNullOrWhiteSpace(signerNationalId))
            return Result.Fail(new BadRequestError(
                "Signer has no SigningNationalId — a remote signature cannot be attributed to this user.",
                "SIGNER_IDENTITY_UNRESOLVED"));

        // Build the provider parameter bag. These are SERVER-SET (authoritative), not free-form client input:
        // the public /sign body must not be able to pick the signing mode (admin-controlled via the tenant
        // SignFlowMode) nor spoof the correlation identity used to attribute an async-callback pending tx.
        // Copy any caller params first, but STRIP the reserved control keys — a client must not be able to
        // force the provider mode or impersonate a signer/tenant by smuggling them through ExtraParameters.
        var providerParams = cmd.ExtraParameters is not null
            ? new Dictionary<string, string>(cmd.ExtraParameters)
            : new Dictionary<string, string>();
        foreach (var reserved in ReservedProviderParamKeys)
            providerParams.Remove(reserved);

        // Correlation for the async-callback (W6) pending tx — ignored by sync/async-poll modes.
        // userId is the internal Guid (cmd.UserId), NOT the CCCD: this fixes the Guid.Empty attribution bug
        // (the provider previously parsed the CCCD-bearing SignRequest.UserId as a Guid and always got Empty).
        providerParams["tenantId"] = cmd.TenantId.ToString();
        providerParams["userId"]   = cmd.UserId;

        // Mode selection is admin-controlled via the tenant SignFlowMode. Only the callback flow overrides the
        // provider's configured DefaultMode; Async/Sync are left untouched so the generic sign path keeps its
        // existing behaviour (no silent repurposing). The provider maps signFlowMode → MysignSignMode.
        if (cfg.SignFlowMode == SignatureFlowMode.AsyncCallback)
            providerParams["signFlowMode"] = nameof(SignatureFlowMode.AsyncCallback);

        var signRequest = new SignRequest(
            Payload: cmd.Payload,
            UserId: signerNationalId,
            Reason: cmd.Reason,
            ExtraParameters: providerParams);

        var signResult = await provider.SignAsync(signRequest, ct);

        // Anti-impersonation: the certificate the provider signed with MUST belong to the assigned
        // signer. Pending results carry no certificate yet — the bind runs at confirm time instead.
        // Skipped for the enrollment possession-proof (SkipBindingGuard): the proof is what establishes
        // the binding, so the guard — which requires an already-approved enrollment — would be circular.
        if (!cmd.SkipBindingGuard
            && signResult.IsSuccess && !signResult.IsPending
            && signResult.SigningCertificate is not null)
        {
            // A signature came back with a certificate — it MUST be bound to the assigned signer.
            // An unparseable signer id cannot be bound → fail closed (never default to allow).
            if (!Guid.TryParse(cmd.UserId, out var signerGuid))
                return Result.Fail(new BadRequestError(
                    "Signer id is not resolvable — cannot bind the signing certificate.",
                    "SIGNER_IDENTITY_UNRESOLVED"));

            var verdict = await signerBindingGuard.VerifyAsync(
                signResult.SigningCertificate, signerGuid, cmd.TenantId, ct);
            if (!verdict.IsBound)
            {
                await auditLogger.LogCommandAsync(
                    action:            "SIGN_PAYLOAD_BINDING_REJECTED",
                    moduleName:        "Identity",
                    resourceType:      "DigitalSignature",
                    resourceId:        cmd.TenantId.ToString(),
                    commandType:       typeof(SignPayloadCommand),
                    dataSnapshot:      JsonSerializer.Serialize(new
                    {
                        providerKey = cfg.ProviderKey,
                        certSerial  = signResult.SigningCertificate.SerialNumber,
                        certSubject = signResult.SigningCertificate.Subject,
                        userId      = cmd.UserId,
                        tenantId    = cmd.TenantId.ToString(),
                        reason      = verdict.Reason,
                    }),
                    cancellationToken: ct);

                return Result.Fail(new BadRequestError(
                    $"Signing certificate is not bound to the assigned signer: {verdict.Reason}",
                    "SIGNER_CERT_MISMATCH"));
            }
        }

        // Structured audit entry per QĐ 742 spec lines 53 + 339.
        // outcome: "success" | "pending" | "failure"
        // Severity: Warning on failure (provider returned IsSuccess=false); Info otherwise.
        var outcome = signResult.IsPending ? "pending"
            : signResult.IsSuccess         ? "success"
            :                                "failure";

        var payloadHash = Convert.ToHexString(SHA256.HashData(cmd.Payload)).ToLowerInvariant();

        var snapshot = JsonSerializer.Serialize(new
        {
            providerKey  = cfg.ProviderKey,
            certSerial   = signResult.SigningCertificate?.SerialNumber,
            certSubject  = signResult.SigningCertificate?.Subject,
            payloadHash,
            isPending    = signResult.IsPending,
            outcome,
            failureReason = signResult.FailureReason,
            userId       = cmd.UserId,
            tenantId     = cmd.TenantId.ToString(),
        });

        // Warning on failure so ops can triage; Info on success/pending.
        // AuditCategoryMapper maps "SIGN_FAILURE" → Low (default) — we use action name
        // "PAYLOAD_SIGNED" for normal flow; failure branch logs separately for visibility.
        var auditAction = outcome == "failure" ? "SIGN_PAYLOAD_FAILED" : "SIGN_PAYLOAD";

        await auditLogger.LogCommandAsync(
            action:       auditAction,
            moduleName:   "Identity",
            resourceType: "DigitalSignature",
            resourceId:   cmd.TenantId.ToString(),
            commandType:  typeof(SignPayloadCommand),
            dataSnapshot: snapshot,
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
