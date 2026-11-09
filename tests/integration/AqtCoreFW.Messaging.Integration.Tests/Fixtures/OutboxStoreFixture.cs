using System.Text.Json;
using AqtCoreFW.Infrastructure.Configuration;
using AqtCoreFW.Infrastructure.Persistence.Outbox;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Domain.Events;
using AqtCoreFW.Tests.SharedFixtures;
using MassTransit;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Testcontainers.MsSql;

namespace AqtCoreFW.Messaging.Integration.Tests.Fixtures;

/// <summary>
/// Fixture for OUTBOX-1-DISP race tests. Provisions a test schema 'outbox_test'
/// with OutboxMessages table matching production shape. Self-contained
/// (manual CREATE TABLE — does not depend on EF migrations).
/// </summary>
public sealed class OutboxStoreFixture : IAsyncLifetime
{
    private const string DbName = "AqtCoreFW_Test_OutboxStore";
    private const string TestSchema = "outbox_test";

    private readonly LocalSqlOptions _opts = LocalSqlOptions.FromEnvironment();

    private readonly MsSqlContainer _sqlContainer = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
        .WithPassword("Gov@Test1234!")
        .WithCleanUp(true)
        .Build();

    public string SqlConnectionString { get; private set; } = string.Empty;
    public string Schema => TestSchema;
    public IOutboxStore Store { get; private set; } = null!;
    public OutboxOptions Options { get; } = new();

    public async Task InitializeAsync()
    {
        if (_opts.IsEnabled)
        {
            await LocalSqlHelper.EnsureDatabaseExistsAsync(_opts, DbName);
            SqlConnectionString = LocalSqlHelper.BuildConnectionString(_opts, DbName);
        }
        else
        {
            await _sqlContainer.StartAsync();
            var csb = new SqlConnectionStringBuilder(_sqlContainer.GetConnectionString())
            {
                TrustServerCertificate = true,
                Encrypt = SqlConnectionEncryptOption.Optional,
                InitialCatalog = DbName
            };

            // Create target DB
            var masterCsb = new SqlConnectionStringBuilder(csb.ConnectionString) { InitialCatalog = "master" };
            await using (var c = new SqlConnection(masterCsb.ConnectionString))
            {
                await c.OpenAsync();
                await using var cmd = c.CreateCommand();
                cmd.CommandText = $"IF DB_ID('{DbName}') IS NULL CREATE DATABASE [{DbName}];";
                await cmd.ExecuteNonQueryAsync();
            }
            SqlConnectionString = csb.ConnectionString;
        }

        await ProvisionSchemaAsync();

        var dbOptionsMonitor = MockOptionsMonitor(new DatabaseOptions { ConnectionString = SqlConnectionString });
        var outboxOptionsMonitor = MockOptionsMonitor(Options);
        var guidGen = Substitute.For<IGuidGenerator>();
        guidGen.NewIdMonotonic().Returns(_ => Guid.NewGuid());
        Store = new OutboxStore(dbOptionsMonitor, outboxOptionsMonitor, guidGen);
    }

    public async Task DisposeAsync()
    {
        if (!_opts.IsEnabled)
            await _sqlContainer.DisposeAsync();
    }

    public async Task TruncateAsync()
    {
        await using var c = new SqlConnection(SqlConnectionString);
        await c.OpenAsync();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = $"DELETE FROM [{TestSchema}].[OutboxMessages];";
        await cmd.ExecuteNonQueryAsync();
    }

    // OUTBOX-DISP-FU3 (v2.133p16-FU): batch INSERT to avoid N round-trips.
    // Chunk at 100 rows/statement (sweet spot vs SqlCommand parameter count).
    public async Task SeedRowsAsync(int count)
    {
        const int ChunkSize = 100;
        await using var c = new SqlConnection(SqlConnectionString);
        await c.OpenAsync();

        for (int offset = 0; offset < count; offset += ChunkSize)
        {
            int batch = Math.Min(ChunkSize, count - offset);
            await using var cmd = c.CreateCommand();
            var values = new List<string>(batch);
            for (int i = 0; i < batch; i++)
            {
                values.Add($"(@id{i}, @type, @payload, SYSUTCDATETIME(), NULL, 0, @schema, NULL)");
                cmd.Parameters.AddWithValue($"@id{i}", Guid.NewGuid());
            }
            cmd.Parameters.AddWithValue("@type", "TestEvent");
            cmd.Parameters.AddWithValue("@payload", "{}");
            cmd.Parameters.AddWithValue("@schema", TestSchema);
            cmd.CommandText = $@"
INSERT INTO [{TestSchema}].[OutboxMessages]
    (Id, EventType, Payload, CreatedAtUtc, ProcessedAtUtc, RetryCount, SchemaName, ClaimedAtUtc)
VALUES
    {string.Join(",\n    ", values)};";
            await cmd.ExecuteNonQueryAsync();
        }
    }

