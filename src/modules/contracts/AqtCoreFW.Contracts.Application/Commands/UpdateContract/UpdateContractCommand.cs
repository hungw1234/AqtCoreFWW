using AqtCoreFW.Contracts.Domain.Entities;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.Contracts.Application.Commands.UpdateContract;

[AuditCategory(AuditCategory.ConfigChange)]
public sealed record UpdateContractCommand(
    long Id,
    Guid TenantId,
    string ContractNumber,
    string Title,
    string CounterpartyName,
    DateTime StartDate,
    DateTime EndDate,
    ContractStatus Status,
    string? Description,
    Guid UpdatedBy) : ICommand<long>;
