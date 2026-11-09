using AqtCoreFW.Forms.Domain.Entities;
using AqtCoreFW.Forms.Domain.Enums;
using AqtCoreFW.Forms.Domain.Events;
using AqtCoreFW.Forms.Domain.Repositories;
using AqtCoreFW.SharedKernel.Application.Data;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Forms.Application.EventHandlers;

/// <summary>
/// Persists a <see cref="FormSubmissionWorkflowLinkPendingAttempt"/> row for each
/// orphan-submission signal. Idempotent on <c>(SubmissionId, TenantId)</c>: first emit
/// inserts; subsequent emits for the same submission are no-ops (this handler captures
/// the FIRST signal only — retry job owns subsequent state updates).
///
/// <para><b>Forward-compatibility (Q8):</b> v1 channel is <see cref="IInternalDomainEvent"/>
/// (in-process MediatR). UPSERT idempotency lets us migrate to Outbox-backed at-least-once
/// delivery later without behaviour change.</para>
///
/// <para><b>RLS tenant pin:</b> the target table carries a BLOCK-AFTER-INSERT RLS policy, so the
/// INSERT must run on a connection whose SESSION_CONTEXT TenantId equals the row's tenant. This
/// handler is published AFTER the submit command commits its own unit of work, so it opens its OWN
/// UoW pinned to <c>notification.TenantId</c> (the server-resolved tenant carried on the event) via
/// <see cref="ICrossModuleUnitOfWork.BeginAsync(Guid, System.Threading.CancellationToken)"/>. Without
/// this, the anonymous public-form path — whose ambient <c>ITenantContext</c> is null — would leave
/// SESSION_CONTEXT unset and the BLOCK predicate would reject the INSERT (error 33504).</para>
///
/// <para><b>Failure mode:</b> handler is fail-soft — never throws. If persistence fails
/// the submission row still exists in <c>forms.FormSubmissions</c>; orphan recovery falls
/// back to ops SQL discovery. We log + swallow so the submit command's response is not
/// disturbed.</para>
/// </summary>
public sealed class FormSubmissionWorkflowLinkPendingEventHandler(
    IFormSubmissionWorkflowLinkPendingAttemptRepository repository,
    ICrossModuleUnitOfWork unitOfWork,
    ILogger<FormSubmissionWorkflowLinkPendingEventHandler> logger)
    : INotificationHandler<FormSubmissionWorkflowLinkPendingEvent>
{
    public async Task Handle(
        FormSubmissionWorkflowLinkPendingEvent notification,
        CancellationToken cancellationToken)
    {
        // Pin SESSION_CONTEXT to the event's server-resolved tenant so the read + INSERT enlist on a
        // connection that satisfies the table's RLS FILTER/BLOCK predicates — correct for BOTH the
        // authenticated and the anonymous public-form path. The submit command already committed its
        // own UoW before publishing this event, so no UoW is active here.
        try
        {
            await unitOfWork.BeginAsync(notification.TenantId, cancellationToken);

            // Use the ENLISTING read (GetTracked…) so the idempotency lookup runs on the same pinned
            // connection as the INSERT. A non-enlisting NoTracking read would open the context's own
            // connection first, after which EnlistAsync (SetDbConnection needs a closed connection)
            // would fail for the subsequent AddAsync.
            var existing = await repository.GetTrackedBySubmissionIdAsync(
                notification.SubmissionId, notification.TenantId, cancellationToken);

            if (existing is not null)
            {
                // Idempotent re-emit (Outbox at-least-once forward-compat). Terminal rows
                // are NEVER mutated by this handler — retry job owns state transitions.
                logger.LogDebug(
                    "Pending-link attempt already exists for submission {SubmissionId} (status {Status}, attempts {AttemptCount}) — no-op",
                    notification.SubmissionId, existing.Status, existing.AttemptCount);
                await unitOfWork.RollbackAsync(cancellationToken);
                return;
            }

            var attempt = FormSubmissionWorkflowLinkPendingAttempt.CreateFromEvent(
                tenantId: notification.TenantId,
                submissionId: notification.SubmissionId,
                formTemplateId: notification.FormTemplateId,
                approvalWorkflowDefinitionId: notification.ApprovalWorkflowDefinitionId,
                submittedBy: notification.SubmittedBy,
                failureCode: notification.FailureCode,
                failureMessage: notification.FailureMessage);

            await repository.AddAsync(attempt, cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);

            logger.LogInformation(
                "Persisted pending-workflow-link attempt for submission {SubmissionId} (definition {DefinitionId}, code {FailureCode})",
                notification.SubmissionId, notification.ApprovalWorkflowDefinitionId, notification.FailureCode);
        }
        catch (Exception ex)
        {
            // Fail-soft: orphan recovery falls back to ops SQL if this row never lands.
            // Roll back the pinned UoW (idempotent — no-op if never begun / already ended) so a
            // failed INSERT doesn't leave a dangling open transaction on the pooled connection.
            await unitOfWork.RollbackAsync(cancellationToken);
            logger.LogError(ex,
                "Failed to persist pending-workflow-link attempt for submission {SubmissionId}. Orphan still discoverable via ops SQL.",
                notification.SubmissionId);
        }
    }
}
