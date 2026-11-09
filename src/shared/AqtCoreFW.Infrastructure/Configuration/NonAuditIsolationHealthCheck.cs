using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AqtCoreFW.Infrastructure.Configuration;

/// <summary>
/// Readiness health check that surfaces a SILENTLY-DORMANT audit read-isolation boundary.
///
/// The <c>NonAudit</c> login (backlog <c>ISO-EF-FIRST-AUDIT-BOUNDARY</c> / <c>ISO-DAPPER-KEYED-DI-INCREMENT</c>)
/// is opt-in: <see cref="NonAuditConnectionString.Get"/> falls back to the broad <c>Default</c> login when
/// <c>ConnectionStrings:NonAudit</c> is unset, so a production deployment that never provisioned the
/// least-privilege login keeps booting — but every non-audit module read then runs on a login that CAN
/// reach <c>[audit]</c>, defeating the isolation with no operator signal. This check makes that state
/// visible instead of silent.
///
/// <para>
/// <b>Degraded — never Unhealthy — is deliberate.</b> It preserves the opt-in fallback (does not fail the
/// pod's readiness probe / take it out of rotation) while still turning <c>/health/ready</c> yellow so the
/// missing provisioning is noticed. The boundary is reported dormant only in Production; dev / test / CI
/// legitimately run without the NonAudit login and stay Healthy to avoid readiness noise (mirrors
/// <see cref="VaultHealthCheck"/>'s dev framing).
/// </para>
/// </summary>
public sealed class NonAuditIsolationHealthCheck(
    IConfiguration configuration,
    IWebHostEnvironment env) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        // Descriptions are deliberately terse: /health/ready is unauthenticated and echoes each check's
        // Description, so we do NOT leak the schema name, provisioning-script path, or isolation mechanism
        // here. The operator-actionable detail lives in the startup-log warning (Program.cs) instead.
        if (NonAuditConnectionString.IsExplicitlyConfigured(configuration))
            return Task.FromResult(HealthCheckResult.Healthy(
                "Audit read-isolation: NonAudit login configured."));

        // Outside Production the login is expected to be unset (opt-in fallback to Default) — Healthy,
        // not yellow, so dev/test/CI readiness stays clean.
        if (!env.IsProduction())
            return Task.FromResult(HealthCheckResult.Healthy(
                "Audit read-isolation opt-in (NonAudit login unset — expected outside Production)."));

        return Task.FromResult(HealthCheckResult.Degraded(
            "Audit read-isolation dormant in Production — NonAudit login not configured; see startup log."));
    }
}
