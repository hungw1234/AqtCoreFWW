using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.AssetManagement.Application.Commands.CreateAssetManagement;

[AuditCategory(AuditCategory.ConfigChange)]
public sealed record CreateAssetManagementCommand(
    Guid TenantId,
    string Title,
    string? Description,
    Guid CreatedBy) : ICommand<long>;
