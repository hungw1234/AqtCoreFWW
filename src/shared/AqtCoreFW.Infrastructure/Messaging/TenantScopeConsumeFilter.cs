using AqtCoreFW.Infrastructure.Security;
using AqtCoreFW.SharedKernel.Domain.Events;
using MassTransit;

namespace AqtCoreFW.Infrastructure.Messaging;

/// <summary>
/// Scoped MassTransit consume filter (Slice 1D) that pins the consume scope to the message's
/// originating tenant. For any message implementing <see cref="ITenantScopedIntegrationEvent"/>
/// with a non-null <c>TenantId</c>, it pushes that tenant into <see cref="AmbientTenantScope"/>
/// for the duration of the consume, so the consumer's <c>ITenantContext</c> — and therefore SQL
/// Server SESSION_CONTEXT (RLS) and the EF global tenant filters — scope to that tenant instead
/// of the cross-tenant background-job default.
///
/// Registered per-message-type via <c>UseConsumeFilter(typeof(TenantScopeConsumeFilter&lt;&gt;), …)</c>,
/// so it runs ahead of every consumer on both the InMemory and RabbitMQ pipelines. Non-marker
/// messages (and marker messages with a null tenant) pass straight through unchanged.
/// </summary>
public sealed class TenantScopeConsumeFilter<T>(AmbientTenantScope tenantScope)
    : IFilter<ConsumeContext<T>>
    where T : class
{
    public async Task Send(ConsumeContext<T> context, IPipe<ConsumeContext<T>> next)
    {
        if (context.Message is ITenantScopedIntegrationEvent { TenantId: { } tenantId })
        {
            using (tenantScope.Push(tenantId))
                await next.Send(context);
        }
        else
        {
            await next.Send(context);
        }
    }

    public void Probe(ProbeContext context) => context.CreateFilterScope("tenantScope");
}
