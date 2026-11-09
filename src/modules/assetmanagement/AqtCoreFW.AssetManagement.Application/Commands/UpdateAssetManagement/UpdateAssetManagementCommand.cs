using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.AssetManagement.Application.Commands.UpdateAssetManagement;

[AuditCategory(AuditCategory.ConfigChange)]
public sealed record UpdateAssetManagementCommand(
    long Id,
    Guid TenantId,
    string Title,
    string? Description,
    Guid UpdatedBy) : ICommand<long>;
