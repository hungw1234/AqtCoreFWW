using AqtCoreFW.SharedKernel.Contracts.Clients;
using FluentResults;
using MediatR;

namespace AqtCoreFW.BusinessProcess.Application.Commands.StartBusinessProcessRun;

/// <summary>
/// P4 Slice 2C: starts a BP-Run — creates a WorkflowInstance and (when the BP pins an intake form
/// and the caller supplies <see cref="IntakeFormData"/>) the intake FormSubmission, atomically.
/// </summary>
/// <remarks>
/// Intentionally NOT an <c>ICommand</c>/<c>IBaseCommand</c>, so the generic <c>AuditBehavior</c> does
/// not fire: a BP-Run start is recorded by the created WorkflowInstance (and the intake FormSubmission)
/// rows — the canonical operational trail — making a thin command-audit row redundant. This mirrors
/// Workflow's sibling runtime commands (ExecuteTransition/CreateWorkflowInstance), which also bypass
/// IBaseCommand; only BP/Workflow definition-config commands carry it. Tenant isolation is enforced by
/// the handler's explicit cross-tenant guard, not the IBaseCommand-gated TenantAwareBehavior.
/// </remarks>
public sealed record StartBusinessProcessRunCommand(
    Guid BpPublicId,
    Guid TenantId,
    string EntityType,
    Guid EntityId,
    IReadOnlyDictionary<string, object?>? IntakeFormData,
    bool ConsentGiven,
    Guid ActorId,
    // Digital-signature proof bundle for an intake form that carries a DigitalSignature field;
    // null otherwise. Verified at ingest against server-recomputed content (QĐ-742).
    BpRunSignaturePayload? Signature = null)
    : IRequest<Result<StartBusinessProcessRunResult>>;

/// <summary>
/// Outcome: the new <c>WorkflowInstance.PublicId</c>, the initial state long Id, and the intake-form
/// submission PublicId when one was created (null when no intake form was pinned).
/// </summary>
public sealed record StartBusinessProcessRunResult(
    Guid WorkflowInstanceId,
    long InitialStateInternalId,
    Guid? IntakeFormSubmissionId);
