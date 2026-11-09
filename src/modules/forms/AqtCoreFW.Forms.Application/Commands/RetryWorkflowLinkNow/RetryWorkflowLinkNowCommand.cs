using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.Forms.Application.Commands.RetryWorkflowLinkNow;

/// <summary>
/// Admin manual override (P6-DEFER-1 phase 05, LOCKED Q4): forces an immediate retry attempt on a
/// pending orphan workflow-link row. Re-arms <c>NextRetryAt = now</c> so the Hangfire retry job
/// (Phase 04, single create path — DRY) claims it on its next tick; does NOT create the workflow
/// instance itself. A <c>Shelved</c> row is un-shelved back to <c>Pending</c> (manual-path only —
/// the auto job never does this). A <c>Succeeded</c> row is a no-op/conflict (already resolved).
/// </summary>
[AuditCategory(AuditCategory.AppAccess)]
public sealed record RetryWorkflowLinkNowCommand(
    Guid SubmissionId,
    Guid TenantId,
    Guid ActorId) : ICommand;
