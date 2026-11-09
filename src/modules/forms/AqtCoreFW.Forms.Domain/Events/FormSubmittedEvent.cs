using AqtCoreFW.SharedKernel.Domain.Events;

namespace AqtCoreFW.Forms.Domain.Events;

/// <summary>
/// Raised when a form submission is successfully persisted.
/// Consumed by notification handler to alert reviewers or trigger follow-up workflows.
/// IInternalDomainEvent → dispatched via MediatR in-process.
/// Phase-02c: MessageId moved to positional ctor param (Rule1 — no Guid.NewGuid() defaults).
/// </summary>
public sealed record FormSubmittedEvent(
    Guid SubmissionId,
    long FormTemplateId,
    Guid TenantId,
    Guid SubmittedBy,
    Guid MessageId) : IInternalDomainEvent
{
    public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
}
