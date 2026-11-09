using Microsoft.Extensions.Configuration;

namespace AqtCoreFW.Infrastructure.Configuration;

/// <summary>
/// Selects the connection string a NON-AUDIT module's EF DbContext must use so its ordinary
/// (non-UoW) reads and writes run under a login that is denied the <c>[audit]</c> schema — the
/// first cross-service read-isolation boundary (backlog <c>ISO-EF-FIRST-AUDIT-BOUNDARY</c>).
///
/// The proof that the boundary holds at the DB + EF layer is in
/// <c>AuditBoundaryEfIsolationTests</c>; ADR-012 records the read/write asymmetry and the
/// documented exceptions (UoW-active reads, the keyed "audit" Dapper reader, messaging, and
/// admin/backup paths) that intentionally remain on the broad <c>Default</c> login.
/// </summary>
public static class NonAuditConnectionString
{
    /// <summary>
    /// Returns the dedicated least-privilege <c>NonAudit</c> login when it is configured, otherwise
    /// falls back to <c>Default</c>. The fallback keeps existing environments (dev / test / CI)
    /// booting unchanged — isolation is opt-in: provision the login and set
    /// <c>ConnectionStrings:NonAudit</c> to activate it. The standalone Dapper reader's DEFAULT
    /// (unkeyed) registration now runs on this NonAudit login too; audit-capable query handlers
    /// resolve the keyed <c>"audit"</c> reader on the broad <c>Default</c> login instead.
    /// Messaging, the audit module's own EF context, <c>CrossModuleUnitOfWork</c>, and admin /
    /// backup paths stay on <c>Default</c> by design because they legitimately span schemas or
    /// own <c>[audit]</c>.
    /// </summary>
    public static string? Get(IConfiguration configuration)
        // Whitespace-check, not ??: an unset-by-blanking key (e.g. a Vault rollback that wrote
        // "" instead of deleting) must fall back to Default exactly like IsExplicitlyConfigured
        // treats it — "" ?? x keeps "" and every NonAudit-routed DbContext then crashes with
        // "The ConnectionString property has not been initialized" (caught 2026-07-23 rehearsal).
        => string.IsNullOrWhiteSpace(configuration.GetConnectionString("NonAudit"))
            ? configuration.GetConnectionString("Default")
            : configuration.GetConnectionString("NonAudit");

    /// <summary>
    /// True only when the dedicated <c>NonAudit</c> login is <b>explicitly</b> configured — i.e. the
    /// isolation boundary is actually active, not silently falling back to <c>Default</c>. Distinct from
    /// <see cref="Get"/>, which always returns a usable string: callers use this to tell "provisioned"
    /// apart from "opt-in fallback" (see <c>NonAuditIsolationHealthCheck</c> and the startup warning that
    /// surface a dormant boundary in Production — backlog <c>ISO-NONAUDIT-PROD-FAILFAST-HEALTHCHECK</c>).
    /// </summary>
    public static bool IsExplicitlyConfigured(IConfiguration configuration)
        => !string.IsNullOrWhiteSpace(configuration.GetConnectionString("NonAudit"));
}
