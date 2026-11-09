using AqtCoreFW.Contracts.Application.DTOs;
using AqtCoreFW.SharedKernel.Application;

namespace AqtCoreFW.Contracts.Application.Queries.GetContract;

public sealed record GetContractQuery(long Id, Guid TenantId) : IQuery<ContractDto>;
