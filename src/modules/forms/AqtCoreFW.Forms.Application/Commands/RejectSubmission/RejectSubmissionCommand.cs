using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.Forms.Application.Commands.RejectSubmission;

/// <summary>Reject a Pending form submission. Transitions status Pending → Rejected.</summary>
[AuditCategory(AuditCategory.AppAccess)]
public sealed record RejectSubmissionCommand(
    Guid SubmissionId,
    Guid TenantId,
    Guid ReviewedBy,
    string Comment) : ICommand;
