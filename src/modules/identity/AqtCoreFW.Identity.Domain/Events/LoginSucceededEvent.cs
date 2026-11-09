using AqtCoreFW.SharedKernel.Domain.Events;

namespace AqtCoreFW.Identity.Domain.Events;

/// <summary>Raised after successful authentication — consumed by Audit module.</summary>
public sealed record LoginSucceededEvent(
    Guid UserId,
    string IpAddress,
    string ClientId,
    Guid MessageId) : IInternalDomainEvent
{
    public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
}
