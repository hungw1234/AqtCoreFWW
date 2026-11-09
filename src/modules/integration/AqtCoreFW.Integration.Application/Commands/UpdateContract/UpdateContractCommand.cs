using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;
using AqtCoreFW.Integration.Application.DTOs;

namespace AqtCoreFW.Integration.Application.Commands.UpdateContract;

[AuditCategory(AuditCategory.ConfigChange)]
public sealed record UpdateContractCommand(
    Guid Id, Guid TenantId, string Title, string? Description,
    DateTime EffectiveDate, DateTime? ExpiryDate,
    string? DataScopeJson) : ICommand<ContractDto>;
