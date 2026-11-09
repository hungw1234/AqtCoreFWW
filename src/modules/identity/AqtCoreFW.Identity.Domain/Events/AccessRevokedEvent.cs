using AqtCoreFW.SharedKernel.Domain.Events;

namespace AqtCoreFW.Identity.Domain.Events;

/// <summary>Raised when access review results in Revoke decision — Audit module logs it.</summary>
public sealed record AccessRevokedEvent(
    Guid UserId,
    Guid RoleId,
    Guid ReviewedBy,
    Guid AccessReviewRecordId,
    Guid MessageId) : IInternalDomainEvent
{
    public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
}
