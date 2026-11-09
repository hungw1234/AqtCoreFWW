using AqtCoreFW.Identity.Infrastructure.Persistence;
using AqtCoreFW.Identity.Infrastructure.Services.Signing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Identity.Infrastructure.BackgroundJobs;

/// <summary>
/// Runs a per-tenant cleanup sweep that stays correct under a least-privilege (non-db_owner) DB login.
///
/// <para>Background hosted services have no ambient tenant, so <c>TenantSessionContextInterceptor</c> sets no
/// <c>SESSION_CONTEXT('TenantId')</c>. An all-tenant sweep against an RLS-protected table would then match
/// ZERO rows under a least-privilege login — the RLS predicate is <c>IS_MEMBER('db_owner')=1 OR TenantId =
/// SESSION_CONTEXT('TenantId')</c> — and silently skip cleanup. This helper enumerates every tenant from the
/// NON-RLS <c>Tenants</c> registry (readable without db_owner and without an ambient tenant), then runs the
/// caller's sweep once per tenant with <c>SESSION_CONTEXT</c> pinned to that tenant, so the RLS predicate
/// matches under both db_owner and least-privilege logins.</para>
///
/// <para>Each tenant gets a fresh DI scope (fresh <see cref="IdentityDbContext"/> + connection) so the pinned
/// session context applies only to that tenant's sweep and cannot bleed to the next iteration; the pooled
/// connection is reset (<c>sp_reset_connection</c> clears SESSION_CONTEXT) when the scope disposes.</para>
///
/// <para>Load-bearing assumption: every scoped row's <c>TenantId</c> exists in the <c>Tenants</c> registry
/// (rows are created in a tenant's request scope; tenants are archived via Status, never hard-deleted). A row
/// whose tenant is absent from the registry would never be enumerated and thus never swept — if a hard-delete
/// tenant feature is ever added, this cleanup path must be revisited to avoid reintroducing unbounded growth.</para>
/// </summary>
internal static class TenantScopedSweep
{
    /// <summary>
    /// Enumerates all tenants and invokes <paramref name="sweepOne"/> once per tenant with that tenant's
    /// <c>SESSION_CONTEXT</c> pinned. The callback MUST also constrain its own query by the passed tenantId
    /// (belt-and-braces: under a db_owner connection the RLS filter is bypassed, so without the explicit
    /// predicate every row would be processed on every tenant's iteration). Returns the summed count the
    /// callback reported across all tenants.
    /// </summary>
    public static async Task<int> RunAsync(
        IServiceProvider serviceProvider,
        Func<IdentityDbContext, Guid, CancellationToken, Task<int>> sweepOne,
        ILogger logger,
        CancellationToken ct)
    {
        // Read the tenant universe from the NON-RLS Tenants table — no db_owner and no ambient tenant
        // needed. A dedicated scope keeps this read isolated from the per-tenant sweeps below.
        List<Guid> tenantIds;
        using (var scope = serviceProvider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            tenantIds = await db.Tenants
                .Select(t => t.PublicId)
                .ToListAsync(ct)
                .ConfigureAwait(false);
        }

        var total = 0;
        foreach (var tenantId in tenantIds)
        {
            try
            {
                // Fresh scope per tenant so the pinned SESSION_CONTEXT is scoped to this iteration only.
                using var scope = serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

                // Pin SESSION_CONTEXT('TenantId') BEFORE the sweep so the RLS FILTER predicate exposes this
                // tenant's rows under a least-privilege login (the interceptor sets nothing for a null ambient
                // tenant). Reuses the same primitive the anonymous doc-flow callback path relies on.
                await DocFlowTenantSession.PinAsync(db, tenantId, ct).ConfigureAwait(false);

                total += await sweepOne(db, tenantId, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw; // shutdown — propagate, do not swallow
            }
            catch (Exception ex)
            {
                // Poison-tenant isolation: one tenant's failure (transient deadlock, concurrency conflict)
                // must not starve the tenants ordered after it in this tick. Log and continue with a partial
                // total; the next scheduled tick retries the skipped tenant.
                logger.LogError(ex,
                    "TenantScopedSweep: sweep failed for tenant {TenantId} — skipping it this tick", tenantId);
            }
        }

        return total;
    }
}
