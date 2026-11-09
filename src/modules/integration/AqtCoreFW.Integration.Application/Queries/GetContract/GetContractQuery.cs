using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.Integration.Application.DTOs;

namespace AqtCoreFW.Integration.Application.Queries.GetContract;

public sealed record GetContractQuery(Guid Id, Guid TenantId) : IQuery<ContractDto>;
