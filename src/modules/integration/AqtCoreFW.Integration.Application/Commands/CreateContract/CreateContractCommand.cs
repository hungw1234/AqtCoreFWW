using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;
using AqtCoreFW.Integration.Application.DTOs;

namespace AqtCoreFW.Integration.Application.Commands.CreateContract;

[AuditCategory(AuditCategory.ConfigChange)]
public sealed record CreateContractCommand(
    Guid TenantId, Guid PartnerPublicId, string Title, string? Description,
    DateTime EffectiveDate, DateTime? ExpiryDate,
    string? DataScopeJson) : ICommand<ContractDto>;
