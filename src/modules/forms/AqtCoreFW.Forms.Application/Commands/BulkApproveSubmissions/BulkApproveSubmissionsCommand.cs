using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.Forms.Application.Commands.BulkApproveSubmissions;

/// <summary>Approve multiple Pending submissions in one operation.</summary>
[AuditCategory(AuditCategory.AppAccess)]
public sealed record BulkApproveSubmissionsCommand(
    IReadOnlyList<Guid> Ids,
    Guid TenantId,
    Guid ReviewedBy,
    string? Comment) : ICommand<BulkActionResult>;

/// <summary>Summary of a bulk approve/reject operation.</summary>
public sealed record BulkActionResult(int Succeeded, int Failed, IReadOnlyList<string> Errors);
