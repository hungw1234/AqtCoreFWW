using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.Infrastructure.Security;

/// <summary>
/// No-op ITenantContext for design-time EF tooling (migrations, scaffolding).
/// IsSystemAdmin = true bypasses tenant filter — ensures all rows are visible during migrations.
/// Never registered in the application DI container.
/// </summary>
public sealed class NullTenantContext : ITenantContext
{
    public static readonly NullTenantContext Instance = new();

    public Guid? TenantId => null;

    // SystemAdmin = true → global filter skips tenant check entirely
    public bool IsSystemAdmin => true;
}
