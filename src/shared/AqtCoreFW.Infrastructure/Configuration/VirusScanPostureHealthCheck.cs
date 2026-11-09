using AqtCoreFW.SharedKernel.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

namespace AqtCoreFW.Infrastructure.Configuration;

/// <summary>
/// Readiness health check that surfaces a SILENTLY-DISABLED upload virus scan.
///
/// Both upload paths gate on the same <c>feature:files.virus-scan</c> flag — the async Files pipeline
/// (<c>ClamAvScanJob</c>) and the synchronous signature-image path (<c>UserSignatureImageStoreAdapter</c>).
/// When it is off, uploads are marked Available WITHOUT being scanned. The only signal was a per-upload
/// <c>LogCritical</c>, which says nothing until someone reads the logs, and nothing at all while no upload
/// happens to be running. This check makes the posture visible on demand instead.
///
/// <para>
/// <b>Degraded — never Unhealthy — is deliberate.</b> Scanning-off is the CURRENT, legitimate state until
/// ops provisions clamd (backlog <c>CLAMAV-PROD-FLAG-POLICY</c>); reporting Unhealthy would pull every
/// production pod out of rotation over a posture the deployment has knowingly shipped with. Degraded turns
/// <c>/health/ready</c> yellow so the gap is noticed without causing an outage. Outside Production the flag
/// is expected to be off (no local clamd in test/CI) and stays Healthy to avoid readiness noise — the same
/// framing as <see cref="NonAuditIsolationHealthCheck"/>.
/// </para>
///
/// <para>
/// Note the asymmetry with the flag being ON: that case needs no check here. The scanner is fail-closed, so
/// an unreachable clamd yields <c>VirusScanStatus.Error</c> and holds the upload Quarantined — it never
/// passes a file unscanned. Only the OFF state can silently let content through.
/// </para>
/// </summary>
public sealed class VirusScanPostureHealthCheck(
    IWebHostEnvironment env,
    IFeatureFlagService? featureFlags = null) : IHealthCheck
{
    /// <summary>Flag key — kept in lock-step with ClamAvScanJob and UserSignatureImageStoreAdapter.</summary>
    private const string VirusScanFlag = "feature:files.virus-scan";

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        // IFeatureFlagService is registered by the SystemParams module, but this check is registered by
        // shared AddGovHealthChecks — a host can wire the second without the first (AuthServer skips both
        // today, so nothing is broken, but the coupling is one registration edit away from being wrong).
        // Optional rather than required: a readiness probe must not throw and turn a missing-module
        // wiring bug into an opaque 500 on /health/ready.
        if (featureFlags is null)
            return Task.FromResult(HealthCheckResult.Degraded(
                "Upload virus scan posture unknown — feature flags unavailable in this host."));

        // Deliberately the GLOBAL row, with no tenant: this probe answers a deployment-level question
        // ("is scanning wired up at all?"), and a health check has no tenant to scope to. The gates that
        // actually enforce scanning read tenant-with-global-fallback, so a per-tenant override can make
        // this report Degraded while that one tenant is in fact scanned. That skew is accepted — the
        // global row is the deployment's posture, and a per-tenant opt-in is not one.
        if (featureFlags.IsEnabled(VirusScanFlag))
            return Task.FromResult(HealthCheckResult.Healthy(
                "Upload virus scan: enabled."));

        // Outside Production there is normally no clamd to scan against — off is expected, so stay green
        // rather than making every dev/test/CI readiness probe yellow.
        if (!env.IsProduction())
            return Task.FromResult(HealthCheckResult.Healthy(
                "Upload virus scan off (expected outside Production)."));

        // Descriptions stay terse: /health/ready is unauthenticated, so this must not advertise which
        // upload paths are unscanned or how the gate is keyed. Operator-actionable detail belongs in the
        // backlog row and the per-upload LogCritical, not in a public probe response.
        return Task.FromResult(HealthCheckResult.Degraded(
            "Upload virus scan disabled in Production — uploads are stored without scanning."));
    }
}
