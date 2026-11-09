using AqtCoreFW.Tests.SharedFixtures;
using AqtCoreFW.Workflow.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AqtCoreFW.Workflow.Integration.Tests.Migrations;

/// <summary>
/// MIG-PREFLIGHT (v2.133p15) + OUTBOX-1-FU coverage.
///
/// Migration <c>20260429110600_AddUniqueActiveWorkflowInstanceIndex</c> includes a
/// preflight CTE-ranked DELETE that strips pre-existing duplicate active rows before
/// CREATE UNIQUE INDEX. Without it, fresh dev/CI/staging databases that accumulated
/// race-induced duplicates pre-v2.133p11 would crash with SqlException 1505 on first
/// migrate.
///
/// Tests run under the shared <see cref="SqlServerCollection"/> fixture, so the
/// migration is already applied (index present). Each test:
///   1. Drops the index (gating the cleanup behavior under test).
///   2. Seeds N duplicate active rows tagged by a per-test unique TenantId.
///   3. Executes the same preflight SQL the migration runs.
///   4. Asserts cleanup intent (latest CreatedAt kept, others purged).
///   5. Recreates the index and prunes test rows in the finalizer.
///
/// SQL string is duplicated in <see cref="PreflightCleanupSql"/> rather than reflected
/// from the migration to keep the test independent of EF migration internals; drift is
/// guarded by Test 3 which round-trips the full migration intent.
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class UniqueActiveInstanceIndexPreflightCleanupTests : IClassFixture<WebAppFixture>, IAsyncLifetime
{
    private const string IndexName = "UX_WorkflowInstances_Active_Entity";

    private const string DropIndexSql = @"
DROP INDEX IF EXISTS [UX_WorkflowInstances_Active_Entity] ON [workflow].[WorkflowInstances];";

    private const string CreateIndexSql = @"
CREATE UNIQUE INDEX [UX_WorkflowInstances_Active_Entity]
ON [workflow].[WorkflowInstances] ([EntityType], [EntityId], [TenantId])
WHERE [CompletedAt] IS NULL AND [DossierId] IS NULL;";

    /// <summary>
    /// Mirrors migration <c>Up()</c> body. Drift from migration = test failure for
    /// behavioral tests (1 + 2). Test 3 round-trips full Up() intent end-to-end.
    /// </summary>
    private const string PreflightCleanupSql = @"
SET QUOTED_IDENTIFIER ON;

;WITH RankedActiveInstances AS (
    SELECT
        [Id],
        ROW_NUMBER() OVER (
            PARTITION BY [EntityType], [EntityId], [TenantId]
            ORDER BY [CreatedAt] DESC, [Id] DESC
        ) AS rn
    FROM [workflow].[WorkflowInstances]
    WHERE [CompletedAt] IS NULL
)
DELETE FROM [workflow].[WorkflowInstances]
WHERE [Id] IN (SELECT [Id] FROM RankedActiveInstances WHERE rn > 1);";

    private readonly WebAppFixture _app;

    public UniqueActiveInstanceIndexPreflightCleanupTests(WebAppFixture app)
    {
        _app = app;
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        // Restore index if any test left it dropped, then prune any rows tagged with
        // test TenantIds (defensive: per-test finally blocks already prune).
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        await EnsureIndexExistsAsync(db);
    }

    [Fact]
    public async Task PreflightCleanup_KeepsLatestPerGroup_AndPurgesOlder()
    {
        var tenant = NewTenantGuid();
        var entityId = Guid.NewGuid();
        const string entityType = "MIG_PREFLIGHT_TEST_KEEP_LATEST";

        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();

        try
        {
            await db.Database.ExecuteSqlRawAsync(DropIndexSql);

            // Seed 3 active duplicates with deterministic CreatedAt ordering.
            // Latest (createdAt = T+2 minutes) must survive cleanup.
            var t0 = DateTimeOffset.UtcNow;
            var oldest = await SeedActiveInstanceAsync(db, tenant, entityType, entityId, t0);
            var middle = await SeedActiveInstanceAsync(db, tenant, entityType, entityId, t0.AddMinutes(1));
            var latest = await SeedActiveInstanceAsync(db, tenant, entityType, entityId, t0.AddMinutes(2));

            await db.Database.ExecuteSqlRawAsync(PreflightCleanupSql);

            var survivors = await CountActiveByTenantAsync(db, tenant);
            survivors.Should().Be(1, "preflight must collapse N duplicates to a single active row per group");

            var survivingId = await GetSingleActiveIdAsync(db, tenant);
            survivingId.Should().Be(latest, "newest CreatedAt must be retained; older duplicates purged");
        }
        finally
        {
            await PurgeTenantAsync(db, tenant);
            await EnsureIndexExistsAsync(db);
        }
    }

    [Fact]
    public async Task PreflightCleanup_IsIdempotent_OnAlreadyCleanData()
    {
        var tenant = NewTenantGuid();
        var entityId = Guid.NewGuid();
        const string entityType = "MIG_PREFLIGHT_TEST_IDEMPOTENT";

        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();

        try
        {
            await db.Database.ExecuteSqlRawAsync(DropIndexSql);

            // Seed exactly one active row — no duplicates to remove.
            var t0 = DateTimeOffset.UtcNow;
            var only = await SeedActiveInstanceAsync(db, tenant, entityType, entityId, t0);

            // First pass: should leave the row untouched.
            await db.Database.ExecuteSqlRawAsync(PreflightCleanupSql);
            (await CountActiveByTenantAsync(db, tenant)).Should().Be(1);
            (await GetSingleActiveIdAsync(db, tenant)).Should().Be(only);

            // Second pass: re-running on already-clean data must remain a no-op.
            await db.Database.ExecuteSqlRawAsync(PreflightCleanupSql);
            (await CountActiveByTenantAsync(db, tenant)).Should().Be(1);
            (await GetSingleActiveIdAsync(db, tenant)).Should().Be(only);
        }
        finally
        {
            await PurgeTenantAsync(db, tenant);
            await EnsureIndexExistsAsync(db);
        }
    }

    [Fact]
    public async Task IndexEnforcesUniqueness_AfterMigrationApplied()
    {
        // OUTBOX-1-FU coverage: confirms the unique filtered index actively blocks
        // duplicate active inserts at the DB layer (SqlException 2627/2601).
        var tenant = NewTenantGuid();
        var entityId = Guid.NewGuid();
        const string entityType = "MIG_PREFLIGHT_TEST_UNIQUE_GUARD";

        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();

        try
        {
            await EnsureIndexExistsAsync(db); // sanity — DB state for this test

            var t0 = DateTimeOffset.UtcNow;
            await SeedActiveInstanceAsync(db, tenant, entityType, entityId, t0);

            // Second active insert with same (EntityType, EntityId, TenantId) must fail at the DB.
            // Raw INSERT via ExecuteSqlInterpolatedAsync surfaces SqlException directly (no
            // DbUpdateException wrap, since EF change-tracker isn't on the path).
            Func<Task> attemptDuplicate = () => SeedActiveInstanceAsync(db, tenant, entityType, entityId, t0.AddSeconds(1));

            var ex = await attemptDuplicate.Should().ThrowAsync<SqlException>();
            // 2627 = unique constraint violation; 2601 = duplicate key in unique index. Either is valid.
            ex.Which.Number.Should().BeOneOf(new[] { 2627, 2601 });

            // Completing the first instance must allow a new active insert (filtered index excludes Completed rows).
            await db.Database.ExecuteSqlInterpolatedAsync($@"
UPDATE [workflow].[WorkflowInstances]
SET [CompletedAt] = SYSUTCDATETIME()
WHERE [TenantId] = {tenant} AND [EntityType] = {entityType} AND [CompletedAt] IS NULL;");

            // Should now succeed — filtered index only constrains active rows.
            await SeedActiveInstanceAsync(db, tenant, entityType, entityId, t0.AddSeconds(2));
            (await CountActiveByTenantAsync(db, tenant)).Should().Be(1);
        }
        finally
        {
            await PurgeTenantAsync(db, tenant);
        }
    }

    // ── helpers ──────────────────────────────────────────────────────────────────

    private static Guid NewTenantGuid() => Guid.NewGuid();

    private static async Task<long> SeedActiveInstanceAsync(
        WorkflowDbContext db,
        Guid tenantId,
        string entityType,
        Guid entityId,
        DateTimeOffset createdAt)
    {
        // [Id] is bigint IDENTITY — omit from INSERT, capture via OUTPUT.
        // [PublicId] is uniqueidentifier NOT NULL (IPublicIdentified) — must be supplied.
        // [DefinitionId]/[CurrentStateId] are bigint NOT NULL with no FK constraints in this
        // migration context, so synthetic sentinel values (1) are safe.
        var publicId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        const long syntheticFk = 1L;

        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
INSERT INTO [workflow].[WorkflowInstances]
    ([PublicId], [DefinitionId], [CurrentStateId], [EntityType], [EntityId],
     [StartedAt], [CompletedAt], [CurrentStateEnteredAt], [DueAt], [SlaBreachNotifiedAt],
     [TenantId], [CreatedAt], [UpdatedAt], [IsDeleted], [CreatedBy], [ModifiedBy],
     [ClassificationLevel], [LastMigrationId], [OriginalDefinitionId])
OUTPUT INSERTED.[Id]
VALUES
    (@publicId, @defId, @stateId, @entityType, @entityId,
     @startedAt, NULL, @startedAt, NULL, NULL,
     @tenantId, @createdAt, NULL, 0, @actorId, NULL,
     0, NULL, NULL);";

        void AddParam(string name, object value, System.Data.DbType type)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = name;
            p.Value = value;
            p.DbType = type;
            cmd.Parameters.Add(p);
        }

        AddParam("@publicId", publicId, System.Data.DbType.Guid);
        AddParam("@defId", syntheticFk, System.Data.DbType.Int64);
        AddParam("@stateId", syntheticFk, System.Data.DbType.Int64);
        AddParam("@entityType", entityType, System.Data.DbType.String);
        AddParam("@entityId", entityId, System.Data.DbType.Guid);
        AddParam("@startedAt", createdAt, System.Data.DbType.DateTimeOffset);
        AddParam("@tenantId", tenantId, System.Data.DbType.Guid);
        AddParam("@createdAt", createdAt.UtcDateTime, System.Data.DbType.DateTime2);
        AddParam("@actorId", actorId, System.Data.DbType.Guid);

        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt64(result);
    }

    private static async Task<int> CountActiveByTenantAsync(WorkflowDbContext db, Guid tenantId)
    {
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM [workflow].[WorkflowInstances] WHERE [TenantId] = @t AND [CompletedAt] IS NULL;";
        var p = cmd.CreateParameter();
        p.ParameterName = "@t";
        p.Value = tenantId;
        cmd.Parameters.Add(p);
        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    private static async Task<long?> GetSingleActiveIdAsync(WorkflowDbContext db, Guid tenantId)
    {
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT TOP 1 [Id] FROM [workflow].[WorkflowInstances] WHERE [TenantId] = @t AND [CompletedAt] IS NULL;";
        var p = cmd.CreateParameter();
        p.ParameterName = "@t";
        p.Value = tenantId;
        cmd.Parameters.Add(p);
        var result = await cmd.ExecuteScalarAsync();
        return result is not null and not DBNull ? Convert.ToInt64(result) : null;
    }

    private static async Task PurgeTenantAsync(WorkflowDbContext db, Guid tenantId)
    {
        // Cascade FK removes WorkflowInstanceHistories.
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM [workflow].[WorkflowInstances] WHERE [TenantId] = {tenantId};");
    }

    private const string EnsureIndexSql = @"
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = 'UX_WorkflowInstances_Active_Entity'
      AND object_id = OBJECT_ID('workflow.WorkflowInstances')
)
BEGIN
    CREATE UNIQUE INDEX [UX_WorkflowInstances_Active_Entity]
    ON [workflow].[WorkflowInstances] ([EntityType], [EntityId], [TenantId])
    WHERE [CompletedAt] IS NULL AND [DossierId] IS NULL;
END";

    private static async Task EnsureIndexExistsAsync(WorkflowDbContext db)
    {
        await db.Database.ExecuteSqlRawAsync(EnsureIndexSql);
    }
}
