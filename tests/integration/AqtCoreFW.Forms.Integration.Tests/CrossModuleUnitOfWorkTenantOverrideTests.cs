using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.Tests.SharedFixtures;
using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace AqtCoreFW.Forms.Integration.Tests;

/// <summary>
/// Integration coverage for the tenant-override <c>BeginAsync(Guid, CancellationToken)</c> overload on
/// <see cref="ICrossModuleUnitOfWork"/> — added for the anonymous public-form submit path, whose caller
/// has no ambient tenant. Proves the override actually reaches SQL Server SESSION_CONTEXT: the pin that
/// the RLS BLOCK/FILTER predicates on tenant tables read. Lives in the Forms integration project purely
/// to reuse <see cref="WebAppFixture"/>'s real SQL Server wiring; the primitive itself is module-agnostic.
///
/// Note: the integration DB connects as a db_owner login, which BYPASSES the RLS predicate — so this
/// test asserts the SESSION_CONTEXT value is actually set (readable regardless of privilege), which is
/// the property that makes the write succeed under a future least-privilege login.
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class CrossModuleUnitOfWorkTenantOverrideTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _app;

    public CrossModuleUnitOfWorkTenantOverrideTests(WebAppFixture app) => _app = app;

    [Fact]
    public async Task BeginAsync_WithTenantOverride_PinsSessionContextToThatTenant()
    {
        var tenant = Guid.NewGuid();

        await using var scope = _app.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<ICrossModuleUnitOfWork>();

        // Bare scope → ambient ITenantContext carries no tenant; the explicit override is the ONLY
        // source. A non-null read-back therefore proves the override wins over the (null) ambient path.
        await uow.BeginAsync(tenant, CancellationToken.None);
        try
        {
            var pinned = await uow.Connection.QuerySingleAsync<string?>(new CommandDefinition(
                "SELECT CAST(SESSION_CONTEXT(N'TenantId') AS nvarchar(64))",
                transaction: uow.Transaction));

            pinned.Should().Be(tenant.ToString(),
                "the tenant-override overload must pin SESSION_CONTEXT so RLS filters to the resolved tenant");
        }
        finally
        {
            await uow.RollbackAsync(CancellationToken.None);
        }
    }
}
