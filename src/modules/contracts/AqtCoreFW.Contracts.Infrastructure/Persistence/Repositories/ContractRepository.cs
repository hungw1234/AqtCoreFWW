using AqtCoreFW.Contracts.Domain.Entities;
using AqtCoreFW.Contracts.Domain.Repositories;
using AqtCoreFW.Contracts.Infrastructure.Persistence;
using FluentResults;
using Microsoft.EntityFrameworkCore;
using AqtCoreFW.SharedKernel.Errors;

namespace AqtCoreFW.Contracts.Infrastructure.Persistence.Repositories;

public sealed class ContractRepository(ContractsDbContext db) : IContractRepository
{
    public Task<bool> ContractNumberExistsAsync(
        string contractNumber,
        long? excludingId = null,
        CancellationToken ct = default) =>
        db.Contracts.AnyAsync(
            contract =>
                contract.ContractNumber == contractNumber &&
                (excludingId == null || contract.Id != excludingId),
            ct);

    public async Task<Result<Contract>> GetByIdAsync(long id, CancellationToken ct = default)
    {
        var contract = await db.Contracts.FirstOrDefaultAsync(item => item.Id == id, ct);
        return contract is not null
            ? Result.Ok(contract)
            : Result.Fail<Contract>(new NotFoundError($"Không tìm thấy hợp đồng '{id}'."));
    }

    public async Task AddAsync(Contract contract, CancellationToken ct = default)
    {
        await db.Contracts.AddAsync(contract, ct);
        await db.SaveChangesAsync(ct);
    }

    public Task SaveChangesAsync(CancellationToken ct = default) =>
        db.SaveChangesAsync(ct);
}
