using AqtCoreFW.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Identity.Infrastructure.Services.Signing;

/// <summary>
/// Pins the doc-flow handler's EF connection to a JWT-derived tenant via SQL Server SESSION_CONTEXT, so
/// the Row-Level-Security predicates on the signing tables filter to that tenant. These callbacks are
/// <c>[AllowAnonymous]</c> machine-to-machine, so the request-scoped tenant interceptor (which reads the
/// authenticated tenant) sets nothing — the signed JWT tenant_id is the source. Opening the connection
/// here keeps it open for the subsequent queries so the context persists; the request-scoped DbContext
/// closes it on dispose, and pooling resets SESSION_CONTEXT (sp_reset_connection) on reuse so the value
/// does not bleed to the next borrower.
/// </summary>
internal static class DocFlowTenantSession
{
    public static async Task PinAsync(IdentityDbContext db, Guid tenantId, CancellationToken ct)
    {
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await db.Database.OpenConnectionAsync(ct).ConfigureAwait(false);

        await db.Database.ExecuteSqlRawAsync(
            "EXEC sp_set_session_context N'TenantId', {0}",
            parameters: [tenantId.ToString()], cancellationToken: ct).ConfigureAwait(false);
    }
}
