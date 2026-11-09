using AqtCoreFW.Forms.Application.Commands.BulkApproveSubmissions;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.Forms.Application.Commands.BulkRejectSubmissions;

/// <summary>Reject multiple Pending submissions in one operation. Comment is required for audit trail.</summary>
[AuditCategory(AuditCategory.AppAccess)]
public sealed record BulkRejectSubmissionsCommand(
    IReadOnlyList<Guid> Ids,
    Guid TenantId,
    Guid ReviewedBy,
    string Comment) : ICommand<BulkActionResult>;
