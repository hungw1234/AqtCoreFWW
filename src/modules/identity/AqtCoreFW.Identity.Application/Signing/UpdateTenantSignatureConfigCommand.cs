using System.Text.Json;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.Identity.Domain.Services;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;
using FluentResults;
using FluentValidation;
using MediatR;

// ActorId is passed from the controller via the command — ICurrentUser not needed in this handler.

namespace AqtCoreFW.Identity.Application.Signing;

/// <summary>
/// Upserts the tenant's digital signature provider config (QĐ 742 §5.1).
/// Creates on first call; updates on subsequent calls (singleton per tenant).
/// AuditCategory.ConfigChange — every write is a config-change audit event.
///
/// [SuppressAutoAudit]: handler emits structured audit with diff (old→new ProviderKey,
/// AllowedCaRootThumbprints changes). AllowedCaRootThumbprints change → Severity.Critical.
/// </summary>
[AuditCategory(AuditCategory.ConfigChange)]
[SuppressAutoAudit]
public sealed record UpdateTenantSignatureConfigCommand(
    Guid TenantId,
    string ProviderKey,
    string? ApiCredentialsRef,
    string AllowedCaRootThumbprints,
    OcspMode OcspMode,
    bool IsEnabled,
    SignatureFlowMode SignFlowMode,
    Guid ActorId,
    byte[]? RowVersion = null) : ICommand;

public sealed class UpdateTenantSignatureConfigCommandValidator
    : AbstractValidator<UpdateTenantSignatureConfigCommand>
{
    public UpdateTenantSignatureConfigCommandValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.ProviderKey)
            .NotEmpty()
            .MaximumLength(64);
        RuleFor(x => x.ApiCredentialsRef)
            .MaximumLength(256)
            .When(x => x.ApiCredentialsRef is not null);
        RuleFor(x => x.AllowedCaRootThumbprints)
            .MaximumLength(2048);
        RuleFor(x => x.ActorId).NotEmpty();
    }
}

public sealed class UpdateTenantSignatureConfigCommandHandler(
    ITenantSignatureConfigRepository repository,
    IAuditLogger auditLogger)
    : IRequestHandler<UpdateTenantSignatureConfigCommand, Result>
{
    public async Task<Result> Handle(
        UpdateTenantSignatureConfigCommand cmd, CancellationToken ct)
    {
        var existing = await repository.GetByTenantIdAsync(cmd.TenantId, ct);

        if (existing is null)
        {
            var created = TenantSignatureConfig.Create(
                cmd.TenantId,
                cmd.ProviderKey,
                cmd.ApiCredentialsRef,
                cmd.AllowedCaRootThumbprints,
                cmd.OcspMode,
                cmd.IsEnabled,
                cmd.SignFlowMode,
                cmd.ActorId);
            await repository.AddAsync(created, ct);

            // New config — emit ConfigChange audit, Info-level action.
            await EmitAuditAsync(
                action: "SIGNATURE_CONFIG_CREATED",
                tenantId: cmd.TenantId,
                providerKeyOld: null,
                providerKeyNew: cmd.ProviderKey,
                rootThumbprintsOld: null,
                rootThumbprintsNew: cmd.AllowedCaRootThumbprints,
                ocspModeOld: null,
                ocspModeNew: cmd.OcspMode,
                isEnabledOld: null,
                isEnabledNew: cmd.IsEnabled,
                signFlowModeOld: null,
                signFlowModeNew: cmd.SignFlowMode,
                ct);
        }
        else
        {
            // Capture old values before mutating for the audit diff.
            var oldProviderKey             = existing.ProviderKey;
            var oldAllowedCaRootThumbprints = existing.AllowedCaRootThumbprints;
            var oldOcspMode                = existing.OcspMode;
            var oldIsEnabled               = existing.IsEnabled;
            var oldSignFlowMode            = existing.SignFlowMode;

            existing.Update(
                cmd.ProviderKey,
                cmd.ApiCredentialsRef,
                cmd.AllowedCaRootThumbprints,
                cmd.OcspMode,
                cmd.IsEnabled,
                cmd.SignFlowMode,
                cmd.ActorId);

            // Pass RowVersion from command for optimistic concurrency (throws DbUpdateConcurrencyException → 409).
            await repository.UpdateAsync(existing, cmd.RowVersion, ct);

            // Determine action: CA root pin change is a Critical security event.
            bool rootPinChanged = !string.Equals(
                oldAllowedCaRootThumbprints, cmd.AllowedCaRootThumbprints,
                StringComparison.OrdinalIgnoreCase);

            var action = rootPinChanged
                ? "CA_ROOT_THUMBPRINTS_CHANGED"  // maps to Severity.Critical in AuditCategoryMapper
                : "SIGNATURE_CONFIG_UPDATED";

            await EmitAuditAsync(
                action: action,
                tenantId: cmd.TenantId,
                providerKeyOld: oldProviderKey,
                providerKeyNew: cmd.ProviderKey,
                rootThumbprintsOld: oldAllowedCaRootThumbprints,
                rootThumbprintsNew: cmd.AllowedCaRootThumbprints,
                ocspModeOld: oldOcspMode,
                ocspModeNew: cmd.OcspMode,
                isEnabledOld: oldIsEnabled,
                isEnabledNew: cmd.IsEnabled,
                signFlowModeOld: oldSignFlowMode,
                signFlowModeNew: cmd.SignFlowMode,
                ct);
        }

        return Result.Ok();
    }

    private Task EmitAuditAsync(
        string action,
        Guid tenantId,
        string? providerKeyOld,
        string providerKeyNew,
        string? rootThumbprintsOld,
        string rootThumbprintsNew,
        OcspMode? ocspModeOld,
        OcspMode ocspModeNew,
        bool? isEnabledOld,
        bool isEnabledNew,
        SignatureFlowMode? signFlowModeOld,
        SignatureFlowMode signFlowModeNew,
        CancellationToken ct)
    {
        // dataSnapshot: diff of old→new config fields (no API credentials — those are Vault refs, not PII but sensitive).
        var snapshot = JsonSerializer.Serialize(new
        {
            tenantId          = tenantId.ToString(),
            providerKeyOld,
            providerKeyNew,
            rootThumbprintsOld,
            rootThumbprintsNew,
            ocspModeOld       = ocspModeOld?.ToString(),
            ocspModeNew       = ocspModeNew.ToString(),
            isEnabledOld,
            isEnabledNew,
            signFlowModeOld   = signFlowModeOld?.ToString(),
            signFlowModeNew   = signFlowModeNew.ToString(),
        });

        return auditLogger.LogCommandAsync(
            action:       action,
            moduleName:   "Identity",
            resourceType: "TenantSignatureConfig",
            resourceId:   tenantId.ToString(),
            commandType:  typeof(UpdateTenantSignatureConfigCommand),
            dataSnapshot: snapshot,
            cancellationToken: ct);
    }
}
