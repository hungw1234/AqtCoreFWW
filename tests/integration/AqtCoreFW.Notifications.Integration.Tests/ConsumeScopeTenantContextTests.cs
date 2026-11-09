using AqtCoreFW.Infrastructure.Security;
using AqtCoreFW.Notifications.Infrastructure.Persistence;
using AqtCoreFW.Tests.SharedFixtures;
using Dapper;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AqtCoreFW.Notifications.Integration.Tests;

/// <summary>
/// Slice 1D — proves the consume-scope tenant chain end to end: an <see cref="AmbientTenantScope"/>
/// push (what <c>TenantScopeConsumeFilter</c> does from a message payload) flows through the scoped
/// <c>ITenantContext</c> factory into <c>TenantSessionContextInterceptor</c>, so a module DbContext
/// opened afterwards has SQL Server SESSION_CONTEXT pinned to that tenant — the value the RLS
/// predicates on tenant tables (e.g. <c>notifications.Notifications</c>) read.
///
/// This is the mechanism proof that survives the current db_owner login: SESSION_CONTEXT is
/// readable regardless of privilege, so the read-back is not a false-green. Enforcement of the RLS
/// BLOCK/FILTER predicate itself still needs a non-db_owner login harness (shared debt with
/// PUBLIC-FORMS-LEAST-PRIV-ANON-READ) and is out of scope here.
///
/// Lives in the Notifications integration project because the real beneficiary is the Notifications
/// write performed by <c>FormSubmissionAcknowledgedConsumer</c>; the chain itself is DbContext-agnostic.
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class ConsumeScopeTenantContextTests : IClassFixture<WebAppFixture>
{
    private const string ReadSessionTenant =
        "SELECT CAST(SESSION_CONTEXT(N'TenantId') AS nvarchar(64))";

    private readonly WebAppFixture _app;

    public ConsumeScopeTenantContextTests(WebAppFixture app) => _app = app;

    [Fact]
    public async Task AmbientTenantPush_PinsSessionContext_ForModuleDbConnection()
    {
        var tenant = Guid.NewGuid();

        await using var scope = _app.Services.CreateAsyncScope();
        var ambient = scope.ServiceProvider.GetRequiredService<AmbientTenantScope>();

        using (ambient.Push(tenant))
        {
            // Resolve the DbContext AFTER the push — this mirrors the real consume path (the filter
            // pushes the tenant, THEN the consumer resolves its DbContext), and matters because the
            // interceptor captures ITenantContext when the DbContext is built.
            var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
            await db.Database.OpenConnectionAsync();
            try
            {
                var pinned = await db.Database.GetDbConnection()
                    .QuerySingleAsync<string?>(ReadSessionTenant);

                pinned.Should().Be(tenant.ToString(),
                    "a payload-derived ambient tenant must reach SESSION_CONTEXT so RLS scopes the consumer's writes");
            }
            finally
            {
                await db.Database.CloseConnectionAsync();
            }
        }
    }

    [Fact]
    public async Task NoAmbientTenant_LeavesSessionContextUnset()
    {
        // Control: without an HttpContext and without an ambient push, the scope resolves the
        // cross-tenant background-job context (TenantId = null), so the interceptor skips the pin.
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();

        await db.Database.OpenConnectionAsync();
        try
        {
            var pinned = await db.Database.GetDbConnection()
                .QuerySingleAsync<string?>(ReadSessionTenant);

            pinned.Should().BeNull(
                "no HttpContext + no ambient push must not pin SESSION_CONTEXT (background/cross-tenant scope)");
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }
}
