using AqtCoreFW.Rules.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.Tests.SharedFixtures;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;

namespace AqtCoreFW.TenantIsolation.Tests.Fixtures;

/// <summary>
/// SQL Server fixture for Rules module TenantIsolation tests with hybrid execution support.
/// Local SQL path: persistent DB with truncate/reseed. Testcontainers path: ephemeral container.
/// Shared across all tests in the RulesTenantIsolation collection — DB setup runs once.
/// </summary>
public sealed class RulesTenantIsolationFixture : IAsyncLifetime
{
    private const string DbName = "AqtCoreFW_Test_RulesTenantIso";

    private readonly LocalSqlOptions _opts = LocalSqlOptions.FromEnvironment();

    private readonly MsSqlContainer _container = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
        .WithPassword("Gov@Test1234!")
        .Build();

    // Cached connection string for local SQL path
    private string? _localConnectionString;

    /// <summary>
    /// Connection string with TrustServerCertificate=True.
    /// Local SQL path: points to persistent AqtCoreFW_Test_RulesTenantIso database.
    /// Testcontainers path: points to ephemeral container database.
    /// </summary>
    public string ConnectionString
    {
        get
        {
            if (_opts.IsEnabled)
            {
                return _localConnectionString
                    ?? LocalSqlHelper.BuildConnectionString(_opts, DbName);
            }

            var csb = new SqlConnectionStringBuilder(_container.GetConnectionString())
            {
                TrustServerCertificate = true,
                Encrypt = SqlConnectionEncryptOption.Optional,
            };
            return csb.ConnectionString;
        }
    }

    public async Task InitializeAsync()
    {
        if (_opts.IsEnabled)
        {
            // Local SQL path: ensure DB exists, run migrations, truncate data
            await LocalSqlHelper.EnsureDatabaseExistsAsync(_opts, DbName);
            _localConnectionString = LocalSqlHelper.BuildConnectionString(_opts, DbName);

            // Run EF migrations (idempotent)
            await using var ctx = CreateContext(new TestTenantContext(null, isSystemAdmin: true));
            await ctx.Database.MigrateAsync();

            // Truncate data tables after migrations complete
            await LocalSqlHelper.TruncateAllDataAsync(_localConnectionString);
            return;
        }

        // Testcontainers path: start ephemeral container
        await _container.StartAsync();

        // Run EF migrations once so all tests share a migrated schema.
        // Use NullTenantContext (IsSystemAdmin=true) so the migration setup sees all data.
        await using var testCtx = CreateContext(new TestTenantContext(null, isSystemAdmin: true));
        await testCtx.Database.MigrateAsync();
    }

    public Task DisposeAsync()
    {
        if (_opts.IsEnabled)
        {
            // Local SQL path: no cleanup — persistent DB reused across runs
            return Task.CompletedTask;
        }

        // Testcontainers path: stop and remove container
        return _container.StopAsync();
    }

    /// <summary>
    /// Creates a <see cref="RulesDbContext"/> scoped to the provided <paramref name="tenantContext"/>.
    /// Each call returns a fresh, independent context — callers are responsible for disposal.
    /// </summary>
    public RulesDbContext CreateContext(ITenantContext tenantContext)
    {
        var opts = new DbContextOptionsBuilder<RulesDbContext>()
            .UseSqlServer(ConnectionString, sql =>
                sql.MigrationsHistoryTable("__EFMigrationsHistory", "rules"))
            // Disable model cache so each context gets a fresh compiled model
            // with its own captured ITenantContext constant.
            // Without this, EF re-uses the first-built model across all contexts
            // in the process — the global filter would always see the migration-time
            // NullTenantContext (IsSystemAdmin=true) regardless of what we pass here.
            .EnableServiceProviderCaching(false)
            .Options;

        return new RulesDbContext(opts, tenantContext);
    }
}

[CollectionDefinition(CollectionName)]
public sealed class RulesTenantIsolationCollection : ICollectionFixture<RulesTenantIsolationFixture>
{
    public const string CollectionName = "RulesTenantIsolation";
}
