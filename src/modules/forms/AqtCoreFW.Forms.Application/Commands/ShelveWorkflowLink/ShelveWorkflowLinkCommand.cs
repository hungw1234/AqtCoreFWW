using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.Forms.Application.Commands.ShelveWorkflowLink;

/// <summary>
/// Admin manual terminalize (P6-DEFER-1 phase 05): dead-letters a pending orphan workflow-link row
/// before <c>MaxAttempts</c> is reached. Calls the Phase-04 <c>MarkShelved</c> mutator (legal only
/// from Pending/Retrying — guard lives in the entity). The originating FormSubmission is left
/// untouched (never cancelled — mirrors the auto-job LOCKED Q2 dead-letter semantics).
/// </summary>
[AuditCategory(AuditCategory.AppAccess)]
public sealed record ShelveWorkflowLinkCommand(
    Guid SubmissionId,
    Guid TenantId,
    Guid ActorId) : ICommand;
