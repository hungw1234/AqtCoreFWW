using AqtCoreFW.SharedKernel.Contracts.Clients;
using FluentResults;
using MediatR;

namespace AqtCoreFW.BusinessProcess.Application.Commands.SubmitBusinessProcessStateForm;

/// <summary>
/// P4 Slice 2C: submits a state-scoped form against a BP-Run's current WorkflowInstance + WorkflowState.
/// Single op (no TransactionScope) — Forms enforces R14 cross-tenant guard internally.
/// </summary>
/// <remarks>
/// Intentionally NOT an <c>ICommand</c>/<c>IBaseCommand</c>, so the generic <c>AuditBehavior</c> does
/// not fire: the submission is recorded by the persisted FormSubmission row — the canonical operational
/// trail — making a thin command-audit row redundant. This mirrors Workflow's sibling runtime commands,
/// which also bypass IBaseCommand; only BP/Workflow definition-config commands carry it. Tenant isolation
/// is enforced by the handler's explicit cross-tenant guard (plus Forms' internal R14 guard), not the
/// IBaseCommand-gated TenantAwareBehavior.
/// </remarks>
public sealed record SubmitBusinessProcessStateFormCommand(
    Guid WorkflowInstanceId,
    long WorkflowStateId,
    Guid BpPublicId,
    long FormTemplateId,
    IReadOnlyDictionary<string, object?> Data,
    bool ConsentGiven,
    Guid TenantId,
    Guid ActorId,
    // Digital-signature proof bundle for a state form that carries a DigitalSignature field;
    // null otherwise. Verified at ingest against server-recomputed content (QĐ-742).
    BpRunSignaturePayload? Signature = null)
    : IRequest<Result<SubmitBusinessProcessStateFormResult>>;

public sealed record SubmitBusinessProcessStateFormResult(Guid FormSubmissionId);
