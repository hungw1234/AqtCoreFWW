using AqtCoreFW.SharedKernel.Application.Data;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Infrastructure.Persistence;

/// <summary>
/// Infrastructure-layer extension for <see cref="ICrossModuleUnitOfWork"/> that enlists an
/// EF Core <see cref="DbContext"/> onto the ambient shared connection and transaction.
///
/// This lives in Infrastructure (not SharedKernel) because <see cref="DbContext"/> is defined
/// in Microsoft.EntityFrameworkCore, and adding that dependency to SharedKernel would expand
/// the cross-cutting dependency surface across every module that imports SharedKernel — even
/// those that have no EF dependency.
/// </summary>
public static class CrossModuleUnitOfWorkExtensions
{
    /// <summary>
    /// Enlists <paramref name="ctx"/> onto the ambient unit of work connection and transaction
    /// so its <c>SaveChangesAsync</c> participates in the same local SQL transaction.
    ///
    /// Idempotent: calling this twice with the same context instance is safe — the second call
    /// is a no-op. Tracked by reference equality on the concrete <see cref="CrossModuleUnitOfWork"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown if <paramref name="uow"/> is not active, or if it is not the concrete
    /// <see cref="CrossModuleUnitOfWork"/> type (i.e. a different implementation was registered).
    /// </exception>
    public static async Task EnlistAsync(
        this ICrossModuleUnitOfWork uow,
        DbContext ctx,
        CancellationToken ct = default)
    {
        if (!uow.IsActive)
            throw new InvalidOperationException(
                "Cannot enlist a DbContext — the unit of work is not active. Call BeginAsync first.");

        // Cast to concrete to access the enlist-tracking set. Active test doubles that aren't a
        // subclass of CrossModuleUnitOfWork will fall to the downcast guard below and throw — by
        // design. To exercise enlistment in tests, either use the concrete CrossModuleUnitOfWork
        // against a real DB, or subclass it for an in-memory shim.
        if (uow is not CrossModuleUnitOfWork concrete)
            throw new InvalidOperationException(
                $"EnlistAsync requires a {nameof(CrossModuleUnitOfWork)} instance. " +
                $"Got: {uow.GetType().FullName}. Register CrossModuleUnitOfWork as the ICrossModuleUnitOfWork implementation.");

        // Skip re-enlisting the same context instance — idempotent by reference.
        if (!concrete.EnlistedContexts.Add(ctx))
            return;

        // Wire the DbContext to use the shared connection WITHOUT taking ownership — the UoW
        // owns the connection lifetime; disposing the DbContext must not close it.
        ctx.Database.SetDbConnection(concrete.Connection);

        // Enlist on the ambient transaction so EF's SaveChangesAsync sends commands within it.
        await ctx.Database.UseTransactionAsync(concrete.Transaction, ct);
    }
}
