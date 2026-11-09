using AqtCoreFW.SharedKernel.Domain.Events;

namespace AqtCoreFW.Forms.Domain.Events;

/// <summary>
/// Raised when a form submission is rejected by a reviewer.
/// Consumed by notification handler to inform the submitter with the rejection reason.
/// IInternalDomainEvent → dispatched via MediatR in-process.
/// Phase-02c: MessageId moved to positional ctor param (Rule1 — no Guid.NewGuid() defaults).
/// </summary>
public sealed record FormRejectedEvent(
    Guid SubmissionId,
    long FormTemplateId,
    Guid TenantId,
    Guid ReviewedBy,
    Guid MessageId) : IInternalDomainEvent
{
    public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
}
