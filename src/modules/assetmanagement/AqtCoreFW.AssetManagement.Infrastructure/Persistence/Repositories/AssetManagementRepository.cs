using AqtCoreFW.AssetManagement.Domain.Entities;
using AqtCoreFW.AssetManagement.Domain.Repositories;
using FluentResults;
using Microsoft.EntityFrameworkCore;
using AqtCoreFW.SharedKernel.Errors;

namespace AqtCoreFW.AssetManagement.Infrastructure.Persistence.Repositories;

public sealed class AssetManagementRepository(AssetManagementDbContext db) : IAssetManagementRepository
{
    public async Task<Result<AssetManagementEntity>> GetByIdAsync(long id, CancellationToken ct = default)
    {
        var entity = await db.AssetManagements.FirstOrDefaultAsync(e => e.Id == id, ct);
        return entity is not null
            ? Result.Ok(entity)
            : Result.Fail<AssetManagementEntity>(new NotFoundError($"Không tìm thấy tài sản '{id}'."));
    }

    public async Task AddAsync(AssetManagementEntity entity, CancellationToken ct = default)
    {
        await db.AssetManagements.AddAsync(entity, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        await db.SaveChangesAsync(ct);
    }
}
