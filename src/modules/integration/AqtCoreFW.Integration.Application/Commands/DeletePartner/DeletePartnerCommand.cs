using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.Integration.Application.Commands.DeletePartner;

[AuditCategory(AuditCategory.ConfigChange)]
public sealed record DeletePartnerCommand(Guid PublicId, Guid TenantId) : ICommand;
