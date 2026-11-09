using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.Contracts.Application.Commands.CreateContract;

[AuditCategory(AuditCategory.ConfigChange)]
public sealed record CreateContractCommand(
    Guid TenantId,
    string ContractNumber,
    string Title,
    string CounterpartyName,
    DateTime StartDate,
    DateTime EndDate,
    string? Description,
    Guid CreatedBy) : ICommand<long>;
