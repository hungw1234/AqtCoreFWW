using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.Forms.Application.Commands.ApproveSubmission;

/// <summary>Approve a Pending form submission. Transitions status Pending → Approved.</summary>
[AuditCategory(AuditCategory.AppAccess)]
public sealed record ApproveSubmissionCommand(
    Guid SubmissionId,
    Guid TenantId,
    Guid ReviewedBy,
    string? Comment) : ICommand;
