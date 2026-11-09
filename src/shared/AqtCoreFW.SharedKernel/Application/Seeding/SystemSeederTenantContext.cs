using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.SharedKernel.Application.Seeding;

/// <summary>
/// Explicit tenant context for install-time seeders (IHostedService) that construct a module
/// DbContext outside the request pipeline. <see cref="IsSystemAdmin"/> = true bypasses the tenant
/// global query filter so a seeder can read/insert across tenants without a JWT-based
/// <see cref="ITenantContext"/> (which is null during startup). <see cref="TenantId"/> = null
/// because seeders pass an explicit tenantId to every operation.
///
/// Shared across module seeders (Organization OrgTree/TenantAssignment, Workflow DemoWorkflow, …)
/// so the seed-context semantic lives in one place — distinct from the Hangfire
/// BackgroundJobTenantContext and the EF-tooling NullTenantContext despite identical field values.
/// </summary>
public sealed class SystemSeederTenantContext : ITenantContext
{
    public static readonly SystemSeederTenantContext Instance = new();

    public Guid? TenantId => null;
    public bool IsSystemAdmin => true;
}
