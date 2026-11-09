using AqtCoreFW.SharedKernel.Domain.Events;

namespace AqtCoreFW.Forms.Domain.Events;

/// <summary>
/// Raised when a form submission persists successfully but the linked workflow instance
/// fails to create (e.g. empty handler pool at initial state — GOV_WFL_014). The submission
/// row exists with <c>WorkflowInstanceId == null</c>; this event surfaces the orphan signal
/// so a downstream handler can persist it for ops/admin visibility and retry.
///
/// <para><b>Trigger:</b> both the <c>!wfResult.IsSuccess</c> rejection branch AND the
/// <c>catch (Exception)</c> hard-throw branch in <c>SubmitFormCommandHandler</c> and
/// <c>SubmitPublicFormCommandHandler</c> — a throw orphans the submission identically to a
/// rejection, so both surface the signal (throw uses <see cref="ExceptionFailureCode"/>).</para>
///
/// <para><b>Channel (Q8):</b> v1 uses <see cref="IInternalDomainEvent"/> (MediatR in-process).
/// Handler MUST be idempotent (UPSERT on <c>(SubmissionId, TenantId)</c>) to remain
/// forward-compatible with a future Outbox migration where at-least-once delivery applies.</para>
///
/// <para><b>Payload contract:</b> IDs + a failure code; the failure-message is a canonical
/// BE message on the rejection branch, but a best-effort raw exception message on the
/// throw branch (<see cref="ExceptionFailureCode"/>). Both are admin-visible only and
/// truncated to 500 chars when persisted — treat the message as diagnostic, not guaranteed
/// PII-free or stable.</para>
///
/// <para><b>Consumer note:</b> an <see cref="ExceptionFailureCode"/> signal can represent an
/// instance that WAS created but whose link-write failed (throw after a successful create) —
/// not only a never-created instance. Any retry/remediation consumer MUST be create-idempotent
/// (verify an existing instance for the submission before re-creating) to avoid duplicates.</para>
/// </summary>
public sealed record FormSubmissionWorkflowLinkPendingEvent(
    Guid SubmissionId,
    long FormTemplateId,
    Guid ApprovalWorkflowDefinitionId,
    Guid TenantId,
    Guid SubmittedBy,
    string FailureCode,
    string? FailureMessage,
    Guid MessageId) : IInternalDomainEvent
{
    /// <summary>
    /// Canonical <see cref="FailureCode"/> when the workflow-link failed by a hard exception
    /// (broker/network/timeout) rather than a structured rejection outcome. Lets ops/admin
    /// distinguish a throw-orphan from a rejection-orphan (which carries the BE GOV_* code).
    /// </summary>
    public const string ExceptionFailureCode = "WF_LINK_EXCEPTION";

    public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
}
