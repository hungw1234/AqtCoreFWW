using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.AssetManagement.Application.Commands.DeleteAssetManagement;

[AuditCategory(AuditCategory.ConfigChange)]
public sealed record DeleteAssetManagementCommand(long Id, Guid TenantId) : ICommand;
