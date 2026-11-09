using AqtCoreFW.SharedKernel.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AqtCoreFW.Infrastructure.Persistence;

/// <summary>
/// EF SaveChanges interceptor — auto-populates CreatedBy and ModifiedBy on AuditableEntity.
/// Resolves current user from IHttpContextAccessor (request scope).
/// For background jobs (no HttpContext), audit fields are left at their default values.
/// </summary>
public sealed class AuditableEntityInterceptor(IHttpContextAccessor httpContextAccessor)
    : SaveChangesInterceptor
{
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null)
            ApplyAuditFields(eventData.Context);

        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        if (eventData.Context is not null)
            ApplyAuditFields(eventData.Context);

        return base.SavingChanges(eventData, result);
    }

    private void ApplyAuditFields(DbContext context)
    {
        var currentUserId = GetCurrentUserId();
        if (!currentUserId.HasValue) return;

        foreach (var entry in context.ChangeTracker.Entries<AuditableEntity>())
        {
            if (entry.State == EntityState.Added)
                entry.Entity.SetAuditCreate(currentUserId.Value);
            else if (entry.State == EntityState.Modified)
                entry.Entity.SetAuditUpdate(currentUserId.Value);
        }
    }

    private Guid? GetCurrentUserId()
    {
        // "sub" = OIDC standard subject claim; "userId" = internal fallback
        var claim = httpContextAccessor.HttpContext?.User?.FindFirst("sub")
            ?? httpContextAccessor.HttpContext?.User?.FindFirst("userId");

        return claim is not null && Guid.TryParse(claim.Value, out var id) ? id : null;
    }
}
