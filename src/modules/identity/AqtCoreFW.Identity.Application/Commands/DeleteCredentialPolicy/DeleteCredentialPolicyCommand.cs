using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.Identity.Application.Commands.DeleteCredentialPolicy;

[AuditCategory(AuditCategory.ConfigChange)]
public sealed record DeleteCredentialPolicyCommand(long Id, Guid ActorId) : ICommand;
