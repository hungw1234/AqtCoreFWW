using AqtCoreFW.ModuleName.Domain.Entities;
using AqtCoreFW.ModuleName.Domain.Repositories;
using FluentResults;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.ModuleName.Infrastructure.Persistence.Repositories;

public sealed class ModuleNameRepository(ModuleNameDbContext db) : IModuleNameRepository
{
    public async Task<Result<ModuleNameEntity>> GetByIdAsync(long id, CancellationToken ct = default)
    {
        var entity = await db.ModuleNames.FirstOrDefaultAsync(e => e.Id == id, ct);
        return entity is not null
            ? Result.Ok(entity)
            : Result.Fail($"ModuleName '{id}' not found.");
    }

    public async Task AddAsync(ModuleNameEntity entity, CancellationToken ct = default)
    {
        await db.ModuleNames.AddAsync(entity, ct);
        await db.SaveChangesAsync(ct);
    }

    public Task SaveChangesAsync(CancellationToken ct = default)
    {
        return db.SaveChangesAsync(ct);
    }
}
