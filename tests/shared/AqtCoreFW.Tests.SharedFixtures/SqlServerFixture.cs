using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

namespace AqtCoreFW.Tests.SharedFixtures;

/// <summary>
/// SQL Server fixture supporting hybrid execution: local SQL Server (dev) or Testcontainers (CI).
/// Controlled by TEST_USE_LOCAL_SQL env var. Shared across all tests in the collection.
/// Local path: uses persistent DB with truncate/reseed. Testcontainers path: ephemeral container.
/// </summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    private const string DbName = "AqtCoreFW_Test_Main";

    private readonly LocalSqlOptions _opts = LocalSqlOptions.FromEnvironment();

    private readonly MsSqlContainer? _container;

    public SqlServerFixture()
    {
        if (!_opts.IsEnabled)
        {
            _container = new MsSqlBuilder()
                .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
                .WithPassword("Gov@Test1234!")   // meets SA password complexity requirements
                .Build();
        }
    }

    /// <summary>
    /// Connection string with TrustServerCertificate=True and Encrypt=Optional.
    /// Local SQL path: points to persistent AqtCoreFW_Test_Main database.
    /// Testcontainers path: points to ephemeral container database.
    /// </summary>
    public string ConnectionString
    {
        get
        {
            if (_opts.IsEnabled)
            {
                return LocalSqlHelper.BuildConnectionString(_opts, DbName);
            }

            var csb = new SqlConnectionStringBuilder(_container!.GetConnectionString())
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
            // Local SQL path: ensure DB exists, truncate data (migrations run via WebAppFixture boot)
            await LocalSqlHelper.EnsureDatabaseExistsAsync(_opts, DbName);
            await LocalSqlHelper.TruncateAllDataAsync(ConnectionString);
            return;
        }

        // Testcontainers path: start ephemeral container
        await _container!.StartAsync();
    }

    public Task DisposeAsync()
    {
        if (_opts.IsEnabled)
        {
            // Local SQL path: no cleanup — persistent DB reused across runs
            return Task.CompletedTask;
        }

        // Testcontainers path: stop and remove container
        return _container!.StopAsync();
    }

    /// <summary>Opens a raw connection. Caller is responsible for disposing.</summary>
    public SqlConnection OpenConnection() => new(ConnectionString);
}

/// <summary>xUnit collection definition — ensures all integration tests share one SQL Server container.</summary>
[CollectionDefinition(CollectionName)]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerFixture>
{
    public const string CollectionName = "SqlServer";
}
