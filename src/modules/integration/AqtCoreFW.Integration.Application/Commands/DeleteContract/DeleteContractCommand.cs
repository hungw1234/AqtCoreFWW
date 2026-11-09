using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.Integration.Application.Commands.DeleteContract;

[AuditCategory(AuditCategory.ConfigChange)]
public sealed record DeleteContractCommand(Guid Id, Guid TenantId) : ICommand;
