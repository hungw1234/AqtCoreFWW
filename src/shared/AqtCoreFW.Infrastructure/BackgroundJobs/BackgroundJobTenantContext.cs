using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.Infrastructure.BackgroundJobs;

/// <summary>
/// Explicit tenant context for Hangfire background jobs.
/// IsSystemAdmin=true — jobs intentionally see all tenants (cross-tenant processing).
/// TenantId=null — no single-tenant scope.
/// Registered in Hangfire's IJobActivatorScope so background jobs get this instead of
/// HttpContextTenantContext (which has no HttpContext and returns null/false).
/// </summary>
public sealed class BackgroundJobTenantContext : ITenantContext
{
    public Guid? TenantId => null;
    public bool IsSystemAdmin => true;
}
