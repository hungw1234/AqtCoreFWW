using AqtCoreFW.Contracts.Application.DTOs;
using AqtCoreFW.SharedKernel.Application;

namespace AqtCoreFW.Contracts.Application.Queries.ListContracts;

public sealed record ListContractsQuery(Guid TenantId) : IQuery<IReadOnlyList<ContractDto>>;
