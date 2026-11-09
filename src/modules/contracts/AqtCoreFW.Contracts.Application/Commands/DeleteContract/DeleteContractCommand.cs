using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.Contracts.Application.Commands.DeleteContract;

[AuditCategory(AuditCategory.ConfigChange)]
public sealed record DeleteContractCommand(long Id, Guid TenantId) : ICommand;
