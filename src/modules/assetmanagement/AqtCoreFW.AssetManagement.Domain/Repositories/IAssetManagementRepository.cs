using AqtCoreFW.AssetManagement.Domain.Entities;
using FluentResults;

namespace AqtCoreFW.AssetManagement.Domain.Repositories;

public interface IAssetManagementRepository
{
    Task<Result<AssetManagementEntity>> GetByIdAsync(long id, CancellationToken ct = default);
    Task AddAsync(AssetManagementEntity entity, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
