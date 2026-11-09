namespace AqtCoreFW.SharedKernel.Application;

/// <summary>
/// Minimal audit logging interface — lives in SharedKernel so all behaviors can use it.
/// Implemented by Audit.Infrastructure.Services.AuditService.
/// </summary>
public interface IAuditLogger
{
    /// <param name="commandType">
    /// Optional MediatR command runtime type. When supplied, the audit pipeline reads
    /// <c>[AuditCategoryAttribute]</c> off this type with precedence over the verb-fallback
    /// in <c>AuditCategoryMapper</c>. Pass <c>null</c> from non-MediatR call sites
    /// (Hangfire jobs, controllers, integration handlers) — verb fallback applies.
    /// </param>
    /// <param name="dataSnapshot">
    /// Optional JSON string with operation-specific evidence fields (e.g. certSerial, payloadHash,
    /// ocspResult). Must NOT contain raw PII — use IDs and non-sensitive metadata only.
    /// </param>
    /// <param name="tenantIdOverride">
    /// Optional tenant to attribute the audit row (and its per-tenant HMAC chain) to, instead of the
    /// ambient <c>ICurrentUser.TenantId</c>. Pass this from a system-to-system command that has no
    /// request principal but knows the owning tenant from a persisted entity (e.g. an anonymous vendor
    /// webhook completing a signing transaction). Leave null for normal authenticated flows.
    /// </param>
    Task LogCommandAsync(
        string action,
        string moduleName,
        string resourceType,
        string? resourceId = null,
        Type? commandType = null,
        string? dataSnapshot = null,
        Guid? tenantIdOverride = null,
        CancellationToken cancellationToken = default);
}
