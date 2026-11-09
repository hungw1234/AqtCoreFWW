using AqtCoreFW.Infrastructure.Messaging;
using AqtCoreFW.Tests.SharedFixtures;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.MsSql;

namespace AqtCoreFW.Messaging.Integration.Tests.Fixtures;

/// <summary>
/// Shared fixture for messaging integration tests.
/// Supports hybrid execution: local SQL Server (dev) or Testcontainers (CI).
/// Wires MassTransit InMemory test harness + SQL Server for MessagingDbContext.
/// All tests in [Collection("MessagingIntegration")] share one DB cold-start.
/// </summary>
public sealed class MessagingFixture : IAsyncLifetime
{
    private const string DbName = "AqtCoreFW_Test_Messaging";

    private readonly LocalSqlOptions _opts = LocalSqlOptions.FromEnvironment();

    private readonly MsSqlContainer _sqlContainer = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
        .WithPassword("Gov@Test1234!")
        .WithCleanUp(true)
        .Build();

    // Root DI container — resolved after harness starts
    public IServiceProvider Services { get; private set; } = null!;

    // MassTransit test harness — provides .Published, .Consumed, .Sent collections
    public ITestHarness Harness { get; private set; } = null!;

    public string SqlConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        if (_opts.IsEnabled)
        {
            // Local SQL path: ensure DB exists, provision schema, truncate data
            await LocalSqlHelper.EnsureDatabaseExistsAsync(_opts, DbName);
            SqlConnectionString = LocalSqlHelper.BuildConnectionString(_opts, DbName);
            await EnsureMessagingSchemaAsync();
            await LocalSqlHelper.TruncateAllDataAsync(SqlConnectionString);
        }
        else
        {
            // Testcontainers path: start ephemeral container
            await _sqlContainer.StartAsync();

            // Build connection string with TrustServerCertificate for SqlClient 6.x
            var csb = new SqlConnectionStringBuilder(_sqlContainer.GetConnectionString())
            {
                TrustServerCertificate = true,
                Encrypt = SqlConnectionEncryptOption.Optional,
            };
            SqlConnectionString = csb.ConnectionString;

            // Ensure messaging schema exists (dead_letters table)
            await EnsureMessagingSchemaAsync();
        }

        var services = new ServiceCollection();

        // MessagingDbContext — needed by DeadLetterConsumer and DeadLetterService
        services.AddDbContext<MessagingDbContext>(opts =>
            opts.UseSqlServer(SqlConnectionString));

        // MassTransit InMemory with test harness
        // AddMassTransitTestHarness replaces real bus with InMemoryTestHarness
        services.AddMassTransitTestHarness(x =>
        {
            x.SetKebabCaseEndpointNameFormatter();
            x.AddConsumer<DeadLetterConsumer>();
            x.AddConsumer<TestOrderConsumer>();
            x.AddConsumer<TestFaultingConsumer>();
        });

        services.AddScoped<DeadLetterService>();

        Services = services.BuildServiceProvider();
        Harness = Services.GetRequiredService<ITestHarness>();
        await Harness.Start();
    }

    public async Task DisposeAsync()
    {
        await Harness.Stop();

        if (_opts.IsEnabled)
        {
            // Local SQL path: no cleanup — persistent DB reused across runs
            return;
        }

        // Testcontainers path: dispose container
        await _sqlContainer.DisposeAsync();
    }

    private async Task EnsureMessagingSchemaAsync()
    {
        // Apply EF migrations against the test DB. This proves the InitialCreate migration
        // (added v2.133p4) applies cleanly on a fresh DB and is idempotent on re-runs.
        await using var db = new MessagingDbContext(
            new DbContextOptionsBuilder<MessagingDbContext>()
                .UseSqlServer(SqlConnectionString, sql =>
                    sql.MigrationsHistoryTable("__EFMigrationsHistory", "messaging"))
                .Options);
        await db.Database.MigrateAsync();
    }
}

[CollectionDefinition(CollectionName)]
public sealed class MessagingCollection : ICollectionFixture<MessagingFixture>
{
    public const string CollectionName = "MessagingIntegration";
}
