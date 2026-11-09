using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.TenantIsolation.Tests.Fixtures;

/// <summary>
/// Test-only ITenantContext — injects a fixed TenantId and IsSystemAdmin flag into DbContexts.
/// Controls which EF global query filters are active during a test.
/// </summary>
internal sealed class TestTenantContext(Guid? tenantId, bool isSystemAdmin = false) : ITenantContext
{
    public Guid? TenantId { get; } = tenantId;
    public bool IsSystemAdmin { get; } = isSystemAdmin;
}
