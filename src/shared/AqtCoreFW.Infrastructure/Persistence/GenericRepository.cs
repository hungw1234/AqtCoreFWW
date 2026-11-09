using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Domain.Repositories;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Infrastructure.Persistence;

/// <summary>
/// Generic EF Core repository — base implementation for all module aggregate repositories.
/// Modules extend this for aggregate-specific queries:
///   public sealed class NotificationRepository(NotificationsDbContext ctx)
///       : GenericRepository&lt;Notification, Guid&gt;(ctx), INotificationRepository
///
/// SaveChanges is called per-operation here. For cross-module atomicity, a repository may be
/// constructed with an <see cref="ICrossModuleUnitOfWork"/>: when one is active, every write
/// (and the read in DeleteAsync) first enlists this context on the shared connection so the
/// SaveChanges joins the single local SQL transaction instead of opening a second physical
/// connection (which would promote to a distributed transaction on Linux, where MSDTC is absent).
/// When no unit of work is supplied or active, behaviour is unchanged.
/// </summary>
public class GenericRepository<T, TId>(ModuleDbContext context, ICrossModuleUnitOfWork? unitOfWork = null)
    : IRepository<T, TId>
    where T : class
{
    protected readonly ModuleDbContext Context = context;

    /// <summary>
    /// Enlists this context on the shared connection+transaction when a cross-module unit of work
    /// is active. MUST run before the first EF I/O in a method (SetDbConnection requires a closed
    /// connection). Idempotent and a no-op when no UoW is active.
    /// </summary>
    protected Task EnsureEnlistedAsync(CancellationToken ct) =>
        unitOfWork is { IsActive: true } ? unitOfWork.EnlistAsync(Context, ct) : Task.CompletedTask;

    public virtual async Task<Result<T>> GetByIdAsync(TId id, CancellationToken cancellationToken = default)
    {
        await EnsureEnlistedAsync(cancellationToken);
        var entity = await Context.Set<T>().FindAsync([id], cancellationToken);
        return entity is null
            ? Result.Fail(new NotFoundError($"{typeof(T).Name} with id '{id}' not found."))
            : Result.Ok(entity);
    }

    public virtual async Task AddAsync(T entity, CancellationToken cancellationToken = default)
    {
        await EnsureEnlistedAsync(cancellationToken);
        await Context.Set<T>().AddAsync(entity, cancellationToken);
        await Context.SaveChangesAsync(cancellationToken);
    }

    public virtual async Task UpdateAsync(T entity, CancellationToken cancellationToken = default)
    {
        await EnsureEnlistedAsync(cancellationToken);

        // Only explicitly attach when detached — if the entity is already tracked (loaded via
        // a tracking query in the same scope), calling Update() forces ALL related entities to
        // Modified state, which causes DbUpdateConcurrencyException when new child entities
        // (with non-default Guid keys) haven't been persisted yet.
        if (Context.Entry(entity).State == EntityState.Detached)
            Context.Set<T>().Update(entity);

        await Context.SaveChangesAsync(cancellationToken);
    }

    public virtual async Task DeleteAsync(TId id, CancellationToken cancellationToken = default)
    {
        await EnsureEnlistedAsync(cancellationToken);
        var entity = await Context.Set<T>().FindAsync([id], cancellationToken);
        if (entity is null) return;

        // SoftDeleteInterceptor converts EntityState.Deleted → IsDeleted = true automatically
        Context.Set<T>().Remove(entity);
        await Context.SaveChangesAsync(cancellationToken);
    }
}
