using AqtCoreFW.SharedKernel.Domain.Events;

namespace AqtCoreFW.Identity.Domain.Events;

/// <summary>Raised after failed login attempt — triggers lockout tracking in Audit module.</summary>
public sealed record LoginFailedEvent(
    string Email,
    string IpAddress,
    string Reason,
    Guid MessageId) : IInternalDomainEvent
{
    public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
}
