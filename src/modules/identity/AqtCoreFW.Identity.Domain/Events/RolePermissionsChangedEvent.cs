using AqtCoreFW.SharedKernel.Domain.Events;

namespace AqtCoreFW.Identity.Domain.Events;

/// <summary>Raised when permissions assigned to a role change — consumed by Audit module.</summary>
public sealed record RolePermissionsChangedEvent(
    Guid RoleId,
    IReadOnlyList<Guid> AddedPermissionIds,
    IReadOnlyList<Guid> RemovedPermissionIds,
    Guid ActorId,
    Guid MessageId) : IInternalDomainEvent
{
    public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
}