    public async Task ForceClaimedAtAsync(Guid id, DateTimeOffset claimedAt)
    {
        await using var c = new SqlConnection(SqlConnectionString);
        await c.OpenAsync();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = $"UPDATE [{TestSchema}].[OutboxMessages] SET ClaimedAtUtc = @t WHERE Id = @id;";
        cmd.Parameters.AddWithValue("@t", claimedAt);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    // --- OUTBOX at-least-once (Slice 1E) white-box dispatch helpers ---
    // These drive the REAL OutboxDispatcher against the REAL SQL Store — the full
    // persist→claim→publish→mark cycle that the mocked unit tests cannot exercise.

    /// <summary>
    /// Builds a real <see cref="OutboxDispatcher"/> wired to the fixture's real SQL
    /// <see cref="Store"/> plus a caller-supplied <paramref name="bus"/> (spy / fault-injector).
    /// Registry holds only the test schema; resolver is the real one (scans loaded assemblies,
    /// so a test event implementing <see cref="IExternalDomainEvent"/> resolves via its FullName).
    /// </summary>
    public OutboxDispatcher BuildDispatcher(IBus bus)
    {
        var registry = new OutboxSchemaRegistry();
        registry.Register(TestSchema);
        return new OutboxDispatcher(
            Store,
            bus,
            registry,
            new OutboxEventTypeResolver(),
            // Fully-qualified: the local `Options` property would otherwise shadow the static class.
            Microsoft.Extensions.Options.Options.Create(Options),
            NullLogger<OutboxDispatcher>.Instance);
    }

    /// <summary>
    /// Seeds ONE pending row whose EventType/Payload are dispatch-resolvable — mirrors the
    /// production write shape (bare <c>Type.FullName</c> token + JSON body) so the dispatcher
    /// publishes it rather than dead-lettering. Returns the row Id.
    /// </summary>
    public async Task<Guid> SeedResolvableRowAsync(IExternalDomainEvent evt)
    {
        var id = Guid.NewGuid();
        await using var c = new SqlConnection(SqlConnectionString);
        await c.OpenAsync();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = $@"
INSERT INTO [{TestSchema}].[OutboxMessages]
    (Id, EventType, Payload, CreatedAtUtc, ProcessedAtUtc, RetryCount, SchemaName, ClaimedAtUtc)
VALUES
    (@id, @type, @payload, SYSUTCDATETIME(), NULL, 0, @schema, NULL);";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@type", evt.GetType().FullName!);
        cmd.Parameters.AddWithValue("@payload", JsonSerializer.Serialize(evt, evt.GetType()));
        cmd.Parameters.AddWithValue("@schema", TestSchema);
        await cmd.ExecuteNonQueryAsync();
        return id;
    }

    /// <summary>Reads dispatch-relevant row state (processed / retry / claim) for assertions.</summary>
    public async Task<OutboxRowState> ReadRowStateAsync(Guid id)
    {
        await using var c = new SqlConnection(SqlConnectionString);
        await c.OpenAsync();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = $@"SELECT ProcessedAtUtc, RetryCount, ClaimedAtUtc
                             FROM [{TestSchema}].[OutboxMessages] WHERE Id = @id;";
        cmd.Parameters.AddWithValue("@id", id);
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync())
            throw new InvalidOperationException($"Outbox row {id} not found.");
        return new OutboxRowState(
            r.IsDBNull(0) ? null : r.GetDateTimeOffset(0),
            r.GetInt32(1),
            r.IsDBNull(2) ? null : r.GetDateTimeOffset(2));
    }

    private async Task ProvisionSchemaAsync()
    {
        await using var c = new SqlConnection(SqlConnectionString);
        await c.OpenAsync();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = $@"
IF SCHEMA_ID('{TestSchema}') IS NULL EXEC('CREATE SCHEMA [{TestSchema}]');
IF OBJECT_ID('[{TestSchema}].[OutboxMessages]') IS NULL
BEGIN
    CREATE TABLE [{TestSchema}].[OutboxMessages] (
        Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
        EventType NVARCHAR(500) NOT NULL,
        Payload NVARCHAR(MAX) NOT NULL,
        CreatedAtUtc DATETIMEOFFSET NOT NULL,
        ProcessedAtUtc DATETIMEOFFSET NULL,
        Error NVARCHAR(2000) NULL,
        RetryCount INT NOT NULL DEFAULT 0,
        SchemaName NVARCHAR(50) NOT NULL,
        ClaimedAtUtc DATETIMEOFFSET NULL
    );
    CREATE INDEX IX_OutboxMessages_outbox_test_ProcessedAt
        ON [{TestSchema}].[OutboxMessages] (ProcessedAtUtc);
END";
        await cmd.ExecuteNonQueryAsync();
    }

    private static IOptionsMonitor<T> MockOptionsMonitor<T>(T value) where T : class, new()
    {
        var monitor = Substitute.For<IOptionsMonitor<T>>();
        monitor.CurrentValue.Returns(value);
        return monitor;
    }
}

/// <summary>Dispatch-relevant projection of an OutboxMessages row (Slice 1E assertions).</summary>
public sealed record OutboxRowState(
    DateTimeOffset? ProcessedAtUtc, int RetryCount, DateTimeOffset? ClaimedAtUtc);

[CollectionDefinition(CollectionName)]
public sealed class OutboxStoreCollection : ICollectionFixture<OutboxStoreFixture>
{
    public const string CollectionName = "OutboxStoreIntegration";
}
