using AqtCoreFW.SharedKernel.Domain.Events;

namespace AqtCoreFW.BusinessProcess.Domain.Events;

/// <summary>Base record for BusinessProcess domain events — internal MediatR in-process dispatch.</summary>
public abstract record BusinessProcessDomainEventBase : IInternalDomainEvent
{
    public abstract Guid MessageId { get; init; }
    public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Raised when a BusinessProcessDefinition is first created.
/// Carries Guid PublicId (T2 AK) — never the internal bigint Id.
/// </summary>
public sealed record BusinessProcessCreatedEvent(
    Guid BusinessProcessPublicId,
    Guid TenantId,
    Guid MessageId) : BusinessProcessDomainEventBase
{
    public override Guid MessageId { get; init; } = MessageId;
}
