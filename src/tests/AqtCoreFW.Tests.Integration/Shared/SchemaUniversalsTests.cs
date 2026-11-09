using System.IO;
using System.Threading.Tasks;
using AqtCoreFW.Tests.Integration.Infrastructure;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Xunit;

namespace AqtCoreFW.Tests.Integration.Shared;

/// <summary>
/// Phase-06 success-criteria gate — verifies the regen'd InitialCreateFoundationV1 migration
/// produced the schema invariants the foundation refactor requires:
///   - datetime2(3) precision on Created/Updated timestamps
///   - composite clustered (CreatedAt, Id) on hot/append-heavy tables
///   - TenantId NOT NULL on tenant-scoped entities
///   - PAGE compression on all app-schema partitions (post-migration script effect)
///
/// Runs against the Testcontainers SQL Server provisioned by DatabaseFixture, so it validates
/// the migration files themselves — not just the dev DB.
/// </summary>
public class SchemaUniversalsTests : IntegrationTestBase
{
    private static readonly string[] AppSchemas =
    [
        "ai", "alerting", "audit", "backup", "collab", "config",
        "files", "forms", "gateway", "identity", "integration",
        "masterdata", "messaging", "notifications", "organization",
        "reporting", "rules", "webhooks", "workflow"
    ];

    private readonly string _connectionString;

    public SchemaUniversalsTests(DatabaseFixture db) : base(db)
    {
        _connectionString = db.SqlConnectionString;
    }

    private string ConnectionString => _connectionString;

    [Fact]
    public async Task CreatedAt_And_UpdatedAt_Are_Datetime2_Precision3()
    {
        const string sql = @"
SELECT t.name AS table_name, c.name AS column_name, c.scale
FROM sys.columns c
INNER JOIN sys.tables t ON t.object_id = c.object_id
INNER JOIN sys.types ty ON ty.user_type_id = c.user_type_id
WHERE c.name IN (N'CreatedAt', N'UpdatedAt')
  AND ty.name = N'datetime2'
  AND t.is_ms_shipped = 0
  AND SCHEMA_NAME(t.schema_id) IN (SELECT value FROM STRING_SPLIT(@schemas, ','))
  AND c.scale <> 3";

        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@schemas", string.Join(",", AppSchemas));

        var offenders = new System.Collections.Generic.List<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            offenders.Add($"{reader.GetString(0)}.{reader.GetString(1)} (scale={reader.GetByte(2)})");
        }

