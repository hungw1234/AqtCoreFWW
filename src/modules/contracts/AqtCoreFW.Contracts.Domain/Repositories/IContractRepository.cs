using AqtCoreFW.Contracts.Domain.Entities;
using FluentResults;

namespace AqtCoreFW.Contracts.Domain.Repositories;

public interface IContractRepository
{
    Task<bool> ContractNumberExistsAsync(
        string contractNumber,
        long? excludingId = null,
        CancellationToken ct = default);
    Task<Result<Contract>> GetByIdAsync(long id, CancellationToken ct = default);
    Task AddAsync(Contract contract, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
