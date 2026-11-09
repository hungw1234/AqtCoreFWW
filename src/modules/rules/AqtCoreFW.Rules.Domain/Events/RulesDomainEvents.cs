using AqtCoreFW.SharedKernel.Domain.Events;

namespace AqtCoreFW.Rules.Domain.Events;

/// <summary>Base record for rules domain events — internal MediatR in-process dispatch.</summary>
public abstract record RulesDomainEventBase : IInternalDomainEvent
{
    public abstract Guid MessageId { get; init; }
    public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
}

/// <summary>Raised when a RuleSet transitions to Active status.</summary>
public sealed record RuleSetActivatedEvent(
    Guid RuleSetPublicId,
    Guid TenantId,
    Guid MessageId) : RulesDomainEventBase
{
    public override Guid MessageId { get; init; } = MessageId;
}

/// <summary>Raised when a rule evaluation completes (success or failure).</summary>
public sealed record RuleEvaluatedEvent(
    Guid RuleSetPublicId,
    string InputHash,
    bool IsSuccess,
    long DurationMs,
    Guid TenantId,
    Guid MessageId) : RulesDomainEventBase
{
    public override Guid MessageId { get; init; } = MessageId;
}
