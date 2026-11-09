using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.Infrastructure.Security;

/// <summary>
/// Explicit single-tenant <see cref="ITenantContext"/> for a background scope that derived its
/// tenant from a message payload rather than an HTTP request (Slice 1D — MassTransit consume).
///
/// <see cref="IsSystemAdmin"/> is intentionally <c>false</c>: a payload-scoped consumer must be
/// pinned to exactly the event's tenant (so SESSION_CONTEXT and the EF global filters isolate to
/// it), NOT granted the cross-tenant escape that <c>BackgroundJobTenantContext</c> carries.
/// </summary>
public sealed class ScopedTenantContext(Guid tenantId, bool isSystemAdmin = false) : ITenantContext
{
    public Guid? TenantId { get; } = tenantId;
    public bool IsSystemAdmin { get; } = isSystemAdmin;
}