        offenders.Should().BeEmpty(
            "CreatedAt/UpdatedAt MUST be datetime2(3) per Phase-2 foundation rule. Offenders: {0}",
            string.Join(", ", offenders));
    }

    [Fact]
    public async Task HotTables_Have_Composite_Clustered_CreatedAt_Id()
    {
        // Hot tables (append-heavy / range-scanned by recency) require clustered (CreatedAt, Id)
        // per HotTableConfigurationBase<T : Entity>. T3 plain-class entities cannot inherit
        // HotTableConfigurationBase (Entity constraint) — they opt out and use monotonic
        // InternalId-clustered which is acceptable for append-only sequential writes.
        // Sampled tables MUST be Entity-derived configs that opted into the base class.
        var hotTables = new (string Schema, string Table)[]
        {
            ("workflow", "WorkflowTasks"),
        };

        const string sql = @"
SELECT i.name, c1.name AS first_col, c2.name AS second_col
FROM sys.indexes i
INNER JOIN sys.tables t ON t.object_id = i.object_id
INNER JOIN sys.index_columns ic1 ON ic1.object_id = i.object_id AND ic1.index_id = i.index_id AND ic1.key_ordinal = 1
INNER JOIN sys.columns c1 ON c1.object_id = ic1.object_id AND c1.column_id = ic1.column_id
LEFT  JOIN sys.index_columns ic2 ON ic2.object_id = i.object_id AND ic2.index_id = i.index_id AND ic2.key_ordinal = 2
LEFT  JOIN sys.columns c2 ON c2.object_id = ic2.object_id AND c2.column_id = ic2.column_id
WHERE i.type_desc = N'CLUSTERED'
  AND SCHEMA_NAME(t.schema_id) = @schema
  AND t.name = @table";

        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();

        foreach (var (schema, table) in hotTables)
        {
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@schema", schema);
            cmd.Parameters.AddWithValue("@table", table);

            await using var reader = await cmd.ExecuteReaderAsync();
            var found = await reader.ReadAsync();
            found.Should().BeTrue("hot table {0}.{1} must have a clustered index", schema, table);

            var firstCol = reader.GetString(1);
            var secondCol = reader.IsDBNull(2) ? null : reader.GetString(2);

            firstCol.Should().Be("CreatedAt",
                "{0}.{1} clustered index must lead with CreatedAt", schema, table);
            secondCol.Should().Be("Id",
                "{0}.{1} clustered index must include Id as second key", schema, table);
        }
    }

    [Fact]
    public async Task TenantScoped_Tables_Have_TenantId_NotNull()
    {
        // Sample of tables that implement ITenantScoped — TenantId must be NOT NULL.
        // Spot-check 4 modules; failure here indicates the entity rebase missed the
        // ITenantScoped → required-property mapping.
        var tenantScoped = new (string Schema, string Table)[]
        {
            ("workflow", "WorkflowDefinitions"),
            ("forms", "FormTemplates"),
            ("integration", "MessageLogs"),
            ("rules", "RuleSets"),
        };

        const string sql = @"
SELECT c.is_nullable
FROM sys.columns c
INNER JOIN sys.tables t ON t.object_id = c.object_id
WHERE c.name = N'TenantId'
  AND SCHEMA_NAME(t.schema_id) = @schema
  AND t.name = @table";

        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();

        foreach (var (schema, table) in tenantScoped)
        {
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@schema", schema);
            cmd.Parameters.AddWithValue("@table", table);

            var nullable = await cmd.ExecuteScalarAsync();
            nullable.Should().NotBeNull("{0}.{1}.TenantId column must exist", schema, table);
            ((bool)nullable!).Should().BeFalse(
                "{0}.{1}.TenantId must be NOT NULL (ITenantScoped invariant)", schema, table);
        }
    }

    [Fact]
    public async Task PageCompression_Script_Applies_To_All_App_Tables()
    {
        // Apply the post-migration page-compression script first (idempotent — Phase-06 Step 7).
        // This validates the deliverable ./infra/scripts/apply-page-compression.sql works against
        // the regen'd schema. After application, every partition in app schemas (excluding
        // OpenIddict framework tables) MUST be PAGE compressed.
        var scriptPath = LocateScript("apply-page-compression.sql");
        var script = await File.ReadAllTextAsync(scriptPath);

        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();

        // Split on GO not needed — script uses sp_executesql in single batch
        await using (var apply = new SqlCommand(script, conn) { CommandTimeout = 120 })
        {
            await apply.ExecuteNonQueryAsync();
        }

        const string verifySql = @"
SELECT COUNT(*) AS uncompressed_count
FROM sys.partitions p
INNER JOIN sys.tables t ON t.object_id = p.object_id
WHERE t.is_ms_shipped = 0
  AND p.data_compression = 0
  AND SCHEMA_NAME(t.schema_id) IN (SELECT value FROM STRING_SPLIT(@schemas, ','))
  AND t.name NOT LIKE 'OpenIddict%'";

        await using var verify = new SqlCommand(verifySql, conn);
        verify.Parameters.AddWithValue("@schemas", string.Join(",", AppSchemas));

        var uncompressed = (int)(await verify.ExecuteScalarAsync() ?? 0);
        uncompressed.Should().Be(0,
            "page compression must cover every app-schema partition after apply-page-compression.sql runs");
    }

    private static string LocateScript(string fileName)
    {
        // Walk up from the test bin dir to find the repo's infra/scripts/ folder.
        var dir = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "infra", "scripts", fileName);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException($"Could not locate infra/scripts/{fileName} from {System.AppContext.BaseDirectory}");
    }
}
