namespace AqtCoreFW.SharedKernel.Domain.Events;

/// <summary>
/// Marks an integration event whose payload carries its originating tenant.
///
/// A MassTransit consumer runs on a background scope with no HttpContext, so the ambient
/// <c>ITenantContext</c> would otherwise resolve to the cross-tenant background-job context
/// (TenantId = null). Any tenant-scoped write the consumer performs would then run with an
/// unset SQL Server SESSION_CONTEXT — invisible today only because the runtime DB login is
/// db_owner (RLS bypass), but a least-privilege login would block it.
///
/// <c>TenantScopeConsumeFilter</c> reads <see cref="TenantId"/> off any message implementing
/// this marker and pushes it into <c>AmbientTenantScope</c> for the duration of the consume,
/// so the consumer's <c>ITenantContext</c> — and therefore SESSION_CONTEXT (RLS) and the EF
/// global tenant filters — scope to the event's tenant.
///
/// <see cref="TenantId"/> null = not tenant-scoped (cross-tenant); the filter is a no-op.
/// </summary>
public interface ITenantScopedIntegrationEvent
{
    Guid? TenantId { get; }
}
