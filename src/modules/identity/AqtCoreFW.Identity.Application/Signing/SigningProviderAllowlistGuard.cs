using System.Text.Json;
using AqtCoreFW.Identity.Application.Telemetry;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.Identity.Domain.Services;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;

namespace AqtCoreFW.Identity.Application.Signing;

/// <summary>
/// Early, fail-closed authorization on the signing request path: denies a request whose <b>assigned signer</b>
/// holds no Approved active enrollment of the requested provider's <see cref="EnrollmentKind"/>. Called from
/// <c>SignatureOrchestrator.RequestSignatureAsync</c> — the single chokepoint shared by BOTH the self-sign HTTP
/// path and the Forms submission-signing path (<c>InProcessSigningRequestClient</c>, FE-supplied providerKey) —
/// so no request row is created and no provider is invoked when access is denied.
/// </summary>
public interface ISigningProviderAllowlistGuard
{
    /// <summary>
    /// Returns <see cref="Result.Ok()"/> when the request may proceed (allowed OR deliberately deferred), or a
    /// failed <see cref="Result"/> carrying a <see cref="ForbiddenError"/> (→ 403) on deny; the deny path emits
    /// exactly one audit row internally.
    /// </summary>
    /// <param name="signerUserId">
    /// The assigned signer (<c>RequestSignatureInput.AssignedSignerId</c>) whose certificate will sign — NOT the
    /// initiator. Mirrors <c>SignerBindingGuard</c>'s subject so the early and late guards stay aligned once a
    /// BP-assigned signer differs from the request initiator.
    /// </param>
    Task<Result> EnsureAllowedAsync(string providerKey, Guid tenantId, Guid signerUserId, CancellationToken ct = default);
}

/// <inheritdoc cref="ISigningProviderAllowlistGuard"/>
public sealed class SigningProviderAllowlistGuard(
    ISignatureProviderResolver resolver,
    IProviderEnrollmentKindMap providerKindMap,
    ISignerCertEnrollmentRepository enrollments,
    IAuditLogger auditLogger) : ISigningProviderAllowlistGuard
{
    private const string DenyAction = "SIGNING_PROVIDER_ALLOWLIST_DENIED";

    public async Task<Result> EnsureAllowedAsync(
        string providerKey, Guid tenantId, Guid signerUserId, CancellationToken ct = default)
    {
        // 1. Precedence (secure ordering): resolve the provider first. An unknown key is NOT ours to authorize —
        //    defer (Ok) so the orchestrator's own routing returns the unchanged 404. Authz applies only to a
        //    provider that actually exists; the resolver is the case-sensitive first gate (no case bypass).
        try
        {
            resolver.Resolve(providerKey);
        }
        catch (KeyNotFoundException)
        {
            return Result.Ok();
        }

        // 2. Registered but not mapped to an enrollment kind → fail-closed deny (never fail-open). An arch drift
        //    test asserts every registered provider key is mapped, so this branch fires only on a config gap.
        if (!providerKindMap.TryResolve(providerKey, out var requiredKind))
            return await DenyAsync(providerKey, requiredKind: null, tenantId, signerUserId, "unmapped", ct);

        // 3. Enrolled-tier allow: an Approved, active enrollment of the required kind for the SIGNER.
        var active = await enrollments.GetActiveByUserAsync(tenantId, signerUserId, ct);
        if (active.Any(e => e.ApprovalStatus == CertApprovalStatus.Approved && e.Kind == requiredKind))
            return Result.Ok();

        // 4. Bootstrap-tier defer: a signer with ZERO enrollment rows (CCCD-bootstrap Mysign) passes the guard so
        //    the late SignerBindingGuard stays authoritative — zero legacy break. The all-status HasAny query
        //    (not the IsActive-filtered GetActive) keeps a Rejected/revoked signer in the enrolled tier, exactly
        //    mirroring the binding guard's tier decision (no downgrade to the weaker bootstrap path).
        if (!await enrollments.HasAnyEnrollmentAsync(tenantId, signerUserId, ct))
            return Result.Ok();

        // 5. Enrolled signer lacking an Approved enrollment of the required kind → fail-closed deny.
        return await DenyAsync(providerKey, requiredKind, tenantId, signerUserId, "not_enrolled", ct);
    }

    /// <summary>
    /// Emits the forensic deny audit row (mirrors <c>SignPayloadCommand</c>'s binding-rejected audit) and returns
    /// a <see cref="ForbiddenError"/>. Attributes the row to <paramref name="tenantId"/> explicitly (this guard is
    /// a cross-cutting service that may run outside an authenticated MediatR pipeline, e.g. the Forms path).
    /// </summary>
    private async Task<Result> DenyAsync(
        string providerKey, EnrollmentKind? requiredKind, Guid tenantId, Guid signerUserId, string reason,
        CancellationToken ct)
    {
        // Alerting signal (a deny = probable FE-bypass / mis-configured client). The rate-based AlertRule scans
        // this counter — the audit row below is forensics only (the evaluator never queries the audit DB).
        SigningMetrics.ProviderAllowlistDenied.Add(1,
            new KeyValuePair<string, object?>("provider", providerKey),
            new KeyValuePair<string, object?>("tenant", tenantId.ToString()),
            new KeyValuePair<string, object?>("reason", reason));   // "unmapped" | "not_enrolled"

        await auditLogger.LogCommandAsync(
            action:            DenyAction,
            moduleName:        "Identity",
            resourceType:      "SigningRequest",
            resourceId:        tenantId.ToString(),
            commandType:       typeof(RequestSignatureCommand),
            dataSnapshot:      JsonSerializer.Serialize(new
            {
                providerKey,
                requiredKind = requiredKind?.ToString(),
                signerUserId = signerUserId.ToString(),
                tenantId     = tenantId.ToString(),
                reason,
            }),
            tenantIdOverride:  tenantId,
            cancellationToken: ct);

        return Result.Fail(new ForbiddenError(
            $"Signing with provider '{providerKey}' is not permitted for the assigned signer."));
    }
}
