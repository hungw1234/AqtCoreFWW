using FluentResults;

namespace AqtCoreFW.SharedKernel.Domain.Repositories;

/// <summary>
/// Generic repository for aggregate roots with typed ID.
/// Query handlers bypass this — use IReadDbConnection (Dapper) directly for reads.
/// </summary>
public interface IRepository<T, TId> where T : class
{
    Task<Result<T>> GetByIdAsync(TId id, CancellationToken cancellationToken = default);
    Task AddAsync(T entity, CancellationToken cancellationToken = default);
    Task UpdateAsync(T entity, CancellationToken cancellationToken = default);
    /// <summary>Soft delete — sets IsDeleted = true, does not remove row.</summary>
    Task DeleteAsync(TId id, CancellationToken cancellationToken = default);
}
