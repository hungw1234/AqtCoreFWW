using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;
using AqtCoreFW.Integration.Application.DTOs;

namespace AqtCoreFW.Integration.Application.Commands.UpdatePartner;

[AuditCategory(AuditCategory.ConfigChange)]
public sealed record UpdatePartnerCommand(
    Guid PublicId, Guid TenantId, string Name, string Code,
    string? ContactEmail, string? ContactPhone,
    string? Endpoint, string? AuthScheme) : ICommand<PartnerDto>;
