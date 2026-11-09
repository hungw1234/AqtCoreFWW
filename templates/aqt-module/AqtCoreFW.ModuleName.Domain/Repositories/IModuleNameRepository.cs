using AqtCoreFW.ModuleName.Domain.Entities;
using FluentResults;

namespace AqtCoreFW.ModuleName.Domain.Repositories;

public interface IModuleNameRepository
{
    Task<Result<ModuleNameEntity>> GetByIdAsync(long id, CancellationToken ct = default);
    Task AddAsync(ModuleNameEntity entity, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
