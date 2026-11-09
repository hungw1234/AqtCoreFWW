using AqtCoreFW.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AqtCoreFW.Infrastructure.Persistence;

/// <summary>
/// EF SaveChanges interceptor — converts hard DELETE operations to soft deletes (IsDeleted = true).
/// Prevents accidental permanent removal of auditable entities.
/// Stateless — registered as singleton.
/// </summary>
public sealed class SoftDeleteInterceptor : SaveChangesInterceptor
{
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null)
            ConvertDeletesToSoftDelete(eventData.Context);

        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        if (eventData.Context is not null)
            ConvertDeletesToSoftDelete(eventData.Context);

        return base.SavingChanges(eventData, result);
    }

    private static void ConvertDeletesToSoftDelete(DbContext context)
    {
        var deletedEntries = context.ChangeTracker.Entries()
            .Where(e => e.State == EntityState.Deleted && e.Entity is ISoftDeletable)
            .ToList();

        foreach (var entry in deletedEntries)
        {
            entry.State = EntityState.Modified;
            ((ISoftDeletable)entry.Entity).MarkDeleted();
        }
    }
}
