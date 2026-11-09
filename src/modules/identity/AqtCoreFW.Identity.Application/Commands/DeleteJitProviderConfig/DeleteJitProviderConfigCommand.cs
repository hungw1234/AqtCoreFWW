using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.Identity.Application.Commands.DeleteJitProviderConfig;

[AuditCategory(AuditCategory.ConfigChange)]
public sealed record DeleteJitProviderConfigCommand(long Id, Guid ActorId) : ICommand;
