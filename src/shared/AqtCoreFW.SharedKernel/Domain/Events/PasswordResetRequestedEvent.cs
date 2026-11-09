namespace AqtCoreFW.SharedKernel.Domain.Events;

/// <summary>
/// Raised by Identity module after admin requests a password reset.
/// Consumed by Notifications module to email the reset link.
/// Kept in SharedKernel so modules are not directly coupled.
/// </summary>
public sealed record PasswordResetRequestedEvent(
    Guid UserId,
    string Email,
    string FullName,
    string ResetToken,
    Guid MessageId) : IInternalDomainEvent
{
    public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
}
