using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Identity.Infrastructure.Persistence;

/// <summary>
/// EF Core interceptor that recomputes all 4 email hash columns whenever an <see cref="ApplicationUser"/>
/// entry is Added or Modified with a non-null Email / NormalizedEmail.
///
/// Hash column strategy (Fixup P0-1 — two-column split):
///   • <see cref="ApplicationUser.EmailHashGlobal"/> / <see cref="ApplicationUser.NormalizedEmailHashGlobal"/>
///     — always computed with Guid.Empty pepper (global sentinel). These are the login lookup columns.
///     MUST match <c>EmailHashedUserStore.FindByEmailAsync</c> which also uses Guid.Empty.
///   • <see cref="ApplicationUser.EmailHashTenant"/> / <see cref="ApplicationUser.NormalizedEmailHashTenant"/>
///     — computed with per-tenant pepper; NULL when TenantId is null or Guid.Empty.
///     For future admin/audit scoped queries — NOT used in login path.
///
/// Why interceptor (not domain setter):
///   • Keeps <c>EmailHashService</c> (Infrastructure) out of the Domain layer.
///   • Fires reliably for any code path that calls SaveChanges: EF identity pipeline,
///     admin bulk-updates, seeders — no call-site coupling.
///   • Idempotent: re-hashing the same email with the same pepper produces the same hash.
///
/// Vault-miss resilience: if the pepper is not provisioned yet (dev Vault startup, missing secret),
/// the hash computation is SKIPPED and a warning is logged. Hash columns remain NULL until
/// the BackfillEmailHashJob runs. This prevents seeder/boot failures when Vault isn't ready.
/// Production deployments MUST provision peppers BEFORE serving traffic.
///
/// Registration: <c>options.AddInterceptors(sp.GetRequiredService&lt;EmailHashInterceptor&gt;())</c>
/// Lifetime: Scoped (EmailHashService → IPepperResolver is Singleton; interceptor is safe as Scoped).
/// </summary>
public sealed class EmailHashInterceptor(
    EmailHashService emailHashService,
    ILogger<EmailHashInterceptor> logger) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        PopulateHashes(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        PopulateHashes(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void PopulateHashes(DbContext? context)
    {
        if (context is null) return;

        foreach (var entry in context.ChangeTracker.Entries<ApplicationUser>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified))
                continue;

            var user = entry.Entity;

            // Only recompute if Email changed (avoids Vault round-trip on unrelated saves).
            if (entry.State == EntityState.Modified
                && !entry.Property(u => u.Email).IsModified
                && !entry.Property(u => u.NormalizedEmail).IsModified)
                continue;

            // P0-1 fix: global hash MUST use Guid.Empty so FindByEmailAsync (which always
            // hashes with Guid.Empty) can find the row. Per-tenant hash is supplemental.
            try
            {
                // Global columns — login lookup path (Guid.Empty pepper, cross-tenant safe).
                if (!string.IsNullOrWhiteSpace(user.Email))
                    user.EmailHashGlobal = emailHashService.ComputeHash(user.Email, Guid.Empty);

                if (!string.IsNullOrWhiteSpace(user.NormalizedEmail))
                    user.NormalizedEmailHashGlobal = emailHashService.ComputeHash(user.NormalizedEmail, Guid.Empty);

                // Tenant columns — per-tenant pepper; NULL for users without a tenant scope.
                if (user.TenantId is Guid tid && tid != Guid.Empty)
                {
                    if (!string.IsNullOrWhiteSpace(user.Email))
                        user.EmailHashTenant = emailHashService.ComputeHash(user.Email, tid);

                    if (!string.IsNullOrWhiteSpace(user.NormalizedEmail))
                        user.NormalizedEmailHashTenant = emailHashService.ComputeHash(user.NormalizedEmail, tid);
                }
                else
                {
                    user.EmailHashTenant = null;
                    user.NormalizedEmailHashTenant = null;
                }
            }
            catch (KeyNotFoundException ex)
            {
                // Pepper not provisioned yet — hash columns remain NULL.
                // BackfillEmailHashJob will populate them once Vault is ready.
                // PRODUCTION: this must not occur — alert if logged in prod.
                logger.LogWarning(ex,
                    "Email pepper not found in Vault for tenant {TenantId}. " +
                    "EmailHash*/NormalizedEmailHash* will be NULL for user {UserId}. " +
                    "Run BackfillEmailHashJob after provisioning the pepper.",
                    user.TenantId?.ToString("N") ?? Guid.Empty.ToString("N"),
                    user.Id);
            }
        }
    }
}
